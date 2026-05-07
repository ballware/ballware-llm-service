namespace Ballware.Llm.Tools;

public interface IToolProvider
{
    string Namespace { get; }
    IEnumerable<Tool> GetToolsForUser(Guid tenantId, Guid userId, IDictionary<string, object> claims);
}