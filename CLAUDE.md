# DistillForge

AI-powered text ingestion tool. Takes a document stream, uses an LLM to extract
action points, and displays them as sticky notes the user can categorise, then
push/share to other platforms (email, GitHub Issues, Azure DevOps, Jira, etc.).

Key feature is that extracted notes can be linked back to relevant passages in source

## Tech Stack
- .NET 10 (C#) across all projects
- API: Azure Functions, **isolated worker model** (not in-process)
- Front end: Blazor WebAssembly (client-side only, no server rendering)
- Database: PostgreSQL (EF Core + Npgsql)
- AI: bring-your-own provider via any OpenAI-compatible chat-completions endpoint
  (Ollama in dev). Plain chat completions + `response_format` only; no
  backend-specific code paths.

## Folder Structure
- `api/` – Azure Functions app
- `blazor/` – Blazor WebAssembly front end
- `core/` – Shared types (DTOs, enums, validation) used by api and blazor
- `data/` – EF Core DbContext, entities, migrations (referenced by api only)
- `tests/` – xUnit tests (currently `DistillForge.Core.Tests`)

## Architecture Rules
- `core` must stay dependency-light: no EF Core, Npgsql, Azure SDKs or
  anything that won't run in the browser.
- `blazor` must never contain secrets, connection strings or API keys.
  Everything in it is publicly downloadable.
- `blazor` talks to the database only via `api`, never directly.
- AI calls go through an abstraction (e.g. `IActionExtractor`) so Ollama can
  be swapped for a hosted provider later.
- Source linking: the model returns segment IDs + verbatim quotes, never
  offsets. Offsets are resolved in code (`core/Extraction/QuoteResolver`).
  Source text is normalised once at ingest and is immutable afterwards.
- Extraction is async: HTTP returns 202, a queue trigger runs the model,
  the client polls `GET /api/runs/{id}` (Azure's HTTP limit is ~230s).
- Export targets (GitHub, DevOps, Jira, email) are separate implementations
  of a common interface, not a big switch statement.

## Configuration
- All connection details come from configuration/env vars, never hardcoded.
- Dev values live in `api/local.settings.json` (gitignored).
- Template: `api/local.settings.sample.json` (committed, no secrets).
- Key settings: `ConnectionStrings__Postgres`, `AzureWebJobsStorage`,
  `AiProvider__BaseUrl` (include `/v1`), `AiProvider__Model`, `AiProvider__ApiKey`,
  `AiProvider__ContextTokens`, `AiProvider__ReserveOutput`, `AiProvider__ResponseFormat`

## Development Environment
- PostgreSQL and Ollama run on separate machines on the local network.
- Front end calls the API via a configurable base URL (`wwwroot/appsettings.json`).
- Local machine-specific details: @~/.claude/distillforge-local.md

## Commands
- Build all: `dotnet build`
- Restore tools (dotnet-ef): `dotnet tool restore`
- Queue storage for the API: `npx azurite` (or `docker compose up azurite`)
- Apply migrations: `dotnet ef database update --project data --startup-project api`
  (reads `ConnectionStrings__Postgres` from the environment)
- Run API: `cd api && func start`
- Run front end: `cd blazor && dotnet watch`
- Tests: `dotnet test`
- New migration: `dotnet ef migrations add <Name> --project data --startup-project api`

## Deployment (planned)
- API + database: Azure (Functions + Azure Database for PostgreSQL)
- Front end: GitHub Pages, possibly Azure Static Web Apps. Decide later.
- GitHub Pages needs: correct `<base href>`, `.nojekyll`, 404.html SPA
  fallback, and CORS on the Function App for the Pages origin.