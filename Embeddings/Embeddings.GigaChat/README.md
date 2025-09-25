# Embeddings.GigaChat

This project demonstrates how to use GigaChat API for generating text embeddings, following the same pattern as the main Embeddings project but using GigaChat instead of GitHub Models.

## Setup

1. Configure your GigaChat authentication data in user secrets:

```bash
dotnet user-secrets set "GigaChat:AuthData" "your-base64-encoded-auth-data"
```

The AuthData should be your GigaChat credentials encoded in Base64 format.

2. Build and run the project:

```bash
dotnet build
dotnet run
```

## Features

- Implements `IEmbeddingGenerator<string, Embedding<float>>` interface
- Compatible with Microsoft.Extensions.AI framework
- Supports single text embedding generation
- Uses GigaChat API `/api/v1/embeddings` endpoint
- Handles authentication and token management automatically

## Usage

The project demonstrates basic embedding generation:

```csharp
// Create an embedding generator for GigaChat
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new GigaChatClient(authData);

// Generate a single embedding
var embedding = await embeddingGenerator.GenerateVectorAsync("Hello, world!");
Console.WriteLine($"Embedding dimensions: {embedding.Span.Length}");
```

## Notes

- GigaChat embeddings API has a limitation of approximately 512 tokens
- SSL certificate validation is disabled for GigaChat endpoints
- The implementation follows the same interface as the main Embeddings project for consistency