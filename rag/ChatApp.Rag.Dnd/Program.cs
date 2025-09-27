using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using ChatApp.Rag.Components;
using ChatApp.Rag.Services;
using ChatApp.Rag.Services.Ingestion;
using ChatApp.Rag.Services.DnD;
using OpenAI;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// You will need to set the endpoint and key to your own values
// You can do this using Visual Studio's "Manage User Secrets" UI, or on the command line:
//   cd this-project-directory
//   dotnet user-secrets set GithubModels:Token YOUR-GITHUB-TOKEN
var credential = new ApiKeyCredential(builder.Configuration["GithubModels:Token"] ?? throw new InvalidOperationException("Missing configuration: GithubModels:Token. See the README for details."));
var openAIOptions = new OpenAIClientOptions()
{
    Endpoint = new Uri("https://models.inference.ai.azure.com")
};

var ghModelsClient = new OpenAIClient(credential, openAIOptions);
var chatClient = ghModelsClient.GetChatClient("gpt-4o-mini").AsIChatClient();
var embeddingGenerator = ghModelsClient.GetEmbeddingClient("text-embedding-3-small").AsIEmbeddingGenerator();

var vectorStorePath = Path.Combine(AppContext.BaseDirectory, "vector-store.db");
var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
builder.Services.AddSqliteCollection<string, IngestedChunk>("data-chatapp_rag-chunks", vectorStoreConnectionString);
builder.Services.AddSqliteCollection<string, IngestedDocument>("data-chatapp_rag-documents", vectorStoreConnectionString);

builder.Services.AddScoped<DataIngestor>();
builder.Services.AddSingleton<SemanticSearch>();
builder.Services.AddSingleton<SimpleWorldManager>();
builder.Services.AddChatClient(chatClient).UseFunctionInvocation().UseLogging();
builder.Services.AddEmbeddingGenerator(embeddingGenerator);

var app = builder.Build();

// Vector collections will be lazy-loaded when needed

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// For D&D mode, we don't need to ingest PDF files by default
// Uncomment the following lines if you want to keep RAG functionality alongside D&D
/*
await DataIngestor.IngestDataAsync(
    app.Services,
    new PDFDirectorySource(Path.Combine(builder.Environment.WebRootPath, "Data")));
*/

app.Run();
