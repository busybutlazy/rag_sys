using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeServer.Services;

public class RagClient(HttpClient http, IConfiguration config, IHttpContextAccessor httpContextAccessor)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? InternalSecret =>
        string.IsNullOrWhiteSpace(config["RAG_INTERNAL_SECRET"])
            ? config["INTERNAL_SECRET"]
            : config["RAG_INTERNAL_SECRET"];

    private void AddHeaders(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(InternalSecret))
            req.Headers.Add("X-Internal-Secret", InternalSecret);
        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;
        if (!string.IsNullOrWhiteSpace(correlationId))
            req.Headers.Add("X-Correlation-Id", correlationId);
    }

    public async Task IngestAsync(string sourceId, string notebookId, string userId, string filePath, string mimeType, RagRetrievalConfig? retrieval)
    {
        var payload = new { source_id = sourceId, notebook_id = notebookId, user_id = userId, file_path = filePath, mime_type = mimeType, retrieval };
        var req = new HttpRequestMessage(HttpMethod.Post, "/ingest") { Content = JsonContent.Create(payload) };
        AddHeaders(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string sourceId, string userId)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/documents/{sourceId}?user_id={userId}");
        AddHeaders(req);
        var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    public async Task DeleteSourceVersionChunksAsync(string sourceId, string userId, string? retrievalVersionId)
    {
        var url = string.IsNullOrEmpty(retrievalVersionId)
            ? $"/documents/{sourceId}/chunks?user_id={userId}"
            : $"/documents/{sourceId}/chunks?user_id={userId}&retrieval_version_id={Uri.EscapeDataString(retrievalVersionId)}";
        var req = new HttpRequestMessage(HttpMethod.Delete, url);
        AddHeaders(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteNotebookVersionChunksAsync(string notebookId, string userId, string retrievalVersionId)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete,
            $"/notebooks/{notebookId}/chunks?user_id={userId}&retrieval_version_id={Uri.EscapeDataString(retrievalVersionId)}");
        AddHeaders(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteNotebookAsync(string notebookId, string userId)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/notebooks/{notebookId}/documents?user_id={userId}");
        AddHeaders(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
    }

    public Task<RagSearchResponse> SearchAsync(
        string query,
        string notebookId,
        string userId,
        string mode,
        int topK,
        string? retrievalVersionId = null,
        double? alpha = null)
    {
        var versionQuery = string.IsNullOrWhiteSpace(retrievalVersionId)
            ? ""
            : $"&retrieval_version_id={Uri.EscapeDataString(retrievalVersionId)}";
        var alphaQuery = alpha is null ? "" : $"&alpha={alpha.Value}";
        return GetAsync<RagSearchResponse>(
            $"/search/{mode}?q={Uri.EscapeDataString(query)}&notebook_id={Uri.EscapeDataString(notebookId)}&user_id={Uri.EscapeDataString(userId)}&top_k={topK}{versionQuery}{alphaQuery}");
    }

    public Task<RagBenchmarkResponse> BenchmarkAsync(string query, string notebookId, string userId, int topK, string? retrievalVersionId = null)
    {
        var versionQuery = string.IsNullOrWhiteSpace(retrievalVersionId)
            ? ""
            : $"&retrieval_version_id={Uri.EscapeDataString(retrievalVersionId)}";
        return GetAsync<RagBenchmarkResponse>(
            $"/search/benchmark?q={Uri.EscapeDataString(query)}&notebook_id={Uri.EscapeDataString(notebookId)}&user_id={Uri.EscapeDataString(userId)}&top_k={topK}{versionQuery}");
    }

    public async Task<RagExperimentRecord> RunExperimentAsync(string notebookId, string userId, ExperimentRunRequest req)
    {
        var experimentConfig = req.Config ?? new ExperimentConfig(["vector", "bm25", "hybrid"]);
        var payload = new
        {
            notebook_id = notebookId,
            user_id = userId,
            name = req.Name,
            queries = req.Queries,
            config = new { modes = experimentConfig.Modes, top_k = experimentConfig.TopK, alpha = experimentConfig.Alpha },
        };
        var msg = new HttpRequestMessage(HttpMethod.Post, "/experiments/run") { Content = JsonContent.Create(payload) };
        AddHeaders(msg);
        var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<RagExperimentRecord>(response);
    }

    public Task<List<RagExperimentRecord>> ListExperimentsAsync(string notebookId, string userId, int limit) =>
        GetAsync<List<RagExperimentRecord>>($"/experiments?notebook_id={notebookId}&user_id={userId}&limit={limit}");

    public Task<RagExperimentRecord> GetExperimentAsync(string notebookId, string userId, string experimentId) =>
        GetAsync<RagExperimentRecord>($"/experiments/{experimentId}?notebook_id={notebookId}&user_id={userId}");

    public Task<RagSourceContentResponse> GetSourceContentAsync(string sourceId, string notebookId, string userId, string? retrievalVersionId = null)
    {
        var versionQuery = string.IsNullOrWhiteSpace(retrievalVersionId)
            ? ""
            : $"&retrieval_version_id={Uri.EscapeDataString(retrievalVersionId)}";
        return GetAsync<RagSourceContentResponse>(
            $"/documents/{sourceId}/content?notebook_id={Uri.EscapeDataString(notebookId)}&user_id={Uri.EscapeDataString(userId)}{versionQuery}");
    }

    public async Task<RagGraphIngestResponse> GraphIngestAsync(
        string sourceId, string notebookId, string userId, string? retrievalVersionId, IReadOnlyList<RagChunkExtraction> chunkExtractions)
    {
        var payload = new
        {
            source_id = sourceId,
            notebook_id = notebookId,
            user_id = userId,
            retrieval_version_id = retrievalVersionId,
            chunk_extractions = chunkExtractions,
        };
        var req = new HttpRequestMessage(HttpMethod.Post, "/graph/ingest") { Content = JsonContent.Create(payload, options: JsonOptions) };
        AddHeaders(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<RagGraphIngestResponse>(response);
    }

    private async Task<T> GetAsync<T>(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
        return await ReadAsync<T>(response);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>(JsonOptions)
        ?? throw new InvalidOperationException("RAG response body was empty.");
}

public record RagChunkResult(
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    [property: JsonPropertyName("retrieval_version_id")] string? RetrievalVersionId,
    string Text);
public record RagSearchResponse(List<RagChunkResult> Results);
public record RagBenchmarkResponse(
    string Query,
    List<RagChunkResult> Vector,
    List<RagChunkResult> Bm25,
    List<RagChunkResult> Hybrid);
public record RagExperimentResultItem(
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("chunk_index")] int ChunkIndex);
public record RagExperimentQueryResult(
    string Query,
    string Mode,
    [property: JsonPropertyName("latency_ms")] int LatencyMs,
    [property: JsonPropertyName("result_count")] int ResultCount,
    List<RagExperimentResultItem> Results);
public record RagExperimentRecord(
    string Id,
    [property: JsonPropertyName("notebook_id")] string NotebookId,
    string Name,
    ExperimentConfig Config,
    string[] Queries,
    List<RagExperimentQueryResult> Results,
    [property: JsonPropertyName("created_at")] string CreatedAt);
public record ExperimentConfig(string[] Modes, int TopK = 5, double Alpha = 0.5);
public record ExperimentRunRequest(string? Name, string[] Queries, ExperimentConfig? Config);
public record RagSourceContentResponse(
    [property: JsonPropertyName("source_id")] string SourceId,
    [property: JsonPropertyName("notebook_id")] string NotebookId,
    List<RagChunkResult> Chunks,
    string Text,
    bool Truncated);
public record RagGraphMention(
    [property: JsonPropertyName("entity_name")] string EntityName,
    [property: JsonPropertyName("entity_type")] string EntityType);
public record RagGraphFactParticipant(
    [property: JsonPropertyName("entity_name")] string EntityName,
    string Role);
public record RagGraphFact(
    string Predicate,
    [property: JsonPropertyName("statement_text")] string StatementText,
    double Confidence,
    List<RagGraphFactParticipant> Participants);
public record RagChunkExtraction(
    [property: JsonPropertyName("chunk_index")] int ChunkIndex,
    List<RagGraphMention> Mentions,
    List<RagGraphFact> Facts);
public record RagGraphIngestResponse(
    [property: JsonPropertyName("entities_written")] int EntitiesWritten,
    [property: JsonPropertyName("facts_written")] int FactsWritten,
    [property: JsonPropertyName("edges_written")] int EdgesWritten,
    [property: JsonPropertyName("skipped_chunks")] List<int> SkippedChunks);
public record RagRetrievalConfig(
    [property: JsonPropertyName("retrieval_version_id")] string RetrievalVersionId,
    [property: JsonPropertyName("chunk_size")] int ChunkSize,
    [property: JsonPropertyName("chunk_overlap")] int ChunkOverlap,
    [property: JsonPropertyName("embedding_model")] string EmbeddingModel,
    [property: JsonPropertyName("embedding_dimensions")] int EmbeddingDimensions,
    [property: JsonPropertyName("search_mode")] string SearchMode,
    [property: JsonPropertyName("top_k")] int TopK,
    [property: JsonPropertyName("hybrid_alpha")] double HybridAlpha);
