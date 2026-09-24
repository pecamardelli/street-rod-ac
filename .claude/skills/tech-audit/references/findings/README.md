# Audit Findings Archive

Each audit run writes one Markdown file here, named:

```
<YYYY-MM-DD>-<section>.md
```

Examples:

- `2026-09-23-security.md`
- `2026-09-23-all.md`
- `2026-09-30-performance.md`

If two runs happen on the same day for the same section, suffix with `-2`, `-3`, etc.

## File structure

Every findings file follows the template in `SKILL.md` Phase 5. Subagent output flows into the severity-ordered sections; the executive summary at the top is what the chat surfaces back to the user.

## Retention

Keep all findings files — they're cheap and the diff between runs is the most valuable signal this skill produces. Only the one-line index lives in `analysis-log.md`.

## Cross-reference

`analysis-log.md` rows link to the findings file via the last column. To trace what was fixed between two runs, diff the two findings files.
