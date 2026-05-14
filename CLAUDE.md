# Eigenfocus API

A read/write .NET 8 Minimal API that wraps an existing Eigenfocus SQLite database (schema owned by a separate Rails app). All endpoint shapes, validation rules, and status codes are specified in `API_Documentation.md` — that document is the source of truth; this file documents how the code is organized to implement it.

## Stack

- .NET 8, ASP.NET Core Minimal API (`Microsoft.NET.Sdk.Web`)
- Dapper 2.1.35 for data access
- Microsoft.Data.Sqlite 8.0.10 (SQLite in WAL mode against a Rails-managed `production.sqlite3`)
- Static `X-API-Key` auth via custom middleware
- Docker via `Dockerfile` + `compose.yml` (publishes on port 8080)
- `InvariantGlobalization=true`; nullable + implicit usings enabled
- No test framework, no linter/formatter configured yet

## Commands

### Build & Serve

- `dotnet build` — restore + compile.
- `dotnet run` — run locally. Requires `ApiKey` and `ConnectionStrings:DefaultConnection` to be set (via `appsettings.Development.json`, user-secrets, or environment variables — `appsettings.json` ships with `ApiKey=""` and a `/data/...` connection string intended for the container).
- `docker compose up --build` — build the image and run the container. `compose.yml` mounts `./data` to `/data` and sets `ApiKey` + connection string via environment. Edit `compose.yml` before running — the committed `ApiKey` value is a placeholder.

The connection string should include `Journal Mode=WAL` so the API does not block Rails writers (see `compose.yml:11`).

### Test

Not configured. No test project, no test runner.

### Lint & Format

Not configured. No `.editorconfig`, no analyzers beyond the SDK defaults.

### Deploy

No CI/CD in the repo. Deployment is whatever runs the Docker image produced by `Dockerfile`.

## Project Conventions

### Feature Placement

```
src/
  Program.cs              # composition root: DI, middleware, endpoint group registration
  Auth/
    ApiKeyMiddleware.cs   # X-API-Key enforcement; /health is anonymous
  Data/
    DbConnectionFactory.cs  # IDbConnectionFactory -> opened SqliteConnection
    IssueRepository.cs      # the only place that SELECTs from `issues` for responses
  Endpoints/
    ApiResults.cs         # error-envelope helpers (BadRequest/NotFound/Conflict)
    ReadEndpoints.cs      # all GETs
    IssueEndpoints.cs     # issue mutations (POST/PUT/PATCH/finish/unfinish)
    CommentEndpoints.cs   # comment creation
  Models/                 # DTOs (one POCO per file, PascalCase props)
    Project.cs, IssueStatus.cs, IssueType.cs, IssueLabel.cs,
    User.cs, Issue.cs, IssueComment.cs
```

- Root namespace is `EigenfocusApi`; sub-namespaces mirror folders (`EigenfocusApi.Endpoints`, etc.).
- Files are one type per file, named after the type (e.g. `IssueRepository.cs` contains `IssueRepository`).
- Models are plain DTOs with public auto-properties — no behavior, no annotations.

### Endpoint Registration

Each endpoint group is a `public static class` in `src/Endpoints/` with a single `public static void Map(IEndpointRouteBuilder app)` method. `Program.cs:42-44` calls each group's `Map`. Handlers themselves are `private static async Task<IResult>` methods that take route params, optional query params, a `JsonElement body` for write endpoints, and DI dependencies (currently just `IDbConnectionFactory`) directly as parameters — Minimal API resolves them. See `src/Endpoints/ReadEndpoints.cs:15-31` for the registration pattern and `src/Endpoints/ReadEndpoints.cs:115-131` for a handler with query parameters.

All routes live under the `/api` group (`app.MapGroup("/api")`); `/health` is the sole exception and is mapped inline in `Program.cs:40`.

### Data Access

