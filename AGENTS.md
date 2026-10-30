# AGENTS.md

- MUST ensure that `refs/` is the single source of truth.
- MUST check if any code, doc, test, or any other thing in the project violates `refs` and auto fix before any commit.
- MUST require the user's explicit approval for every change to `refs/`.
- MUST only describe externally observable behavior in `refs/`: CLI interface, API, file formats, filesystem interaction behavior, network interaction behavior, etc.
