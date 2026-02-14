using OllamaSharp.Models.Chat;
using OllamaSharp.Tools;

namespace Ballware.Llm.Engine.Ollama.Internal;

sealed class OllamaCallbackTool : Tool, IAsyncInvokableTool
{
    private readonly Func<IDictionary<string, object?>?, Task<object?>> _invoke;

    public OllamaCallbackTool(
        string name,
        string description,
        Parameters parameters,
        Func<IDictionary<string, object?>?, Task<object?>> invoke)
    {
        Type = "function";
        Function = new Function
        {
            Name = name,
            Description = description,
            Parameters = parameters
        };

        _invoke = invoke;
    }

    public Task<object?> InvokeMethodAsync(IDictionary<string, object?>? args)
        => _invoke(args);
}