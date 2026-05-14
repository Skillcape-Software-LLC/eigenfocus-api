# Eigenfocus API

A read/write [.NET 8 Minimal API](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis) that wraps an existing [Eigenfocus](https://eigenfocus.com/) SQLite database. The schema is owned by the upstream Rails app — this service exposes a focused HTTP surface (issues, comments, projects, users, custom fields) for integrations that need to read or mutate Eigenfocus data without going through the web UI.

For the full endpoint contract see **[API_Documentation.md](API_Documentation.md)**. Supported Eigenfocus version: pro-edition:1.4.0 

## Quick start (Docker)

Each tagged release publishes a multi-arch image to GitHub Container Registry and attaches a deployment bundle to the GitHub Release.

1. Grab the bundle from the [latest release](../../releases/latest) — either the `eigenfocus-api-vX.Y.Z-docker.zip` archive or the loose `compose.yml` and `.env.example` files.
2. Drop them in a working directory and create your `.env`:
   ```bash
   cp .env.example .env
   # edit .env and set API_KEY
   ```
3. Place your Eigenfocus database at `./data/production.sqlite3` (the path the compose file mounts into the container).
4. Bring it up:
   ```bash
   docker compose up -d
   curl http://localhost:8080/health
   ```

To pin to a specific version, edit `compose.yml` and change the `image:` tag; major/minor floating tags (e.g. `:1`, `:1.2`) are also published.

## Configuration

| Variable | Required | Description |
|---|---|---|
| `ApiKey` | yes | Static value compared against the `X-API-Key` request header. |
| `ConnectionStrings__DefaultConnection` | no | SQLite connection string. Defaults to `Data Source=/data/production.sqlite3;Mode=ReadWrite;Cache=Shared;Journal Mode=WAL`. |

`Journal Mode=WAL` is required when the upstream Rails app writes to the same database concurrently — without it readers and writers will block each other (`SQLITE_BUSY`).

Every request must include the API key:

```
X-API-Key: <your-key>
```

`/health` is the only anonymous endpoint.

## Local development

Requires the .NET 8 SDK.

```bash
dotnet build
dotnet run
```

Supply `ApiKey` and the connection string via `appsettings.Development.json`, `dotnet user-secrets`, or environment variables — the committed `appsettings.json` ships with an empty key and a container-style path that will not exist locally.

The repo has no test or lint setup; see [CLAUDE.md](CLAUDE.md) for the conventions that govern code changes.

## Releasing

Releases are tag-driven. Push a `vX.Y.Z` tag and the [`Publish Docker image`](.github/workflows/docker-publish.yml) workflow will:

1. Build a multi-arch (`linux/amd64`, `linux/arm64`) image from the repo `Dockerfile`.
2. Push it to `ghcr.io/<owner>/<repo>` with tags `X.Y.Z`, `X.Y`, `X`, and `latest`.
3. Generate a deployment bundle (`compose.yml` pinned to this tag + `.env.example`) and attach it to the auto-created GitHub Release.

```bash
git tag v1.0.0
git push origin v1.0.0
```

**First release only:** the package is created private. Open the repo's *Packages* tab and flip visibility to public (or grant access to the consumers) once.

## Repository layout

```
src/                Minimal API source — see CLAUDE.md for conventions
Dockerfile          Multi-stage build, runs on port 8080
compose.yml         Local dev compose (builds from source)
API_Documentation.md   Endpoint contract — source of truth
CLAUDE.md           Project conventions and exemplar feature
.github/workflows/  CI — Docker publish on tag
```
