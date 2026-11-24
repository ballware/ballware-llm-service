# Tenant metadata templates (synthetic)

> **Non-normative.** Everything on this page is a **derived, anonymized example** of tenant metadata
> shape that fits how the LLM Service consumes metadata. It is **not** the productive configuration of any
> tenant: no ids, names, layouts or scripts from any real tenant were copied. Treat it as a starting
> pattern to adapt, and verify against your own tenant metadata before applying it.

## Why these entities appear in tenant metadata

The generic editing operations check a right candidate composed as
`<application>.<entity>.<right>` with `application="ai"` and `entity` ∈ {`context`, `origin`, `embedding`,
`chunk`, `image`, `session`} (`src/Ballware.Llm.Service/Startup.cs:250-255`). The verdicts come from tenant
metadata that the service fetches from the Meta Service (`Adapter/MetaServiceMetadataAdapter.cs:20-32`), so a
tenant without `ai.<entity>` metadata gets `404 Entity <entity> not found for tenant <tenantId>` for every
generic operation on that resource. Full mechanics:
[../concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md).

> `session` is the exception that needs metadata even for ordinary use: without an `ai.session` entity the
> **whole** `/ai/session/*` mount answers `404 Entity session not found for tenant <id>`, including `all`
> and `byid`.

## Rights naming

| Right | Granted by operation | Candidate example |
| --- | --- | --- |
| `ai.context.view` / `.add` / `.edit` / `.delete` | `all`, `query`, `byid` / `new` / `save`, `savebatch` (with `identifier=primary`) / `remove/{id}` | |
| `ai.origin.view` … `ai.origin.delete` | same | |
| `ai.embedding.view` … `ai.embedding.delete` | same | |
| `ai.chunk.view` … `ai.chunk.delete` | same | |
| `ai.image.view` … `ai.image.delete` | same | |
| `ai.session.view` … `ai.session.delete` | same | |
| `ai.session.admin` | not an operation: unlocks **all** sessions of the tenant in the repository's owner filter | |
| `ai.<entity>.<identifier>` | `save`/`savebatch` with a non-`primary` identifier, and `export`/`exporturl` (identifier **is** the right) | `ai.origin.exportjson` |

Recommended minimal set per entity: `view`, `add`, `edit`, `delete` plus one export identifier per
exportable entity (e.g. `exportjson`). For `session`: `view`, `add`, `edit`, `delete`, optionally `admin`,
and **no** export identifier (export/import are unsupported for sessions).

## Tenant-level script (synthetic pattern)

