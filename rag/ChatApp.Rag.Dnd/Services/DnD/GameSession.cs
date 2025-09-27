using Microsoft.Extensions.VectorData;

namespace ChatApp.Rag.Services.DnD;

public class GameSession
{
    private const int VectorDimensions = 2;
    private const string VectorDistanceFunction = DistanceFunction.CosineDistance;

    [VectorStoreKey]
    public required string Key { get; set; }

    [VectorStoreData(IsIndexed = true)]
    public required string WorldId { get; set; }

    [VectorStoreData]
    public required string SessionName { get; set; }

    [VectorStoreData]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [VectorStoreData]
    public long LastActivityAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [VectorStoreData]
    public string? WorldSummary { get; set; }

    [VectorStoreData]
    public int MessageCount { get; set; } = 0;

    // The vector is not used but required for some vector databases
    [VectorStoreVector(VectorDimensions, DistanceFunction = VectorDistanceFunction)]
    public ReadOnlyMemory<float> Vector { get; set; } = new ReadOnlyMemory<float>([0, 0]);
}