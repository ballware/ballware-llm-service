using Ballware.Llm.Data.Persistables;
using Microsoft.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Postgres;

public class LlmDbContext : DbContext, ILlmDbContext
{
    public LlmDbContext(DbContextOptions<LlmDbContext> options) : base(options) {}
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
    }
    
    public DbSet<Context> Contexts { get; set; }
    public DbSet<Origin>  Origins { get; set; }
    public DbSet<Embedding>  Embeddings { get; set; }
    public DbSet<Chunk>  Chunks { get; set; }
    public DbSet<Image> Images { get; set; }
    public DbSet<ChatSession> ChatSessions { get; set; }
    public DbSet<ChatMessage> ChatMessages { get; set; }
}
