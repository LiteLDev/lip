# AGENTS.md

## Scope

- Keep this file policy-only: workflow rules, memory rules, and skill-use guidance.
- Keep it stable and under 100 lines.
- Do not put project facts, experiment details, repo corrections, or session history here.
- Do not list transient MCP or skill inventory here; record only stable rules for when to use available capabilities.
- Put repo-specific guidance in `MEMORY.md`.
- Edit this file only when operating rules or memory/skill guidance change.

## Memory

- `MEMORY.md` must keep exactly three top-level sections: `## Memo`, `## Recent`, and `## History`.
- `## Memo` is for durable repo facts and workflow caveats worth rereading every run; keep it under 100 lines.
- `## Recent` is latest-first and keeps only the most recent 10 session bullets.
- `## History` is latest-first, keeps older compressed bullets, and should stay at 20 items or fewer.
- Do not duplicate `MEMORY.md` layout policy inside `MEMORY.md`; keep structure rules only in `AGENTS.md`.
- When `Recent` would exceed 10 items, rotate older items into `History` and compress them instead of letting `Recent` grow.
- When `History` would exceed 20 items, compress it back down instead of letting it grow indefinitely.
- If content is process or policy guidance rather than repo memory, put it in `AGENTS.md`, not `MEMORY.md`.
- If repo state conflicts with docs or memory, trust the repo and fix `MEMORY.md`.
- Update `MEMORY.md` as soon as you confirm a durable fact, outcome, or repo-specific workflow caveat; do not wait for a commit.
- Before finishing any non-trivial task, run a memory checkpoint: update `MEMORY.md` or explicitly conclude no durable repo memory changed.
- Before any requested commit, save relevant updates to `MEMORY.md`.

## Routine

For any non-trivial task:

- Read `MEMORY.md`.
- If recent context matters, skim recent commits and discussion-bearing commit messages.
- Reality-check the repo with fast commands before trusting docs or memory.
- Read only task-relevant files after the reality check.
- Record durable repo-specific findings in `MEMORY.md` during the work, not only at the end.
- Before the final response, do the memory checkpoint.

## Skills

- Use any explicitly named skill.
- Use any clearly matching skill.
- If a workflow is recurring, specialized, or clearly high-leverage and no installed skill fits, try `$find-skills` before inventing a new workflow.

## Rules

- Match the language, tone, and structure already used in the target file unless the user asks for a change.
- Prefer small, surgical edits over broad rewrites when updating existing files.
- When changing package management, build tooling, or release/docs workflows, update related CI and workflow files in the same change.
- Always use `pnpm` for JavaScript package management and script execution in this repo.
- Use `dotnet` as the default build, test, and formatting tool for the main codebase.
- After code changes, run `dotnet format --verify-no-changes` and `dotnet test` when feasible.
- If you change `docs/` content or the docs site config (`docs/docs.config.ts`), run `pnpm build` and `pnpm linkcheck` from `docs/`.
- Do not edit the vendored kit in `docs/kit/`; change levimc-kit upstream and re-vendor it, keeping the local kit patches listed in `MEMORY.md` until they are upstreamed.
- Before any requested commit, save relevant updates to `MEMORY.md`.
- For any requested commit, write a detailed commit body with the session goal, tasks, key rationale, findings, and results.
