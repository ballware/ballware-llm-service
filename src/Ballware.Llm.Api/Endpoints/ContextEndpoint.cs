using System.Security.Claims;
using Ballware.Llm.Api.Model;
using Ballware.Llm.Context;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Quartz;

namespace Ballware.Llm.Api.Endpoints;

public static class ContextEndpoint
{
    private const string ApiTag = "Context";
    private const string ApiOperationPrefix = "Context";

    public static IEndpointRouteBuilder MapContextUserApi(this IEndpointRouteBuilder app,
        string basePath,
        string apiTag = ApiTag,
        string apiOperationPrefix = ApiOperationPrefix,
        string authorizationScope = "mlApi",
        string apiGroup = "llm")
    {
        app.MapPost(basePath + "/simulatesystemprompt", HandleSimulateSystemPrompt)
            .RequireAuthorization(authorizationScope)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "SimulateSystemPrompt")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Simulate system prompt with provided template and knowledge");
        
        return app;
    }
    
    private static async Task<IResult> HandleSimulateSystemPrompt(
        ISchedulerFactory schedulerFactory, 
        IPrincipalUtils principalUtils, 
        IModelContextProvider contextProvider,
        ClaimsPrincipal user, 
        ContextSimulateSystemPromptRequest body)
    {
        var tenantId = principalUtils.GetUserTenandId(user);

        var systemPrompt = await contextProvider.ProvideSystemPromptAsync(tenantId, body.ContextIdentifier, body.InitialPrompt);
        var estimatedTokenCount = await contextProvider.EstimateTokenCountAsync(tenantId, body.ContextIdentifier, systemPrompt);
        
        return Results.Ok(new ContextSimulateSystemPromptResponse()
        {
            SystemPrompt = systemPrompt,
            EstimatedTokenCount = estimatedTokenCount
        });
    }
    
    private static async Task<IResult> HandleCreateEmbedding(
        ISchedulerFactory schedulerFactory, 
        IPrincipalUtils principalUtils, 
        IOriginRepository originRepository, 
        ClaimsPrincipal user, 
        OriginCreateEmbeddingRequest body)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        var userId = principalUtils.GetUserId(user);

        foreach (var id in body.Ids)
        {
            var jobData = new JobDataMap();
        
            jobData.Add("tenantId", tenantId);
            jobData.Add("userId", userId);
            jobData.Add("id", id);
            jobData.Add("model", body.Model);
            jobData.Add("activate", body.Activate);

            await (await schedulerFactory.GetScheduler()).TriggerJob(JobKey.Create("createembedding", "origin"), jobData);
        }
        
        return Results.Accepted();
    }
}