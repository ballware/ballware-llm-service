using Ballware.Llm.Api;
using Ballware.Llm.Api.Endpoints;
using Ballware.Llm.Context;
using Ballware.Llm.Context.Configuration;
using Ballware.Llm.Data.Ef;
using Ballware.Llm.Data.Ef.Configuration;
using Ballware.Llm.Data.Ef.Postgres;
using Ballware.Llm.Docling.Client;
using Ballware.Llm.Embedding;
using Ballware.Llm.Embedding.Configuration;
using Ballware.Llm.Engine.Commons;
using Ballware.Llm.Ingestion.Docling;
using Ballware.Llm.Ingestion.Docling.Configuration;
using Ballware.Llm.Jobs;
using Ballware.Llm.Engine.Ollama;
using Ballware.Llm.Engine.Ollama.Configuration;
using Ballware.Llm.Service.Adapter;
using Ballware.Llm.Service.Configuration;
using Ballware.Llm.Service.Extensions;
using Ballware.Llm.Service.Mappings;
using Ballware.Meta.Service.Client;
using Ballware.Shared.Api;
using Ballware.Storage.Service.Client;
using Ballware.Shared.Api.Endpoints;
using Ballware.Shared.Authorization;
using Ballware.Shared.Authorization.Jint;
using Ballware.Shared.Data.Repository;
using Duende.AccessTokenManagement;
using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.OpenApi;
using Serilog;
using Quartz;
using Scope = Duende.AccessTokenManagement.Scope;
using Uri = System.Uri;

namespace Ballware.Llm.Service;

public class Startup(IWebHostEnvironment environment, ConfigurationManager configuration, IServiceCollection services)
{
    private string ClaimTypeScope { get; }= "scope";
    
    private IWebHostEnvironment Environment { get; } = environment;
    private ConfigurationManager Configuration { get; } = configuration;
    private IServiceCollection Services { get; } = services;