Evaluated as `function rightsCheck() { … }` with `right` (the fully composed candidate) and `userinfo`
(the caller's claim map). Without a script the tenant stage defaults to `true`
(`JavascriptTenantRightsChecker.cs:10-14`).

```javascript
// synthetic pattern, not a production script
const owned = (userinfo.right || []);
if (right.startsWith('ai.context.')) {
  // context rows are the tenant's own chat configuration: administrators only
  return owned.includes('ai.context.admin');
}
if (right.startsWith('ai.session.')) {
  // sessions are user-owned by default; ai.session.admin unlocks the whole tenant
  if (right === 'ai.session.admin') return owned.includes('ai.session.admin');
  if (right === 'ai.session.import' || right.startsWith('ai.session.export')) return false; // unsupported
  return owned.includes('ai.session.view') || owned.includes('ai.session.admin');   // view / all / query / byid
}
if (right.startsWith('ai.chunk.') || right.startsWith('ai.image.')) {
  // derived data: read-only for everyone, mutations only via the job endpoints' flows
  return right.endsWith('.view');
}
if (right.startsWith('ai.origin.') || right.startsWith('ai.embedding.')) {
  return owned.includes(right);
}
return false;   // unknown application: deny
```

Note that `right` claims arrive as separate claims (`Authorization.RightClaim`, default `right`) and
`userinfo` groups repeated claim types into arrays (`DefaultPrincipalUtils.cs:49-78`).

## Entity-level script (synthetic pattern)

The LLM Service maps **only** `CustomScripts.ExtendedRightsCheck` into the metadata the entity checker
evaluates (`src/Ballware.Llm.Hosting/Mappings/MetaServiceLlmMetadataProfile.cs:10-11`). Available
variables: `application`, `entity`, `right`, `param`, `result` (tenant verdict), `userinfo`; the return
value is parsed with `bool.Parse` (`JavascriptEntityRightsChecker.cs:19-25`).

A common pattern for knowledge administration is a **synthetic right** that gates the embedding-trigger
flow behind the ordinary origin edit right:

```javascript
// synthetic extendedRightsCheck for the "origin" entity
if (right === 'createembedding') {
  // derived right: granted implicitly to anyone who may edit origins
  return result === true && (userinfo.right || []).includes('ai.origin.edit');
}
if (right === 'exportjson') {
  return (userinfo.right || []).includes('ai.origin.export');
}
return result;   // otherwise inherit the tenant verdict
```

`createembedding` is shown here purely as an example of a synthetic/custom right name. It is **not** a
right the LLM Service checks: `POST /ai/origin/createembedding` verifies only the `mlApi` scope
(`src/Ballware.Llm.Api/Endpoints/OriginEndpoint.cs:35`), so metadata rights do not gate it today. If a
tenant wants embedding creation gated, that must be enforced in the metadata of the UI-facing application
that calls this service, not here.

For the `session` entity note that the entity stage receives the **bare** right (`view`, `add`, `edit`,
`delete`) — the same stage is also reused by the actor resolver, which evaluates the candidate
`ai.session.admin` with `right === 'admin'`:

```javascript
// synthetic extendedRightsCheck for the "session" entity
const owned = (userinfo.right || []);
if (right === 'import' || right.startsWith('export')) {
  return false;   // not supported for sessions
}
if (right === 'admin') {
  return owned.includes('ai.session.admin');
}
return result === true && (owned.includes('ai.session.' + right) || owned.includes('ai.session.admin'));
```

`ai.session.admin` has two effects: it passes the mount's right checks for every session operation, and it
lifts the repository's owner filter so the actor sees sessions created by other users.

## Entity metadata skeleton (synthetic)

Shape of the entity metadata the Meta Service returns to the LLM Service
(`src/Ballware.Meta.Api/Public/ServiceEntity.cs`, consumed through the Meta Service client):

```jsonc
{
  "application": "ai",
  "entity": "origin",
  "customScripts": { "extendedRightsCheck": "<see pattern above>" },
  "customFunctions": [ { "id": "createembedding", "type": "external", "options": {} } ],
  "listQuery":    [ { "identifier": "primary", "query": "…", "parameters": [] } ],
  "byIdQuery":    [ { "identifier": "primary", "query": "…" } ],
  "newQuery":     [ { "identifier": "primary", "query": "…" } ],
  "saveStatement":[ { "identifier": "primary", "query": "…" } ],
  "removeStatement": "…",
  "stateAllowedScript": null
}
```

Two caveats specific to the LLM Service:

1. **`ListQuery`/`ByIdQuery`/`NewQuery`/`SaveStatement` in tenant metadata do not drive the LLM
   operations.** The handlers delegate to the service's own repositories
   (`TenantableEndpointHandlerFactory.cs:60-127` → `ITenantableRepository.AllAsync/QueryAsync/ByIdAsync/SaveAsync`),
   and the identifier→query mapping for the LLM entities is implemented in code
   (`src/Ballware.Llm.Data.Ef/Repository/EmbeddingBaseRepository.cs:16-29`,
   `…/ChunkBaseRepository.cs:10-23`, `…/ImageBaseRepository.cs:11-24`). Metadata for these entities is
   effectively a **rights catalog** plus export definitions.
2. `StateAllowedScript` is **never populated** in the mapped metadata (`MetaServiceLlmMetadataProfile.cs`
   maps only `ExtendedRightsCheck`), and the state checker returns `false` when the script is empty
   (`JavascriptEntityRightsChecker.cs:31-46`). Do not author state scripts expecting this service to run
   them.

For `embedding`, the same pattern applies with `entity: "embedding"`, and — because chunk and image rows
are derived data — a typical split is: full rights on `origin`/`embedding`, `view` only on
`chunk`/`image`, and mutations of `chunk`/`image` restricted to maintenance roles. Remember the import
job quirk: the shared job checks `meta.<entity>.<identifier>` (`TenantableImportJob.cs:67`), so if import
is used, a `meta.origin.primary` / `meta.embedding.primary`-style right is what the job evaluates, not
`ai.origin.primary`.

## What to avoid

- Do not copy ids, display names, layouts or scripts from an existing tenant into another tenant.
- Do not assume a right name is enforced by this service — check the operation's source (the endpoint
  policy list in [../concepts/tenancy-and-authorization.md](../concepts/tenancy-and-authorization.md)).
- Do not rely on `ai.<entity>.<right>` being sufficient for the chat endpoints: they only require an
  authenticated user, so knowledge access control at the chat layer comes from which `context` rows and
  `origin` rows the tenant exposes, not from rights scripts.
