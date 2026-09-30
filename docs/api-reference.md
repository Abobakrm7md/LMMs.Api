# HTTP API reference

Base URLs (Development, from `Agent.Api/Properties/launchSettings.json`):

- HTTPS: `https://localhost:7098`
- HTTP: `http://localhost:5054`

All request and response bodies are JSON unless stated otherwise. Enum values are serialized as strings (a `JsonStringEnumConverter` is registered in `Program.cs`). In Development, interactive documentation is available at `/swagger` and the OpenAPI document at `/openapi/v1.json`.

## Endpoint summary

| Method | Route | Auth | Description |
| --- | --- | --- | --- |
| `POST` | `/api/auth/register` | anonymous | Create an account; returns a JWT |
| `POST` | `/api/auth/login` | anonymous | Sign in; returns a JWT |
| `POST` | `/api/chat` | anonymous | Legacy chat: streams a raw text answer (in-memory conversation) |
| `POST` | `/api/conversations` | Bearer | Create a persisted conversation |
| `GET` | `/api/conversations` | Bearer | List the caller's conversations |
| `GET` | `/api/conversations/{id}` | Bearer | Conversation detail with a message page |
| `GET` | `/api/conversations/{id}/messages` | Bearer | Paged message history |
| `POST` | `/api/conversations/{id}/messages` | Bearer | Send a message; streamed SSE answer |
| `DELETE` | `/api/conversations/{id}` | Bearer | Delete a conversation |
| `GET` | `/health` | anonymous | Health check (includes SQL Server via EF Core) |

Authentication uses JWT bearer tokens: send `Authorization: Bearer <token>` on protected routes. Tokens are issued by register/login, expire after `Authentication:Jwt:AccessTokenMinutes` (default 30, clamped 5–120), and carry the user id (`nameidentifier`), username (`name`), and email (`email`) claims.

## Authentication

### `POST /api/auth/register`

Creates a user and returns an access token.

```json
{
  "email": "ada@example.com",
  "userName": "ada.lovelace",
  "displayName": "Ada Lovelace",
  "password": "at-least-12-characters"
}
```

| Field | Rules |
| --- | --- |
| `email` | required, valid email, ≤ 256 chars |
| `userName` | required, 3–64 chars; letters, digits, `.`, `_`, `-` only |
| `displayName` | required, 1–128 chars |
| `password` | required, 12–128 chars |

- **201 Created** — `AuthResultDto` (below). Also returned when the email/username already exists? No — duplicates return **409 Conflict** (`ProblemDetails`).
- **400 Bad Request** — validation failure (`ValidationProblemDetails`).
- **409 Conflict** — an account with that email or username already exists.

### `POST /api/auth/login`

```json
{ "emailOrUserName": "ada.lovelace", "password": "at-least-12-characters" }
```

- **200 OK** — `AuthResultDto`; `LastLoginAt` is updated on the user record.
- **401 Unauthorized** — invalid identifier or password (`ProblemDetails`).

### `AuthResultDto`

```json
{
  "token": { "value": "<jwt>", "expiresAt": "2026-09-30T15:30:00+00:00" },
  "user": { "id": "guid", "email": "ada@example.com", "userName": "ada.lovelace", "displayName": "Ada Lovelace" }
}
```

## Conversations (persisted, authorized)

All routes below require a valid bearer token and only ever expose data owned by the caller; foreign ids return **404 Not Found**.

### `POST /api/conversations`

```json
{ "title": "Optional title (≤ 200 chars)" }
```

- **201 Created** — `ConversationSummaryDto`; a missing/blank title becomes `"New chat"`.
- **400 Bad Request** — title longer than 200 characters.

### `GET /api/conversations?take=30`

Returns up to `take` (clamped 1–100, default 30) summaries ordered by `updatedAt` descending.

`ConversationSummaryDto`:

```json
{
  "id": "guid",
  "title": "New chat",
  "createdAt": "2026-09-30T12:00:00+00:00",
  "updatedAt": "2026-09-30T12:05:00+00:00",
  "lastMessageAt": "2026-09-30T12:05:00+00:00"
}
```

### `GET /api/conversations/{id}?beforeSequence=&take=100`

