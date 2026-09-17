using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var openAiApiKey = builder.Configuration["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException(
        "OpenAI:ApiKey is not configured. Run: dotnet user-secrets set \"OpenAI:ApiKey\" \"<key>\"");
var embeddingModel = builder.Configuration["OpenAI:EmbeddingModel"] ?? "text-embedding-3-small";

var openAiClient = new OpenAIClient(openAiApiKey);
builder.Services.AddSingleton(openAiClient);
builder.Services.AddEmbeddingGenerator(openAiClient.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.Run();
