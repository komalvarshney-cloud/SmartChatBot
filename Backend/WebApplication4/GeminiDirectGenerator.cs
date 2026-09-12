using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace WebApplication4.Services;

public class GeminiDirectGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly string _apiKey;
    private readonly HttpClient _http;

    public GeminiDirectGenerator(string apiKey)
    {
        _apiKey = apiKey;
        _http = new HttpClient();
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
      IEnumerable<string> values,
      EmbeddingGenerationOptions? options = null,
      CancellationToken cancellationToken = default)
    {
        var embeddings = new List<Embedding<float>>();

        foreach (var text in values)
        {
            // 1. Updated Payload: Strictly using the globally available "embedding-001"
            var payload = new
            {
                model = "models/embedding-001",
                content = new { parts = new[] { new { text = text } } }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            // 2. Updated URL endpoint mapping to "embedding-001"
            var response = await _http.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-001:embedContent?key={_apiKey}",
                content,
                cancellationToken);

            // 3. Transparent Error Handling (No more hidden 404s)
            if (!response.IsSuccessStatusCode)
            {
                var errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new Exception($"Gemini API Failed! Status: {response.StatusCode}. Details: {errorDetails}");
            }

            // 4. Vector Extraction
            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonResponse);

            var vector = doc.RootElement
                .GetProperty("embedding")
                .GetProperty("values")
                .EnumerateArray()
                .Select(x => x.GetSingle())
                .ToArray();

            embeddings.Add(new Embedding<float>(vector));
        }

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    // Standard interface requirements
    public void Dispose() => _http.Dispose();
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}