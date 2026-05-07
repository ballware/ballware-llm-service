using System.Globalization;

namespace Ballware.Llm.Tools.Common.Internal;

internal class MathToolProvider : IToolProvider
{
    private readonly List<Tool> _tools = [];

    public MathToolProvider()
    {
        _tools.Add(new Tool("add", "Adds two numbers", [
                new ToolParameter("a", "number", "The first number"),
                new ToolParameter("b", "number", "The second number")
            ],  args =>
            {
                if (args.TryGetValue("a", out var aObj) && args.TryGetValue("b", out var bObj) &&
                    Double.TryParse((string?)aObj, out double a) && Double.TryParse((string?)bObj, out double b))
                {
                    var result = (a + b).ToString(CultureInfo.InvariantCulture);
                    
                    return Task.FromResult(result);
                }

                return Task.FromResult("Invalid arguments");
            })
        );
        
        _tools.Add(new Tool("subtract", "Subtracts one number from the other", [
                new ToolParameter("a", "number", "The first number"),
                new ToolParameter("b", "number", "The second number")
            ],  args =>
            {
                if (args.TryGetValue("a", out var aObj) && args.TryGetValue("b", out var bObj) &&
                    Double.TryParse((string?)aObj, out double a) && Double.TryParse((string?)bObj, out double b))
                {
                    var result = (a - b).ToString(CultureInfo.InvariantCulture);
                    
                    return Task.FromResult(result);
                }

                return Task.FromResult("Invalid arguments");
            })
        );
    }

    public string Namespace => "math";

    public IEnumerable<Tool> GetToolsForUser(Guid tenantId, Guid userId, IDictionary<string, object> claims)
    {
        return _tools;
    }
}