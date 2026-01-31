using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ballware.Llm.Data.Ef.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Postgres.Internal;

class InitializationWorker : IHostedService
{
    private IServiceProvider ServiceProvider { get; }

    public InitializationWorker(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = ServiceProvider.CreateAsyncScope();

        var options = scope.ServiceProvider.GetRequiredService<LlmStorageOptions>();

        if (options.AutoMigrations)
        {
            var context = scope.ServiceProvider.GetRequiredService<LlmDbContext>();

            await context.Database.MigrateAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
