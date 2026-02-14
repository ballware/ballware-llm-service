using System.ComponentModel.DataAnnotations;
using Ballware.Llm.Engine.Agent;

namespace Ballware.Llm.Engine.Ollama.Configuration;

public class OllamaOptions
{
    [Required]
    public required string Url { get; set; }
    
    [Required]
    public required string InferenceModel { get; set; }
    
    [Required]
    public required string EmbeddingModel  { get; set; }
}