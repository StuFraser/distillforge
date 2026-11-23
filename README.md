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

> **Status:** early development, run locally. You can paste a document, extract notes with
> your own AI model, add, edit and delete notes, sort them into categories by drag and
> drop, and merge duplicates. File upload, sign-in and export are still to come (see
> [Roadmap](#roadmap)). The API has no authentication yet, so don't expose it beyond a
> trusted network.

## What you can do

- **Extract** action points from pasted text or an uploaded `.txt`, `.md`, `.docx` or text-based
  `.pdf` file. Long documents are split into token-sized
  windows, extracted window by window, then consolidated. You can cancel a run part-way.
- **Trace** every note to its source: click a note and its evidence is highlighted.
- **Edit** notes (title, summary, owner, due date, colour) and filter by party, deadline
  and possible duplicates.
- **Add and delete** notes. Right-click a note for Edit / Delete (delete asks first).
  Add your own note by right-clicking empty space in a notes panel, or by selecting a
  passage in the source and clicking "New note from selection", which attaches the passage
  as the note's evidence. Manual notes survive re-extraction.
- **Categorise** notes: add, rename, recolour and delete categories per workspace, and drag
  notes between Uncategorised and the categories.
- **Merge duplicates.** Likely repeats are merged automatically; notes that *might* be the
  same task are flagged ⚠ for you to review. Merging is non-destructive and can be undone.
- **Share by email.** Turn Uncategorised, a category or everything into a plain-text email: preview it,
  choose the notes and details, then open it in your mail program or copy it. Nothing is sent from
  DistillForge, so it needs no credentials. Very long lists are cut at a whole note (mail links are
  length-limited); Copy always gets the full text.
- **Choose the model** for each extraction from the providers you've configured.
- **Archive** workspaces (read-only) and keep several documents side by side.

There is a built-in **Help** page (the `?` in the top bar) written for end users.

## Bring your own AI

DistillForge doesn't host a model. It talks to any **OpenAI-compatible
chat-completions endpoint**, so it works with Ollama, LM Studio, OpenAI, Azure OpenAI,
OpenRouter and others. It only uses plain chat completions plus `response_format`, with
no code specific to any one backend.

Providers and models are managed in the app (the ⚙ in the top bar): a provider is an
endpoint plus optional API key, and each of its models has its own context window,
reserved output size, structured-output mode and a **Thinking** level. Thinking
(Model default, Off, Low, Medium, High) is sent as the OpenAI-style `reasoning_effort`
parameter, only when set. Reasoning models such as Qwen 3.5 or Gemma 4 spend output tokens
on hidden reasoning and can hit `finish_reason=length` at a small reserved output, so set
them to Off. Leave it on Model default for non-reasoning models (e.g. Llama 3.1) and for services
that reject the parameter; DeepSeek-R1 ignores it. API keys are encrypted at rest and are
never sent back to the browser. The model for each extraction is picked from a dropdown.

The `AiProvider__*` settings in `api/local.settings.json` are only a **fallback**, used
when no model has been configured in the app.

**Window size affects quality.** The context window you give a model decides how big each
extraction window is, and smaller windows often surface more (and more detailed) notes at
the cost of speed and some near-duplicates. See "Getting better results" in the Help page.

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
local model can easily outlast Azure's roughly 230-second HTTP timeout. A run can be
cancelled (`POST /api/runs/{id}/cancel`). Notes are only written when a run finishes, so
cancelling or failing never touches existing notes. The queue function is also bound by
`functionTimeout` in `api/host.json` (10 minutes); a run that exceeds it is marked failed.

## Duplicates and merging

Overlapping windows report some tasks twice, and the same task can be raised more than
once in a document (a request, then a later confirmation).

- **Automatic:** repeats from the same passage are merged in code, and the model's
  "exactly the same task" suggestions in the consolidation pass are applied. The model only
  suggests; code does the merging, so evidence is never rewritten. A **Merging** setting
  (low / medium / aggressive, next to the model dropdown) controls how readily it merges:
  low only flags, medium merges clear repeats (and won't merge notes with clearly different
  owners), aggressive also merges differently-worded repeats. Long lists are consolidated in batches.
- **Flagged:** the consolidation pass also lists notes that *might* be the same task, and a
  whole-document title comparison adds more. These show a ⚠ and are never merged
  automatically.
- **Manual:** drag a note onto another, or use the buttons in a note's detail view, to merge
  it into that note. A merged note keeps its own text and evidence, is hidden on the main
  screen, follows its parent's category, and can be unmerged.

## Architecture

```
Blazor WASM  ──HTTP──▶  Azure Functions (isolated)  ──▶  PostgreSQL
 (browser)                    │      ▲
                              ▼      │ queue: extraction-runs
                        OpenAI-compatible endpoint (Ollama, LM Studio, hosted…)
```

| Folder   | Project                    | Purpose |
|----------|----------------------------|---------|
| `core/`  | `DistillForge.Core`        | Segmenter, window planner, quote resolver, validator, de-duplication and merge rules, highlight runs, email export formatting, DTOs and validation. Light on dependencies and runs in the browser. |
| `data/`  | `DistillForge.Data`        | EF Core DbContext, entities, migrations (Npgsql), API-key encryption. |
| `api/`   | `DistillForge.Api`         | Azure Functions (isolated worker): HTTP endpoints, queue trigger, extraction runner and the database-free `ExtractionPipeline` it shares with the benchmark, AI client, file-to-text conversion. |
| `blazor/`| `DistillForge.Blazor`      | Standalone Blazor WebAssembly front end, including the Help page. |
| `tests/` | `DistillForge.Core.Tests`, `DistillForge.Api.Tests` | xUnit tests: core logic, validation, key encryption, email export; and the API note endpoints against in-memory SQLite (foreign keys enforced). |
| `tools/` | `DistillForge.Bench`       | Benchmark harness: runs the real extraction pipeline over `samples/` for every model × window-size combination, with no database or UI. See `tools/DistillForge.Bench/README.md`. |

Rules worth knowing:
- `blazor` holds no secrets. Everything in it can be downloaded by anyone, and it
  reaches data only through the API.
- `core` must not reference EF Core, Npgsql or Azure SDKs.
- AI access goes through `IActionExtractor`, built per run from the chosen model.
  Export targets that call external services will be separate implementations of one shared interface.

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

Then edit `api/local.settings.json`. You can leave the `AiProvider__*` settings out and
configure providers in the app instead (⚙ in the top bar).

| Setting | Example | Notes |
|---------|---------|-------|
| `ConnectionStrings__Postgres` | `Host=db;Database=distillforge;Username=…;Password=…` | |
| `AzureWebJobsStorage` | `UseDevelopmentStorage=true` | Azurite |
| `Auth__Mode` | `Dev` | `Dev` = fake user, no sign-in (Development only). Leave unset (Entra) once the app registrations exist; see [Sign-in](#4-sign-in-microsoft-entra-external-id) |
| `Auth__TenantId`, `Auth__ClientId` | *(GUIDs)* | Entra mode only: your tenant and the **API** app registration. `Auth__Audience` is an optional extra accepted audience |
| `Auth__Authority` | `https://<name>.ciamlogin.com/<tenant-id>/v2.0` | Entra External ID only. Where the API loads signing keys and the expected issuer from. Leave empty for a workforce tenant |
| `Auth__ClaimOrphanedData` | `true` / `false` | One-off: the first *new* user to sign in takes over rows created before auth existed (otherwise they are invisible to everyone). Turn on for that first sign-in, then off |
| `Encryption__Key` | *(32 random bytes, base64)* | `openssl rand -base64 32`. Needed to store API keys; back it up, as losing it makes stored keys unreadable |
| `AiProvider__BaseUrl` | `http://mini.lan:11434/v1` | **Fallback only**, used when no model is configured in the app. Include the `/v1` |
| `AiProvider__Model` | `llama3.1:8b` | Fallback |
| `AiProvider__ApiKey` | `ollama` | Fallback. Any non-empty value works for Ollama |
| `AiProvider__ContextTokens` | `8192` | Fallback. Match the server (e.g. `OLLAMA_CONTEXT_LENGTH`) |
| `AiProvider__ReserveOutput` | `2048` | Fallback. Tokens kept free for the model's reply |
| `AiProvider__SafetyMargin` | `0.1` | Used for all models |
| `AiProvider__ResponseFormat` | `JsonSchema` | Fallback. `JsonObject` or `None` for backends without schema support |
| `AiProvider__TimeoutSeconds` | `300` | Fallback |
| `Host.CORS` | `http://localhost:5189,https://localhost:7163` | Blazor dev origins |

### 3. Create the database

```bash
dotnet tool restore
dotnet ef database update --project data/DistillForge.Data.csproj --startup-project api/DistillForge.Api.csproj
```

`dotnet ef` reads the connection string from `api/local.settings.json`. A
`ConnectionStrings__Postgres` environment variable, if set, takes precedence (useful in CI).

### 4. Sign-in (Microsoft Entra External ID)

Sign-in uses **Microsoft Entra External ID**, Microsoft's service for public sign-up (email one-time
code, and social providers if you add them). It is free for the first 50,000 monthly active users;
check current pricing. Skip this section for local work and use dev mode (see [API](#api)).

The tenant and app IDs are not secrets: `blazor/wwwroot/appsettings.json` is downloaded by every
visitor, and there is no client secret anywhere. Security rests on the API validating every token.

One-off setup in the [Entra admin center](https://entra.microsoft.com):

1. **External tenant.** Entra ID → Overview → Manage tenants → Create → **External**. This needs an
   Azure subscription *in the directory you create it from* (otherwise the dropdown is empty). Choose
   the data location carefully: it cannot be changed. Then switch into the new tenant.
2. **API registration** (`DistillForge API`): single tenant. Under *Expose an API*, accept the default
   Application ID URI (`api://<client-id>`) and add a scope `access_as_user` (admins and users).
3. **Web registration** (`DistillForge Web`): add a **Single-page application** redirect URI for each
   front-end origin (e.g. `http://localhost:5189/authentication/login-callback`, and your deployed URL).
   Under *API permissions* add `access_as_user` from the API and **grant admin consent**.
4. **User flow.** External Identities → User flows → New: *Email one-time passcode* (or with password),
   collecting **Display Name**, returned in the token (otherwise the name claim is `unknown`). Add the
   web app to the flow's *Applications*, or sign-up is not offered.

Then configure both sides:

| Where | Setting | Value |
|-------|---------|-------|
| `api/local.settings.json` | `Auth__Mode` | leave unset or `Entra` |
| | `Auth__TenantId` | the external tenant's id |
| | `Auth__Authority` | `https://<name>.ciamlogin.com/<tenant-id>/v2.0` |
| | `Auth__ClientId` | the **API** registration's client id |
| `blazor/wwwroot/appsettings.json` | `Auth:TenantId` | the external tenant's id |
| | `Auth:Authority` | `https://<name>.ciamlogin.com/<tenant-id>` |
| | `Auth:ClientId` | the **Web** registration's client id |
| | `Auth:ApiScope` | `api://<api-client-id>/access_as_user` |

Notes:

- **Issuer:** the API reads the expected issuer and signing keys from the tenant's published metadata.
  External ID uses `<tenant-id>.ciamlogin.com`, a workforce tenant `login.microsoftonline.com`.
  A workforce tenant also works: leave `Auth__Authority` empty and omit `Auth:Authority`.
  Workforce accounts must sign in with their tenant UPN (`you@<name>.onmicrosoft.com`); a personal
  Microsoft account is rejected unless invited as a guest.
- **Tiers:** each user has a `Tier` (`Lite` by default, or `Full`) in the `AppUsers` table, deliberately
  kept out of the identity provider so it stays swappable. Promote someone with
  `UPDATE "AppUsers" SET "Tier" = 'Full' WHERE "DisplayName" = '…';`. Per-tier limits are not enforced yet.
- **Sign-up is open:** anyone who finds the site can create an account, so per-user caps matter.

### 5. Run

```bash
npx azurite                    # if not using docker compose
cd api && func start           # API on http://localhost:7071/api
cd blazor && dotnet watch      # UI on http://localhost:5189
```

The front end finds the API through `blazor/wwwroot/appsettings.json` (`ApiBaseUrl`).

## API

Every endpoint requires sign-in: a valid access token from the configured Entra tenant (scope `access_as_user`), otherwise 401.
All data (workspaces, notes, AI providers) belongs to the signed-in user; other users' data returns 404. For local work without
sign-in, set `Auth__Mode=Dev` (a fixed fake user, no tokens; the API refuses to start with it outside Development) and leave the
`Auth` section of `blazor/wwwroot/appsettings.json` empty. Everything is under `/api`.

| Area | Routes |
|------|--------|
| Workspaces | `GET/POST workspaces`, `GET workspaces/{id}`, `PUT workspaces/{id}/source` (**wipes** notes, categories and runs), `POST workspaces/{id}/archive`, `POST workspaces/{id}/unarchive` |
| Extraction | `POST workspaces/{id}/extract` (`202`, or `409` if one is running; optional `{ aiModelId }`), `GET workspaces/{id}/runs`, `GET runs/{id}`, `POST runs/{id}/cancel` |
| Notes | `GET workspaces/{id}/actions` (includes merged notes), `POST workspaces/{id}/actions` (manual note; optional `sourceStart`/`sourceEnd` become its evidence), `PUT actions/{id}`, `DELETE actions/{id}` (returns the full note list; notes merged into it are freed), `PUT actions/{id}/category` |
| Merging | `POST actions/{id}/merge` `{ targetId }`, `POST actions/{id}/unmerge`, `POST actions/{id}/not-duplicate` `{ otherId }` |
| Categories | `GET/POST workspaces/{id}/categories`, `PUT/DELETE categories/{id}` |
| AI providers | `GET/POST ai/providers`, `PUT/DELETE ai/providers/{id}`, `POST ai/providers/{id}/models`, `PUT/DELETE ai/models/{id}`, `GET ai/models`, `POST ai/discover` (lists an endpoint's models; also "test connection") |

## Development

```bash
dotnet build
dotnet test
dotnet ef migrations add <Name> --project data/DistillForge.Data.csproj --startup-project api/DistillForge.Api.csproj
```

### Benchmarking models and window sizes

`tools/DistillForge.Bench` runs the extraction pipeline over the documents in `samples/` (text, Markdown,
Word and PDF) for each model and window profile you list in `bench.json`, and writes a table plus JSON and
CSV results to `bench-results/` (gitignored). Use it to tune defaults and catch regressions after changing
prompts or the pipeline. Local servers must really run the context size you configure (for Ollama, check
`ollama ps`); otherwise the results are invalid, and the run warns that the prompt was truncated.

```bash
cp tools/DistillForge.Bench/bench.sample.json tools/DistillForge.Bench/bench.json   # gitignored; edit
dotnet run --project tools/DistillForge.Bench -- --config tools/DistillForge.Bench/bench.json --max-docs 2
```

For iterating on the pipeline plumbing, a 3–4B model is enough. Switch to an 8B model
when tuning prompts and checking how faithfully quotes are copied.

## Roadmap

1. ✅ **Walking skeleton:** paste, segment, single-window extraction, notes, highlighting
2. ✅ (mostly) **Large documents:** token-budgeted windows with overlap, de-duplication in
   code plus a model consolidation pass, cancel and timeout handling, a benchmark harness
   (`tools/DistillForge.Bench`). Still to do: fuzzy quote matching (about 5% of quotes fall back
   to whole-segment highlighting in testing). Consolidation is now batched so small windows
   don't skip it, but it only compares notes within a batch (position order)
3. **Categories** ✅ (add/rename/delete, drag and drop), **bring-your-own AI settings** ✅,
   **duplicate flags and merging** ✅, in-app **Help** ✅, **manual notes** ✅,
   **deleting notes** ✅
4. ✅ **File upload:** txt, md, PDF (text only, no OCR), docx. Tables and multi-column PDFs
   are not yet checked closely
5. Sign-in (Entra External ID, open sign-up), per-user data and AI provider settings: 🚧 sign-in,
   token validation, per-user data and the Lite/Full `Tier` column are done; SSRF guard and the
   per-tier usage limits still to do
6. Export per category: ✅ **email** (preview, then open in your mail program or copy; no credentials).
   Next: GitHub Issues, then DevOps, Jira and others, behind one shared interface. These need saved
   credentials, so they follow Entra ID
7. Deployment: Azure Functions + Azure Database for PostgreSQL; front end on GitHub
   Pages or Azure Static Web Apps. Note Azure's Consumption plan caps `functionTimeout`
   at 10 minutes, so very long runs may need one queue message per window

## License

Copyright (C) 2026 Stuart Fraser

DistillForge is licensed under the [GNU Affero General Public License v3.0](LICENSE).
If you run a modified version of DistillForge as a network service, you must make your
modified source code available to its users (section 13).
