# Agent.Frontend — Angular chat client

Standalone **Angular 19** single-page application. It is fully self-contained (own `package.json`, `angular.json`, Node toolchain) and is **not built or served by the .NET backend** — backend and frontend communicate over HTTP only.

- **Stack:** Angular 19 (standalone components), TypeScript 5.7, RxJS 7.8, `marked` + `highlight.js` (markdown rendering with code highlighting), Karma/Jasmine for tests
- **Dev server:** `http://localhost:4200`, requests under `/api` proxied to `https://localhost:7098` via `proxy.conf.json` (`secure: false`, `changeOrigin: true`)

## Commands

```bash
npm ci          # install exact dependencies (package-lock.json)
npm start       # ng serve → http://localhost:4200
npm run build   # production build → dist/agent-frontend
npm test        # Karma + Jasmine unit tests
```

## Structure

```text
Agent.Frontend/
├── proxy.conf.json        # /api → https://localhost:7098
├── src/
│   ├── index.html         # shell
│   ├── main.ts            # bootstrapApplication with appConfig
│   ├── styles.css         # global styles
│   └── app/
│       ├── app.config.ts      # ApplicationConfig: zone change detection (coalesced) + router
│       ├── app.routes.ts      # routes: 'payment' → PaymentComponent; '' redirects to /chat
│       ├── app.component.*    # the chat application (component, template, styles, spec)
│       └── payment/           # standalone Moyasar payment form (component, template, styles, spec)
└── LMMs.Api/ai-chat-fe/   # ⚠️ nested copy of an earlier app draft — not part of the Angular workspace, kept for reference
```

## `AppComponent` — the chat application

One standalone component holds the whole chat experience (login/register panel, conversation sidebar, message list, composer with attachment button). Key behaviors:

- **Auth flow** — register/login against `/api/auth/*`; the JWT is kept in **`sessionStorage`** (cleared when the tab closes; sign-out removes it) and sent as a `Bearer` header on every API call via a shared `fetch` helper.
- **Conversations** — loads `/api/conversations` on sign-in, creates conversations, loads message pages, deletes conversations.
- **Streaming** — `sendMessage` POSTs `multipart/form-data` to `/api/conversations/{id}/messages` and **manually parses the SSE stream** (`fetch` + `ReadableStream` reader, splitting `event:`/`data:` frames): `message-start` links optimistic UI rows to persisted ids, `delta` chunks append to the streaming assistant message, `completed`/`error` finalize it. `EventSource` can't be used because the endpoint requires POST + auth headers + form data.
- **Attachments** — picked with a file input and uploaded with the prompt; shown as a `📎` marker on the user message (the backend persists the same marker).
- **Rendering** — assistant markdown is rendered with `marked` and code blocks highlighted with `highlight.js`; auto-scrolls to the latest message.
- **Message states** — mirrors the backend status model (`Streaming` / `Completed` / `Cancelled` / `Failed`) with styling + a loading indicator while streaming.

## `PaymentComponent`

Standalone demo page (`/payment`) embedding a [Moyasar](https://moyasar.com/) checkout form (10.00 SAR, visa/mastercard/mada). Loading Moyasar's script at runtime; **the publishable API key is intentionally empty** and must be supplied through deployment configuration before enabling the page. Unrelated to the chat API.

## Notes & conventions

- The router file uses an `NgModule` wrapper (`AppRoutingModule`) while the app bootstraps standalone — `app.config.ts` calls `provideRouter(routes)`, and the module class is legacy scaffolding.
- `LMMs.Api/ai-chat-fe/` is a nested, orphaned draft of the UI; the real app is `src/`. It is excluded from `angular.json` and safe to ignore (or delete in a cleanup).
- CORS on the API allows only `http://localhost:4200` / `https://localhost:4200`; other origins require changing the `AllowFrontend` policy in `Agent.Api/Program.cs`.
