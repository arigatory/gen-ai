using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Embeddings.GigaChat;

// get credentials from user secrets
IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var authData = config["GigaChat:Token"] ?? throw new InvalidOperationException("Missing configuration: GigaChat:Token.");

// Create an embedding generator for GigaChat
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new GigaChatClient(authData);

// 1: Generate a single embedding
var embedding = await embeddingGenerator.GenerateVectorAsync("Hello, world!");
Console.WriteLine($"Embedding dimensions: {embedding.Span.Length}");
foreach (var value in embedding.Span)
{
   Console.Write("{0:0.00}, ", value);
}