Returns `ConversationDetailDto` — the summary fields plus a `messagePage`. **404** when not found/not owned.

### `GET /api/conversations/{id}/messages?beforeSequence=&take=100`

Returns only the `MessagePageDto`. Paging is cursor-based: pass `beforeSequence` to fetch messages with a lower `SequenceNumber`; `nextBeforeSequence` in the response is the cursor for the next (older) page, or `null` when there is none. `take` is clamped 1–100.

`MessagePageDto`:

```json
{
  "messages": [
    {
      "id": "guid",
      "sequenceNumber": 1,
      "role": "User",
      "status": "Completed",
      "content": "Hello",
      "createdAt": "2026-09-30T12:00:00+00:00",
      "updatedAt": "2026-09-30T12:00:00+00:00",
      "modelName": null
    }
  ],
  "nextBeforeSequence": null
}
```

`role` is `"User"` or `"Assistant"`; `status` is `"Streaming"`, `"Completed"`, `"Cancelled"`, or `"Failed"`.

### `POST /api/conversations/{id}/messages`

`multipart/form-data` (request size limit 11 MB):

| Field | Rules |
| --- | --- |
| `prompt` | required, 1–16,000 chars after trimming |
| `file` | optional; `txt`, `csv`, `json`, `xml`, `md`, `pdf`, or `docx`; non-empty, ≤ 10 MB |

The response is a **Server-Sent Events** stream (`Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`).

#### SSE events

Each frame is `event: <type>\ndata: <ConversationStreamEvent JSON>\n\n`:

| `type` | Payload fields | Meaning |
| --- | --- | --- |
| `message-start` | `userMessageId`, `assistantMessageId` | Both messages were persisted; the assistant message is a `Streaming` placeholder |
| `delta` | `assistantMessageId`, `text` | One streamed token chunk of the answer |
| `completed` | `assistantMessageId`, `status` | The answer finished and was persisted as `Completed` |
| `error` | `text` | The assistant could not finish; the placeholder is finalized as `Failed` (or `Cancelled` when the client disconnects) |

Example stream:

```text
event: message-start
data: {"type":"message-start","userMessageId":"...","assistantMessageId":"..."}

event: delta
data: {"type":"delta","assistantMessageId":"...","text":"Today is "}

event: delta
data: {"type":"delta","assistantMessageId":"...","text":"September 30, 2026."}

event: completed
data: {"type":"completed","assistantMessageId":"...","status":"completed"}
```

Pre-stream failures (unknown/foreign conversation id, invalid attachment) are returned as ordinary **404**/**400** responses before the stream starts. Mid-stream validation failures are delivered as an `error` event.

Server-side notes: the first prompt auto-titles a `"New chat"` conversation; attachments are stored before streaming starts and are visible to the agent through the `read_file` tool; every completed tool call is persisted as a `ToolExecution` row linked to the assistant message.

### `DELETE /api/conversations/{id}`

Deletes the conversation and (via cascade) its messages and tool executions. **204 No Content** on success; **404** when not found/not owned.

## Legacy chat (anonymous, in-memory)

### `POST /api/chat`

`multipart/form-data`: `prompt` (required), `file` (optional), `conversationId` (optional — falls back to the `X-Conversation-Id` header, then `"default"`).

Streams the raw answer text (no SSE event envelopes, though the content type is `text/event-stream`; each chunk is written and flushed as-is). Conversation history is process-local (`InMemoryConversationStore`, trimmed to the last 20 messages) and is **not** tied to a user account; this endpoint predates the persisted conversation API and is kept for lightweight/anonymous use. Prefer `/api/conversations/{id}/messages` for real clients.

## Errors

| Status | Shape | When |
| --- | --- | --- |
| 400 | `ValidationProblemDetails` / `ProblemDetails` | Invalid request fields or attachment |
| 401 | `ProblemDetails` | Bad credentials, or missing/invalid bearer token (empty body from the auth middleware) |
| 404 | empty body | Conversation not found / not owned |
| 409 | `ProblemDetails` | Registration conflict (email or username taken) |
| 500 | `ProblemDetails` | Unhandled exception (via `UseExceptionHandler` + `AddProblemDetails`) |
