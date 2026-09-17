using Microsoft.Extensions.VectorData;

namespace PolicyLens.Api.Models;

public sealed class PolicyChunk
{
    [VectorStoreKey]
    public required string Id { get; set; }

    [VectorStoreData]
    public required int SectionNumber { get; set; }

    [VectorStoreData]
    public required string SectionTitle { get; set; }

    [VectorStoreData]
    public required string Text { get; set; }

    [VectorStoreVector(1536, DistanceFunction = DistanceFunction.CosineSimilarity)]
    public ReadOnlyMemory<float> Embedding { get; set; }
}
