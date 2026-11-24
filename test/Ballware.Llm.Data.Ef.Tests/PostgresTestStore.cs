using Ballware.Llm.Data.Ef.Model;
using Ballware.Llm.Data.Ef.Postgres;
using Ballware.Llm.Data.Persistables;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Pgvector.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Tests;

public sealed class PostgresTestStore : IAsyncDisposable
{
    private const ushort PostgresPort = 5432;

    private readonly IContainer container;

    private PostgresTestStore(IContainer container, string connectionString)
    {
        this.container = container;
        ConnectionString = connectionString;
    }

    public string ConnectionString { get; }

    public static async Task<PostgresTestStore> StartAsync()
    {
        var container = new ContainerBuilder()
            .WithImage("pgvector/pgvector:pg17")
            .WithEnvironment("POSTGRES_USER", "llmtest")
            .WithEnvironment("POSTGRES_PASSWORD", "llmtest")
            .WithEnvironment("POSTGRES_DB", "llmtest")
            .WithPortBinding(PostgresPort, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(PostgresPort))
            .Build();

        try
        {
            await container.StartAsync();
        }
        catch (Exception exception)
        {
            await container.DisposeAsync();
            Assert.Ignore($"Docker/Postgres konnte nicht gestartet werden: {exception.Message}");
            throw;
        }

        var store = new PostgresTestStore(
            container,
            $"Host=127.0.0.1;Port={container.GetMappedPublicPort(PostgresPort)};Database=llmtest;Username=llmtest;Password=llmtest");

        await store.InitializeAsync();

        return store;
    }

    public LlmDbContext CreateContext()
    {
        return new LlmDbContext(new DbContextOptionsBuilder<LlmDbContext>()
            .UseNpgsql(ConnectionString, options => options.UseVector())
            .ReplaceService<IModelCustomizer, LlmModelBaseCustomizer<Context, Origin, Embedding, Chunk, Image, ChatSession, ChatMessage>>()
            .Options);
    }

    private async Task InitializeAsync()
    {
        await using var context = CreateContext();

        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }
}
