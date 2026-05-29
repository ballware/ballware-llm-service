using Ballware.Assistant;
using Ballware.Assistant.Workflow.MsAgentNetwork.Configuration;
using Ballware.Assistant.Workflow.MsAgentNetwork.Internal;
using Microsoft.Extensions.DependencyInjection;

namespace Ballware.Assistant.Workflow.MsAgentNetwork;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareAssistantWorkflowForMsAgentNetwork(
        this IServiceCollection services,
        Action<MsAgentNetworkWorkflowOptions>? configure = null)
    {
        var options = new MsAgentNetworkWorkflowOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<IAssistantWorkflow, MsAgentNetworkAssistantWorkflow>();
        services.AddScoped<IAssistantSessionFactory, MsAgentNetworkAssistantSessionFactory>();

        return services;
    }
}
