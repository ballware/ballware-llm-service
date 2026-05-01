using System.Text;
using Microsoft.Extensions.AI;

namespace Ballware.Llm.Engine.Ollama.Internal;

internal static class ToolSelector
{
    public static IReadOnlyList<AITool> SelectTools(
        string prompt,
        IReadOnlyList<AITool> tools,
        int minToolsPerRequest,
        int maxToolsPerRequest)
    {
        if (tools.Count == 0)
        {
            return [];
        }

        if (maxToolsPerRequest <= 0)
        {
            return [];
        }

        var maxTools = Math.Max(1, maxToolsPerRequest);
        var minTools = Math.Clamp(minToolsPerRequest, 0, maxTools);

        if (tools.Count <= maxTools)
        {
            return tools.ToList();
        }

        var tokens = Tokenize(prompt).ToList();
        var message = $" {prompt.ToLowerInvariant()} ";

        var ranked = tools
            .Select((tool, index) => ScoreTool(tool, index, message, tokens))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Index)
            .ToList();

        var selected = ranked
            .Where(candidate => candidate.Score > 0)
            .Take(maxTools)
            .ToList();

        if (selected.Count < minTools)
        {
            selected = ranked.Take(minTools).ToList();
        }

        return selected
            .Take(maxTools)
            .Select(candidate => candidate.Tool)
            .ToList();
    }

    private static ToolCandidate ScoreTool(
        AITool tool,
        int index,
        string normalizedPrompt,
        IReadOnlyList<string> promptTokens)
    {
        var name = (TryReadPropertyAsString(tool, "Name") ?? string.Empty).ToLowerInvariant();
        var description = (TryReadPropertyAsString(tool, "Description") ?? string.Empty).ToLowerInvariant();
        var searchable = $" {name} {description} ";
        var score = 0;

        if (!string.IsNullOrEmpty(name) && normalizedPrompt.Contains($" {name} ", StringComparison.Ordinal))
        {
            score += 40;
        }

        foreach (var token in promptTokens)
        {
            if (token.Length < 3)
            {
                continue;
            }

            if (name.Equals(token, StringComparison.Ordinal))
            {
                score += 30;
                continue;
            }

            if (name.Contains(token, StringComparison.Ordinal))
            {
                score += 14;
            }

            if (description.Contains(token, StringComparison.Ordinal))
            {
                score += 5;
            }

            if (searchable.Contains($" {token} ", StringComparison.Ordinal))
            {
                score += 8;
            }
        }

        if (score == 0 && name.Contains('.', StringComparison.Ordinal))
        {
            var namespaceToken = name.Split('.')[0];
            if (normalizedPrompt.Contains($" {namespaceToken} ", StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        return new ToolCandidate(tool, index, score);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var lower = value.ToLowerInvariant();
        var current = new StringBuilder();
        var result = new List<string>();

        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-')
            {
                current.Append(ch);
            }
            else if (current.Length > 0)
            {
                result.Add(current.ToString());
                current.Clear();
            }
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? TryReadPropertyAsString(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName);
        var value = property?.GetValue(target);
        return value?.ToString();
    }

    private sealed record ToolCandidate(AITool Tool, int Index, int Score);
}
