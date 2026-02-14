namespace Ballware.Llm.Engine.Ollama;

public class OllamaToolParameter(string name, string type, string description)
{
    public string Name { get; } = name;
    public string Type { get; } = type;
    public string Description { get; } = description;
}

public class OllamaTool(string name, string description, IEnumerable<OllamaToolParameter> parameters, Func<IDictionary<string, object?>, Task<string>> executor)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public IEnumerable<OllamaToolParameter> Parameters { get; } = parameters;
    public Func<IDictionary<string, object?>, Task<string>> Executor { get; } = executor;
}