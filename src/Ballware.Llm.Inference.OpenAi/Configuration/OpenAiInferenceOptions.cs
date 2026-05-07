using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Inference.OpenAi.Configuration;

public class OpenAiInferenceOptions
{
    public string? Model { get; set; }
    
    [Required]
    public required string ApiKey { get; set; }
    
    [Required]
    public required string Endpoint { get; set; }   
}