namespace PolicyLens.Api.Models;

public sealed record RetrievedChunk(int SectionNumber, string SectionTitle, string Text, double? Score);
