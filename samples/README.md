# Sample source documents

Fictional, realistic documents for testing extraction end to end. Paste the contents of
a `.txt` file into a new workspace. All companies, people, emails and phone numbers are
made up.

| File | Type | Detected strategy | What it exercises |
|------|------|-------------------|-------------------|
| `01-stocktake-feature-call.txt` | ~15 min call transcript: SaaS client manager, primary user, two focus users | Transcript | Many actions with owners and dates, agreed vs. parked scope, decisions spread across speakers, actions recapped at the end (useful for de-duplication) |
| `02-customer-email-stock-receipt.txt` | Customer email asking for a new goods-in process and chasing JIT / manufacturing demand follow-ups | Prose | Requests aimed at the recipient, follow-ups owned by third parties (Ellie, Raj), explicit deadlines, `From:`/`To:` headers that look like speaker lines |
| `03-rfp-wms-requirements.txt` | RFP for a WMS with ~90 numbered requirements and a functional-level estimate request | Prose | Long document, requirement IDs and MoSCoW priorities, response obligations and deadlines scattered through the sections |

The transcript uses `[mm:ss] Name: text`, so `StrategyDetector` picks Transcript (one
segment per speaker turn). Some prose paragraphs are over 512 characters on purpose,
so the sentence-level split gets exercised as well.
