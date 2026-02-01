using Ballware.Llm.Ingestion.Docling.Configuration;
using Ballware.Llm.Ingestion.Docling.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Llm.Ingestion.Docling;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareIngestionForDocling(this IServiceCollection services, DoclingOptions options)
    {
        services.AddSingleton(options);
        services.AddScoped<IDoclingClient, DoclingHttpApiClient>();
        services.AddScoped<IKnowledgeIngestor, DoclingJsonIngestor>();
        
        return services;
    }
}