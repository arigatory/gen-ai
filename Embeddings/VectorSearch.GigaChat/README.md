# VectorSearch.GigaChat

This project demonstrates how to perform vector search using GigaChat embeddings with the Semantic Kernel InMemory vector store, following the same pattern as the main VectorSearch project but using GigaChat instead of GitHub Models.

## Setup

1. Configure your GigaChat authentication token in user secrets:

```bash
dotnet user-secrets set "GigaChat:Token" "your-base64-encoded-auth-data"
```

The Token should be your GigaChat credentials encoded in Base64 format.

2. Build and run the project:

```bash
dotnet build
dotnet run
```

## Features

- Uses GigaChat API for generating embeddings
- Implements vector search using Semantic Kernel InMemory connector
- Populates a vector store with movie data and their embeddings
- Performs similarity search queries against the movie database
- Automatically determines GigaChat embedding dimensions
- Compatible with Microsoft.Extensions.AI framework

## Functionality

The project:

1. **Creates embeddings**: Generates vector embeddings for movie descriptions using GigaChat
2. **Populates vector store**: Stores movies with their embeddings in an in-memory vector database
3. **Performs search**: Executes similarity search queries to find relevant movies
4. **Returns results**: Shows the most similar movies with their similarity scores

## Example Query

The project searches for "A science fiction movie about space travel" and returns the most similar movies from the database, demonstrating semantic search capabilities.

## Movie Data

The project includes a sample dataset of 5 movies:
- Lion King
- Inception
- The Matrix
- Shrek
- Interstellar

## Notes

- GigaChat embeddings API has a limitation of approximately 512 tokens
- SSL certificate validation is disabled for GigaChat endpoints
- The project automatically tests and determines the correct embedding dimensions for GigaChat
- Uses cosine similarity for vector comparisons