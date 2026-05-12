using System.Net.Http.Json;

namespace BeServer.Services;

public class RagClient(HttpClient http)
{
    public async Task IngestAsync(string sourceId, string filePath, string mimeType)
    {
        var payload = new { source_id = sourceId, file_path = filePath, mime_type = mimeType };
        var response = await http.PostAsJsonAsync("/ingest", payload);
        response.EnsureSuccessStatusCode();
    }
}
