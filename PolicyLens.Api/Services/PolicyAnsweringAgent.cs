using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using PolicyLens.Api.Models;

namespace PolicyLens.Api.Services;

/// <summary>
/// Wraps an <see cref="AIAgent"/> (Microsoft Agent Framework) around an OpenAI chat client,
/// instructed to answer strictly from the retrieved policy excerpts and say so when they don't cover the question.
/// </summary>
public sealed class PolicyAnsweringAgent
{
    private const string Instructions = """
        You are PolicyLens, an assistant that answers employee questions about company policy.
        Answer ONLY using the policy excerpts provided below the question. Do not use any outside
        knowledge and do not invent information that is not present in the excerpts.
        If the excerpts do not contain enough information to answer the question, say clearly that
        the policy does not contain this information. Keep answers concise and factual.
        """;

    private readonly AIAgent _agent;

    public PolicyAnsweringAgent(IChatClient chatClient)
    {
        _agent = chatClient.AsAIAgent(instructions: Instructions, name: "PolicyLens");
    }

    public async Task<string> AnswerAsync(
        string question, IReadOnlyList<RetrievedChunk> chunks, CancellationToken cancellationToken = default)
    {
        var excerpts = string.Join(
            "\n\n",
            chunks.Select(c => $"[Section {c.SectionNumber}: {c.SectionTitle}]\n{c.Text}"));

        var prompt = $"""
            Policy excerpts:
            {excerpts}

            Question: {question}
            """;

        var response = await _agent.RunAsync(prompt, cancellationToken: cancellationToken);
        return response.Text;
    }
}
