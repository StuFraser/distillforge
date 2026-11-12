# DistillForge.Bench

Runs the real extraction pipeline (`ExtractionPipeline`: window planning, extraction, validation, quote resolution,
pass 1 dedupe, pass 2 consolidation) over the documents in `samples/` for every **model × window profile**
combination, with no database, queue or UI. Use it to compare models and window sizes, and to spot regressions
after changing prompts or pipeline code.

## Use

```bash
cp tools/DistillForge.Bench/bench.sample.json tools/DistillForge.Bench/bench.json   # gitignored; edit models/profiles
dotnet run --project tools/DistillForge.Bench -- --config tools/DistillForge.Bench/bench.json
```

Handy options: `--docs 01,04` (name prefixes), `--models`, `--profiles`, `--max-docs 2` (smoke test),
`--runs 3` (repeats, shows spread), `--baseline bench-results/bench-<earlier>.json` (note-count deltas). `--help` lists all.

For hosted providers put the key in an environment variable and name it with `apiKeyEnv`; don't put keys in `bench.json`.
Ctrl+C stops after the current call and still writes the results.

## Output

A table on the console, plus `bench-results/bench-<timestamp>.json` (per-run metrics, every note and warning) and a `.csv`.
Per run: notes, windows and failed windows, notes found before/after each merge pass, whether pass 2 ran or was skipped
(candidate list over budget), evidence quality (exact / normalised / **whole-segment fallback**), notes with owner and due,
tokens, and seconds.

## Limits

- No ground truth yet: it measures volume and reliability, not whether each note is a *correct* action.
  Next step is `*.expected.json` files per sample and precision/recall scoring.
- Flag counts and merging in the database layer (`ActionItemLink`, `NoteGraph`) aren't exercised.
- Ollama only honours the context window you configured server-side (`num_ctx`); the profile sets how the app windows the
  document, not what the server allows.
