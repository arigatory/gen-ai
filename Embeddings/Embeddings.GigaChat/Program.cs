using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Embeddings.GigaChat;
using System.Numerics.Tensors;

// get credentials from user secrets
IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var authData = config["GigaChat:Token"] ?? throw new InvalidOperationException("Missing configuration: GigaChat:Token.");

// Create an embedding generator for GigaChat
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new GigaChatClient(authData);

// 1: Generate a single embedding
// var embedding = await embeddingGenerator.GenerateVectorAsync("Hello, world!");
// Console.WriteLine($"Embedding dimensions: {embedding.Span.Length}");
// foreach (var value in embedding.Span)
// {
//    Console.Write("{0:0.00}, ", value);
// }


// Compare multiple embeddings using Cosine Similarity
var catVector = await embeddingGenerator.GenerateVectorAsync("cat");
var dogVector = await embeddingGenerator.GenerateVectorAsync("dog");
var kittenVector = await embeddingGenerator.GenerateVectorAsync("kitten");

Console.WriteLine($"cat-dog similarity: {TensorPrimitives.CosineSimilarity(catVector.Span, dogVector.Span):F2}");
Console.WriteLine($"cat-kitten similarity: {TensorPrimitives.CosineSimilarity(catVector.Span, kittenVector.Span):F2}");
Console.WriteLine($"dog-kitten similarity: {TensorPrimitives.CosineSimilarity(dogVector.Span, kittenVector.Span):F2}");