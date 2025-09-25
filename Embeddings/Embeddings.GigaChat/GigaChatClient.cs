using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Embeddings.GigaChat;

public class GigaChatClient : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly JsonSerializerOptions _jsonOptions;

    public GigaChatClient(string authData)
    {
        var handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        _httpClient = new HttpClient(handler);
        _httpClient.BaseAddress = new Uri("https://gigachat.devices.sberbank.ru/");
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "GigaChatClient/1.0");
        _accessToken = GetAccessTokenAsync(authData).GetAwaiter().GetResult();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public EmbeddingGeneratorMetadata Metadata => new("GigaChat", new Uri("https://gigachat.devices.sberbank.ru/"));

    public TService? GetService<TService>(object? key = null) where TService : class => null;

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = new List<Embedding<float>>();

        foreach (var value in values)
        {
            var embedding = await GenerateVectorAsync(value, options, cancellationToken);
            embeddings.Add(embedding);
        }

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    public async Task<Embedding<float>> GenerateVectorAsync(string value, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var request = new GigaChatEmbeddingRequest
        {
            Input = [value]
        };

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        Console.WriteLine($"Request JSON: {json}");
        Console.WriteLine($"Request URL: {_httpClient.BaseAddress}api/v1/embeddings");

        var response = await _httpClient.PostAsync("api/v1/embeddings", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Error response: {errorContent}");
        }

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var gigaChatResponse = JsonSerializer.Deserialize<GigaChatEmbeddingResponse>(responseJson, _jsonOptions);

        if (gigaChatResponse?.Data?.Length > 0)
        {
            var embeddingData = gigaChatResponse.Data[0];
            var vector = embeddingData.Embedding.ToArray();
            return new Embedding<float>(vector);
        }

        throw new InvalidOperationException("Не удалось получить embedding от GigaChat");
    }

    private async Task<string> GetAccessTokenAsync(string authData)
    {
        var handler = new HttpClientHandler()
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authData);
        client.DefaultRequestHeaders.Add("RqUID", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("User-Agent", "GigaChatClient/1.0");

        var formParams = new List<KeyValuePair<string, string>>
        {
            new("scope", "GIGACHAT_API_PERS")
        };

        var content = new FormUrlEncodedContent(formParams);

        var response = await client.PostAsync("https://ngw.devices.sberbank.ru:9443/api/v2/oauth", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<GigaChatTokenResponse>(responseJson);

        return tokenResponse?.AccessToken ?? throw new InvalidOperationException("Не удалось получить токен доступа");
    }
}

public class GigaChatEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "Embeddings";

    [JsonPropertyName("input")]
    public string[] Input { get; set; } = [];
}

public class GigaChatEmbeddingResponse
{
    [JsonPropertyName("object")]
    public string? Object { get; set; }

    [JsonPropertyName("data")]
    public GigaChatEmbeddingData[]? Data { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("usage")]
    public GigaChatUsage? Usage { get; set; }
}

public class GigaChatEmbeddingData
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = "";

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = [];

    [JsonPropertyName("index")]
    public int Index { get; set; }
}

public class GigaChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int? TotalTokens { get; set; }
}

public class GigaChatTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_at")]
    public long ExpiresAt { get; set; }
}