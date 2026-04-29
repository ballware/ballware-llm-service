using Ballware.Llm.Data.Persistables;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ballware.Llm.Data.Ef.Model;

public class LlmModelBaseCustomizer<TContext, TOrigin, TEmbedding, TChunk, TImage> 
    : RelationalModelCustomizer 
    where TContext : Context
    where TOrigin : Origin 
    where TEmbedding : Embedding 
    where TChunk : Chunk
    where TImage : Image
{
    public LlmModelBaseCustomizer(ModelCustomizerDependencies dependencies) 
        : base(dependencies)
    {
    }
    
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(new ValueConverter<DateTime, DateTime>(
                        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
                        v => DateTime.SpecifyKind(v, DateTimeKind.Utc)));
                }

                if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(new ValueConverter<DateTime?, DateTime?>(
                        v => v.HasValue ? v.Value.ToUniversalTime() : v,
                        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v));
                }
            }
        }
        
        modelBuilder.Entity<TContext>().HasKey(d => d.Id);
        modelBuilder.Entity<TContext>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
        modelBuilder.Entity<TContext>().HasIndex(d => d.TenantId);
        modelBuilder.Entity<TContext>().HasIndex(d => new { d.TenantId, d.Identifier }).IsUnique();
        
        modelBuilder.Entity<TOrigin>().HasKey(d => d.Id);
        modelBuilder.Entity<TOrigin>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
        modelBuilder.Entity<TOrigin>().HasIndex(d => d.TenantId);
        modelBuilder.Entity<TOrigin>().HasIndex(d => new { d.TenantId, d.OwnerEntity, d.OwnerId, d.Name }).IsUnique();
        
        modelBuilder.Entity<TEmbedding>().HasKey(d => d.Id);
        modelBuilder.Entity<TEmbedding>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
        modelBuilder.Entity<TEmbedding>().HasIndex(d => d.TenantId);
        
        modelBuilder.Entity<TChunk>().HasKey(d => d.Id);
        modelBuilder.Entity<TChunk>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
        modelBuilder.Entity<TChunk>().HasIndex(d => d.TenantId);

        modelBuilder.Entity<TImage>().HasKey(d => d.Id);
        modelBuilder.Entity<TImage>().HasIndex(d => new { d.TenantId, d.Uuid }).IsUnique();
        modelBuilder.Entity<TImage>().HasIndex(d => d.TenantId);
        modelBuilder.Entity<TImage>().HasIndex(d => new { d.TenantId, d.OriginId });
    }
}
