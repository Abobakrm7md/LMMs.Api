# Agent.Tests — test suite

xUnit test project covering the domain invariants, the agent pipeline, tool behavior, and infrastructure pieces that can run without external services. Everything uses hand-rolled fakes — no mocking framework, no network, no database.

- **Project:** `Agent.Tests/Agent.Tests.csproj` (`net9.0`, xUnit 2.9, `xunit.runner.visualstudio`)
- **References:** `Agent.Application`, `Agent.Domain`, `Agent.Infrastructure`
- **Run:** `dotnet test` from the repository root

All tests currently live in `Agent.Tests/ArchitectureTests.cs` (the file predates the clean-architecture split; the name reflects an earlier intent).

## Test classes

| Class | Scope | Highlights |
| --- | --- | --- |
| `DomainTests` | `Agent.Domain` | `Conversation.TrimToLast` drops oldest messages; a `ToolResult` with empty output is a failure. |
| `PlannerTests` | Planning | No tools → direct plan **without calling the model**; valid model JSON → parsed multi-step plan. |
| `ToolRegistryTests` | Tool selection | `File`-category tools are hidden unless an attachment exists; general/web tools always offered. |
| `ExecutorTests` | Execution | Multi-step plans execute in order and complete (`GetDate` → `Calculator`). |
| `InfrastructureToolTests` | Tools & storage | `LocalAttachmentStore` save→read round-trip in a temp directory (limits honored); `CalculatorTool` evaluates `5*10` offline. |
| `AgentOrchestrationTests` | End-to-end agent | Attachment flows to `read_file` and tool output reaches the final prompt; pre-cancelled requests abort before planning; plan → execute → stream assembles the final streamed answer with tool context. |

## Test doubles

Hand-written fakes declared `file`-local in the test file:

| Double | Implements | Behavior |
| --- | --- | --- |
| `FakeChatModel` | `IChatModel` | Returns scripted completion JSON (plans) and a scripted streamed answer; counts completions; captures the messages sent to streaming so tests can assert the final prompt content. Honors cancellation tokens. |
| `StubTool` | `IAgentTool` | Named tool with an optional argument→output handler; records invocations. |
| `StubPlanner` | `IPlanner` | Always returns a direct plan and aborts advisory decisions. |

## What is intentionally not covered here

- **EF Core persistence** (repositories, migrations) — validated manually/through the `/health` check; would need an in-memory provider or Testcontainers-backed SQL Server.
- **JWT issuance/validation and HTTP middleware** — suitable for `WebApplicationFactory<Program>` integration tests (the `public partial class Program` hook already exists).
- **Ollama / Serper clients** — external services; their failure paths are designed to degrade gracefully instead of throwing.

Adding tests for a new tool or pipeline behavior follows the same pattern: script a `FakeChatModel` plan, run `PlanningAgent`/`AgentExecutor` with stub tools, and assert on streamed chunks and captured prompts.