- Connections come from `IDbConnectionFactory.Create()` (`src/Data/DbConnectionFactory.cs`) which returns an opened `SqliteConnection`. Always `using var conn = factory.Create();` at the top of a handler.
- Dapper is configured **once** in `Program.cs:10` with `DefaultTypeMap.MatchNamesWithUnderscores = true`, so snake_case DB columns (`project_id`, `created_at`) map to PascalCase POCO props (`ProjectId`, `CreatedAt`) automatically. Do not annotate properties.
- **Always select an explicit column list — never `SELECT *`.** See the constant `IssueRepository.IssueColumns` (`src/Data/IssueRepository.cs:14`) and every query in `ReadEndpoints.cs`.
- The `users` query column list is fixed: `id, alias, locale, timezone, role, created_at` (`src/Endpoints/ReadEndpoints.cs:98`). **Do not** join or select from the `accounts` table; user identity exposed by this API lives entirely on `users`.
- Reserved words are quoted with double quotes inside the SQL string, e.g. `""default""` in `ReadEndpoints.cs:76`.

### Issue Hydration (mandatory)

Every endpoint that returns an issue MUST load it through `IssueRepository.LoadByIdAsync` or `IssueRepository.LoadManyAsync` (`src/Data/IssueRepository.cs:23,39`). These methods own:

- The canonical column list for issues.
- `custom_fields` JSON parsing into `Dictionary<string, JsonElement>`.
- Label hydration via a single follow-up query against `issue_label_links` + `issue_labels`.

Mutation handlers commit their transaction first, then call `LoadByIdAsync` to build the response — see `IssueEndpoints.cs:107` and `IssueEndpoints.cs:249`.

### Error Envelope

Use the helpers in `src/Endpoints/ApiResults.cs`:

- `ApiResults.BadRequest(detail)` → 400 `{ "error": "Validation failed", "detail": "..." }`
- `ApiResults.NotFound(detail)` → 404 `{ "error": "Not found", "detail": "..." }`
- `ApiResults.Conflict(detail)` → 409 `{ "error": "Constraint violation", "detail": "..." }`
- 401 is emitted by `ApiKeyMiddleware` directly (`src/Auth/ApiKeyMiddleware.cs:48`).
- 500 is emitted by the global exception handler in `Program.cs:23-36`.

Status-code mapping (400/401/404/409/500) is specified in `API_Documentation.md`. Use `BadRequest` for malformed input or missing required fields; `Conflict` for foreign-key / scope violations (e.g. label belongs to a different project); `NotFound` for missing primary entities.

### Partial-Update / PUT Pattern

PUT and PATCH endpoints accept the raw body as a `JsonElement` rather than a typed DTO so absent fields are distinguishable from explicit-null. The pattern lives in `IssueEndpoints.cs:111-251` (`UpdateIssue`):

- `TryGetProperty(body, "field", out var prop)` — was the key present at all?
- `TryReadLong(prop, out var value)` — reads a number or null (returns `false` for any other kind, so the handler can return `BadRequest`).
- `TryReadDate(prop, out var value)` — same shape for ISO date strings.
- Each present field appends one `"col = @param"` clause to a `List<string> updates` and one parameter to a `DynamicParameters`.

Reuse these helpers (currently `private static` in `IssueEndpoints.cs:332-394`) when adding new partial-update endpoints; if they need to be shared, promote them rather than duplicating.

### Transactions

Any write that touches more than one table MUST run inside a single `IDbTransaction` opened on the same connection. Established cases:

- **Issue create**: insert into `issues` + insert into `issue_label_links` (`IssueEndpoints.cs:62-105`).
- **Issue update with `labelIds`**: update `issues` + diff into `issue_label_links` (insert added, delete removed) (`IssueEndpoints.cs:210-247`).
- **Comment create**: insert into `issue_comments` + increment `issues.comments_count` (`CommentEndpoints.cs:43-56`).

Commit with `tx.Commit()`; the `using` declaration handles rollback on exception.

### Counter Cache & Timestamps

- `issues.comments_count` is a Rails-style counter cache **maintained by this app** — there is no DB trigger. Increment it in the same transaction as any `issue_comments` insert (`CommentEndpoints.cs:52`). A future delete-comment endpoint must decrement it the same way.
- `updated_at` must be set explicitly on every `UPDATE` — Rails model callbacks do not fire from this process. Use `DateTime.UtcNow` captured once per handler as `@now`. See `IssueEndpoints.cs:214,272,292` and `CommentEndpoints.cs:53`.
- `created_at` / `updated_at` are both set on inserts (same `@now` value).

