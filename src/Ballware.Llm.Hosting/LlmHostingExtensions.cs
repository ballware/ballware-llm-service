using Ballware.Llm.Context;
using Ballware.Llm.Context.Configuration;
using Ballware.Llm.Data.Ef;
using Ballware.Llm.Data.Ef.Configuration;
using Ballware.Llm.Data.Ef.Postgres;
using Ballware.Llm.Data.Repository;
using Ballware.Llm.Embedding;
using Ballware.Llm.Embedding.Configuration;
using Ballware.Llm.Embedding.OpenAi;
using Ballware.Llm.Embedding.OpenAi.Configuration;
using Ballware.Llm.Embedding.SemanticKernel;
using Ballware.Llm.Hosting.Adapter;
using Ballware.Llm.Hosting.Configuration;
using Ballware.Llm.Hosting.Extensions;
using Ballware.Llm.Hosting.Mappings;
using Ballware.Llm.Inference.OpenAi;
using Ballware.Llm.Inference.OpenAi.Configuration;
using Ballware.Llm.Metadata;
using Ballware.Llm.Tools.Common;
using Ballware.Llm.Tools.Embedding;
using Ballware.Meta.Service.Client;
using Ballware.Shared.Api;
using Ballware.Shared.Authorization;
using Ballware.Shared.Authorization.Jint;
using Ballware.Shared.Data.Repository;
using Ballware.Storage.Service.Client;
using Duende.AccessTokenManagement;
using Mapster;
using MapsterMapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Scope = Duende.AccessTokenManagement.Scope;
using Uri = System.Uri;

namespace Ballware.Llm.Hosting;

public static class LlmHostingExtensions
{
    public static IServiceCollection AddBallwareLlmCoreConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var llmStorageOptions = configuration.GetSection("Llm").Get<LlmStorageOptions>();
        var llmStorageConnectionString = configuration.GetConnectionString("LlmStorageConnection");
        var inferenceOptions = configuration.GetSection("Inference").Get<OpenAiInferenceOptions>();
        var embeddingOptions = configuration.GetSection("Embedding").Get<OpenAiEmbeddingOptions>();
        var metaClientOptions = configuration.GetSection("MetaClient").Get<ServiceClientOptions>();
        var storageClientOptions = configuration.GetSection("StorageClient").Get<ServiceClientOptions>();
        var corsOptions = configuration.GetSection("Cors").Get<CorsOptions>();

        if (llmStorageOptions == null || string.IsNullOrEmpty(llmStorageConnectionString))
        {
            throw new ConfigurationException("Required configuration for llm storage is missing");
        }

        if (inferenceOptions == null)
        {
            throw new ConfigurationException("Required configuration for inference is missing");
        }

        if (embeddingOptions == null)
        {
            throw new ConfigurationException("Required configuration for embedding is missing");
        }

        if (metaClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for metaClient is missing");
        }

        if (storageClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for storageClient is missing");
        }

