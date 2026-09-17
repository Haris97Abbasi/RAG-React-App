using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using OpenAI;
using PolicyLens.Api.Models;
using PolicyLens.Api.Services;

var builder = WebApplication.CreateBuilder(args);

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

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<VectorStoreSeeder>();
    await seeder.SeedAsync();
}

app.Run();
