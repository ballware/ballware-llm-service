# Tenancy and authorization

## Authentication

The Service validates JWT bearer tokens against an OIDC issuer (`src/Ballware.Llm.Service/Startup.cs:71-103`):

| Setting | Source |
| --- | --- |
| `DefaultAuthenticateScheme` / `DefaultChallengeScheme` = `JwtBearerDefaults.AuthenticationScheme` | `Startup.cs:72-76` |
| `MapInboundClaims = false` — claim names appear exactly as issued | `Startup.cs:77` |
| `Authority`, `Audience`, `RequireHttpsMetadata` (default `true`) | `Startup.cs:78-80`, `Configuration/AuthorizationOptions.cs:8-12, 25` |
| `SaveToken = true` — the raw access token is kept for MCP delegation | `Startup.cs:81`, consumed at `ChatEndpoint.cs:72`, `ChatHub.cs:32`, `Ballware.Llm.App/Chat/BallwareChatResponseProvider.cs:242` |
| `ValidIssuer = Authorization:Issuer ?? Authorization:Authority` | `Startup.cs:84` |

Claims the service relies on (config keys and defaults, `Configuration/AuthorizationOptions.cs:14-23`):

| Claim | Config key | Default | Used for |
| --- | --- | --- | --- |
| `tenant` | `Authorization.TenantClaim` | `tenant` | tenant scope of every operation |
| `sub` | `Authorization.UserIdClaim` | `sub` (`JwtRegisteredClaimNames.Sub`) | acting user id (audit columns, storage uploads, job user) |
| `right` | `Authorization.RightClaim` | `right` | user rights, exposed to scripts as `userinfo` |
| `scope` | — | — | policy selection (hard-coded claim name `ClaimTypeScope = "scope"`, `Startup.cs:35`) |

**SignalR exception:** the hub cannot set headers from a browser WebSocket, so a bearer token is also
accepted as a query parameter `?access_token=`, but **only** for paths starting with `/ai/hubs`
(`Startup.cs:87-102`).

## Scope → policy mapping

Two policies are built in `Startup.cs:105-119`. Both split the `scope` claim value on spaces and compare
each part ordinally:

| Policy | Matches | Default scope name | Used by |
| --- | --- | --- | --- |
| `mlApi` | `Authorization.RequiredMetaScope` | `mlApi` (`AuthorizationOptions.cs:26`) | every generic editing operation (`TenantantableEditingEndpoint.cs:23-114` via `Startup.cs:250-254`), `simulatesystemprompt` (`ContextEndpoint.cs:26`), `addfromattachment`/`createembedding` (`OriginEndpoint.cs:25, 35`), `recreate` (`EmbeddingEndpoint.cs:25`) |
| `serviceApi` | `Authorization.RequiredServiceScope` | `serviceApi` (`AuthorizationOptions.cs:27`) | **nothing** — the policy exists but no endpoint references it |

Two operations use the **default** policy instead of a named one, i.e. "any authenticated user":
`POST /ai/v1/chat/completions` (`ChatEndpoint.cs:29`) and the hub `ChatHub` (`[Authorize]`,
`ChatHub.cs:12`). The anonymous export download skips authorization entirely
(`TenantantableEditingEndpoint.cs:126`).

## Tenant resolution

There is no tenant path segment (except in `download/{tenantId}/{id}`); the tenant is always derived
from the caller's claim:

- `IPrincipalUtils` comes from `ballware-shared-dotnet` and is registered by
  `AddBallwareLlmSharedAuthorizationUtils(tenantClaim, userIdClaim, rightClaim)`
  (`src/Ballware.Llm.Hosting/LlmHostingExtensions.cs:247-254`, called with the configured claim names
  at `Startup.cs:149-153`).
- `DefaultPrincipalUtils.GetUserTenandId` / `GetUserId` parse the configured claims and return
  **`Guid.Empty`** when the claim is missing or not a GUID
  (`Internal/DefaultPrincipalUtils.cs:18-34`) — an unparseable tenant therefore surfaces as
  `404 Tenant 00000000-0000-0000-0000-000000000000 not found`, not as `401`.
- Generic handlers bind tenant/user/claims/rights through
  `Ballware.Shared.Api.Endpoints/Bindings/UserTenantId.cs`, `UserId.cs`, `UserClaims.cs`,
  `UserRights.cs`, each throwing `UnauthorizedAccessException("User is not authenticated.")` for an
  unauthenticated principal.
- LLM-specific endpoints read the same values directly: `OriginEndpoint.cs:55-56, 79-80`,
  `EmbeddingEndpoint.cs:44-45`, `ContextEndpoint.cs:45`, `ChatEndpoint.cs:69-72`, `ChatHub.cs:29-32`.

## Where entity and tenant metadata come from

Rights are not evaluated against local tables — the service asks the **Meta Service** per request
(`src/Ballware.Llm.Hosting/Adapter/MetaServiceMetadataAdapter.cs`), which is registered as both
`IAuthorizationMetadataProvider` and `IExportMetadataProvider`
(`src/Ballware.Llm.Hosting/LlmHostingExtensions.cs:266-267`):

| Purpose | Meta Service call | Line |
| --- | --- | --- |
| Tenant metadata | `MetaClient.TenantServiceMetadataAsync(tenantId)` | `:20-25` |
| Entity metadata | `MetaClient.EntityServiceMetadataForTenantByIdentifierAsync(tenantId, entity)` | `:27-32` |

