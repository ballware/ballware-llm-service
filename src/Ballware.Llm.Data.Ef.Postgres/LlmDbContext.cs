using Microsoft.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Postgres;

public class LlmDbContext : DbContext, ILlmDbContext
{
    public LlmDbContext(DbContextOptions<LlmDbContext> options) : base(options) {}
}