    public void InitializeServices()
    {
        CorsOptions? corsOptions = Configuration.GetSection("Cors").Get<CorsOptions>();
        AuthorizationOptions? authorizationOptions =
            Configuration.GetSection("Authorization").Get<AuthorizationOptions>();
        SwaggerOptions? swaggerOptions = Configuration.GetSection("Swagger").Get<SwaggerOptions>();
        LlmStorageOptions? llmStorageOptions = Configuration.GetSection("Llm").Get<LlmStorageOptions>();
        ContextOptions? contextOptions = Configuration.GetSection("Context").Get<ContextOptions>();
        EmbeddingOptions? embeddingOptions = Configuration.GetSection("Embedding").Get<EmbeddingOptions>();
        OllamaOptions? ollamaOptions = Configuration.GetSection("Ollama").Get<OllamaOptions>();
        DoclingOptions? doclingOptions = Configuration.GetSection("Docling").Get<DoclingOptions>();
        McpOptions? mcpAgentOptions = Configuration.GetSection("Mcp").Get<McpOptions>();
        ServiceCredentialOptions? serviceCredentialOptions = Configuration.GetSection("ServiceCredentials").Get<ServiceCredentialOptions>(); 
        ServiceClientOptions? storageClientOptions = Configuration.GetSection("StorageClient").Get<ServiceClientOptions>();
        ServiceClientOptions? metaClientOptions = Configuration.GetSection("MetaClient").Get<ServiceClientOptions>();
        
        var llmStorageConnectionString = Configuration.GetConnectionString("LlmStorageConnection");
        
        Services.AddOptionsWithValidateOnStart<AuthorizationOptions>()
            .Bind(Configuration.GetSection("Authorization"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<SwaggerOptions>()
            .Bind(Configuration.GetSection("Swagger"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<LlmStorageOptions>()
            .Bind(Configuration.GetSection("Llm"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<ContextOptions>()
            .Bind(Configuration.GetSection("Context"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<EmbeddingOptions>()
            .Bind(Configuration.GetSection("Embedding"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<OllamaOptions>()
            .Bind(Configuration.GetSection("Ollama"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<DoclingOptions>()
            .Bind(Configuration.GetSection("Docling"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<McpOptions>()
            .Bind(Configuration.GetSection("Mcp"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<ServiceCredentialOptions>()
            .Bind(Configuration.GetSection("ServiceCredentials"))
            .ValidateDataAnnotations();
       
        Services.AddOptionsWithValidateOnStart<ServiceClientOptions>()
            .Bind(Configuration.GetSection("MetaClient"))
            .ValidateDataAnnotations();
        
        if (authorizationOptions == null)
        {
            throw new ConfigurationException("Required configuration for authorization is missing");
        }

        if (llmStorageOptions == null || string.IsNullOrEmpty(llmStorageConnectionString))
        {
            throw new ConfigurationException("Required configuration for llm storage is missing");
        }
        
        if (contextOptions == null)
        {
            contextOptions = new ContextOptions();
        }

        if (embeddingOptions == null)
        {
            embeddingOptions = new EmbeddingOptions();
        }
        
        if (ollamaOptions == null)
        {
            throw new ConfigurationException("Required configuration for ollama is missing");
        }

        if (doclingOptions == null)
        {
            throw new ConfigurationException("Required configuration for docling is missing");
        }

        if (mcpAgentOptions == null)
        {
            mcpAgentOptions = new McpOptions();
        }

        if (serviceCredentialOptions == null)
        {
            serviceCredentialOptions = new ServiceCredentialOptions();       
        }
        
        if (metaClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for metaClient is missing");
        }

        if (storageClientOptions == null)
        {
            throw new ConfigurationException("Required configuration for storageClient is missing");       
        }
            
        
        Services.AddMemoryCache();
        Services.AddDistributedMemoryCache();
        
        Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.Authority = authorizationOptions.Authority;
            options.Audience = authorizationOptions.Audience;
            options.RequireHttpsMetadata = authorizationOptions.RequireHttpsMetadata;
            options.SaveToken = true;
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
            {
                ValidIssuer = authorizationOptions.Issuer ?? authorizationOptions.Authority
            };
            
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];

                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) &&
                        path.StartsWithSegments("/ai/hubs"))
                    {
                        context.Token = accessToken;
                    }

                    return Task.CompletedTask;
                }
            };
        });

        Services.AddAuthorizationBuilder()
            .AddPolicy("mlApi", policy => policy.RequireAssertion(context =>
                context.User
                    .Claims
                    .Where(c => ClaimTypeScope == c.Type)
                    .SelectMany(c => c.Value.Split(' '))
                    .Any(s => s.Equals(authorizationOptions.RequiredMetaScope, StringComparison.Ordinal)))
            )
            .AddPolicy("serviceApi", policy => policy.RequireAssertion(context =>
                context.User
                    .Claims
                    .Where(c => ClaimTypeScope == c.Type)
                    .SelectMany(c => c.Value.Split(' '))
                    .Any(s => s.Equals(authorizationOptions.RequiredServiceScope, StringComparison.Ordinal)))
            );

        if (corsOptions != null)
        {
            Services.AddCors(options =>
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
        
        Services.Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = null;
        });
        
        Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

        var clientCredentialsTokenManagementBuilder = Services.AddClientCredentialsTokenManagement();
        
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
            var httpClientBuilder = Services.AddHttpClient($"mcp.{mcpClientOptions.Namespace}", client =>
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
        };
        
        var storageClientBuilder = Services.AddHttpClient<StorageServiceClient>(client =>
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
        
        var metaClientBuilder = Services.AddHttpClient<MetaServiceClient>(client =>
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
        
        Services.AddHttpClient<DoclingApiClient>(client =>
        {
            client.BaseAddress = new Uri(doclingOptions.Url);
            client.Timeout = TimeSpan.FromSeconds(doclingOptions.RequestTimeoutSeconds);
        });

        var mapsterConfig = new TypeAdapterConfig()
            .AddBallwareLlmStorageMappings();
        
        new MetaServiceLlmMetadataProfile().Register(mapsterConfig);

        Services.AddSingleton(mapsterConfig);
        Services.AddScoped<IMapper, ServiceMapper>();
        
        Services.AddHttpContextAccessor();

        Services.AddMvcCore()
            .AddApiExplorer();

        Services.AddSignalR();
        Services.AddControllers();
        
        Services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));
        Services.AddBallwareLlmBackgroundJobs();
        
        Services.AddBallwareSharedJintRightsChecker();
        Services.AddBallwareSharedApiDependencies();
        
        Services.AddBallwareSharedAuthorizationUtils(authorizationOptions.TenantClaim, authorizationOptions.UserIdClaim, authorizationOptions.RightClaim);
        
        if ("postgres".Equals(llmStorageOptions.Provider, StringComparison.InvariantCultureIgnoreCase) &&
            !string.IsNullOrEmpty(llmStorageConnectionString))
        {
            Services.AddBallwareLlmStorageForPostgres(llmStorageOptions, llmStorageConnectionString);
        }
        
        Services.AddScoped<IAuthorizationMetadataProvider, MetaServiceMetadataAdapter>();
        Services.AddScoped<IJobMetadataProvider, MetaServiceMetadataAdapter>();
        Services.AddScoped<IExportMetadataProvider, MetaServiceMetadataAdapter>();
        Services.AddScoped<IFileStorageProvider, StorageServiceFileStorageAdapter>();
        Services.AddScoped<IJobFileStorageAdapter, StorageServiceFileStorageAdapter>();
        Services.AddScoped<ITenantableRepositoryHook<Data.Public.Origin, Data.Persistables.Origin>, OriginPayloadRepositoryHook>();
        Services.AddScoped<ITenantableRepositoryHook<Data.Public.Image, Data.Persistables.Image>, ImagePayloadRepositoryHook>();

        Services.AddBallwareLlmApiDependencies();
        Services.AddBallwareLlmContext(contextOptions);
        Services.AddBallwareLlmEngineCommons();
        Services.AddBallwareOllamaEngine(ollamaOptions, (serviceProvider, toolRegistry) =>
        {
            toolRegistry.RegisterBallwareLlmCommonTools(serviceProvider);
            
            foreach (var mcpClientOptions in mcpAgentOptions.Agents)
            {
                toolRegistry.RegisterMcpService(new McpService($"mcp.{mcpClientOptions.Namespace}", mcpClientOptions.UserTokenDelegation));
            }
        });
        
        Services.AddBallwareIngestionForDocling(doclingOptions);
        Services.AddBallwareEmbeddingChunker(embeddingOptions);
        
        Services.AddEndpointsApiExplorer();
        
        if (swaggerOptions != null)
        {
            Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("llm", new OpenApiInfo
                {
                    Title = "ballware LLM user API",
                    Version = "v1"
                });
                
                c.SwaggerDoc("service", new OpenApiInfo
                {
                    Title = "ballware LLM service API",
                    Version = "v1"
                });
                
                c.EnableAnnotations();

                c.AddSecurityDefinition("oidc", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OpenIdConnect,
                    OpenIdConnectUrl = new Uri(authorizationOptions.Authority + "/.well-known/openid-configuration")
                });

                c.AddSecurityRequirement((document) => new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecuritySchemeReference("oidc", document),
                        swaggerOptions.RequiredScopes.Split(" ").ToList()
                    }
                });
            });
        }
    }

    public void InitializeApp(WebApplication app)
    {
        if (Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            IdentityModelEventSource.ShowPII = true;
        }
        
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
                var exception = exceptionFeature?.Error;

                if (exception != null)
                {
                    Log.Error(exception, "Unhandled exception occurred");

                    var problemDetails = new ProblemDetails
                    {
                        Type = "https://httpstatuses.com/500",
                        Title = "An unexpected error occurred.",
                        Status = StatusCodes.Status500InternalServerError,
                        Detail = app.Environment.IsDevelopment() ? exception.ToString() : null,
                        Instance = context.Request.Path
                    };

                    context.Response.StatusCode = problemDetails.Status.Value;
                    context.Response.ContentType = "application/problem+json";
                    await context.Response.WriteAsJsonAsync(problemDetails);
                }
            });
        });

        app.UseCors();
        app.UseRouting();

        app.UseAuthorization();

        app.MapChatEndpoint("ai/hubs");
        app.MapContextUserApi("/ai/context");
        app.MapOriginUserApi("/ai/origin");
        app.MapEmbeddingUserApi("/ai/embedding");
        
        app.MapTenantableEditingApi<Data.Public.Context>("/ai/context", "ai", "context", "Context", "Context", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Origin>("/ai/origin", "ai", "origin", "Origin", "Origin", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Embedding>("/ai/embedding", "ai", "embedding", "Embedding", "Embedding", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Chunk>("/ai/chunk", "ai", "chunk", "Chunk", "Chunk", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Image>("/ai/image", "ai", "image", "Image", "Image", "mlApi", "llm");
        
        var authorizationOptions = app.Services.GetService<IOptions<AuthorizationOptions>>()?.Value;
        var swaggerOptions = app.Services.GetService<IOptions<SwaggerOptions>>()?.Value;

        if (swaggerOptions != null && authorizationOptions != null)
        {

            app.UseSwagger();

            if (swaggerOptions.EnableClient)
            {
                app.UseSwaggerUI(c =>
                {
                    c.SwaggerEndpoint("llm/swagger.json", "ballware LLM user API");
                    c.SwaggerEndpoint("service/swagger.json", "ballware LLM service API");

                    c.OAuthClientId(swaggerOptions.ClientId);
                    c.OAuthClientSecret(swaggerOptions.ClientSecret);
                    c.OAuthScopes(swaggerOptions.RequiredScopes?.Split(" "));
                    c.OAuthUsePkce();
                });
            }
        }
    }
}