        services.AddOptionsWithValidateOnStart<LlmStorageOptions>()
            .Bind(configuration.GetSection("Llm"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ContextOptions>()
            .Bind(configuration.GetSection("Context"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ChunkingOptions>()
            .Bind(configuration.GetSection("Chunking"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<OpenAiInferenceOptions>()
            .Bind(configuration.GetSection("Inference"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<OpenAiEmbeddingOptions>()
            .Bind(configuration.GetSection("Embedding"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<McpOptions>()
            .Bind(configuration.GetSection("Mcp"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ServiceCredentialOptions>()
            .Bind(configuration.GetSection("ServiceCredentials"))
            .ValidateDataAnnotations();

        services.AddOptionsWithValidateOnStart<ServiceClientOptions>()
            .Bind(configuration.GetSection("MetaClient"))
            .ValidateDataAnnotations();

        services.AddMemoryCache();
        services.AddDistributedMemoryCache();

        services.AddHttpContextAccessor();

        if (corsOptions != null)
        {
            services.AddCors(options =>
            {
                options.AddDefaultPolicy(c =>
                {
                    c.WithOrigins(corsOptions.AllowedOrigins.Split(','))
                        .WithMethods(corsOptions.AllowedMethods.Split(','))
                        .WithHeaders(corsOptions.AllowedHeaders.Split(','))
                        .AllowCredentials();
                });
            });
        }

        return services;
    }

    public static IServiceCollection AddBallwareLlmClients(this IServiceCollection services, IConfiguration configuration)
    {
        var serviceCredentialOptions = configuration.GetSection("ServiceCredentials").Get<ServiceCredentialOptions>();
        var mcpAgentOptions = configuration.GetSection("Mcp").Get<McpOptions>();
        var storageClientOptions = configuration.GetSection("StorageClient").Get<ServiceClientOptions>();
        var metaClientOptions = configuration.GetSection("MetaClient").Get<ServiceClientOptions>();

        if (storageClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for storageClient is missing");
        }

        if (metaClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for metaClient is missing");
        }

        serviceCredentialOptions ??= new ServiceCredentialOptions();
        mcpAgentOptions ??= new McpOptions();

        var clientCredentialsTokenManagementBuilder = services.AddClientCredentialsTokenManagement();

        foreach (var serviceCredential in serviceCredentialOptions.Credentials)
        {
            clientCredentialsTokenManagementBuilder
                .AddClient(serviceCredential.Identifier, client =>
                {
                    client.TokenEndpoint = new Uri(serviceCredential.TokenEndpoint);

                    client.ClientId = ClientId.Parse(serviceCredential.ClientId);
                    client.ClientSecret = ClientSecret.Parse(serviceCredential.ClientSecret);

                    client.Scope = Scope.Parse(serviceCredential.Scopes);
                });
        }

        foreach (var mcpClientOptions in mcpAgentOptions.Agents)
        {
            var httpClientBuilder = services.AddHttpClient($"mcp.{mcpClientOptions.Namespace}", client =>
                    {
                        client.BaseAddress = new Uri(mcpClientOptions.ServiceUrl);
                    })
#if DEBUG
                    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
                    {
                        ServerCertificateCustomValidationCallback =
                            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    })
#endif
                ;

            if (!mcpClientOptions.UserTokenDelegation && !string.IsNullOrEmpty(mcpClientOptions.CredentialIdentifier))
            {
                httpClientBuilder.AddClientCredentialsTokenHandler(
                    ClientCredentialsClientName.Parse(mcpClientOptions.CredentialIdentifier));
            }
        }

        var storageClientBuilder = services.AddHttpClient<StorageServiceClient>(client =>
            {
                client.BaseAddress = new Uri(storageClientOptions.ServiceUrl);
            })
#if DEBUG
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
#endif
            ;

        if (!string.IsNullOrEmpty(storageClientOptions.CredentialIdentifier))
        {
            storageClientBuilder.AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse(storageClientOptions.CredentialIdentifier));
        }

        var metaClientBuilder = services.AddHttpClient<MetaServiceClient>(client =>
            {
                client.BaseAddress = new Uri(metaClientOptions.ServiceUrl);
            })
#if DEBUG
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            })
#endif
            ;

        if (!string.IsNullOrEmpty(metaClientOptions.CredentialIdentifier))
        {
            metaClientBuilder.AddClientCredentialsTokenHandler(ClientCredentialsClientName.Parse(metaClientOptions.CredentialIdentifier));
        }

        return services;
    }

    public static IServiceCollection AddBallwareLlmDataStore(this IServiceCollection services, IConfiguration configuration)
    {
        var llmStorageOptions = configuration.GetSection("Llm").Get<LlmStorageOptions>();
        var llmStorageConnectionString = configuration.GetConnectionString("LlmStorageConnection");

        if (llmStorageOptions == null || string.IsNullOrEmpty(llmStorageConnectionString))
        {
            throw new ConfigurationException("Required configuration for llm storage is missing");
        }

        if ("postgres".Equals(llmStorageOptions.Provider, StringComparison.InvariantCultureIgnoreCase))
        {
            services.AddSingleton(configuration.GetSection("Chat").Get<ChatSessionStoreOptions>() ?? new ChatSessionStoreOptions());

            services.AddBallwareLlmStorageForPostgres(llmStorageOptions, llmStorageConnectionString);
        }

        return services;
    }

    public static IServiceCollection AddBallwareLlmSharedAuthorizationUtils(this IServiceCollection services, IConfiguration configuration, string sectionName = "Authorization")
    {
        var claimsOptions = configuration.GetSection(sectionName).Get<LlmAuthorizationClaimsOptions>() ?? new LlmAuthorizationClaimsOptions();

        return services.AddBallwareLlmSharedAuthorizationUtils(claimsOptions.TenantClaim, claimsOptions.UserIdClaim, claimsOptions.RightClaim);
    }

    public static IServiceCollection AddBallwareLlmSharedAuthorizationUtils(this IServiceCollection services, string tenantClaim, string userIdClaim, string rightClaim)
    {
        services.AddSingleton(new LlmAuthorizationClaimsOptions
        {
            TenantClaim = tenantClaim,
            UserIdClaim = userIdClaim,
            RightClaim = rightClaim
        });

        services.AddBallwareSharedJintRightsChecker();

        services.AddBallwareSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim);

        return services;
    }

    public static IServiceCollection AddBallwareLlmMetadataAndStorageAdapters(this IServiceCollection services)
    {
        var mapsterConfig = new TypeAdapterConfig()
            .AddBallwareLlmStorageMappings();

        new MetaServiceLlmMetadataProfile().Register(mapsterConfig);

        services.AddSingleton(mapsterConfig);
        services.AddScoped<IMapper, ServiceMapper>();

        services.AddScoped<IAuthorizationMetadataProvider, MetaServiceMetadataAdapter>();
        services.AddScoped<IExportMetadataProvider, MetaServiceMetadataAdapter>();
        services.AddScoped<IFileStorageProvider, StorageServiceFileStorageAdapter>();

        services.AddScoped<ITenantableRepositoryHook<Data.Public.Origin, Data.Persistables.Origin>, OriginPayloadRepositoryHook>();
        services.AddScoped<ITenantableRepositoryHook<Data.Public.Image, Data.Persistables.Image>, ImagePayloadRepositoryHook>();

        services.AddScoped<IChatSessionActorResolver, MetaServiceChatSessionActorResolver>();

        return services;
    }

    public static IServiceCollection AddBallwareLlmTools(this IServiceCollection services)
    {
        services.AddBallwareLlmCommonTools();
        services.AddBallwareLlmEmbeddingTools();

        return services;
    }

    public static IServiceCollection AddBallwareLlmContextAndInference(this IServiceCollection services, IConfiguration configuration)
    {
        var contextOptions = configuration.GetSection("Context").Get<ContextOptions>();
        var chunkingOptions = configuration.GetSection("Chunking").Get<ChunkingOptions>();
        var inferenceOptions = configuration.GetSection("Inference").Get<OpenAiInferenceOptions>();
        var embeddingOptions = configuration.GetSection("Embedding").Get<OpenAiEmbeddingOptions>();
        var mcpAgentOptions = configuration.GetSection("Mcp").Get<McpOptions>();

        if (inferenceOptions == null)
        {
            throw new ConfigurationException("Required configuration for inference is missing");
        }

        if (embeddingOptions == null)
        {
            throw new ConfigurationException("Required configuration for embedding is missing");
        }

        contextOptions ??= new ContextOptions();
        chunkingOptions ??= new ChunkingOptions();
        mcpAgentOptions ??= new McpOptions();

        services.AddBallwareLlmContext(contextOptions);
        services.AddBallwareEmbeddingOpenAi(embeddingOptions);

        services.AddBallwareInferenceEngineForOpenAi(inferenceOptions, (serviceProvider, toolRegistry) =>
        {
            toolRegistry.RegisterBallwareLlmCommonTools(serviceProvider);
            toolRegistry.RegisterBallwareLlmToolsForEmbeddings();

            foreach (var mcpClientOptions in mcpAgentOptions.Agents)
            {
                toolRegistry.RegisterMcpService(new McpService($"mcp.{mcpClientOptions.Namespace}", mcpClientOptions.UserTokenDelegation));
            }
        });

        services.AddBallwareSemanticKernelChunker();
        services.AddBallwareEmbeddingChunker(chunkingOptions, (chunkerRegistry) =>
        {
            chunkerRegistry.RegisterSemanticKernelChunker();
        });

        return services;
    }

}
