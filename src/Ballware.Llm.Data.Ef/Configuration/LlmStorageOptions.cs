using System.ComponentModel.DataAnnotations;

namespace Ballware.Llm.Data.Ef.Configuration;

public sealed class LlmStorageOptions
{
    [Required]
    public required string Provider { get; set; }
    
    public bool AutoMigrations { get; set; } = false;
}