using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Embedding.OpenAi.Configuration;

public class OpenAiEmbeddingOptions
{
    public string? Model { get; set; }
    
    [Required]
    public required string ApiKey { get; set; }
    
    [Required]
    public required string Endpoint { get; set; }   
}