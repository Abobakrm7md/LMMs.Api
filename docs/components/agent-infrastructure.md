# Agent.Infrastructure — external adapters

Implements every port defined by `Agent.Application` against real services: Ollama for chat, SQL Server via EF Core for persistence, ASP.NET Core JWT + Identity for authentication, Serper for web search, local disk for attachments, and the concrete tool catalog.

- **Project:** `Agent.Infrastructure/Agent.Infrastructure.csproj` (`net9.0`)
- **References:** `Agent.Application`, `Microsoft.AspNetCore.App` framework reference
- **Key packages:** `Microsoft.Extensions.AI(.Ollama)`, `Microsoft.EntityFrameworkCore(.SqlServer/.Design/.Tools)`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.Extensions.Identity.Core`, `PdfPig`, `DocumentFormat.OpenXml`, health checks

## Dependency injection

`AddAgentInfrastructure(services, configuration)` splits into two registration blocks:

- **`AddChatAndTools`** — binds `AI:Ollama`, registers the `OllamaChatClient` (`IChatClient`) and `IChatModel`; binds `Files`, registers `IAttachmentStore` (scoped) and the legacy `IConversationStore` (singleton); registers `TimeProvider.System`; binds `Search:Serper` and registers `SerperWebSearchProvider` as a typed `HttpClient`; registers all five `IAgentTool` implementations (scoped).
- **`AddPersistenceAndAuthentication`** — **fails fast** when `ConnectionStrings:LMMsDatabase` is missing or `Authentication:Jwt` lacks issuer/audience/32+-char signing key (see [../configuration.md](../configuration.md#startup-validation-fail-fast)); registers `AppDbContext` (SQL Server), `/health` DB check, JWT bearer validation (issuer, audience, HMAC key, lifetime, 1-min skew), `IHttpContextAccessor`, and scoped adapters: `HttpCurrentUser`, all four repositories, `EntityFrameworkUnitOfWork`, `IdentityPasswordService`, `JwtTokenService`.

## `AI/Ollama/`

| Type | Notes |
| --- | --- |
| `OllamaChatModel` | Adapts `Microsoft.Extensions.AI.IChatClient` to `IChatModel`: maps domain roles to `ChatRole`s, forwards temperature (`ChatModelOptions`, default 0.1), and explicitly **disables client-side tool calling** (`ToolMode = None`) — tool use is owned by the planner/executor, not the SDK. `CompleteAsync` returns full text; `StreamAsync` yields text deltas. |
| `OllamaOptions` | `AI:Ollama` — `Endpoint` (`http://localhost:11434`), `Model` (`llama3.1`). |

Swap AI providers by implementing `IChatModel` and changing only this registration — nothing upstream changes.

## `Authentication/`

| Type | Notes |
| --- | --- |
| `JwtTokenService` | Issues HMAC-SHA256 JWTs with `nameidentifier` (user id), `name` (username), `email` claims; lifetime = `AccessTokenMinutes` clamped 5–120; re-validates the signing key (≥ 32 chars) when issuing. |
| `JwtOptions` | `Authentication:Jwt` — Issuer, Audience, SigningKey, AccessTokenMinutes (30). |
| `IdentityPasswordService` | Hash/verify via ASP.NET Core Identity `PasswordHasher<ApplicationUser>` (rehash-on-success supported). |
| `HttpCurrentUser` | Reads the current `ClaimsPrincipal` from `IHttpContextAccessor` and exposes id/email/username; `UserId` parses the `nameidentifier` claim as a `Guid`. |

## `Persistence/`

### `AppDbContext`

DbSets: `Users`, `Conversations`, `ChatMessages`, `ToolExecutions`. Configurations are applied from the assembly (`ApplyConfigurationsFromAssembly`).

### Entity configurations (`Configurations/`)

| Entity | Mapping highlights |
| --- | --- |
| `ApplicationUser` → `Users` | Unique indexes on `NormalizedEmail` and `NormalizedUserName`; lengths 256/64/128/512; `datetimeoffset(3)`; `rowversion` |
| `Conversation` → `Conversations` | `Title` ≤ 200; check `NextMessageSequence >= 0`; FK → user, cascade delete; history-friendly indexes; `rowversion` |
| `ConversationMessage` → `ChatMessages` | Enums as `byte`; `Content` = `nvarchar(max)`; unique `(ConversationId, SequenceNumber)`; check `SequenceNumber > 0`; cascade delete with conversation; `rowversion` |
| `ToolExecution` → `ToolExecutions` | `ToolName` ≤ 128; FK → assistant message, cascade delete |

