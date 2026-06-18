using System.Net.Http.Json;
using System.Text.Json;
using BeServer.Data.Entities;

namespace BeServer.Services;

// Phase 19 Gate B: runs after a successful ingest/reindex when the target
// retrieval version has EnableGraph set. Never throws -- a failed
// extraction must not fail the underlying ingestion, so every failure
// mode here degrades to a status string instead of an exception.
public class GraphExtractionService(
    RagClient rag,
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<GraphExtractionService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Caps how many of a source's chunks are sent to the AI server for
    // extraction in one go. Sources with many chunks would otherwise trigger
    // an unbounded number of sequential LLM calls; truncating (rather than
    // failing the job) keeps ingestion never-break while still extracting a
    // useful prefix of the document.
    public const int MaxChunksPerGraphExtraction = 50;

    private string? AiInternalSecret =>
        string.IsNullOrWhiteSpace(config["AI_INTERNAL_SECRET"])
            ? config["INTERNAL_SECRET"]
            : config["AI_INTERNAL_SECRET"];

    public async Task<string> ExtractAndIngestAsync(
        string sourceId,
        string notebookId,
        string userId,
        NotebookRetrievalVersion targetVersion,
        CancellationToken cancellationToken = default)
    {
        if (!targetVersion.EnableGraph)
            return GraphExtractionStatuses.Skipped;

        try
        {
            var content = await rag.GetSourceContentAsync(sourceId, notebookId, userId, targetVersion.Id);
            if (content.Chunks.Count == 0)
                return GraphExtractionStatuses.Skipped;

            var chunksToExtract = content.Chunks.Count > MaxChunksPerGraphExtraction
                ? content.Chunks.Take(MaxChunksPerGraphExtraction).ToList()
                : content.Chunks;

            var extractions = await ExtractAsync(chunksToExtract, targetVersion.GraphExtractionModel, cancellationToken);
            await rag.GraphIngestAsync(sourceId, notebookId, userId, targetVersion.Id, extractions);
            return GraphExtractionStatuses.Succeeded;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Graph extraction failed for source {SourceId} (retrieval version {VersionId}); vector/BM25 retrieval is unaffected.",
                sourceId,
                targetVersion.Id);
            return GraphExtractionStatuses.Failed;
        }
    }

    // Aggregates per-source outcomes for a notebook-scope reindex job into
    // a single job-level status: any failure dominates, then any success,
    // then skipped, with null meaning nothing was attempted at all.
    public static string? Aggregate(IEnumerable<string> statuses)
    {
        var seen = statuses.ToList();
        if (seen.Count == 0)
            return null;
        if (seen.Contains(GraphExtractionStatuses.Failed))
            return GraphExtractionStatuses.Failed;
        if (seen.Contains(GraphExtractionStatuses.Succeeded))
            return GraphExtractionStatuses.Succeeded;
        return GraphExtractionStatuses.Skipped;
    }

    private async Task<List<RagChunkExtraction>> ExtractAsync(
        List<RagChunkResult> chunks, string? model, CancellationToken cancellationToken)
    {
        var payload = new
        {
            chunks = chunks.Select(c => new { chunk_index = c.ChunkIndex, text = c.Text }),
            model,
        };
        var client = httpClientFactory.CreateClient("ai-server");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/ai/extract/graph")
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        if (!string.IsNullOrEmpty(AiInternalSecret))
            req.Headers.Add("X-Internal-Secret", AiInternalSecret);

        using var res = await client.SendAsync(req, cancellationToken);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<List<RagChunkExtraction>>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("AI server graph extraction response was empty.");
    }
}
