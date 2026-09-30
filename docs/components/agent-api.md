# Agent.Api — presentation layer

ASP.NET Core 9 web project and **composition root** of the backend. It maps HTTP requests to application services, streams responses, and wires all dependencies together. It contains no business logic: controllers translate between HTTP contracts and application commands/DTOs.

- **Project:** `Agent.Api/Agent.Api.csproj` (`Microsoft.NET.Sdk.Web`, `net9.0`)
- **References:** `Agent.Application`, `Agent.Infrastructure`; packages `Microsoft.AspNetCore.OpenApi`, `Swashbuckle.AspNetCore`
- **Runtime:** `https://localhost:7098` / `http://localhost:5054` (development launch profiles)

## Composition root — `Program.cs`

Service registration and middleware order:

1. `AddControllers().AddJsonOptions(...)` — enums serialize as strings (`JsonStringEnumConverter`).
2. `AddOpenApi()` + `AddSwaggerGen()` — OpenAPI document and Swagger UI (mapped only in Development: `/openapi/v1.json`, `/swagger`).
3. `AddProblemDetails()` + `UseExceptionHandler()` — unhandled exceptions surface as RFC 7807 `ProblemDetails`.
4. CORS policy `AllowFrontend` — origins `http://localhost:4200` and `https://localhost:4200` (the Angular dev server), any header, any method.
5. `AddAgentApplication()` — application services (see [agent-application.md](agent-application.md#dependency-injection)).
6. `AddAgentInfrastructure(builder.Configuration)` — database, JWT, Ollama, tools, storage (see [agent-infrastructure.md](agent-infrastructure.md#dependency-injection)). **Validates required configuration at startup.**
7. Pipeline: developer API docs (Development only) → exception handler → HTTPS redirection → CORS → `UseAuthentication` → `UseAuthorization` → `/health` → controllers.

`public partial class Program` exists so integration tests can reference the entry point (`WebApplicationFactory<Program>`).

## Controllers

### `AuthController` — `api/auth`

Anonymous account endpoints; thin mapping over `IAuthService`.

| Action | Route | Success | Maps exceptions |
| --- | --- | --- | --- |
| `Register` | `POST api/auth/register` | 201 `AuthResultDto` | `ConflictException` → 409, `RequestValidationException` → 400 |
| `Login` | `POST api/auth/login` | 200 `AuthResultDto` | `UnauthorizedAccessException` → 401 |

### `ConversationsController` — `api/conversations` (`[Authorize]`)

The primary chat API. All operations are scoped to the authenticated user via `IConversationTurnService`; ownership is verified before a streaming response begins so foreign ids return a real 404.

| Action | Route | Notes |
| --- | --- | --- |
| `Create` | `POST /api/conversations` | 201 `ConversationSummaryDto` |
| `List` | `GET /api/conversations?take=30` | `take` clamped 1–100 |
| `Get` | `GET /api/conversations/{id}` | Detail + first message page; 404 when not owned |
| `GetMessages` | `GET /api/conversations/{id}/messages` | Cursor paging via `beforeSequence`, `take` (1–100) |
| `SendMessage` | `POST /api/conversations/{id}/messages` | Multipart; **SSE stream** — see below |
| `Delete` | `DELETE /api/conversations/{id}` | 204; 404 when not owned |

`SendMessage` behavior:

- Pre-stream validation: 404 for unknown/foreign conversation; 400 (`ProblemDetails` JSON) when the attachment is empty, over 10 MB, or not one of `.txt .csv .json .xml .md .pdf .docx`.
- Response headers: `text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no` (disables proxy buffering for real-time delivery).
- Serializes each `ConversationStreamEvent` as `event: {type}\ndata: {json}\n\n` and flushes per event.
- `RequestValidationException` mid-stream → an `error` SSE event; client abort → the service finalizes the placeholder message as `Cancelled`; any other failure → a generic `error` event.
- Request size limit: 11 MB (`[RequestSizeLimit]`).

### `ChatController` — legacy endpoint

`POST /api/chat` (anonymous, multipart). Uses the in-memory `IConversationStore` (keyed by form `conversationId` → `X-Conversation-Id` header → `"default"`) and the `IAgent` interface. Writes **raw text chunks** with a `text/event-stream` content type (no SSE envelopes). Kept for lightweight anonymous chat; new clients should use the authorized conversations API.

## HTTP contracts (`Contracts/`)

| Type | Fields | Validation |
| --- | --- | --- |
| `RegisterRequest` | `Email`, `UserName`, `DisplayName`, `Password` | email ≤ 256; username 3–64; display name ≤ 128; password 12–128 |
| `LoginRequest` | `EmailOrUserName`, `Password` | required; ≤ 256 / ≤ 128 |
| `ChatRequest` | `Prompt`, `File?`, `ConversationId?` | `Prompt` required (multipart form) |
| `CreateConversationRequest` | `Title?` | ≤ 200 |
| `SendConversationMessageRequest` | `Prompt`, `File?` | required, ≤ 16,000 |

API contracts are deliberately separate from application commands (`RegisterUserCommand`, `SendMessageCommand`, …) so the wire format can evolve independently of the use cases.

## Cross-cutting behavior

| Concern | Implementation |
| --- | --- |
| Authentication | JWT bearer registered by infrastructure; `[Authorize]` on conversations |
| Errors | `ProblemDetails` everywhere; exceptions mapped in controllers or by the exception-handler middleware |
| CORS | Only the Angular dev origins (`localhost:4200`) |
| Health | `GET /health` includes an EF Core `DbContext` check against SQL Server |
| API docs | Swagger UI / OpenAPI in Development only |

For endpoint payloads and the SSE event catalog see [../api-reference.md](../api-reference.md).
