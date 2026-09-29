# SQL Server Persistence and User Management Design

**Project:** LMMs.Api
**Status:** Implemented design reference (initial SQL Server/local-auth conversation persistence)
**Scope:** Extend the current AI-agent application with SQL Server persistence, local user accounts, protected conversations, and a conversation-aware Angular UI.

---

## 1. Executive summary

The application already has a useful agent core: it plans tool use, executes a bounded sequence of approved tools, then streams a final model answer. The extension should **keep that core**, while moving responsibility for user identity, conversation ownership, persistence, and request orchestration into application services.

The key design decision is:

> SQL Server becomes the source of truth for users, conversations, messages, and useful tool-execution records. The agent receives a bounded, read-only history context and does not know about EF Core, SQL Server, `HttpContext`, or JWT claims.

The new request path will be:

```text
Angular UI
  -> authenticated API endpoint
  -> ConversationTurnService
       -> ICurrentUser
       -> conversation/message repositories (SQL Server)
       -> IAgent (existing planning/execution core)
       -> tool-execution repository (SQL Server)
  -> SSE/fetch response stream
```

There is no existing persisted history to migrate: the current history is a static in-process list and disappears on restart. The initial migration therefore creates an empty database and replaces the static list rather than importing data.

---

## 2. Existing architecture analysis

### 2.1 Current request flow

| Concern | Current location | Current behavior |
|---|---|---|
| HTTP chat endpoint | `Controllers/ChatController.cs` | `POST /api/chat` receives `ChatRequest` as multipart form data and streams raw response chunks. |
| Chat history | `ChatController.cs` | A process-wide `static List<ChatMessage>` is used for every request. It is not user-scoped, durable, or concurrency-safe. |
| Agent façade | `Services/AgentAnswer.cs` | Forwards the request and chat list to `IAgent`. |
| Agent implementation | `Services/PlanningAgent.cs` | Adds the user message, selects tools, creates a plan, executes tools, builds LLM messages, streams final text, and appends the assistant message. |
| Planning | `Planning/ChatClientPlanner.cs` | Uses the configured `IChatClient` to return a JSON plan or recovery directive. |
| Execution | `Planning/SequentialPlanExecutor.cs` | Resolves prior results, invokes approved tools, and limits total/adaptive steps. |
| Tool invocation | `Planning/AgentToolInvoker.cs` | Matches an approved registered tool and binds arguments. |
| Tool registration | `Program.cs` | Registers calculator, date/time, file-reading, and web-search tools as scoped services. |
| Frontend state | `ai-chat-fe/src/app/app.component.ts` | Holds all messages only in component memory; posts to hard-coded `https://localhost:7098/api/chat`. |

### 2.2 Existing strengths to preserve

- `PlanningAgent`, `ChatClientPlanner`, `SequentialPlanExecutor`, `PlanStepResolver`, and `AgentToolInvoker` already form a useful planner/executor pipeline.
- `AgentToolSelector` already limits which tools are exposed for each request.
- The existing executor has cancellation support, tool-result validation, and bounded execution.
- The API already streams response text using `fetch` on the Angular client.

### 2.3 Current boundaries that must change

1. `ChatController` must stop owning a static global history list.
2. `PlanningAgent` must stop mutating a web-controller-owned `List<ChatMessage>` as the source of truth.
3. `ChatRequest` and `IFormFile` should be converted at the API boundary into an application-level turn request; the agent should not own HTTP upload persistence.
4. Tool execution data needs an application-visible event/result so it can be saved without making the agent depend on EF Core.
5. Every conversation query must be scoped to the authenticated user.

---

## 3. Target architecture

### 3.1 Logical layers

```text
Agent.Api            API host: controllers, authentication middleware, DI, HTTP DTOs
        ↓
Agent.Application     use cases, repository interfaces, current-user abstraction,
                     conversation turn orchestration, agent contracts
        ↓
Agent.Domain          entities, enums, value-oriented rules; no EF Core or HTTP
        ↑
Agent.Infrastructure  EF Core DbContext/configurations/repositories, SQL Server,
                     JWT implementation, attachment storage implementation
```

Recommended projects for the target state:

```text
Agent.Domain
Agent.Application
Agent.Infrastructure
Agent.Api                (ASP.NET Core host and controllers)
Agent.Tests
```

Project references:

```text
LMMs.Api           -> Agent.Application, Agent.Infrastructure
Agent.Infrastructure -> Agent.Application, Agent.Domain
Agent.Application   -> Agent.Domain
Agent.Domain        -> no project dependency
```

This can be introduced incrementally. Initially, the folders and namespaces may live in the existing API project to reduce churn, but **EF configurations and repository implementations must remain separated from agent logic**. Splitting into the projects above should be completed before the persistence surface grows further.

### 3.2 Dependency rules

