# Eigenfocus API

A read/write [.NET 8 Minimal API](https://learn.microsoft.com/aspnet/core/fundamentals/minimal-apis) that puts an HTTP surface in front of your existing [Eigenfocus](https://eigenfocus.com/) SQLite database.

> **Independent third-party integration.** This project is **not** sponsored by, affiliated with, partnered with, or endorsed by Eigenfocus or its maintainers. It is an unofficial utility built by [Skillcape Software](https://skillcapesoftware.com) to read and write data in an Eigenfocus install you already operate. "Eigenfocus" is the property of its respective owner.
>
> Tested against **Eigenfocus Pro 1.4.0**.

## What it does

Eigenfocus stores its data in a SQLite database owned by the upstream Rails app. This service runs alongside your Eigenfocus install, reads from (and writes to) that same database, and exposes a focused HTTP API for issues, comments, projects, users, labels, and custom fields — so external tools and integrations can interact with Eigenfocus data without driving the web UI.

For the full endpoint contract — request shapes, validation rules, status codes — see **[API_Documentation.md](API_Documentation.md)**.

## Install (Docker)

You need:

- Docker with `docker compose` available.
- A running (or at-rest) Eigenfocus Pro install on the same host, and the host path where it keeps its `production.sqlite3` file.
- A secret value of your choosing to use as the API key.

### 1. Create a `compose.yml`

Drop this into a new directory on the host:

```yaml
services:
  eigenfocus-api:
    image: ghcr.io/skillcape-software-llc/eigenfocus-api:latest
    container_name: eigenfocus-api
    restart: unless-stopped
    ports:
      - "8080:8080"
    environment:
      - ApiKey=replace-with-a-long-random-secret
      - ConnectionStrings__DefaultConnection=Data Source=/data/database/production.sqlite3;Mode=ReadWrite;Cache=Shared
    volumes:
      # Map the directory on the HOST that already contains your
      # Eigenfocus production.sqlite3 file to /data inside the container.
      # The API expects to find /data/production.sqlite3.
      - /path/to/eigenfocus/data:/data
```

You are **not** moving or copying your Eigenfocus database. You are mounting the directory that already contains it so this container can read from (and write to) it in place.

### 2. Set your API key

Replace `replace-with-a-long-random-secret` with a strong value. Every API request must send this exact value in the `X-API-Key` header.

### 3. Bring it up

```bash
docker compose up -d
curl http://localhost:8080/health
```

A `200 OK` from `/health` means the container is up. From there, hit any endpoint described in [API_Documentation.md](API_Documentation.md) with the `X-API-Key` header attached.

## Configuration

| Variable | Required | Description |
|---|---|---|
| `ApiKey` | yes | Static value compared against the `X-API-Key` header on every request. |
| `ConnectionStrings__DefaultConnection` | no | SQLite connection string. Defaults to `Data Source=/data/production.sqlite3;Mode=ReadWrite;Cache=Shared` — only override if your database lives at a non-standard path inside the container. |

WAL journaling is enabled automatically on every connection the API opens, so this service will not block the Rails app's writers (and vice versa). You don't need — and shouldn't add — `Journal Mode=WAL` to the connection string yourself.

## Authenticating requests

Every request must include the API key:

```
X-API-Key: <your-key>
```

`/health` is the only anonymous endpoint. Anything else without a valid `X-API-Key` returns `401`.

## Upgrading

Images are published to GitHub Container Registry at `ghcr.io/skillcape-software-llc/eigenfocus-api` with floating and pinned tags:

- `:latest` — newest published release
- `:1`, `:1.2` — track a major or minor line
- `:1.2.3` — pin an exact version

To control upgrades, change the `image:` line in your `compose.yml` to the tag you want, then:

```bash
docker compose pull
docker compose up -d
```

## License

Licensed under the [Apache License, Version 2.0](LICENSE). Copyright © 2026 Skillcape Software LLC.

## Links

- [API_Documentation.md](API_Documentation.md) — endpoint contract (source of truth)
- [Eigenfocus](https://eigenfocus.com/) — the upstream product this integrates with (independent of this project)
- [Skillcape Software](https://skillcape.software) — maintainer
