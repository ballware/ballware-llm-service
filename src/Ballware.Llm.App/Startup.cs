using System.Globalization;
using Ballware.Llm.App.Chat;
using Ballware.Llm.App.Components;
using Ballware.Llm.Hosting;
using Ballware.Shared.Web.Oidc;
using Ballware.Shared.Web.Oidc.Configuration;
using Ballware.Shared.Web.Session;
using Ballware.Shared.Web.Session.Configuration;
using DevExpress.AIIntegration;
using DevExpress.AIIntegration.Chat;
using DevExpress.Blazor;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Serilog;

namespace Ballware.Llm.App;

public class Startup(IWebHostEnvironment environment, ConfigurationManager configuration, IServiceCollection services)
{
    private IWebHostEnvironment Environment { get; } = environment;
    private ConfigurationManager Configuration { get; } = configuration;
    private IServiceCollection Services { get; } = services;

    private string FrameAncestorsPolicy { get; } = ResolveFrameAncestorsPolicy(configuration);

    private static string ResolveFrameAncestorsPolicy(ConfigurationManager configuration)
    {
        var configured = configuration["ContentSecurity:FrameAncestors"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            return "'self'";
        }

        return NormalizeFrameAncestorsPolicy(configured.Trim());
    }

    private static string NormalizeFrameAncestorsPolicy(string policy)
    {
        if (string.Equals(policy, "self", StringComparison.OrdinalIgnoreCase))
        {
            return "'self'";
        }

        if (string.Equals(policy, "none", StringComparison.OrdinalIgnoreCase))
        {
            return "'none'";
        }

        if (policy.StartsWith("self ", StringComparison.OrdinalIgnoreCase))
        {
            return "'self' " + policy.Substring(5);
        }

        return policy;
    }

    private bool FramingFromForeignOriginsAllowed =>
        !string.Equals(FrameAncestorsPolicy, "'self'", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(FrameAncestorsPolicy, "'none'", StringComparison.OrdinalIgnoreCase);

    public void InitializeServices()
    {
        Services.AddBallwareLlmCoreConfiguration(Configuration);

        var sessionOptions = Configuration.GetSection("Session").Get<WebSessionOptions>() ?? new WebSessionOptions
        {
            EnableSession = true,
            SessionCookieName = ".Ballware.Llm.Session",
            SessionCacheKeyPrefix = "llm:session:",
            SessionCacheExpirationInMinutes = 60,
            SuppressDefaultSessionCookie = true,
            DataProtectionPurpose = "Ballware.Llm.Session.ClaimsPrincipal"
        };

        Services.AddBallwareBrowserAuthorization(Configuration, sessionOptions);

        Services.Configure<OpenIdConnectOptions>(
            OpenIdConnectDefaults.AuthenticationScheme,
            static options => options.MapInboundClaims = false);

        var detailedErrors = Configuration.GetValue<bool?>("DetailedErrors") ?? Environment.IsDevelopment();

        Services.AddRazorComponents()
            .AddInteractiveServerComponents(options => options.DetailedErrors = detailedErrors)
            .AddHubOptions(options => options.MaximumReceiveMessageSize = 256 * 1024);

        Services.Configure<AntiforgeryOptions>(options =>
        {
            options.SuppressXFrameOptionsHeader = FramingFromForeignOriginsAllowed;
            options.Cookie.Name = ".Ballware.Antiforgery";
        });

        Services.AddBallwareLlmClients(Configuration);
        Services.AddBallwareLlmDataStore(Configuration);
        Services.AddBallwareLlmSharedAuthorizationUtils(Configuration);
        Services.AddBallwareLlmMetadataAndStorageAdapters();
        Services.AddBallwareLlmTools();
        Services.AddBallwareLlmContextAndInference(Configuration);

        Services.AddMvcCore();
        Services.AddDevExpressBlazor();
        Services.AddDevExpressAI();

        Services.AddOptions<ChatSessionOptions>().Bind(Configuration.GetSection("Chat"));

        Services.AddScoped<BallwareChatScope>();
        Services.AddScoped<BallwareChatResponseProvider>();
        Services.AddKeyedScoped<IChatResponseProvider>(
            BallwareChatScope.ProviderServiceKey,
            static (serviceProvider, _) => serviceProvider.GetRequiredService<BallwareChatResponseProvider>());
    }

    public void InitializeApp(WebApplication app)
    {
        var authorizationOptions = app.Services.GetRequiredService<IOptions<BrowserAuthorizationOptions>>().Value;

        if (string.IsNullOrWhiteSpace(authorizationOptions.PublicBaseUrl))
        {
            Log.Warning(
                "Authorization:PublicBaseUrl is not configured. The OIDC sign-in redirect_uri is derived from the incoming request ({ClientId}), so every host the app is reached with must be registered as a valid redirect URI for its callback path at the identity provider.",
                authorizationOptions.ClientId);
        }

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

        app.UseForwardedHeaders(new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        });

        app.UseRouting();

        app.UseBallwareWebSession();
        app.UseBallwareChallengeDifferentiation();

        app.UseAuthentication();
        app.UseBallwareSignInChallengeFallback();

        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseStaticFiles();

        app.Use(async (context, next) =>
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            await next();
        });

        app.MapBallwareSignOnEndpoint();

        app.MapRazorComponents<Ballware.Llm.App.Components.App>()
            .AddInteractiveServerRenderMode(options =>
            {
                options.ContentSecurityFrameAncestorsPolicy = FrameAncestorsPolicy;
            });
    }
}
