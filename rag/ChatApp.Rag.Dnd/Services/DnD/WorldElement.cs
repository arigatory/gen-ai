using Microsoft.Extensions.VectorData;

namespace ChatApp.Rag.Services.DnD;

public class WorldElement
{
    private const int VectorDimensions = 1536;
    private const string VectorDistanceFunction = DistanceFunction.CosineDistance;

    [VectorStoreKey]
    public required string Key { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public required string ElementType { get; set; } // Character, Location, Event, Item, etc.

    [VectorStoreData(IsIndexed = true)]
    public required string WorldId { get; set; } // Identifier for the game world

    [VectorStoreData]
    public required string Name { get; set; }

    [VectorStoreData]
    public required string Description { get; set; }

    [VectorStoreData]
    public string? ParentElementId { get; set; } // For hierarchical relationships

    [VectorStoreData]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [VectorStoreData]
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [VectorStoreData]
    public required string SourceMessage { get; set; } // Original message that created/mentioned this element

    [VectorStoreData]
    public string? MessageAuthor { get; set; } // Player or DM

    [VectorStoreData]
    public string? AdditionalData { get; set; } // JSON for extra properties specific to element type

    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction)]
    public string? Vector => Description;
}