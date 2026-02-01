using Ballware.Llm.Jobs.Internal;
using Ballware.Shared.Api.Jobs;
using Ballware.Shared.Data.Repository;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.AspNetCore;

namespace Ballware.Llm.Jobs;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBallwareLlmBackgroundJobs(this IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            var importJobName = "import";

            q.AddJob<OriginAddFromAttachmentJob>(OriginAddFromAttachmentJob.Key, configurator => configurator.StoreDurably());
            q.AddJob<TenantableImportJob<Data.Public.Origin, ITenantableRepository<Data.Public.Origin>>>(new JobKey(importJobName, "origin"), configurator => configurator.StoreDurably());
            q.AddJob<TenantableImportJob<Data.Public.Embedding, ITenantableRepository<Data.Public.Embedding>>>(new JobKey(importJobName, "embedding"), configurator => configurator.StoreDurably());
        });

        services.AddQuartzServer(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        return services;
    }
}