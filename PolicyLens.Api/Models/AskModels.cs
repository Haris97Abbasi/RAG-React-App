namespace PolicyLens.Api.Models;

public sealed record AskRequest(string Question);

public sealed record AskResponse(string Answer, IReadOnlyList<string> Sources);
