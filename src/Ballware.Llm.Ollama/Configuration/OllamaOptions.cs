using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Ollama.Configuration;

public class OllamaOptions
{
    [Required]
    public required string Url { get; set; }
    
    [Required]
    public required string Model { get; set; }
}