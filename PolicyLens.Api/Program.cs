using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using OpenAI;
using PolicyLens.Api.Models;
using PolicyLens.Api.Services;

var builder = WebApplication.CreateBuilder(args);

const string ReactDevCorsPolicy = "ReactDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ReactDevCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddOpenApi();

var openAiApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException(
        "OpenAI:ApiKey is not configured. Run: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\"");
var embeddingModel = builder.Configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";
var chatModel = builder.Configuration["OpenAI:ChatModel"] ?? "gpt-4o-mini";

var openAiClient = new OpenAIClient(openAiApiKey);
builder.Services.AddSingleton(openAiClient);
builder.Services.AddEmbeddingGenerator(openAiClient.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());
builder.Services.AddSingleton(openAiClient.GetChatClient(chatModel).AsIChatClient());
builder.Services.AddSingleton<PolicyAnsweringAgent>();

var vectorDbPath = Path.Combine(builder.Environment.ContentRootPath, "policylens.db");
builder.Services.AddSingleton<VectorStoreCollection<string, PolicyChunk>>(
    new SqlitePolicyChunkCollection($"Data Source={vectorDbPath}", "policy_chunks"));

builder.Services.AddSingleton<PdfIngestionService>();
builder.Services.AddScoped<VectorStoreSeeder>();
builder.Services.AddScoped<PolicyRetrievalService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(ReactDevCorsPolicy);

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<VectorStoreSeeder>();
    await seeder.SeedAsync();
}

app.MapPost("/api/ask", async (
    AskRequest request,
    PolicyRetrievalService retrieval,
    PolicyAnsweringAgent agent,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest("Question must not be empty.");
    }

    var chunks = await retrieval.RetrieveTopChunksAsync(request.Question, top: 3, cancellationToken);
    var answer = await agent.AnswerAsync(request.Question, chunks, cancellationToken);
    var sources = chunks.Select(c => c.SectionTitle).ToList();

    return Results.Ok(new AskResponse(answer, sources));
});

app.Run();
