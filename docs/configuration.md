# Configuration reference

Configuration is read from `Agent.Api/appsettings.json`, environment-specific files, environment variables, and [.NET user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (the `Agent.Api` project has a `UserSecretsId`). Standard .NET precedence applies, so any key below can be overridden with an environment variable using `__` as the separator (e.g. `ConnectionStrings__LMMsDatabase`).

## Sections

### `ConnectionStrings`

| Key | Required | Description |
| --- | --- | --- |
| `LMMsDatabase` | **Yes** | SQL Server connection string. The infrastructure layer **throws at startup** when it is missing or empty. |

Example: `Server=localhost,1433;Database=LMMs;User Id=sa;Password=<strong-password>;TrustServerCertificate=True;Encrypt=True`

### `Authentication:Jwt`

Bound to `JwtOptions` and validated when the token service is used.

| Key | Default | Description |
| --- | --- | --- |
| `Issuer` | — | Token issuer. Required — startup fails when empty. |
| `Audience` | — | Token audience. Required — startup fails when empty. |
| `SigningKey` | — | HMAC-SHA256 symmetric key. Must be **at least 32 characters**; startup fails otherwise. |
| `AccessTokenMinutes` | `30` | Access-token lifetime; clamped to 5–120 minutes when tokens are created. |

Bearer validation enables issuer, audience, signing-key, and lifetime checks with a 1-minute clock skew.

### `AI:Ollama`

Bound to `OllamaOptions`; configures the chat model behind `IChatModel` (`Microsoft.Extensions.AI` `OllamaChatClient`).

| Key | Default | Description |
| --- | --- | --- |
| `Endpoint` | `http://localhost:11434` | Ollama server URL. |
| `Model` | `llama3.1` | Model name served by Ollama (must be pulled beforehand, e.g. `ollama pull llama3.1`). |

### `Search:Serper`

Bound to `SerperOptions`; configures the `web_search` tool backed by <https://google.serper.dev>.

| Key | Default | Description |
| --- | --- | --- |
| `Endpoint` | `https://google.serper.dev/search` | Serper search endpoint. |
| `ApiKey` | empty | Serper API key, sent as the `X-API-KEY` header. When empty the tool degrades gracefully: searches return the text `"Web search is not configured."` instead of failing. |

### `Files`

Bound to `AttachmentStorageOptions`; configures `LocalAttachmentStore`.

| Key | Default | Description |
| --- | --- | --- |
| `Directory` | `Files` | Directory (relative to the API working directory) where uploaded attachments are written. Created on demand. |
| `MaximumCharacters` | `4000` | Cap on extracted text returned to the agent by `read_file` (longer content is truncated with a marker). |
| `MaximumBytes` | `10485760` (10 MB) | Maximum upload size. `ConversationsController` additionally enforces a matching 10 MB attachment limit plus an 11 MB multipart request limit. |

Supported attachment extensions (enforced in both the controller and the store): `.txt`, `.csv`, `.json`, `.xml`, `.md`, `.pdf`, `.docx`.

### Other

| Key | Description |
| --- | --- |
| `Logging:LogLevel` | Standard .NET logging levels (`Microsoft.AspNetCore` defaults to `Warning`). |
| `AllowedHosts` | Standard ASP.NET Core host filtering (`*` in the checked-in settings). |

## Supplying secrets

Never put real secrets in `appsettings.json`. For local development use user secrets from the repository root:

```bash
dotnet user-secrets set "ConnectionStrings:LMMsDatabase" "Server=localhost,1433;Database=LMMs;User Id=sa;Password=<strong-password>;TrustServerCertificate=True;Encrypt=True" --project Agent.Api
dotnet user-secrets set "Authentication:Jwt:Issuer" "LMMs.Api" --project Agent.Api
dotnet user-secrets set "Authentication:Jwt:Audience" "LMMs.Web" --project Agent.Api
dotnet user-secrets set "Authentication:Jwt:SigningKey" "<random-secret-at-least-32-chars>" --project Agent.Api
dotnet user-secrets set "Search:Serper:ApiKey" "<serper-api-key>" --project Agent.Api
```

In deployed environments use the platform's secret store or environment variables (`ConnectionStrings__LMMsDatabase`, `Authentication__Jwt__SigningKey`, `Search__Serper__ApiKey`, …). See [local-persistence-setup.md](local-persistence-setup.md) for the full walkthrough.

> ⚠️ **Security note.** The checked-in `Agent.Api/appsettings.json` currently contains placeholder-looking but real values — a SQL password, a JWT signing key, and a Serper API key. Treat them as compromised: rotate the Serper key and database password, and move all of them into user secrets/environment variables. Production deployments must supply fresh values through secure configuration.

## Startup validation (fail-fast)

`AddAgentInfrastructure` validates at startup and throws `InvalidOperationException` when:

- `ConnectionStrings:LMMsDatabase` is missing or empty, or
- `Authentication:Jwt:Issuer` / `:Audience` are empty, or
- `Authentication:Jwt:SigningKey` is shorter than 32 characters.

`JwtTokenService` re-validates the signing key when issuing tokens, and a database connectivity check is exposed at `/health` (`AddDbContextCheck<AppDbContext>`).

## Applying the database schema

```bash
dotnet ef database update --project Agent.Infrastructure --startup-project Agent.Api
```

The included migration `InitialIdentityAndConversations` creates `Users`, `Conversations`, `ChatMessages`, and `ToolExecutions` with ownership/history indexes, check constraints, and `rowversion` concurrency tokens.
