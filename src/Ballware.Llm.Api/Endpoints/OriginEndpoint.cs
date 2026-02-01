using System.Security.Claims;
using Ballware.Llm.Api.Model;
using Ballware.Llm.Data.Repository;
using Ballware.Shared.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Quartz;

namespace Ballware.Llm.Api.Endpoints;

public static class OriginEndpoint
{
    private const string ApiTag = "Origin";
    private const string ApiOperationPrefix = "Origin";

    public static IEndpointRouteBuilder MapOriginUserApi(this IEndpointRouteBuilder app,
        string basePath,
        string apiTag = ApiTag,
        string apiOperationPrefix = ApiOperationPrefix,
        string authorizationScope = "mlApi",
        string apiGroup = "llm")
    {
        app.MapPost(basePath + "/addfromattachment", HandleAddFromAttachment)
            .RequireAuthorization(authorizationScope)
            .DisableAntiforgery()
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName(apiOperationPrefix + "AddFromAttachment")
            .WithGroupName(apiGroup)
            .WithTags(apiTag)
            .WithSummary("Add knowledge origin from attachment");
        
        return app;
    }
    
    private static async Task<IResult> HandleAddFromAttachment(
        ISchedulerFactory schedulerFactory, 
        IPrincipalUtils principalUtils, 
        IOriginRepository originRepository, 
        ClaimsPrincipal user, 
        OriginAddFromAttachmentRequest body)
    {
        var tenantId = principalUtils.GetUserTenandId(user);
        var userId = principalUtils.GetUserId(user);

        var jobData = new JobDataMap();
        
        jobData.Add("tenantId", tenantId);
        jobData.Add("userId", userId);
        jobData.Add("ownerEntity", body.OwnerEntity);
        jobData.Add("ownerId", body.OwnerId);
        jobData.Add("attachmentId", body.AttachmentId);

        await (await schedulerFactory.GetScheduler()).TriggerJob(JobKey.Create("addfromattachment", "origin"), jobData);
        
        return Results.Accepted();
    }
}