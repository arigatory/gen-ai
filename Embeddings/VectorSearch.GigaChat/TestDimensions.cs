using Microsoft.Extensions.Configuration;

namespace VectorSearch.GigaChat;

public static class TestDimensions
{
    public static async Task<int> GetEmbeddingDimensionsAsync()
    {
        try
        {
            IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
            var authData = config["GigaChat:Token"] ?? throw new InvalidOperationException("Missing configuration: GigaChat:Token.");

            var client = new GigaChatClient(authData);
            var testEmbedding = await client.GenerateVectorAsync("test");

            return testEmbedding.Vector.Length;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error testing dimensions: {ex.Message}");
            return 1024; // Default fallback
        }
    }
}