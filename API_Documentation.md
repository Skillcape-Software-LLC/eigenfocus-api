# Project Management API — Design Documentation

## Overview

A .NET Core Minimal API providing focused read/write access to an Eigenfocus SQLite database. Covers issue management, custom fields, user assignment, and comments.

---

## Stack

| Concern | Choice |
|---|---|
| Framework | .NET 8 Minimal API |
| Data access | Dapper + `Microsoft.Data.Sqlite` |
| Auth | Static API key middleware (`X-API-Key` header) |
| Config | `appsettings.json` + environment variables via `compose.yml` |
| Serialization | `System.Text.Json` (camelCase) |

---

## Authentication

Every request must include:

```
X-API-Key: <your-key>
```

```yaml
# compose.yml
environment:
  - ApiKey=your-secret-key-here
  - ConnectionStrings__DefaultConnection=Data Source=/data/production.sqlite3;Mode=ReadWrite;Cache=Shared;Journal Mode=WAL
```

Middleware rejects missing or invalid keys with `401 Unauthorized`.

---

## Project Structure

```
/src
  Program.cs
  Auth/
    ApiKeyMiddleware.cs
  Endpoints/
    IssueEndpoints.cs
    CommentEndpoints.cs
    ReadEndpoints.cs        # All GET operations
  Models/
    Issue.cs
    IssueComment.cs
    IssueLabel.cs
    IssueStatus.cs
    IssueType.cs
    Project.cs
    User.cs
  Data/
    DbConnectionFactory.cs
```

---

## Endpoints

### Health

| Method | Path | Description |
|---|---|---|
| `GET` | `/health` | Returns 200 — no auth required |

---

### Read Operations