- Controllers know HTTP, DTOs, and `IConversationTurnService`/`IAuthService`.
- Application services know repository interfaces and `IAgent`; they do not know `DbContext`, SQL Server, JWT, or `HttpContext`.
- `PlanningAgent` knows the agent request/history contract and tools. It does not know users, conversations, repositories, or EF Core.
- Infrastructure implements repositories and `ICurrentUser`; no domain/application type references `HttpContext` directly.
- `DbContext` is used only in Infrastructure repositories/unit-of-work implementations.

---

## 4. Relational database model

All timestamps are UTC `datetimeoffset(3)`. Use `uniqueidentifier` IDs generated by the application (`Guid.NewGuid()`), allowing IDs to be known before inserts and avoiding database round trips. Database names below are illustrative.

### 4.1 `Users`

| Column | SQL type | Required | Notes |
|---|---|---:|---|
| `Id` | `uniqueidentifier` | Yes | Primary key. |
| `Email` | `nvarchar(256)` | Yes | Original normalized-display email. |
| `NormalizedEmail` | `nvarchar(256)` | Yes | Upper-invariant value used for uniqueness/lookups. |
| `UserName` | `nvarchar(64)` | Yes | User-facing login name. |
| `NormalizedUserName` | `nvarchar(64)` | Yes | Upper-invariant value used for uniqueness/lookups. |
| `DisplayName` | `nvarchar(128)` | Yes | Shown in the UI. |
| `PasswordHash` | `nvarchar(512)` | Yes | Only a framework password-hasher output; never plaintext. |
| `CreatedAt` | `datetime2(3)` | Yes | UTC. |
| `UpdatedAt` | `datetime2(3)` | Yes | UTC. |
| `LastLoginAt` | `datetime2(3)` | No | Optional useful audit data. |
| `RowVersion` | `rowversion` | Yes | Optimistic concurrency token. |

Indexes:

- Primary key: `PK_Users(Id)`.
- Unique index: `UX_Users_NormalizedEmail`.
- Unique index: `UX_Users_NormalizedUserName`.

### 4.2 `Conversations`

| Column | SQL type | Required | Notes |
|---|---|---:|---|
| `Id` | `uniqueidentifier` | Yes | Primary key. |
| `UserId` | `uniqueidentifier` | Yes | FK to `Users(Id)`. Owner. |
| `Title` | `nvarchar(200)` | Yes | Starts as `New chat` or a safe title derived from first prompt. |
| `CreatedAt` | `datetime2(3)` | Yes | UTC. |
| `UpdatedAt` | `datetime2(3)` | Yes | UTC; update when a turn is completed or a title changes. |
| `LastMessageAt` | `datetime2(3)` | No | Efficient sort field for the sidebar. |
| `RowVersion` | `rowversion` | Yes | Optimistic concurrency token. |

Indexes:

- Primary key: `PK_Conversations(Id)`.
- Non-unique index: `IX_Conversations_UserId_UpdatedAt` on `(UserId ASC, UpdatedAt DESC)`, including `Id`, `Title`, and `LastMessageAt` where supported. This supports the conversation sidebar without loading messages.

Relationship/delete behavior:

- `Conversation.UserId` is required.
- `User -> Conversations` uses cascade delete only if the product supports full account deletion. Otherwise use `Restrict` and implement a deliberate user-deletion workflow. Do not leave orphaned conversations.

### 4.3 `ChatMessages`

A message is the user-visible record. Tool records are kept separately rather than injected as fake chat messages.

| Column | SQL type | Required | Notes |
|---|---|---:|---|
| `Id` | `uniqueidentifier` | Yes | Primary key. |
| `ConversationId` | `uniqueidentifier` | Yes | FK to `Conversations(Id)`. |
| `SequenceNumber` | `int` | Yes | Monotonic ordering within one conversation. |
| `Role` | `tinyint` | Yes | `User = 1`, `Assistant = 2`; reserve `System = 3` only if product-visible system notices are required. |
| `Content` | `nvarchar(max)` | Yes | User or assistant text. Enforce application limits. |
| `Status` | `tinyint` | Yes | `Completed`, `Streaming`, `Cancelled`, or `Failed`; primarily meaningful for assistant messages. |
| `CreatedAt` | `datetime2(3)` | Yes | UTC. |
| `UpdatedAt` | `datetime2(3)` | Yes | UTC. |
| `ModelName` | `nvarchar(128)` | No | Optional provenance for assistant responses; not required for ordinary chat history. |
| `RowVersion` | `rowversion` | Yes | Optimistic concurrency token. |

Indexes/constraints:

- Primary key: `PK_ChatMessages(Id)`.
- Unique index: `UX_ChatMessages_ConversationId_SequenceNumber`.
- Index: `IX_ChatMessages_ConversationId_SequenceNumber` for chronological history retrieval.
- Check constraint: `SequenceNumber > 0`.

