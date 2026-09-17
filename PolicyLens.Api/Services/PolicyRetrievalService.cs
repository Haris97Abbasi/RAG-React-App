using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using PolicyLens.Api.Models;

namespace PolicyLens.Api.Services;

/// <summary>
/// Embeds a user question and finds the most semantically similar policy chunks via vector search.
/// </summary>
public sealed class PolicyRetrievalService(
    VectorStoreCollection<string, PolicyChunk> collection,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
{
    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveTopChunksAsync(
        string question, int top = 3, CancellationToken cancellationToken = default)
    {
        var embeddingResult = await embeddingGenerator.GenerateAsync([question], cancellationToken: cancellationToken);
        var queryVector = embeddingResult[0].Vector;

        var results = new List<RetrievedChunk>();
        await foreach (var result in collection.SearchAsync(queryVector, top, cancellationToken: cancellationToken))
        {
            results.Add(new RetrievedChunk(
                result.Record.SectionNumber,
                result.Record.SectionTitle,
                result.Record.Text,
                result.Score));
        }

        return results;
    }
}