#### Projects

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/projects` | List all active (non-archived) projects |
| `GET` | `/api/projects/{id}` | Get project by ID |
| `GET` | `/api/projects/{id}/statuses` | List issue statuses for a project |
| `GET` | `/api/projects/{id}/types` | List issue types for a project |
| `GET` | `/api/projects/{id}/labels` | List issue labels for a project |

**Project model:**
```json
{
  "id": 1,
  "name": "string",
  "archivedAt": null,
  "timeTrackingEnabled": true,
  "openToAllUsers": true,
  "groupId": null,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

**Status model:**
```json
{
  "id": 1,
  "name": "In Progress",
  "projectId": 1,
  "initial": false,
  "final": false
}
```

**Type model:**
```json
{
  "id": 1,
  "name": "Bug",
  "projectId": 1,
  "default": false,
  "hexColor": "#FF0000"
}
```

**Label model:**
```json
{
  "id": 1,
  "title": "string",
  "projectId": 1,
  "hexColor": "#FF0000"
}
```

---

#### Users

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/users` | List all users |
| `GET` | `/api/users/{id}` | Get user by ID |

**User model:**
```json
{
  "id": 1,
  "alias": "string",
  "locale": "en",
  "timezone": "America/Chicago",
  "role": "admin",
  "createdAt": "2024-01-01T00:00:00Z"
}
```

> Never expose `accounts` or any field from that table, especially `encrypted_password`.

---

#### Issues

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/projects/{projectId}/issues` | List issues for a project |
| `GET` | `/api/issues/{id}` | Get issue by ID (includes labels, custom fields) |

**Query params — GET /api/projects/{projectId}/issues:**
- `statusId=1`
- `typeId=2`
- `assigneeId=3`
- `archivingStatus=active` — one of `active` (default), `archived`, `finished`, `all`
- `parentId=0` — pass `0` to filter top-level issues only; omit for all

**Issue model:**
```json
{
  "id": 1,
  "title": "string",
  "description": "string",
  "projectId": 1,
  "statusId": 1,
  "typeId": 1,
  "assigneeId": null,
  "parentId": null,
  "rank": 100,
  "dueDate": null,
  "startDate": null,
  "endDate": null,
  "archivedAt": null,
  "finishedAt": null,
  "commentsCount": 0,
  "customFields": {},
  "labels": [],
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

---

#### Comments

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/issues/{issueId}/comments` | List comments on an issue |

**Comment model:**
```json
{
  "id": 1,
  "content": "string",
  "issueId": 1,
  "authorId": 1,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

---

### Write Operations

#### Create Issue

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/projects/{projectId}/issues` | Create a new issue |

**Request body:**
```json
{
  "title": "string",
  "description": "string",
  "statusId": 1,
  "typeId": 1,
  "assigneeId": null,
  "parentId": null,
  "dueDate": null,
  "startDate": null,
  "endDate": null,
  "labelIds": []
}
```

**Validation:**
- `title` — required; return `400` if blank
- `statusId` — required; must belong to the project
- `typeId` — required; must belong to the project
- `assigneeId` — optional; must be a valid user ID if provided
- `labelIds` — optional; each must belong to the project

**Implementation notes:**
- Assign `rank` as `SELECT MAX(rank) FROM issues WHERE project_id = @projectId` + 1. Default to 1 if no issues exist yet.
- Insert label links into `issue_label_links` after issue insert, within the same transaction.
- Returns the created issue model with `201 Created`.

---

#### Update Issue

| Method | Path | Description |
|---|---|---|
| `PUT` | `/api/issues/{id}` | Update issue fields |

Supports partial update — only fields present in the request body are applied.

**Request body (all fields optional):**
```json
{
  "title": "string",
  "description": "string",
  "statusId": 1,
  "typeId": 1,
  "assigneeId": 1,
  "dueDate": null,
  "startDate": null,
  "endDate": null,
  "labelIds": []
}
```

**Validation:**
- `statusId` — if provided, must belong to the same project as the issue
- `typeId` — if provided, must belong to the same project as the issue
- `assigneeId` — if provided, must be a valid user ID; pass `null` explicitly to unassign
- `labelIds` — if provided, replaces the full label set. Each ID must belong to the project. Diff against current `issue_label_links` and insert/delete accordingly within the same transaction.

---

#### Update Custom Fields

| Method | Path | Description |
|---|---|---|
| `PATCH` | `/api/issues/{id}/custom-fields` | Merge-update the `custom_fields` JSON blob |

**Request body:**
```json
{
  "fieldKey": "value",
  "anotherKey": 42
}
```

**Implementation notes:**
- `custom_fields` is a `jsonb` column stored as a JSON string in SQLite.
- Merge the incoming object into the existing blob — do not replace the entire object. Read current value, merge at the key level, write back.
- No server-side type validation against `custom_field_definitions` — pass values through as-is.
- Returns the full updated issue model.

---

#### Mark Issue Finished / Unfinished

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/issues/{id}/finish` | Set `finished_at` to current UTC timestamp |
| `POST` | `/api/issues/{id}/unfinish` | Clear `finished_at` to null |

> Finished and archived are independent states. An issue can be finished without being archived.

---

#### Leave a Comment

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/issues/{issueId}/comments` | Add a comment to an issue |

**Request body:**
```json
{
  "content": "string",
  "authorId": 1
}
```

**Validation:**
- `content` — required; return `400` if blank or null
- `authorId` — required; must be a valid user ID

**Implementation notes:**
- Insert into `issue_comments`.
- In the same transaction, execute `UPDATE issues SET comments_count = comments_count + 1 WHERE id = @issueId`.
- Returns the created comment model with `201 Created`.

---

## Error Responses

All errors return a consistent envelope:

```json
{
  "error": "Validation failed",
  "detail": "title is required."
}
```

| Status | When |
|---|---|
| `400` | Validation failure / malformed body |
| `401` | Missing or invalid `X-API-Key` |
| `404` | Resource not found |
| `409` | Constraint violation (e.g. `statusId` doesn't belong to this project) |
| `500` | Unhandled server error |

---

## Implementation Notes

- **WAL mode:** Required to avoid `SQLITE_BUSY` lock contention with Eigenfocus writing concurrently. Set in connection string: `Journal Mode=WAL`
- **`comments_count`:** Rails counter cache — no DB trigger. Must be maintained manually: increment on comment insert, decrement on comment delete, in the same transaction.
- **`custom_fields`:** Read-merge-write pattern. Never overwrite the full blob from a partial update request.
- **`rank`:** Assign `MAX(rank) + 1` on issue create. Never expose reorder endpoints — rank manipulation belongs to the app.
- **camelCase:** Configure `JsonSerializerOptions` with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`.
- **Scoped validation:** `statusId`, `typeId`, and `labelIds` must all be validated against the issue's `project_id` — not just existence, but project membership.
