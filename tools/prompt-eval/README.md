# Prompt A/B/C test

The test from `docs/llm-prompting-research-2026-11-21.md` §10: does a shorter prompt write as well as the full one?
It scores quality and rule-following separately.

Prompt files contain real profile data. Keep every eval folder under `.local/prompt-eval/` (gitignored), never in the
repo.

## 1. Make the variants
For each listing (one in-field, one consultancy, one outside your field), copy the external prompt from the UI. Use
the concise template, plus one application-answers prompt. Save it as `.local/prompt-eval/<listing>/C.txt`.

Then make the other two variants **only by deleting text** from `C.txt`:

| Variant | Keep |
|---|---|
| `A.txt` | the task paragraph, the listing, "Essential rules", the lines from the skill `categoryId` rule through "Required JSON shape", the profile, and the reminder. Delete everything from "Preferences:" up to the categoryId rule. |
| `B.txt` | A, plus these preferences: the theme paragraph, the bullet-writing block, the space/bullet-strength paragraph, "technologies", skill selection, and the summary and headline paragraphs. |
| `C.txt` | unchanged. |

For an answers prompt there are only two variants: `C.txt`, and `A.txt` without the "Question types:" list.

## 2a. Manual run (any external chat)
Paste each variant into a fresh chat 3 times, using the same model each time. Save the replies as
`<folder>/manual/A-1.txt`, `A-2.txt`, … `C-3.txt`. Then blind and check them:

```bash
dotnet run tools/prompt-eval/prompt-eval.cs -- .local/prompt-eval/<listing> --blind .local/prompt-eval/<listing>/manual
```

## 2b. Runner (built-in provider)
```bash
export PROMPT_EVAL_MODEL=<model id>   # e.g. the Gemini Flash model the app uses
export PROMPT_EVAL_API_KEY=...        # your own key; never commit it
dotnet run tools/prompt-eval/prompt-eval.cs -- .local/prompt-eval/<listing> --runs 3
```

`PROMPT_EVAL_ENDPOINT` defaults to Gemini's OpenAI-compatible API. Set it to use OpenAI or Mistral instead.

## 3. Score
- `replies/<id>.txt` holds each reply under a random name.
- `checks.csv` holds the mechanical checks: valid JSON, bullets over 140 characters, em dashes, banned words,
  headline present, summary length.
- Score quality from 1 to 5 in `scores.csv` without opening `key.json`.
- Optionally paste a reply into the UI preview to see which bullets fall back.
- Then unblind with `key.json` and compare mean quality and check counts per variant.

Re-run the checks after editing replies with `--check-only`.
