using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel.Connectors.InMemory;
using VectorSearch.GigaChat;

// get credentials from user secrets
IConfigurationRoot config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();

var authData = config["GigaChat:Token"] ?? throw new InvalidOperationException("Missing configuration: GigaChat:Token.");

// Create an embedding generator for GigaChat
IEmbeddingGenerator<string, Embedding<float>> generator = new GigaChatClient(authData);

// Test to determine embedding dimensions
Console.WriteLine("Testing embedding dimensions...");
var dimensions = await TestDimensions.GetEmbeddingDimensionsAsync();
Console.WriteLine($"GigaChat embedding dimensions: {dimensions}");

// Create and populate the vector store
var vectorStore = new InMemoryVectorStore();

var moviesStore = vectorStore.GetCollection<int, Movie>("movies");

await moviesStore.EnsureCollectionExistsAsync();

foreach (var movie in MovieData.Movies)
{
    System.Console.WriteLine($"Generating vector for {movie.Title}");
    // generate the embedding vector for the movie description
    movie.Vector = await generator.GenerateVectorAsync(movie.Description);

    // add the overall movie to the in-memory vector store's movie collection
    await moviesStore.UpsertAsync(movie);
}

//1-Embed the user's query
//2-Vectorized search
//3-Returns the records

// generate the embedding vector for the user's prompt
//var query = "I want to see family friendly movie";
var query = "A science fiction movie about space travel";
var queryEmbedding = await generator.GenerateVectorAsync(query);

// search the knowledge store based on the user's prompt
var searchResults = moviesStore.SearchAsync(queryEmbedding, top: 2);

// see the results just so we know what they look like
await foreach (var result in searchResults)
{
    Console.WriteLine($"Title: {result.Record.Title}");
    Console.WriteLine($"Description: {result.Record.Description}");
    Console.WriteLine($"Score: {result.Score}");
    Console.WriteLine();
}
