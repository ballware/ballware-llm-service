using Ballware.Shared.Data.Repository;
using Ballware.Llm.Data.Ef.Configuration;
using Ballware.Llm.Data.Ef.Model;
using Ballware.Llm.Data.Ef.Postgres.Internal;
using Ballware.Llm.Data.Ef.Postgres.Repository;
using Ballware.Llm.Data.Ef.Repository;
using Ballware.Llm.Data.Persistables;
using Ballware.Llm.Data.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Data.Ef.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmStorageForPostgres(this IServiceCollection services, LlmStorageOptions options, string connectionString)
    {
        services.AddSingleton(options);
        services.AddDbContext<LlmDbContext>(o =>
        {
            o.UseNpgsql(connectionString, o =>
            {
                o.UseVector();
                o.MigrationsAssembly(typeof(LlmDbContext).Assembly.FullName);
            });

            o.ReplaceService<IModelCustomizer, LlmModelBaseCustomizer<Context, Origin, Embedding, Chunk, Image>>();
        });

        services.AddScoped<ILlmDbContext, LlmDbContext>();
        
        services.AddScoped<ITenantableRepository<Public.Context>, ContextBaseRepository>();
        services.AddScoped<IContextRepository, ContextBaseRepository>();
        
        services.AddScoped<ITenantableRepository<Public.Origin>, OriginBaseRepository>();
        services.AddScoped<IOriginRepository, OriginBaseRepository>();

        services.AddScoped<ITenantableRepository<Public.Embedding>, EmbeddingBaseRepository>();
        services.AddScoped<IEmbeddingRepository, EmbeddingBaseRepository>();
        
        services.AddScoped<ITenantableRepository<Public.Chunk>, ChunkRepository>();
        services.AddScoped<IChunkRepository, ChunkRepository>();

        services.AddScoped<ITenantableRepository<Public.Image>, ImageRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        
        services.AddHostedService<InitializationWorker>();
        
        return services;
    }
}
