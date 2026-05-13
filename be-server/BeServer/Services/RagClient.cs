using System.Net.Http.Json;

namespace BeServer.Services;

public class RagClient(HttpClient http, IConfiguration config)
{
    private string? InternalSecret => config["INTERNAL_SECRET"];

    private void AddSecret(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(InternalSecret))
            req.Headers.Add("X-Internal-Secret", InternalSecret);
    }

    public async Task IngestAsync(string sourceId, string notebookId, string filePath, string mimeType)
    {
        var payload = new { source_id = sourceId, notebook_id = notebookId, file_path = filePath, mime_type = mimeType };
        var req = new HttpRequestMessage(HttpMethod.Post, "/ingest")
        {
            Content = JsonContent.Create(payload)
        };
        AddSecret(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(string sourceId)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"/documents/{sourceId}");
        AddSecret(req);
        var response = await http.SendAsync(req);
        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
            response.EnsureSuccessStatusCode();
    }

    public async Task<string> SearchAsync(string query, string notebookId, string mode, int topK)
    {
        var url = $"/search/{mode}?q={Uri.EscapeDataString(query)}&notebook_id={notebookId}&top_k={topK}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddSecret(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> BenchmarkAsync(string query, string notebookId, int topK)
    {
        var url = $"/search/benchmark?q={Uri.EscapeDataString(query)}&notebook_id={notebookId}&top_k={topK}";
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddSecret(req);
        var response = await http.SendAsync(req);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> RunExperimentAsync(string notebookId, ExperimentRunRequest req)
    {
        var payload = new
        {
            notebook_id = notebookId,
            name = req.Name,
            queries = req.Queries,
            config = new
            {
                modes = req.Config.Modes,
                top_k = req.Config.TopK,
                alpha = req.Config.Alpha,
            },
        };
        var msg = new HttpRequestMessage(HttpMethod.Post, "/experiments/run")
        {
            Content = JsonContent.Create(payload)
        };
        AddSecret(msg);
        var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> ListExperimentsAsync(string notebookId, int limit)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, $"/experiments?notebook_id={notebookId}&limit={limit}");
        AddSecret(msg);
        var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> GetExperimentAsync(string notebookId, string experimentId)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, $"/experiments/{experimentId}?notebook_id={notebookId}");
        AddSecret(msg);
        var response = await http.SendAsync(msg);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}

public record ExperimentConfig(string[] Modes, int TopK = 5, double Alpha = 0.5);
public record ExperimentRunRequest(string? Name, string[] Queries, ExperimentConfig Config);
