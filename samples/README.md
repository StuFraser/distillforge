# Sample source documents

Fictional, realistic documents for testing extraction end to end. Paste the contents of
a `.txt` or `.md` file into a new workspace; the `.docx` and `.pdf` files are for testing file import and text extraction. All companies, people, emails and phone numbers are
made up.

| File | Type | Detected strategy | What it exercises |
|------|------|-------------------|-------------------|
| `01-stocktake-feature-call.txt` | ~15 min call transcript: SaaS client manager, primary user, two focus users | Transcript | Many actions with owners and dates, agreed vs. parked scope, decisions spread across speakers, actions recapped at the end (useful for de-duplication) |
| `02-customer-email-stock-receipt.txt` | Customer email asking for a new goods-in process and chasing JIT / manufacturing demand follow-ups | Prose | Requests aimed at the recipient, follow-ups owned by third parties (Ellie, Raj), explicit deadlines, `From:`/`To:` headers that look like speaker lines |
| `03-rfp-wms-requirements.txt` | RFP for a WMS with ~90 numbered requirements and a functional-level estimate request | Prose | Long document, requirement IDs and MoSCoW priorities, response obligations and deadlines scattered through the sections |
| `04-board-meeting-minutes.docx` | Word: board minutes, ~400 words | Prose | Numbered agenda items, decisions ("approved") vs. actions, owners and dates inline |
| `05-project-status-report.docx` | Word: project status report with a risk table | Prose | Table of risks with owners, a decision with a deadline, numbered actions |
| `06-incident-postmortem.md` | Markdown: SEV-2 postmortem, ~500 words | Prose | Timeline bullets, action-item table, lessons that imply actions without owners |
| `07-sprint-retro-notes.md` | Markdown: very short retro (~180 words) | Prose | Short-document edge case; ideas vs. firm actions |
| `08-remote-working-policy.pdf` | PDF: HR policy, 2 pages | Prose | Obligations ("must") rather than tasks, few named owners, one dated action |
| `09-statement-of-work.docx` | Word: SOW with milestone table | Prose | Obligations on both parties, dated milestones, out-of-scope list |
| `10-support-chat-log.txt` | Plain text: support chat | Prose / Transcript | Commitments by the agent, customer deadline, side issue to be raised separately |
| `11-research-summary.pdf` | PDF: literature review, 2 pages | Prose | Mostly informational, recommendations list but few real actions (tests false positives) |
| `12-onboarding-checklist.md` | Markdown: task checklist with `- [ ]` items | Prose | Already-structured tasks, role-based sections |
| `13-quarterly-business-review.pdf` | PDF: account review with metrics table, 2 pages | Prose | Actions spread through the document and recapped in a final list (de-duplication) |
| `14-product-requirements.docx` | Word: PRD, ~860 words, numbered requirements | Prose | Requirements vs. open questions, review deadlines |
| `15-newsletter-article.md` | Markdown: staff newsletter | Prose | Mostly noise with a few real deadlines buried in it |

The transcript uses `[mm:ss] Name: text`, so `StrategyDetector` picks Transcript (one
segment per speaker turn). Some prose paragraphs are over 512 characters on purpose,
so the sentence-level split gets exercised as well.
