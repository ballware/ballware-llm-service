namespace Ballware.Llm.Engine.Agent;

public class ToolParameter(string name, string type, string description)
{
    public string Name { get; } = name;
    public string Type { get; } = type;
    public string Description { get; } = description;
}

public class Tool(string name, string description, IEnumerable<ToolParameter> parameters, Func<IDictionary<string, object?>, Task<string>> executor)
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public IEnumerable<ToolParameter> Parameters { get; } = parameters;
    public Func<IDictionary<string, object?>, Task<string>> Executor { get; } = executor;
}