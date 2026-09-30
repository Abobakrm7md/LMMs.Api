# Agent architecture

## Dependency direction

```text
Agent.Frontend ──HTTP──→ Agent.Api
Agent.Api ───────────────→ Agent.Application ─→ Agent.Domain
    └────────────────────→ Agent.Infrastructure ─┘
Agent.Infrastructure ───→ Agent.Application ───→ Agent.Domain
Agent.Tests ─────────────→ Agent.Application/Domain
```

- **Domain** contains conversation, plan, step, execution directive, and tool-result concepts. It has no package or project dependencies.
- **Application** owns the agent use case, planning, execution, tool registry, and ports for chat, attachments, search, and conversation storage. It does not reference Ollama, ASP.NET Core, HTTP clients, or concrete storage.
- **Infrastructure** adapts `Microsoft.Extensions.AI.IChatClient`/Ollama to `IChatModel`, implements SQL Server persistence, JWT authentication, external search and attachment storage, and contains independently registered tools.
- **Frontend** is a standalone Angular project with its own Node dependencies and build lifecycle. It communicates with the API over HTTP and is not built or served by the backend.
- **API** maps multipart HTTP input to `AgentRequest`, streams the application result, and acts as the composition root.

## Request flow

```text
HTTP request → PlanningAgent → IPlanner → Plan → IAgentExecutor
             → IToolRegistry → IAgentTool(s) → execution context
             → IChatModel → streamed answer
```

Planning never executes tools. The executor never creates the initial plan. The agent coordinates both and constructs only the final answer context.

## Extension points

- Add an AI provider by implementing `IChatModel` and changing only composition.
- Add a tool by implementing `IAgentTool` and registering it. `PlanningAgent` and `AgentExecutor` require no changes.
- Add web search providers behind `IWebSearchProvider`. Repository and document search should get focused ports when introduced rather than becoming modes of one large search service.
- Add planning or execution strategies by implementing `IPlanner` or `IAgentExecutor`.
- Replace process-local conversation memory by implementing `IConversationStore`.
- Replace local attachment storage by implementing `IAttachmentStore`.

## Configuration

Ollama is configured under `AI:Ollama`, Serper under `Search:Serper`, and local attachment storage under `Files`. Secrets such as `Search:Serper:ApiKey` must be supplied through environment variables or user secrets and are not stored in source.
