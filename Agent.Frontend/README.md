# Agent Frontend

This is the standalone Angular frontend for the Agent API. Its Node dependencies and build configuration are fully contained in this project (`package.json`, `package-lock.json`, and `angular.json`). The backend does not build or serve this application.

## Development

```bash
npm ci
npm start
```

The development application runs at `http://localhost:4200`. Requests under `/api` are forwarded to the backend at `https://localhost:7098` by `proxy.conf.json`.

Start the backend separately from the repository root:

```bash
dotnet run --project Agent.Api
```

## Production build

```bash
npm run build
```

Build output is written under `dist/agent-frontend`.
