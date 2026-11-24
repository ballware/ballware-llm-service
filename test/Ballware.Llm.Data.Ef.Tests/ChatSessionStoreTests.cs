using Ballware.Llm.Data.Ef.Configuration;
using Ballware.Llm.Data.Ef.Postgres;
using Ballware.Llm.Data.Ef.Repository;
using Ballware.Llm.Data.Repository;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Ballware.Llm.Data.Ef.Tests;

[TestFixture]
[NonParallelizable]
public class ChatSessionStoreTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    private static PostgresTestStore store = null!;

    private LlmDbContext context = null!;
    private TestChatSessionActorResolver resolver = null!;
    private ChatSessionBaseRepository sessions = null!;
    private ContextBaseRepository contexts = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        store = await PostgresTestStore.StartAsync();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await store.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        await TruncateAsync();

        context = store.CreateContext();

        var mapper = new Mapper(new TypeAdapterConfig().AddBallwareLlmStorageMappings());

        resolver = new TestChatSessionActorResolver();

        sessions = new ChatSessionBaseRepository(
            mapper,
            context,
            new ChatSessionStoreOptions(),
            resolver,
            null);

        contexts = new ContextBaseRepository(mapper, context, sessions);
    }

    [TearDown]
    public async Task TearDown()
    {
        await context.DisposeAsync();
        await TruncateAsync();
    }

    [Test]
    public async Task All_Query_And_ById_ShouldOnlyReturnOwnSessions_And_SaveAndRemoveShouldRejectForeignSessions()
    {
        var ownSession = await sessions.CreateAsync(TenantId, UserA, "global", "Eigene Unterhaltung");
        var foreignSession = await sessions.CreateAsync(TenantId, UserB, "global", "Fremde Unterhaltung");

        var all = (await sessions.AllAsync(TenantId, "primary", Claims(UserA))).ToList();
        var query = (await sessions.QueryAsync(TenantId, "primary", Claims(UserA), new Dictionary<string, object>())).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(all.Select(session => session.Id), Is.EquivalentTo(new[] { ownSession.Id }), "all must be owner scoped.");
            Assert.That(query.Select(session => session.Id), Is.EquivalentTo(new[] { ownSession.Id }), "query must be owner scoped.");
            Assert.That(all.Single().OwnerUserId, Is.EqualTo(UserA), "owner is exposed read only.");
        });

        Assert.That(await sessions.ByIdAsync(TenantId, "primary", Claims(UserA), foreignSession.Id), Is.Null, "foreign byid must not be visible.");

        Assert.That(
            await CaptureAsync(() => sessions.SaveAsync(TenantId, UserA, "primary", Claims(UserA), new Public.ChatSession
            {
                Id = foreignSession.Id,
                Title = "Fremder Titel"
            })),
            Is.TypeOf<ChatSessionNotVisibleException>(),
            "save on a foreign session must be rejected.");

        Assert.That(
            await CaptureAsync(() => sessions.RemoveAsync(TenantId, UserA, Claims(UserA), RemoveParams(foreignSession.Id))),
            Is.TypeOf<ChatSessionNotVisibleException>(),
            "remove on a foreign session must be rejected.");

        Assert.That(await ReloadSessionAsync(foreignSession.Id), Is.Not.Null, "rejected save/remove must not delete the foreign session.");
    }

    [Test]
    public async Task AdminActor_ShouldSee_Edit_And_DeleteForeignSessions()
    {
        var ownSession = await sessions.CreateAsync(TenantId, UserA, "global", "Eigene Unterhaltung");
        var foreignSession = await sessions.CreateAsync(TenantId, UserB, "global", "Fremde Unterhaltung");

        resolver.AddAdmin(UserA);

        await sessions.SaveAsync(TenantId, UserA, "primary", Claims(UserA), new Public.ChatSession
        {
            Id = foreignSession.Id,
            Title = "Durch Admin umbenannt"
        });

        var all = (await sessions.AllAsync(TenantId, "primary", Claims(UserA))).ToList();
        var scoped = (await sessions.QueryAsync(TenantId, "primary", Claims(UserA), new Dictionary<string, object> { { "context", "global" } })).ToList();
        var visible = await sessions.ByIdAsync(TenantId, "primary", Claims(UserA), foreignSession.Id);
        var removeResult = await sessions.RemoveAsync(TenantId, UserA, Claims(UserA), RemoveParams(foreignSession.Id));
        var remainingForeign = await ReloadSessionAsync(foreignSession.Id);

        Assert.Multiple(() =>
        {
            Assert.That(all.Select(session => session.Id), Is.EquivalentTo(new[] { ownSession.Id, foreignSession.Id }), "admin must see all sessions of the tenant.");
            Assert.That(scoped.Count, Is.EqualTo(2));
            Assert.That(visible, Is.Not.Null);
            Assert.That(visible!.Title, Is.EqualTo("Durch Admin umbenannt"));
            Assert.That(removeResult.Result, Is.True);
            Assert.That(remainingForeign, Is.Null);
        });
    }

    [Test]
    public async Task Create_ShouldKeepDerivedTitle_And_AppendShouldIncreaseSequencePerTurn()
    {
        var session = await sessions.CreateAsync(TenantId, UserA, "global", "Wie konfiguriere ich die Kontextfenster?");

        Assert.That(session.Title, Is.EqualTo("Wie konfiguriere ich die Kontextfenster?"));

        await sessions.AppendMessagesAsync(TenantId, UserA, session.Id,
        [
            Message("user", "Wie konfiguriere ich die Kontextfenster?"),
            Message("assistant", "Über num_ctx im Kontext.")
        ]);

        await sessions.AppendMessagesAsync(TenantId, UserA, session.Id,
        [
            Message("user", "Und wie groß ist sicher?"),
            Message("assistant", "Das hängt vom Modell ab.")
        ]);

        var persisted = await ReloadSessionAsync(session.Id);
        var messages = await ReloadMessagesAsync(session.Id);

        Assert.Multiple(() =>
        {
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.UserId, Is.EqualTo(UserA));
            Assert.That(messages.Select(message => message.Sequence), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(messages.Select(message => message.Role), Is.EqualTo(new[] { "user", "assistant", "user", "assistant" }));
            Assert.That(messages.All(message => message.CreatorId == UserA), Is.True, "message creator is the owner.");
        });
    }

    [Test]
    public async Task GetMessages_ShouldReturnTheLatestMessagesInAscendingOrder()
    {
        var session = await sessions.CreateAsync(TenantId, UserA, "global", "Verlauf");

        await sessions.AppendMessagesAsync(TenantId, UserA, session.Id,
        [
            Message("user", "Frage 1"),
            Message("assistant", "Antwort 1"),
            Message("user", "Frage 2"),
            Message("assistant", "Antwort 2"),
            Message("user", "Frage 3"),
            Message("assistant", "Antwort 3")
        ]);

        var resumed = (await sessions.GetMessagesAsync(TenantId, UserA, session.Id, 3)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(resumed.Select(message => message.Sequence), Is.EqualTo(new[] { 4, 5, 6 }), "resume replays the tail in ascending order.");
            Assert.That(resumed.Select(message => message.Content), Is.EqualTo(new[] { "Antwort 2", "Frage 3", "Antwort 3" }));
        });
    }

    [Test]
    public async Task RemovingAContext_ShouldDeleteItsSessionsAndMessages_And_RemovingASession_ShouldDeleteItsMessages()
    {
        var cascadeContext = new Persistables.Context
        {
            TenantId = TenantId,
            Uuid = Guid.NewGuid(),
            Identifier = "cascade"
        };

        context.Contexts.Add(cascadeContext);
        await context.SaveChangesAsync();

        var cascadedSession = await sessions.CreateAsync(TenantId, UserA, "cascade", "Kontext-Unterhaltung");

        await sessions.AppendMessagesAsync(TenantId, UserA, cascadedSession.Id,
        [
            Message("user", "Frage im Kontext"),
            Message("assistant", "Antwort im Kontext")
        ]);

        var removed = await contexts.RemoveAsync(TenantId, UserA, Claims(UserA), RemoveParams(cascadeContext.Uuid));

        var removedSession = await ReloadSessionAsync(cascadedSession.Id);
        var removedMessages = await ReloadMessagesAsync(cascadedSession.Id);

        Assert.Multiple(() =>
        {
            Assert.That(removed.Result, Is.True);
            Assert.That(removedSession, Is.Null, "context removal must delete the session.");
            Assert.That(removedMessages.Count, Is.EqualTo(0), "context removal must delete the messages.");
        });

        var standaloneSession = await sessions.CreateAsync(TenantId, UserA, "global", "Einzelne Unterhaltung");

        await sessions.AppendMessagesAsync(TenantId, UserA, standaloneSession.Id,
        [
            Message("user", "Frage"),
            Message("assistant", "Antwort")
        ]);

        await sessions.DeleteForUserAsync(TenantId, UserA, standaloneSession.Id);

        var deletedSession = await ReloadSessionAsync(standaloneSession.Id);
        var deletedMessages = await ReloadMessagesAsync(standaloneSession.Id);

        Assert.Multiple(() =>
        {
            Assert.That(deletedSession, Is.Null);
            Assert.That(deletedMessages.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Save_ShouldApplyTitleOnly_And_CannotAlterOwnershipContextOrHistory()
    {
        var session = await sessions.CreateAsync(TenantId, UserA, "global", "Ursprungstitel");

        await sessions.AppendMessagesAsync(TenantId, UserA, session.Id, [Message("user", "Echte Frage")]);

        await sessions.SaveAsync(TenantId, UserA, "primary", Claims(UserA), new Public.ChatSession
        {
            Id = session.Id,
            Title = "Neuer Titel",
            ContextIdentifier = "anderer-kontext",
            OwnerUserId = UserB,
            Messages =
            [
                Message("user", "Gefälschte Nachricht"),
                Message("assistant", "Gefälschte Antwort")
            ]
        });

        var persisted = await ReloadSessionAsync(session.Id);
        var messages = await ReloadMessagesAsync(session.Id);

        Assert.Multiple(() =>
        {
            Assert.That(persisted, Is.Not.Null);
            Assert.That(persisted!.Title, Is.EqualTo("Neuer Titel"), "title is the only writable member.");
            Assert.That(persisted.UserId, Is.EqualTo(UserA), "ownership cannot be rewritten through save.");
            Assert.That(persisted.ContextIdentifier, Is.EqualTo("global"), "context binding cannot be rewritten through save.");
            Assert.That(messages.Count, Is.EqualTo(1), "history is immutable through save.");
            Assert.That(messages.Single().Content, Is.EqualTo("Echte Frage"));
        });

        Assert.That((await sessions.GetMessagesAsync(TenantId, UserA, session.Id, 50)).Single().Content, Is.EqualTo("Echte Frage"));
    }

    [Test]
    public async Task ById_ShouldIncludeMessages_While_AllAndQuery_ShouldNot()
    {
        var session = await sessions.CreateAsync(TenantId, UserA, "global", "Mit Verlauf");

        await sessions.AppendMessagesAsync(TenantId, UserA, session.Id,
        [
            Message("user", "Frage"),
            Message("assistant", "Antwort")
        ]);

        var detailed = await sessions.ByIdAsync(TenantId, "primary", Claims(UserA), session.Id);
        var listed = (await sessions.AllAsync(TenantId, "primary", Claims(UserA))).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(detailed, Is.Not.Null);
            Assert.That(detailed!.Messages, Is.Not.Null);
            Assert.That(detailed.Messages!.Select(message => message.Content), Is.EqualTo(new[] { "Frage", "Antwort" }));
            Assert.That(listed.Single().Messages, Is.Null, "list results must not carry history.");
        });
    }

    [Test]
    public async Task New_ShouldStampTheActingUser_And_SaveOnAnUnknownSessionShouldNotBeVisible()
    {
        var stub = await sessions.NewAsync(TenantId, "primary", Claims(UserA));
        var persistedStub = await ReloadSessionAsync(stub.Id);

        Assert.Multiple(() =>
        {
            Assert.That(stub.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(stub.OwnerUserId, Is.EqualTo(UserA), "new template is stamped with the acting user.");
            Assert.That(persistedStub, Is.Null, "new must not persist a row.");
        });

        Assert.That(
            await CaptureAsync(() => sessions.SaveAsync(TenantId, UserA, "primary", Claims(UserA), new Public.ChatSession
            {
                Id = Guid.NewGuid(),
                Title = "Unbekannt"
            })),
            Is.TypeOf<ChatSessionNotVisibleException>());
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static Public.ChatSessionMessage Message(string role, string content)
    {
        return new Public.ChatSessionMessage
        {
            Role = role,
            Content = content
        };
    }

    private static IDictionary<string, object> Claims(Guid userId)
    {
        return new Dictionary<string, object>
        {
            { "sub", userId.ToString() },
            { "tenant", TenantId.ToString() }
        };
    }

    private static IDictionary<string, object> RemoveParams(Guid id)
    {
        return new Dictionary<string, object>
        {
            { "Id", id }
        };
    }

    private async Task TruncateAsync()
    {
        await using var truncateContext = store.CreateContext();

        await truncateContext.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE chat_message, chat_session, "context"
            """);
    }

    private async Task<Persistables.ChatSession?> ReloadSessionAsync(Guid sessionId)
    {
        await using var reloadContext = store.CreateContext();

        return await reloadContext.Set<Persistables.ChatSession>()
            .AsNoTracking()
            .FirstOrDefaultAsync(session => session.TenantId == TenantId && session.Uuid == sessionId);
    }

    private async Task<List<Persistables.ChatMessage>> ReloadMessagesAsync(Guid sessionId)
    {
        await using var reloadContext = store.CreateContext();

        return await reloadContext.Set<Persistables.ChatMessage>()
            .AsNoTracking()
            .Where(message => message.TenantId == TenantId && message.SessionId == sessionId)
            .OrderBy(message => message.Sequence)
            .ToListAsync();
    }

    private sealed class TestChatSessionActorResolver : IChatSessionActorResolver
    {
        private readonly HashSet<Guid> adminUsers = [];

        public void AddAdmin(Guid userId)
        {
            adminUsers.Add(userId);
        }

        public Task<ChatSessionActor> ResolveActorAsync(Guid tenantId, Guid? userId, IDictionary<string, object> claims)
        {
            var resolvedUserId = userId ?? Guid.Empty;

            if (resolvedUserId == Guid.Empty && claims.TryGetValue("sub", out var sub) && Guid.TryParse(sub?.ToString(), out var claimUserId))
            {
                resolvedUserId = claimUserId;
            }

            return Task.FromResult(new ChatSessionActor(resolvedUserId, adminUsers.Contains(resolvedUserId)));
        }
    }
}