Relationship/delete behavior:

- `Conversation -> ChatMessages`: required, cascade delete.

### 4.4 `ToolExecutions`

One row represents one relevant tool invocation and its final result. It deliberately stores no planner chain-of-thought or transient agent prompt.

| Column | SQL type | Required | Notes |
|---|---|---:|---|
| `Id` | `uniqueidentifier` | Yes | Primary key. |
| `AssistantMessageId` | `uniqueidentifier` | Yes | FK to the assistant placeholder/final message produced by the turn. |
| `StepNumber` | `int` | Yes | Planner step identifier for display/audit correlation. |
| `ToolName` | `nvarchar(128)` | Yes | Actual normalized registered tool name. |
| `Description` | `nvarchar(500)` | No | Sanitized planner step description. |
| `ArgumentsJson` | `nvarchar(max)` | No | Bounded and redacted serialized invocation arguments. |
| `Output` | `nvarchar(max)` | No | Bounded, redacted tool output when useful for audit/reconstruction. |
| `Error` | `nvarchar(2000)` | No | Safe error reason; no secrets or stack trace. |
| `Succeeded` | `bit` | Yes | Final execution outcome. |
| `StartedAt` | `datetime2(3)` | Yes | UTC. |
| `CompletedAt` | `datetime2(3)` | Yes | UTC. |

Indexes/constraints:

- Primary key: `PK_ToolExecutions(Id)`.
- Unique index: `UX_ToolExecutions_AssistantMessageId_StepNumber`.
- Index: `IX_ToolExecutions_AssistantMessageId`.
- Check constraint: `StepNumber > 0`.

Relationship/delete behavior:

- `ChatMessage (assistant) -> ToolExecutions`: required, cascade delete.
- Do **not** also create a cascading `ConversationId` FK here; that can create SQL Server multiple-cascade-path problems. The conversation is obtained through the assistant message.

### 4.5 Optional `MessageAttachments`

The current UI supports uploaded files. If uploads remain available after persistence is introduced, add this table and move binary data to private object/file storage rather than storing arbitrary files in the app working directory.

| Column | Required | Notes |
|---|---:|---|
| `Id` | Yes | GUID PK. |
| `ChatMessageId` | Yes | FK to the user message. |
| `StorageKey` | Yes | Private blob/object-storage key, not an externally accessible URL. |
| `OriginalFileName` | Yes | Display-only, sanitized. |
| `ContentType` | Yes | Validated server-side type. |
| `SizeBytes` | Yes | Enforce a strict limit. |
| `Sha256` | Yes | Useful audit/deduplication value. |
| `CreatedAt` | Yes | UTC. |
| `DeletedAt` | No | Retention/deletion tracking. |

Raw attachment content should not be replicated into the prompt unless the explicit `read_file` tool reads the current attachment. Add server-side file signature validation, a malware/quarantine policy, and scheduled cleanup.

### 4.6 Entity relationship diagram

```text
User 1 ────── * Conversation 1 ────── * ChatMessage
                                         │
                                         ├──── 0..* ToolExecution   (only Assistant message)
                                         │
                                         └──── 0..* MessageAttachment (only User message, optional)
```

---

## 5. Domain entities and EF Core configuration

### 5.1 Domain types

Suggested domain types:

```text
ApplicationUser
Conversation
ChatMessage
ToolExecution
MessageAttachment                 // only if uploads are retained

ChatMessageRole                   // User, Assistant, System
ChatMessageStatus                 // Streaming, Completed, Cancelled, Failed
```

Entities contain identity, navigations, and simple invariant helpers. They should not contain `DbContext`, controller, JWT, or LLM code.

### 5.2 EF Core context

`AppDbContext` belongs in Infrastructure:

```csharp
public sealed class AppDbContext : DbContext
{
    public DbSet<ApplicationUser> Users => Set<ApplicationUser>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ToolExecution> ToolExecutions => Set<ToolExecution>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
```

Keep mappings in separate classes, for example:

```text
Infrastructure/Persistence/Configurations/
  ApplicationUserConfiguration.cs
  ConversationConfiguration.cs
  ChatMessageConfiguration.cs
  ToolExecutionConfiguration.cs
  MessageAttachmentConfiguration.cs
```

Each `IEntityTypeConfiguration<T>` explicitly configures table names, required columns, string lengths, indexes, foreign keys, delete behavior, `rowversion`, and UTC-compatible types. Avoid data annotations for persistence configuration so the domain model remains clean.

### 5.3 Timestamp policy

