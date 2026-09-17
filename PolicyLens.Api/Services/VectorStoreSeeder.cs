using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using PolicyLens.Api.Models;

namespace PolicyLens.Api.Services;

/// <summary>
/// Populates the SQLite vector store from the policy PDF on startup, once.
/// Skips re-ingestion on subsequent runs by checking whether section 1 already exists.
/// </summary>
public sealed class VectorStoreSeeder(
    VectorStoreCollection<string, PolicyChunk> collection,
    PdfIngestionService pdfIngestionService,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<VectorStoreSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        if (await collection.GetAsync("1", cancellationToken: cancellationToken) is not null)
        {
            logger.LogInformation("Policy vector store already populated; skipping ingestion.");
            return;
        }

        var pdfRelativePath = configuration["Policy:PdfPath"]
            ?? "../PolicyLens_Sample_Employee_Remote_Work_Security_Policy.pdf";
        var pdfPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, pdfRelativePath));

        var sections = pdfIngestionService.ExtractSections(pdfPath);
        logger.LogInformation("Extracted {Count} policy sections from {Path}.", sections.Count, pdfPath);

        foreach (var section in sections)
        {
            var embeddingResult = await embeddingGenerator.GenerateAsync([section.Text], cancellationToken: cancellationToken);
            var chunk = new PolicyChunk
            {
                Id = section.Number.ToString(),
                SectionNumber = section.Number,
                SectionTitle = section.Title,
                Text = section.Text,
                Embedding = embeddingResult[0].Vector,
            };
            await collection.UpsertAsync(chunk, cancellationToken);
        }

        logger.LogInformation("Ingested {Count} policy chunks into the vector store.", sections.Count);
    }
}