### Repositories (`Repositories/`)

| Type | Implements | Notes |
| --- | --- | --- |
| `UserRepository` | `IUserRepository` | Lookups by normalized email/username. |
| `ConversationRepository` | `IConversationRepository` | All reads owner-scoped; `ListOwnedAsync` projects summaries ordered by `UpdatedAt` desc; `DeleteOwnedAsync` returns whether a row was removed. |
| `ChatMessageRepository` | `IChatMessageRepository` | `GetRecentForAgentAsync` returns the newest completed messages within message/character budgets for agent history; `GetPageOwnedAsync` implements the `beforeSequence` cursor page and computes `NextBeforeSequence`. |
| `ToolExecutionRepository` | `IToolExecutionRepository` | Append-only adds. |
| `EntityFrameworkUnitOfWork` | `IUnitOfWork` | Wraps `DbContext.SaveChangesAsync`. |

### Migrations

`20260929130000_InitialIdentityAndConversations` creates all four tables with constraints, indexes, and `rowversion` columns. Apply with `dotnet ef database update --project Agent.Infrastructure --startup-project Agent.Api`.

## `Search/Serper/`

| Type | Notes |
| --- | --- |
| `SerperWebSearchProvider` | POSTs `{ q }` to the Serper endpoint with the `X-API-KEY` header. Parses `answerBox`, `knowledgeGraph`, and the top 3 organic results into a compact plain-text digest. **Degrades gracefully:** missing key → `"Web search is not configured."`; invalid query → guidance text; non-2xx → `"Web search is temporarily unavailable."` — the tool never throws into the agent loop. |
| `SerperOptions` | `Search:Serper` — Endpoint, ApiKey. |

## `Files/`

| Type | Notes |
| --- | --- |
| `LocalAttachmentStore` | Saves uploads under `Files:Directory` as `{guid}{ext}` and extracts text for the agent. Allowlist: `.txt .csv .json .xml .md .pdf .docx`; enforces `MaximumBytes` (10 MB) while streaming to disk and deletes partial files on failure; `ReadTextAsync` guards against path traversal (`Path.GetFileName` must round-trip), caps output at `MaximumCharacters` (4,000, truncated with a marker). Extraction: text formats read whole, CSV first 20 lines, PDF first 5 pages (PdfPig), DOCX body text (Open XML). |
| `AttachmentStorageOptions` | `Files` section — Directory (`Files`), MaximumCharacters (4000), MaximumBytes (10 MB). |

## `Conversations/`

`InMemoryConversationStore` — singleton `ConcurrentDictionary<string, Conversation>` backing the legacy anonymous `/api/chat` endpoint. Replace `IConversationStore` to change the legacy flow's memory; the persisted conversation API does not use it.

## Tools

Concrete `IAgentTool` implementations, all registered scoped:

| Tool | Definition (name / params / category) | Behavior |
| --- | --- | --- |
| `CalculatorTool` | `Calculator` / `input` / General | Evaluates one math expression with `DataTable.Compute`; normalizes `× ÷ −`; invalid input → failed `ToolResult`, never throws. |
| `GetDateTool` | `GetDate` / none / General | Local date `yyyy-MM-dd` from injected `TimeProvider`. |
| `GetTimeTool` | `GetTime` / none / General | Local time `HH:mm:ss` from injected `TimeProvider`. |
| `WebSearchTool` | `web_search` / `query` / WebSearch | Delegates to `IWebSearchProvider`; empty query → failure. |
| `FileReaderTool` | `read_file` / `source` / **File** | Reads the current attachment via `IAttachmentStore.ReadTextAsync(context.AttachmentId)`; fails cleanly when no attachment is present or the file is gone. Hidden from the planner unless an attachment exists (`ToolRegistry.Select`). |

Add a tool by implementing `IAgentTool` and adding one registration line — `PlanningAgent` and `AgentExecutor` pick it up automatically.