### `custom_fields` Read-Merge-Write

`issues.custom_fields` is stored as a JSON string. The PATCH `/issues/{id}/custom-fields` endpoint reads the current value, merges incoming keys on top (incoming wins per-key, untouched keys preserved), and writes the serialized result — see `IssueEndpoints.MergeCustomFields` (`src/Endpoints/IssueEndpoints.cs:299-317`). Do not implement custom-field updates as a blind overwrite.

### Rank on Create

New issues get `rank = MAX(rank) + 1` scoped to the project (`IssueEndpoints.cs:64-67`). The MAX query runs inside the create transaction. No reordering endpoint exists yet; when added, it must also run inside a transaction.

### Scope Validation

References from one table to another are validated against `project_id` before the write:

- `statusId` / `typeId` / `labelId` must belong to the same `project_id` as the issue (returns 409 if not). See `IssueEndpoints.BelongsToProject` (`src/Endpoints/IssueEndpoints.cs:405-408`) and its callers.
- `assigneeId` and comment `authorId` are validated for existence in `users` only (returns 409 if missing); they are not project-scoped.
- Parent entities (`projectId`, `issueId` in route) are validated for existence and return 404 if missing.

### JSON & Naming

- camelCase wire format is configured once in `Program.cs:12-17` via `ConfigureHttpJsonOptions` (`PropertyNamingPolicy = CamelCase`, `PropertyNameCaseInsensitive = true`, no ignore conditions). **Do not** annotate individual properties with `[JsonPropertyName]`.
- POCO properties are PascalCase; SQL column lists are snake_case; route segments are kebab-case (e.g. `/issues/{id}/custom-fields`).

### Auth

- `ApiKeyMiddleware` (`src/Auth/ApiKeyMiddleware.cs`) compares the `X-API-Key` header to `configuration["ApiKey"]` with `StringComparison.Ordinal`. Missing/empty server key → 401 with "Server API key is not configured."; missing/wrong header → 401 with "Missing or invalid API key.".
- `/health` is the only anonymous path (`ApiKeyMiddleware.cs:45-46`). To add another anonymous path, extend `IsAnonymousPath` — do not bypass the middleware in `Program.cs`.
- The middleware is registered before any endpoint mapping in `Program.cs:38`.

### Open Risk: Column Names

Column names in the SELECT/INSERT/UPDATE strings (e.g. `archived_at`, `time_tracking_enabled`, `open_to_all_users`, `group_id`, `hex_color`, `comments_count`) were inferred from the Rails snake_case conventions described in `API_Documentation.md` and have not yet been validated against a real `production.sqlite3`. The first run will surface any mismatches as Dapper / SQLite errors; the fix is a mechanical column-name update in the affected query — do not refactor the surrounding code.

### Exemplar Feature

> When adding a new write endpoint, follow the pattern established by **create-issue**.

1. `src/Endpoints/IssueEndpoints.cs:15` — register the route inside `Map` on the `/api` group.
2. `src/Endpoints/IssueEndpoints.cs:22-109` (`CreateIssue`) — handler signature: route params + `JsonElement body` + `IDbConnectionFactory`.
3. Validate body shape and required fields with the `TryGet*` helpers; return `ApiResults.BadRequest` for any malformed/missing input.
4. Open the connection (`factory.Create()`), then run existence + scope checks; return `ApiResults.NotFound` for missing parents, `ApiResults.Conflict` for cross-project / FK violations.
5. Open a transaction, compute any derived values (e.g. `rank = MAX + 1`), `INSERT ... RETURNING id`, then write side-effect rows (labels, counter caches) inside the same `tx`. Commit explicitly.
6. After commit, return the canonical response by loading through `IssueRepository.LoadByIdAsync` and using `Results.Created($"/api/...", issue)`.

For the read side, mirror `src/Endpoints/ReadEndpoints.cs:115-131` (`GetProjectIssues`): existence-check the parent, build a `(string where, DynamicParameters p)` tuple for filters, and route through `IssueRepository.LoadManyAsync` so labels and `custom_fields` are hydrated identically.
