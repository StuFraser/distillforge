# DistillForge

AI-powered text ingestion tool. Takes a document stream, uses an LLM to extract
action points, and displays them as sticky notes the user can categorise, then
push/share to other platforms (email, GitHub Issues, Azure DevOps, Jira, etc.).

## Tech Stack
- .NET 10 (C#) across all projects
- API: Azure Functions, **isolated worker model** (not in-process)
- Front end: Blazor WebAssembly (client-side only, no server rendering)
- Database: PostgreSQL (EF Core + Npgsql)
- AI: Ollama in dev; production provider TBD, so keep it behind an interface

## Folder Structure
- `api/` – Azure Functions app
- `blazor/` – Blazor WebAssembly front end
- `core/` – Shared types (DTOs, enums, validation) used by api and blazor
- `data/` – EF Core DbContext, entities, migrations (referenced by api only)

## Architecture Rules
- `core` must stay dependency-light: no EF Core, Npgsql, Azure SDKs or
  anything that won't run in the browser.
- `blazor` must never contain secrets, connection strings or API keys.
  Everything in it is publicly downloadable.
- `blazor` talks to the database only via `api`, never directly.
- AI calls go through an abstraction (e.g. `IActionExtractor`) so Ollama can
  be swapped for a hosted provider later.
- Export targets (GitHub, DevOps, Jira, email) are separate implementations
  of a common interface, not a big switch statement.

## Configuration
- All connection details come from configuration/env vars, never hardcoded.
- Dev values live in `api/local.settings.json` (gitignored).
- Key settings: `ConnectionStrings__Postgres`, `Ollama__BaseUrl`, `Ollama__Model`

## Development Environment
- PostgreSQL and Ollama run on separate machines on the local network.
- Front end calls the API via a configurable base URL (`wwwroot/appsettings.json`).
- Local machine-specific details: @~/.claude/distillforge-local.md

## Commands
- Build all: `dotnet build`
- Run API: `cd api && func start`
- Run front end: `cd blazor && dotnet watch`
- Tests: `dotnet test`
- New migration: `dotnet ef migrations add <Name> --project data --startup-project api`

## Deployment (planned)
- API + database: Azure (Functions + Azure Database for PostgreSQL)
- Front end: GitHub Pages, possibly Azure Static Web Apps. Decide later.
- GitHub Pages needs: correct `<base href>`, `.nojekyll`, 404.html SPA
  fallback, and CORS on the Function App for the Pages origin.