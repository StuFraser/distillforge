# DistillForge

Turn long documents into action points you can act on.

Paste in a meeting transcript, project plan or contract. DistillForge uses an LLM to
extract action points and shows them as sticky notes. Click a note and the passage it
came from is highlighted in the source, so every action point can be traced back to its
evidence.

The aim is for notes to be grouped into categories and published per category to other
tools (email, GitHub Issues, Azure DevOps, Jira, Trello, calendars). It's built for
business analysts, project consultants and anyone else with long documents full of
things that need doing.

> **Status:** early development. Milestone 1 (the walking skeleton) is in place:
> paste text → extract → sticky notes → click to highlight the source.
> Categories, file upload, sign-in and export are still to come (see [Roadmap](#roadmap)).

## Bring your own AI

DistillForge doesn't host a model. It talks to any **OpenAI-compatible
chat-completions endpoint**, so it works with Ollama, LM Studio, OpenAI, Azure OpenAI,
OpenRouter and others. It only uses plain chat completions plus `response_format`, with
no code specific to any one backend.

## How source linking works

LLMs are bad at counting characters, so the model never returns character positions.

1. **Normalise:** at ingest, line endings are normalised once. After that, the source
   text never changes.
2. **Segment:** the text is split into non-overlapping segments of at most 512
   characters, labelled `S1`, `S2`, and so on. Splitting tries structural units first
   (paragraphs for prose, speaker turns for transcripts), then sentences, then the last
   whitespace before the limit. Words are never split.
3. **Extract:** the model sees `[S12] text…` and returns JSON. Each action point lists
   its evidence as a segment ID plus a short verbatim quote:
   ```json
   { "action_points": [{
       "id": "ap_1", "title": "Send revised budget to finance",
       "summary": "…", "owner": "Sam", "due": "Friday",
       "evidence": [{ "segment_id": "S12", "quote": "I'll get the revised budget over by Friday" }]
   }]}
   ```
4. **Validate and resolve:** the code drops evidence that points at segment IDs that
   don't exist (a guard against hallucination). It then finds each quote inside its
   segment, trying in order:
   - **exact** match
   - **normalised** match: ignores whitespace, curly quotes, dashes and case, and copes
     with `…` in the quote
   - **whole segment**: if the quote still can't be found, the whole segment is
     highlighted and the note is flagged low confidence

Extraction runs **asynchronously**. `POST …/extract` returns `202`, a queue-triggered
function calls the model, and the front end polls the run until it finishes. Calls to a
local model can easily outlast Azure's roughly 230-second HTTP timeout.

## Architecture

```
Blazor WASM  ──HTTP──▶  Azure Functions (isolated)  ──▶  PostgreSQL
 (browser)                    │      ▲
                              ▼      │ queue: extraction-runs
                        OpenAI-compatible endpoint (Ollama, LM Studio, hosted…)
```

| Folder   | Project                    | Purpose |
|----------|----------------------------|---------|
| `core/`  | `DistillForge.Core`        | Segmenter, quote resolver, validator, highlight runs, DTOs. Light on dependencies and runs in the browser. |
| `data/`  | `DistillForge.Data`        | EF Core DbContext, entities, migrations (Npgsql). |
| `api/`   | `DistillForge.Api`         | Azure Functions (isolated worker): HTTP endpoints, queue trigger, AI client. |
| `blazor/`| `DistillForge.Blazor`      | Standalone Blazor WebAssembly front end. |
| `tests/` | `DistillForge.Core.Tests`  | xUnit tests for the core logic. |

Rules worth knowing:
- `blazor` holds no secrets. Everything in it can be downloaded by anyone, and it
  reaches data only through the API.
- `core` must not reference EF Core, Npgsql or Azure SDKs.
- AI access goes through `IActionExtractor`. Export targets will be separate
  implementations of one shared interface.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) (`func`)
- PostgreSQL (local, on another machine, or via `docker compose`)
- Azurite for queue storage: `npx azurite`, or via `docker compose`
- An OpenAI-compatible model endpoint, e.g. [Ollama](https://ollama.com) running an
  8B instruct model

### 1. Dependencies (optional Docker route)

```bash
docker compose up -d          # Postgres on :5432, Azurite on :10000-10002
```

### 2. Configure the API

```bash
cp api/local.settings.sample.json api/local.settings.json   # gitignored
```

Then edit `api/local.settings.json`:

| Setting | Example | Notes |
|---------|---------|-------|
| `ConnectionStrings__Postgres` | `Host=db;Database=distillforge;Username=…;Password=…` | |
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | Azurite |
| `AiProvider__BaseUrl` | `http://mini.lan:11434/v1` | Include the `/v1` |
| `AiProvider__Model` | `llama3.1:8b` | |
| `AiProvider__ApiKey` | `ollama` | Any non-empty value works for Ollama |
| `AiProvider__ContextTokens` | `8192` | Set it to match the server (e.g. `OLLAMA_CONTEXT_LENGTH`) |
| `AiProvider__ReserveOutput` | `2048` | Tokens kept free for the model's reply |
| `AiProvider__SafetyMargin` | `0.1` | |
| `AiProvider__ResponseFormat` | `JsonSchema` | `JsonObject` or `None` for backends without schema support |
| `AiProvider__TimeoutSeconds` | `300` | |
| `Host.CORS` | `http://localhost:5189,https://localhost:7163` | Blazor dev origins |

### 3. Create the database

```bash
dotnet tool restore
export ConnectionStrings__Postgres="Host=…;Database=distillforge;Username=…;Password=…"
dotnet ef database update --project data --startup-project api
```

### 4. Run

```bash
npx azurite                    # if not using docker compose
cd api && func start           # API on http://localhost:7071/api
cd blazor && dotnet watch      # UI on http://localhost:5189
```

The front end finds the API through `blazor/wwwroot/appsettings.json` (`ApiBaseUrl`).

## API (milestone 1)

Endpoints are anonymous until Entra ID sign-in is added.

| Method | Route | Description |
|--------|-------|-------------|
| `GET`  | `/api/workspaces` | List active workspaces |
| `POST` | `/api/workspaces` | Create from `{ sourceText, title?, strategy? }` |
| `GET`  | `/api/workspaces/{id}` | Source text and segments |
| `PUT`  | `/api/workspaces/{id}/source` | Replace the source; **wipes** notes, categories and runs |
| `POST` | `/api/workspaces/{id}/extract` | Queue an extraction (`202`, or `409` if one is already running) |
| `GET`  | `/api/runs/{id}` | Run status, token usage, warnings and error |
| `GET`  | `/api/workspaces/{id}/actions` | Action items with resolved evidence spans |

## Development

```bash
dotnet build
dotnet test
dotnet ef migrations add <Name> --project data --startup-project api
```

For iterating on the pipeline plumbing, a 3–4B model is enough. Switch to an 8B model
when tuning prompts and checking how faithfully quotes are copied.

## Roadmap

1. ✅ **Walking skeleton:** paste, segment, single-window extraction, notes, highlighting
2. Large documents: token-budgeted windows with overlap, de-duplication in code plus a
   model consolidation pass, fuzzy quote matching, a model comparison harness
3. Categories (colours, context menu, arrange by source order), manual notes
4. File upload: txt, md, PDF (text only, no OCR), docx
5. Entra ID sign-in, and per-user AI provider settings with encrypted API keys
6. Export per category: GitHub Issues first, then DevOps, Jira, email and others
7. Deployment: Azure Functions + Azure Database for PostgreSQL; front end on GitHub
   Pages or Azure Static Web Apps

## License

Copyright (C) 2026 Stuart Fraser

DistillForge is licensed under the [GNU Affero General Public License v3.0](LICENSE).
If you run a modified version of DistillForge as a network service, you must make your
modified source code available to its users (section 13).