Outbound calls authenticate as the **service** itself using client credentials
(`ServiceCredentials.Credentials[]` selected by `MetaClient.CredentialIdentifier`,
`LlmHostingExtensions.cs:147-161, 202-217`) and act *on behalf of* the tenant/user passed as arguments.
The Meta Service base address is `MetaClient.ServiceUrl` (`appsettings.json` of each host supplies it;
the Service repo's tracked `appsettings.json` has none, so it must come from
`appsettings.local.json` or environment variables).

Mapping detail: `MetaServiceLlmMetadataProfile.cs:10-11` maps exactly one member —

```csharp
config.NewConfig<Ballware.Meta.Service.Client.ServiceEntity, EntityMetadata>()
      .Map(dest => dest.RightsCheckScript, src => src.CustomScripts.ExtendedRightsCheck);
```

i.e. the tenant-authored `CustomScripts.extendedRightsCheck` becomes the `RightsCheckScript` the entity
rights checker evaluates. `Tenant` is mapped by convention (`:13`), so `ServiceTenant.RightsCheckScript`
maps through unchanged. `EntityMetadata.StateAllowedScript` (`src/Ballware.Llm.Metadata/EntityMetadata.cs:7-11`)
is therefore always `null`, and no state-transition scripting is used by this service.

## Rights grammar

The generic handlers compose a candidate `<application>.<entity>.<operation>`. With
`application="ai"` and `entity=<resource>` (`Startup.cs:250-254`) the candidates are:

| Operation | Right | Source |
| --- | --- | --- |
| `all`, `query`, `byid` | `view` | `TenantableEndpointHandlerFactory.cs:66, 79, 110` |
| `new` | `add` | `TenantableEndpointHandlerFactory.cs:91` |
| `save`, `savebatch` | `edit` — unless `identifier` is present and not `primary`, then the identifier itself | `TenantableEndpointHandlerFactory.cs:17, 122-123, 140-141` |
| `remove/{id}` | `delete` | `TenantableEndpointHandlerFactory.cs:168-169` |
| `export`, `exporturl` | the `identifier` itself | `TenantableEndpointHandlerFactory.cs:238, 265` |
| `import` | endpoint policy only; the job checks `<application>.<entity>.<identifier>` — with the shared job hard-coding `application="meta"` (`TenantableImportJob.cs:67`) | `TenantableEndpointHandlerFactory.cs:188-220` |
| `download/{tenantId}/{id}` | anonymous | `TenantantableEditingEndpoint.cs:126` |

Concrete candidates: `ai.context.view`, `ai.origin.edit`, `ai.embedding.add`, `ai.chunk.delete`,
`ai.image.view`. (The strings are built at runtime by
`JavascriptTenantRightsChecker.cs:19` — they do not appear as literals in the source.)

> The LLM-specific job triggers are **not** rights-checked: `addfromattachment`, `createembedding` and
> `recreate` verify only the `mlApi` scope, so rights on `ai.origin`/`ai.embedding` do not gate them.
> Same for the chat endpoints.

## Script-based enforcement

Two Jint stages run per generic operation (`Startup.cs:149-153` registers
`AddBallwareSharedJintRightsChecker`):

1. **Tenant level** (`JavascriptTenantRightsChecker.cs:8-25`): default verdict `true`; if the tenant's
   `RightsCheckScript` is empty it is skipped. Otherwise it runs as
   `function rightsCheck() { … }` with:

   | Variable | Value |
   | --- | --- |
   | `right` | the fully composed candidate, e.g. `"ai.origin.edit"` |
   | `userinfo` | the caller's claim map (`IPrincipalUtils.GetUserClaims`, JSON-parsed) |

2. **Entity level** (`JavascriptEntityRightsChecker.cs:8-29`): starts from the tenant verdict and can
   override it. Runs as `function extendedRightsCheck() { … }` with:

   | Variable | Value |
   | --- | --- |
   | `application` | `"ai"` |
   | `entity` | the resource, e.g. `"origin"` |
   | `right` | the bare operation (`view`/`add`/`edit`/`delete`) or the export/save identifier |
   | `param` | item or query parameters (for `savebatch`, one call per item) |
   | `result` | the tenant-level verdict |
   | `userinfo` | the caller's claim map |

   The script's return value is parsed with `bool.Parse`, so it must evaluate to `true`/`false`.

State scripts (`StateAllowedScript`, evaluated with `state`, `hasRight(…)`, `hasAnyRight(…)` and
returning `false` when absent — `JavascriptEntityRightsChecker.cs:31-46`) are **unused** by this service.

## Expected status codes

| Situation | Status | Body | Source |
| --- | --- | --- | --- |
| Missing/invalid token, or missing scope | `401` | empty | `Startup.cs:105-119` |
| Right denied by tenant/entity scripts | `401` | empty | `EditingEndpointBuilder.cs:110-114` |
| Tenant metadata not found | `404` | `Tenant <id> not found` | `EditingEndpointBuilder.cs:70-75` |
| Entity metadata not found for the tenant | `404` | `Entity <name> not found for tenant <id>` | `EditingEndpointBuilder.cs:77-82` |
| Item not found (`byid`) | `404` | empty (repository returns no row) | `TenantableEndpointHandlerFactory.cs` |
| Remove precondition failed | `400` | joined messages | `TenantableEndpointHandlerFactory.cs:177-179` |
| Hub connect without a valid token | `401` | — | `ChatHub.cs:19-22` |
| Meta/Storage Service unreachable, invalid query identifier, missing entity parameter | `500` | `application/problem+json` | `Startup.cs:211-236` |

## Related pages

- [metadata-templates.md](../api/metadata-templates.md) — synthetic tenant-metadata templates for these
  rights.
- [payload-offloading.md](payload-offloading.md) — storage operations that inherit the acting user from
  these claims.
- [architecture.md](architecture.md) — where the authorization stage sits in the pipeline.
