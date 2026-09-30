# LMMs.Api documentation

This directory is the documentation hub for the whole solution. Start with the [root README](../README.md) for a project overview and quick start, and with [ARCHITECTURE.md](../ARCHITECTURE.md) for the layering rules.

## Guides

| Document | Contents |
| --- | --- |
| [api-reference.md](api-reference.md) | Every HTTP endpoint: methods, routes, request/response bodies, validation rules, status codes, and the Server-Sent Events stream format. |
| [configuration.md](configuration.md) | All configuration sections (`ConnectionStrings`, `Authentication:Jwt`, `AI:Ollama`, `Search:Serper`, `Files`), defaults, validation at startup, and how to supply secrets. |
| [local-persistence-setup.md](local-persistence-setup.md) | Step-by-step local setup: SQL Server, user secrets, EF Core migration, running API + frontend, first use. |
| [sql-server-user-management-design.md](sql-server-user-management-design.md) | Design record for the SQL-backed user management and conversation persistence model. |

## Component reference

One document per solution project, covering purpose, key types, behavior, limits, and extension points:

| Component | Document |
| --- | --- |
| `Agent.Api` — ASP.NET Core presentation layer | [components/agent-api.md](components/agent-api.md) |
| `Agent.Application` — use cases, agent pipeline, ports | [components/agent-application.md](components/agent-application.md) |
| `Agent.Domain` — entities and planning/tool model | [components/agent-domain.md](components/agent-domain.md) |
| `Agent.Infrastructure` — Ollama, SQL Server, JWT, Serper, files, tools | [components/agent-infrastructure.md](components/agent-infrastructure.md) |
| `Agent.Frontend` — Angular chat client | [components/agent-frontend.md](components/agent-frontend.md) |
| `Agent.Tests` — xUnit test suite | [components/agent-tests.md](components/agent-tests.md) |

## Reading order for new contributors

1. [Root README](../README.md) — what the system does and how to run it.
2. [ARCHITECTURE.md](../ARCHITECTURE.md) — dependency direction and request flow.
3. [components/agent-application.md](components/agent-application.md) — the agent pipeline, the heart of the system.
4. [api-reference.md](api-reference.md) — how clients talk to the API.
5. The component doc for whichever project you are changing.
