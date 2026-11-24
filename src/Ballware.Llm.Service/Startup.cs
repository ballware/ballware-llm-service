using Ballware.Llm.Api;
using Ballware.Llm.Api.Endpoints;
using Ballware.Llm.Data;
using Ballware.Llm.Docling.Client;
using Ballware.Llm.Hosting;
using Ballware.Llm.Hosting.Adapter;
using Ballware.Llm.Hosting.Configuration;
using Ballware.Llm.Ingestion.Docling;
using Ballware.Llm.Ingestion.Docling.Configuration;
using Ballware.Llm.Jobs;
using Ballware.Llm.Service.Adapter;
using Ballware.Llm.Service.Configuration;
using Ballware.Shared.Api;
using Ballware.Shared.Api.Endpoints;
using Microsoft.AspNetCore.Authentication;
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
using Uri = System.Uri;

namespace Ballware.Llm.Service;

public class Startup(IWebHostEnvironment environment, ConfigurationManager configuration, IServiceCollection services)
{
    private string ClaimTypeScope { get; } = "scope";

    private IWebHostEnvironment Environment { get; } = environment;
    private ConfigurationManager Configuration { get; } = configuration;
    private IServiceCollection Services { get; } = services;

    public void InitializeServices()
    {
        AuthorizationOptions? authorizationOptions = Configuration.GetSection("Authorization").Get<AuthorizationOptions>();
        SwaggerOptions? swaggerOptions = Configuration.GetSection("Swagger").Get<SwaggerOptions>();
        DoclingOptions? doclingOptions = Configuration.GetSection("Docling").Get<DoclingOptions>();

        Services.AddOptionsWithValidateOnStart<AuthorizationOptions>()
            .Bind(Configuration.GetSection("Authorization"))
            .ValidateDataAnnotations();

        Services.AddOptionsWithValidateOnStart<SwaggerOptions>()
            .Bind(Configuration.GetSection("Swagger"))
            .ValidateDataAnnotations();

        Services.AddOptionsWithValidateOnStart<DoclingOptions>()
            .Bind(Configuration.GetSection("Docling"))
            .ValidateDataAnnotations();

        if (authorizationOptions == null)
        {
            throw new ConfigurationException("Required configuration for authorization is missing");
        }

        if (doclingOptions == null)
        {
            throw new ConfigurationException("Required configuration for docling is missing");
        }

        Services.AddBallwareLlmCoreConfiguration(Configuration);

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

        Services.Configure<JsonOptions>(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = null;
        });

        Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = null;
        });

        Services.AddBallwareLlmClients(Configuration);

        Services.AddHttpClient<DoclingApiClient>(client =>
        {
            client.BaseAddress = new Uri(doclingOptions.Url);
            client.Timeout = TimeSpan.FromSeconds(doclingOptions.RequestTimeoutSeconds);
        });

        Services.AddMvcCore()
            .AddApiExplorer();

        Services.AddSignalR();
        Services.AddControllers();

        Services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));
        Services.AddBallwareLlmBackgroundJobs();

        Services.AddBallwareSharedApiDependencies();
        Services.AddBallwareLlmSharedAuthorizationUtils(
            authorizationOptions.TenantClaim,
            authorizationOptions.UserIdClaim,
            authorizationOptions.RightClaim
        );

        Services.AddBallwareLlmDataStore(Configuration);

        Services.AddBallwareLlmMetadataAndStorageAdapters();
        Services.AddScoped<IJobMetadataProvider, MetaServiceMetadataAdapter>();
        Services.AddScoped<IJobFileStorageAdapter, StorageServiceJobFileStorageAdapter>();

        Services.AddBallwareLlmApiDependencies();
        Services.AddBallwareLlmTools();
        Services.AddBallwareLlmContextAndInference(Configuration);
        Services.AddBallwareIngestionForDocling(doclingOptions);

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
                    if (exception is ChatSessionNotVisibleException notVisible)
                    {
                        Log.Warning(notVisible, "Chat session is not visible for the acting user");

                        var notFound = new ProblemDetails
                        {
                            Type = "https://httpstatuses.com/404",
                            Title = "Not found.",
                            Status = StatusCodes.Status404NotFound,
                            Detail = app.Environment.IsDevelopment() ? notVisible.Message : null,
                            Instance = context.Request.Path
                        };

                        context.Response.StatusCode = notFound.Status.Value;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsJsonAsync(notFound);

                        return;
                    }

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

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapChatEndpoint("ai/hubs");
        app.MapOpenAiChatCompletionsEndpoint("/ai/v1");
        app.MapContextUserApi("/ai/context");
        app.MapOriginUserApi("/ai/origin");
        app.MapEmbeddingUserApi("/ai/embedding");

        app.MapTenantableEditingApi<Data.Public.Context>("/ai/context", "ai", "context", "Context", "Context", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Origin>("/ai/origin", "ai", "origin", "Origin", "Origin", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Embedding>("/ai/embedding", "ai", "embedding", "Embedding", "Embedding", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Chunk>("/ai/chunk", "ai", "chunk", "Chunk", "Chunk", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.Image>("/ai/image", "ai", "image", "Image", "Image", "mlApi", "llm");
        app.MapTenantableEditingApi<Data.Public.ChatSession>("/ai/session", "ai", "session", "Session", "Session", "mlApi", "llm");

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
