using System.Configuration;
using Ballware.Llm.Docling.Client;
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

        if ("markdown".Equals(options.OutputFormat))
        {
            services.AddScoped<IKnowledgeIngestor, DoclingMarkdownIngestor>();
        } 
        else if ("json".Equals(options.OutputFormat))
        {
            services.AddScoped<IKnowledgeIngestor, DoclingJsonIngestor>();    
        }
        else
        {
            throw new ConfigurationException($"Unsupported output format: {options.OutputFormat}");
        }
        
        return services;
    }
}
