namespace Ballware.Llm.Ollama;

public interface IOllamaModelBridge
{
    IOllamaChat StartChat();
}