namespace Ballware.Llm.Context;

public interface IModelTool
{
    string Name { get; }
    string Description { get; }
    
}

public interface IModelToolProvider
{
    
}