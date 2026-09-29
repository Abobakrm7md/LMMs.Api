# Local SQL Server and authentication setup

The application now requires a SQL Server connection string and a JWT signing key at startup. They are intentionally not stored in `appsettings.json`.

## 1. Start SQL Server

Use an existing SQL Server instance or a local SQL Server container. Create an empty `LMMs` database (EF Core can create it through the migration below).

Example connection string for a local SQL Server instance:

```text
Server=localhost,1433;Database=LMMs;User Id=sa;Password=<strong-password>;TrustServerCertificate=True;Encrypt=True
```

## 2. Set development secrets

From the repository root:

```bash
dotnet user-secrets set "ConnectionStrings:LMMsDatabase" "Server=localhost,1433;Database=LMMs;User Id=sa;Password=<strong-password>;TrustServerCertificate=True;Encrypt=True" --project LMMs.Api

dotnet user-secrets set "Authentication:Jwt:Issuer" "LMMs.Api" --project LMMs.Api
dotnet user-secrets set "Authentication:Jwt:Audience" "LMMs.Web" --project LMMs.Api
dotnet user-secrets set "Authentication:Jwt:SigningKey" "replace-with-a-random-secret-of-at-least-32-characters" --project LMMs.Api

# Optional: enables the existing web-search tool
dotnet user-secrets set "Search:SerperApiKey" "<serper-api-key>" --project LMMs.Api
```

Use deployment secret storage/environment variables rather than user secrets outside local development.

## 3. Apply the included migration

```bash
dotnet ef database update --project LMMs.Api --startup-project LMMs.Api
```

The included initial migration creates `Users`, `Conversations`, `ChatMessages`, and `ToolExecutions`, including ownership and history indexes.

## 4. Run the API and frontend

```bash
dotnet run --project LMMs.Api --launch-profile https
cd LMMs.Api/ai-chat-fe
npm ci
npm start
```

The Angular development server proxies `/api` to `https://localhost:7098`, configured in `proxy.conf.json`.

## 5. First use

1. Open the Angular UI.
2. Create an account with a username, email, display name, and password of at least 12 characters.
3. Sign in; the UI receives a short-lived JWT held in session storage.
4. Start a new chat. Its user and assistant messages, title, timestamps, and relevant tool executions are saved to SQL Server.
