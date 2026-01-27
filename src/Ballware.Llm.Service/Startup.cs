using Ballware.Llm.Service.Configuration;
using Ballware.Shared.Api.Endpoints;
using Ballware.Shared.Authorization;
using Ballware.Shared.Authorization.Jint;
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
using Microsoft.OpenApi.Models;
using Serilog;

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
        
        Services.AddOptionsWithValidateOnStart<AuthorizationOptions>()
            .Bind(Configuration.GetSection("Authorization"))
            .ValidateDataAnnotations();
        
        Services.AddOptionsWithValidateOnStart<SwaggerOptions>()
            .Bind(Configuration.GetSection("Swagger"))
            .ValidateDataAnnotations();
        
        if (authorizationOptions == null)
        {
            throw new ConfigurationException("Required configuration for authorization is missing");
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
            options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters()
            {
                ValidIssuer = authorizationOptions.Authority
            };
        });

        Services.AddAuthorizationBuilder()
            .AddPolicy("llmApi", policy => policy.RequireAssertion(context =>
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
                    c.WithOrigins(corsOptions.AllowedOrigins)
                        .WithMethods(corsOptions.AllowedMethods)
                        .WithHeaders(corsOptions.AllowedHeaders);
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
        
        Services.AddHttpContextAccessor();

        Services.AddMvcCore()
            .AddApiExplorer();

        Services.AddControllers();
        
        Services.AddBallwareSharedJintRightsChecker();
        Services.AddBallwareSharedApiDependencies();
        
        Services.AddBallwareSharedAuthorizationUtils(authorizationOptions.TenantClaim, authorizationOptions.UserIdClaim, authorizationOptions.RightClaim);
        
        Services.AddEndpointsApiExplorer();
        
        if (swaggerOptions != null)
        {
            Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("llm", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "ballware LLM user API",
                    Version = "v1"
                });
                
                c.SwaggerDoc("service", new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = "ballware LLM service API",
                    Version = "v1"
                });
                
                c.EnableAnnotations();

                c.AddSecurityDefinition("oidc", new OpenApiSecurityScheme
                {
                    Type = Microsoft.OpenApi.Models.SecuritySchemeType.OpenIdConnect,
                    OpenIdConnectUrl = new Uri(authorizationOptions.Authority + "/.well-known/openid-configuration")
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme {
                            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oidc" }
                        },
                        swaggerOptions.RequiredScopes.Split(" ")
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
        
        var authorizationOptions = app.Services.GetService<IOptions<AuthorizationOptions>>()?.Value;
        var swaggerOptions = app.Services.GetService<IOptions<SwaggerOptions>>()?.Value;

        if (swaggerOptions != null && authorizationOptions != null)
        {
            app.MapSwagger();

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