Use an application clock abstraction:

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
```

Set `CreatedAt` and `UpdatedAt` in application services/repositories. A `SaveChangesInterceptor` may enforce that every added/modified auditable entity gets UTC timestamps, but the implementation must be deterministic and covered by tests.

---

## 6. Application abstractions

### 6.1 Current user

```csharp
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    string? Email { get; }
    string? UserName { get; }
}
```

`HttpCurrentUser` is a scoped API/Infrastructure implementation that reads validated claim values from `IHttpContextAccessor`. No agent, domain entity, repository contract, or application service receives `HttpContext`.

The conversation application service requires an authenticated `UserId`; it rejects unauthenticated use rather than accepting a client-supplied user ID.

### 6.2 Repositories

Repository methods should be ownership-aware. Do not expose a generic `GetByIdAsync(id)` for protected conversation operations.

```csharp
public interface IUserRepository
{
    Task<ApplicationUser?> FindByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct);
    Task<ApplicationUser?> FindByNormalizedUserNameAsync(string normalizedUserName, CancellationToken ct);
    Task AddAsync(ApplicationUser user, CancellationToken ct);
}

public interface IConversationRepository
{
    Task<Conversation> AddAsync(Conversation conversation, CancellationToken ct);
    Task<Conversation?> GetOwnedAsync(Guid userId, Guid conversationId, CancellationToken ct);
    Task<PagedResult<ConversationSummary>> ListOwnedAsync(Guid userId, ConversationCursor? cursor, int take, CancellationToken ct);
    Task<bool> DeleteOwnedAsync(Guid userId, Guid conversationId, CancellationToken ct);
}

public interface IChatMessageRepository
{
    Task<int> GetNextSequenceNumberAsync(Guid conversationId, CancellationToken ct);
    Task AddAsync(ChatMessage message, CancellationToken ct);
    Task<IReadOnlyList<ChatMessageContext>> GetRecentForAgentAsync(
        Guid userId, Guid conversationId, int maxMessages, int maxCharacters, CancellationToken ct);
    Task<MessagePage> GetPageOwnedAsync(
        Guid userId, Guid conversationId, int? beforeSequence, int take, CancellationToken ct);
    Task UpdateAsync(ChatMessage message, CancellationToken ct);
}

