using System.Security.Claims;
using Ballware.Llm.Api.Model;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Quartz;

namespace Ballware.Llm.Api.Endpoints;

public static class EmbeddingEndpoint
{
    private const string ApiTag = "Embedding";
    private const string ApiOperationPrefix = "Embedding";

    public static IEndpointRouteBuilder MapEmbeddingUserApi(this IEndpointRouteBuilder app,
        string basePath,
        string apiTag = ApiTag,
        string apiOperationPrefix = ApiOperationPrefix,
        string authorizationScope = "mlApi",
        string apiGroup = "llm")
    {
        app.MapPost(basePath + "/recreate", HandleRecreate)
            .RequireAuthorization(authorizationScope)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "Recreate")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Recreate embedding after modification of origin content or options");
        
        return app;
    }
    
    private static async Task<IResult> HandleRecreate(
        ISchedulerFactory schedulerFactory, 
        IPrincipalUtils principalUtils, 
        IEmbeddingRepository embeddingRepository, 
        ClaimsPrincipal user, 
        RecreateEmbeddingRequest body)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        var userId = principalUtils.GetUserId(user);

        foreach (var id in body.Ids)
        {
            var jobData = new JobDataMap();
    
            jobData.Add("tenantId", tenantId);
            jobData.Add("userId", userId);
            jobData.Add("id", id);
        
            await (await schedulerFactory.GetScheduler()).TriggerJob(JobKey.Create("recreate", "embedding"), jobData);
        }
            
        return Results.Created();
    }
    
}