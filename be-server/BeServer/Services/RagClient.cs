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

    public async Task IngestAsync(string sourceId, string filePath, string mimeType)
    {
        var payload = new { source_id = sourceId, file_path = filePath, mime_type = mimeType };
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
}
