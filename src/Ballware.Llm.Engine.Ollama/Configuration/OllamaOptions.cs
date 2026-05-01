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

    [Range(0, 128)]
    public int MaxToolsPerRequest { get; set; } = 10;

    [Range(0, 128)]
    public int MinToolsPerRequest { get; set; } = 3;
}