public interface IToolExecutionRepository
{
    Task AddRangeAsync(IEnumerable<ToolExecution> executions, CancellationToken ct);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

Ownership is checked through the join between conversation and user before returning a message page or changing/deleting a conversation. Repositories use parameterized LINQ/EF Core queries; raw string-concatenated SQL is prohibited.

### 6.3 Auth and token abstractions

```csharp
public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterUserCommand command, CancellationToken ct);
    Task<AuthResult> LoginAsync(LoginCommand command, CancellationToken ct);
}

public interface ITokenService
{
    AccessToken Create(ApplicationUser user, DateTimeOffset now);
}
```

Use `Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>` (or a directly equivalent vetted password hasher) for hashing/verification. This does **not** require adopting the complete ASP.NET Core Identity UI/schema and is appropriate for the requested simple local authentication.

---

## 7. Agent integration design

### 7.1 Required boundary change

The current `IAgent.RunAsync(ChatRequest, List<ChatMessage>, ...)` mutates a list owned by the controller. That boundary must change because the database is now the source of truth.

Replace it with an application-neutral request and streamed event contract:

```csharp
public sealed record AgentTurnRequest(
    string Prompt,
    IReadOnlyList<ConversationMessageContext> History,
    AgentToolOptions ToolOptions,
    AttachmentReference? Attachment);

public abstract record AgentEvent;
public sealed record ToolExecutionCompleted(ToolExecutionResult Result) : AgentEvent;
public sealed record TextDelta(string Text) : AgentEvent;
public sealed record AgentTurnCompleted(string? ModelName) : AgentEvent;

public interface IAgent
{
    IAsyncEnumerable<AgentEvent> RunAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken);
}
```

This is a small but valuable change:

- `PlanningAgent` still plans with `ChatClientPlanner`.
- `SequentialPlanExecutor` still performs the existing tool sequence.
- `AgentToolInvoker` and tool registration remain unchanged in principle.
- The agent exposes tool completion as an event, so the application layer can persist it.
- The agent has no dependency on EF Core or user identity.
- The application service owns durable state and response streaming.

`PlanningAgent` should build model `ChatMessage` instances from the supplied read-only history and current prompt. It must no longer append to or trim a controller-owned list. Existing planner/executor tests can be adapted to assert emitted events instead of mutable-list side effects.

### 7.2 Persisted data versus transient agent state

Persist:

- User messages and final/partial assistant messages.
- The assistant message status (`Streaming`, `Completed`, `Cancelled`, `Failed`).
- Tool name, sanitized arguments, output/error, outcome, and timestamps when tools were used.
- Conversation title and timestamps.

Do not persist by default:

- Full planner prompts, hidden reasoning, token-by-token model state, retry internals, or the complete transient plan JSON.
- API keys, authorization headers, raw cookies, stack traces, or unredacted sensitive tool output.

### 7.3 Conversation turn service

`IConversationTurnService` is the new orchestration boundary.

```csharp
public interface IConversationTurnService
{
    Task<ConversationDto> CreateAsync(CreateConversationCommand command, CancellationToken ct);
    Task<PagedResult<ConversationSummaryDto>> ListAsync(ConversationListQuery query, CancellationToken ct);
    Task<ConversationDetailDto> GetAsync(Guid conversationId, MessagePageQuery page, CancellationToken ct);
    IAsyncEnumerable<ConversationStreamEvent> SendMessageAsync(
        Guid conversationId,
        SendMessageCommand command,
        CancellationToken ct);
    Task DeleteAsync(Guid conversationId, CancellationToken ct);
}
```

The service gets the current user only through `ICurrentUser`, not from endpoint parameters.

### 7.4 Turn sequence

```text
1. API authenticates the bearer token and constructs SendMessageCommand.
2. ConversationTurnService requires ICurrentUser.UserId.
3. Load conversation with GetOwnedAsync(userId, conversationId).
   - Return 404 for a missing or non-owned conversation; never disclose ownership.
4. Validate message/attachment limits.
5. Allocate a sequence number and save the user ChatMessage.
6. Create and save an empty Assistant ChatMessage with status Streaming.
7. Load a bounded recent history query for the agent, including the new user message.
8. Convert it to AgentTurnRequest and run the existing PlanningAgent pipeline.
9. For each ToolExecutionCompleted event, persist a ToolExecution linked to the assistant placeholder.
10. For each TextDelta event, append to an in-memory response buffer, send a delta event,
    and checkpoint the assistant content periodically (for example every 1 second or 4 KB).
11. On completion, save final assistant content/status, update conversation UpdatedAt/LastMessageAt,
    and send a completion event.
12. On cancellation, mark the assistant message Cancelled and retain bounded partial content.
    On unexpected error, mark it Failed and send a safe error event.
```

Do not hold one database transaction open while waiting for the LLM or streaming to the browser. Commit durable user/assistant placeholders before model work; use short transactions/`SaveChanges` for status and final content updates.

### 7.5 History loading policy

There are two intentionally different queries:

1. **UI history:** paginated messages for viewing a conversation. The UI can load older pages on demand.
2. **Agent history:** only the newest relevant user/assistant messages, bounded by count and character/token budget. This preserves current behavior while preventing a large historic conversation from becoming a huge database/model request.

The existing planner currently receives only the last six truncated conversation items and final answer construction uses a budget. Preserve that policy in an explicit `ConversationHistoryPolicy` configuration, for example `MaxPlannerMessages = 6`, `MaxAgentMessages = 20`, and a bounded character/token budget.

---

## 8. Authentication and authorization

### 8.1 Local authentication choice

Implement simple local accounts with password hashing and JWT bearer authentication. No external identity provider is necessary for this phase.

Registration command:

```json
{
  "email": "user@example.com",
  "userName": "user",
  "displayName": "Example User",
  "password": "a-long-user-chosen-password"
}
```

Login command:

```json
{
  "emailOrUserName": "user@example.com",
  "password": "a-long-user-chosen-password"
}
```

Auth response:

```json
{
  "accessToken": "<JWT>",
  "expiresAt": "2026-09-29T12:00:00Z",
  "user": {
    "id": "guid",
    "email": "user@example.com",
    "displayName": "Example User"
  }
}
```

JWT claims include a stable `sub`/`NameIdentifier` user ID and user name/email. Configure issuer, audience, signing key, lifetime, and clock skew through protected configuration. Signing keys never go in source control.

### 8.2 Endpoint authorization

- `POST /api/auth/register` and `POST /api/auth/login`: anonymous, rate limited.
- All `/api/conversations/**` routes: `[Authorize]`.
- Existing anonymous `POST /api/chat` must be removed or deprecated after the authenticated message endpoint is released. It must not retain a static fallback history.

### 8.3 Ownership rule

Every access path uses this rule:

```text
Requested conversation exists AND conversation.UserId == ICurrentUser.UserId
```

This validation happens in the repository/application query, not only in the controller. A client-provided conversation ID never grants access.

Return `404 Not Found` for a conversation that either does not exist or is not owned by the caller. This avoids revealing that another user's conversation ID exists.

### 8.4 Additional security controls

- Enforce HTTPS in production and configure exact allowed frontend origins; do not use unrestricted CORS.
- Hash passwords with the framework hasher; never log passwords, bearer tokens, API keys, cookies, or full authorization headers.
- Validate email, username, display name, title, prompt, and attachment metadata/size.
- Apply ASP.NET Core rate limiting to registration, login, message creation, and web-search-enabled requests.
- Use a short-lived JWT. For a future stronger session design, add refresh tokens in secure HttpOnly cookies with rotation and anti-forgery protections.
- Keep the SPA's access token in memory or session storage, not local storage; use a strict Content Security Policy and avoid unsafe HTML rendering.
- Apply output sanitization and restrictive Markdown rendering for model/tool content.
- Do not expose raw database exceptions, tool stack traces, provider credentials, or internal paths to clients.
- Keep EF Core LINQ queries parameterized; prohibit dynamic SQL generated from client data.
- Enforce maximum prompt size, maximum assistant buffer, max pagination size, max attachment size, and existing agent tool-step limits.

---

## 9. REST API design

All conversation endpoints require `Authorization: Bearer <token>`.

### 9.1 Authentication

| Method/path | Request | Response | Notes |
|---|---|---|---|
| `POST /api/auth/register` | `RegisterRequest` | `201 Created` with `AuthResponse` | Validates unique normalized email/user name and hashes password. |
| `POST /api/auth/login` | `LoginRequest` | `200 OK` with `AuthResponse` | Uses the same generic error for unknown user/bad password. |
| `GET /api/auth/me` | none | `200 OK` with current user | Optional but useful for application startup. |

### 9.2 Conversations

| Method/path | Request | Response | Notes |
|---|---|---|---|
| `POST /api/conversations` | `{ "title": "Optional title" }` | `201 Created` with `ConversationSummary` | Creates an empty conversation owned by current user. |
| `GET /api/conversations?take=30&cursor=...` | query only | `200 OK` paged conversation summaries | Returns title, dates, and last-message information; never message bodies. |
| `GET /api/conversations/{id}?beforeSequence=...&take=100` | query only | `200 OK` conversation detail plus initial message page | Ownership checked. Supports loading the complete UI history incrementally. |
| `GET /api/conversations/{id}/messages?beforeSequence=...&take=100` | query only | `200 OK` `MessagePage` | Optional explicit pagination endpoint. |
| `POST /api/conversations/{id}/messages` | `multipart/form-data` or JSON | streaming response | Sends one user turn and streams assistant output. |
| `DELETE /api/conversations/{id}` | none | `204 No Content` | Deletes only an owned conversation. |

### 9.3 Message streaming contract

Use a real SSE response rather than a `text/event-stream` header with unframed raw text. Since the client needs POST + optional upload, use `fetch`, parse SSE frames manually, and return frames such as:

```text
event: message-start
data: {"userMessageId":"...","assistantMessageId":"..."}

event: delta
data: {"text":"Hello"}

event: delta
data: {"text":" world"}

event: completed
data: {"assistantMessageId":"...","status":"completed"}
```

Set `Cache-Control: no-cache` and the proxy-specific no-buffering header where appropriate. If keeping raw fetch chunks instead, use `text/plain; charset=utf-8` rather than claiming SSE. The preferred design is framed SSE over `fetch`.

### 9.4 DTO guidance

Keep HTTP DTOs separate from entity types. Never return `PasswordHash`, normalized fields, row versions, internal storage keys, or raw tool-call content unless a deliberately authorized diagnostic endpoint is added later.

---

## 10. Frontend design

### 10.1 Application state/services

Split the current large `AppComponent` responsibilities into focused Angular services/components:

```text
core/
  auth.service.ts              login, register, token, current user
  auth.interceptor.ts          attaches bearer token
  auth.guard.ts                protects chat route
  conversation-api.service.ts  typed REST/SSE calls
  conversation-store.service.ts selected conversation and UI state

features/auth/
  login component
  registration component

features/chat/
  chat-shell component
  conversation-sidebar component
  chat-thread component
  chat-composer component
```

The frontend must use a relative `/api` base URL and an Angular development proxy. It must remove the current hard-coded `https://localhost:7098/api/chat` URL.

### 10.2 Layout

```text
+--------------------------+-----------------------------------------+
| Conversations            | Conversation                            |
| [+ New Chat]             | title, dates (optional)                 |
|                          |                                         |
| Chat 1                   | User and assistant messages             |
| Chat 2                   |                                         |
| Chat 3                   |                                         |
|                          |                                         |
|                          |-----------------------------------------|
|                          | Message input / attachment / send       |
+--------------------------+-----------------------------------------+
```

Behavior:

1. On startup, load a valid authentication state (`GET /api/auth/me` or local short-lived token state).
2. Load `GET /api/conversations` into the sidebar.
3. Selecting a conversation calls `GET /api/conversations/{id}` and displays newest messages; older pages load on demand.
4. **New Chat** calls `POST /api/conversations`, adds the result to the sidebar, and selects it.
5. Sending calls `POST /api/conversations/{id}/messages`, appends the persisted user message, creates an assistant placeholder, and applies `delta` events to it.
6. Completion updates the local message and moves the conversation to the top based on `UpdatedAt`.
7. The client never sends a `userId`; authorization derives it from the token.

### 10.3 Frontend safeguards

- Disable duplicate sends while a turn is running for one conversation.
- Allow cancelling the request with `AbortController`; render persisted partial/cancelled output safely after reloading.
- Use typed DTOs rather than mirroring EF entities.
- Configure `marked` not to permit raw HTML, and retain Angular sanitization of rendered content.
- Add tests for auth guard, listing/selecting conversations, paged history, streaming deltas, cancel behavior, and an unauthorized/expired-token redirect.

---

## 11. EF Core setup and migrations

### 11.1 Packages

Add packages with versions aligned to the .NET 9 application:

```text
Microsoft.EntityFrameworkCore.SqlServer
Microsoft.EntityFrameworkCore.Design             (Infrastructure; development tooling)
Microsoft.EntityFrameworkCore.Tools              (development tooling as needed)
Microsoft.AspNetCore.Authentication.JwtBearer
Microsoft.Extensions.Identity.Core               (PasswordHasher without full Identity UI)
```

Keep versions aligned with the target framework and centralize package versions if the solution later adopts Central Package Management.

### 11.2 Configuration

Use configuration with environment overrides; a connection string and JWT signing key must never be committed:

```json
{
  "ConnectionStrings": {
    "LMMsDatabase": ""
  },
  "Authentication": {
    "Jwt": {
      "Issuer": "LMMs.Api",
      "Audience": "LMMs.Web",
      "SigningKey": "",
      "AccessTokenMinutes": 30
    }
  }
}
```

Local development uses .NET user secrets or environment variables, for example:

```text
ConnectionStrings__LMMsDatabase=Server=...;Database=LMMs;Trusted_Connection=True;TrustServerCertificate=True
Authentication__Jwt__SigningKey=<at-least-32-byte-random-secret>
```

Register SQL Server and authentication in `Program.cs`:

```text
AddDbContext<AppDbContext>(UseSqlServer(connectionString))
AddScoped repositories/application services/current user
AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(...)
AddAuthorization()
UseAuthentication() before UseAuthorization()
```

### 11.3 Migration commands

After adding the Infrastructure project and DbContext:

```bash
dotnet ef migrations add InitialUserConversationPersistence \
  --project Agent.Infrastructure \
  --startup-project LMMs.Api \
  --output-dir Persistence/Migrations

dotnet ef database update \
  --project Agent.Infrastructure \
  --startup-project LMMs.Api
```

For a temporary single-project implementation, target `LMMs.Api` for both project arguments, but keep migrations under `Infrastructure/Persistence/Migrations` and preserve the same abstractions.

### 11.4 Deployment strategy

- Apply migrations in the deployment pipeline using a dedicated migration job or controlled startup process.
- Back up production databases before applying destructive migrations.
- Do not automatically run unreviewed destructive migrations in multiple production instances at startup.
- Health checks should verify database connectivity without exposing connection details.

---

## 12. Incremental implementation plan

### Phase 0 — security cleanup prerequisite

Before introducing a database:

1. Rotate the currently hard-coded web-search credential.
2. Remove tracked production logs/uploads from `Agent.Api/Files/`, ignore runtime upload storage, and scrub shared Git history as required.
3. Move Ollama and web-search configuration into protected configuration/options.
4. Replace the frontend hard-coded localhost endpoint with a relative API base/proxy.

### Phase 1 — create domain/application/infrastructure foundation

1. Add Domain, Application, and Infrastructure projects (or corresponding separated folders for a short transition).
2. Add entities/enums and separate EF Core configurations.
3. Add `AppDbContext`, SQL Server package/configuration, repositories, and unit of work.
4. Add the initial migration and validate it against a local SQL Server database.
5. Add repository integration tests using a disposable SQL Server-compatible test environment; do not use EF InMemory as the sole relational test substitute.

### Phase 2 — local authentication and current-user abstraction

1. Add user repository, registration/login validators, `PasswordHasher`, JWT token service, and `ICurrentUser` implementation.
2. Add auth endpoints and authentication middleware.
3. Add `[Authorize]` to new conversation routes.
4. Test duplicate registration, incorrect credentials, expired/invalid token, and anonymous rejection.

### Phase 3 — conversation CRUD and read history

1. Add conversation/message repositories and an ownership-enforcing conversation service.
2. Add create/list/get/delete conversation endpoints with paging.
3. Replace all static chat state in `ChatController`.
4. Add authorization tests proving user A receives `404` when reading, posting to, or deleting user B's conversation.
5. Build the frontend login, sidebar, new conversation, selection, and message-loading flow.

### Phase 4 — integrate persistence with the existing agent

1. Introduce `AgentTurnRequest` and `AgentEvent` contracts.
2. Adapt `PlanningAgent` to consume a read-only history and emit tool/delta/completion events.
3. Create `ConversationTurnService` to persist the user message, assistant placeholder, tool records, and final/partial assistant answer.
4. Add the protected `POST /api/conversations/{id}/messages` SSE endpoint.
5. Adapt existing planning/executor tests and add turn-service tests with a fake agent.
6. Add end-to-end tests for persisted history surviving a server restart.

### Phase 5 — attachments, reliability, and polish

1. Replace `FileContext` paths with `IAttachmentStorage` and persisted attachment metadata if file features remain enabled.
2. Add file size/type/signature validation, retention, cleanup, and safe content extraction limits.
3. Add rate limiting, health checks, structured telemetry, and audit-safe logging.
4. Add conversation-title generation/renaming only after the durable core works.

---

## 13. Example end-to-end message flow

```text
Browser
  POST /api/conversations/8d.../messages
  Authorization: Bearer <JWT>
  Form: prompt="What is 25 * 4?"

ChatMessagesController
  -> [Authorize] validates JWT
  -> ConversationTurnService.SendMessageAsync(8d..., command)

ConversationTurnService
  -> ICurrentUser returns user 1a...
  -> IConversationRepository.GetOwnedAsync(1a..., 8d...)
  -> writes User message sequence 7, commits
  -> writes Assistant placeholder sequence 8 (Streaming), commits
  -> loads bounded history for this owned conversation
  -> PlanningAgent.RunAsync(AgentTurnRequest)

PlanningAgent (existing core preserved)
  -> ChatClientPlanner creates Calculator plan
  -> SequentialPlanExecutor invokes Calculator
  -> emits ToolExecutionCompleted

ConversationTurnService
  -> writes ToolExecution linked to assistant message 8
  -> receives TextDelta events, sends SSE frames to browser
  -> buffers/checkpoints assistant text
  -> updates message 8 to Completed and conversation UpdatedAt

Browser
  -> appends delta text to assistant placeholder
  -> on completed, refreshes sidebar metadata/order
```

If the same browser attempts user B's conversation ID, `GetOwnedAsync(userAId, userBConversationId)` returns no result and the API returns `404`; no agent call, message insert, or tool invocation occurs.

---

## 14. Testing strategy

### Unit tests

- Auth service: password validation, duplicate identity handling, successful/failed verification.
- `ICurrentUser`: valid/missing/malformed claim behavior.
- Conversation service: title creation, validation, ownership denial, sequence ordering, status transitions.
- Planning agent: existing planner/executor behavior plus `AgentEvent` emission.
- Tool-result mapper: redaction/truncation and no secret-bearing values persisted.

### Integration tests

- EF configurations, indexes, constraints, cascades/restrictions, concurrency token behavior.
- API: register/login, token-required routes, list pagination, user isolation, delete behavior.
- Streaming message endpoint with a fake `IAgent`.
- Restart scenario: create data, instantiate a fresh service scope, load same conversation/history successfully.

### Frontend tests

- Login/register validation and routing.
- Sidebar list, new-chat creation, selection, and pagination.
- Streaming delta rendering and cancellation.
- Expired token handling and no cross-user ID assumptions.

CI should run `dotnet test`, Angular build/tests, dependency audit review, and secret scanning.

---

## 15. Migration from current in-memory behavior

1. **Do not migrate the current static list.** It has no owner, is not durable, and may contain data from more than one user. Treat it as unsafe transient state.
2. Add the SQL migration and local-auth routes behind a feature branch/environment flag.
3. Release the authenticated conversation API and frontend first.
4. Change the old `/api/chat` endpoint to return a deprecation response or remove it once the frontend uses `/api/conversations/{id}/messages`.
5. Delete `_chatMessages` from `ChatController`; a database-backed repository/application service is the sole history authority.
6. If historical files must be retained, migrate only vetted attachment metadata/content through a separate audited process. Do not import existing production log files as chat attachments.

---

## 16. Acceptance criteria

The implementation is complete when:

- Users can register and log in with securely hashed passwords.
- Every protected request derives its user identity from validated authentication, not the request body.
- A user can create, list, open, continue, and delete only their own conversations.
- Conversations, messages, timestamps, titles, and useful tool execution records survive an API restart.
- User A cannot read, write to, or delete user B's conversation, even with a known GUID.
- The existing planner/executor/tool architecture is still used and has no EF Core or `HttpContext` dependency.
- The agent receives only a bounded read-only history context.
- The response remains streamed while the final/partial assistant response is persisted safely.
- Attachments, if enabled, have server-side validation and private, cleaned-up storage.
- Automated tests cover ownership isolation, persistence/restart behavior, auth, message streaming, and existing agent behavior.
