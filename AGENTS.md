# AGENTS.md

- MUST ensure that `specs/` is the single source of truth.
- MUST check if any code, doc, test, or any other thing in the project violates `specs` and auto fix before any commit.
- MUST require the user's explicit approval for every change to `specs/`.
- MUST only describe externally observable behavior in `specs/`: CLI interface, API, file formats, filesystem interaction behavior, network interaction behavior, etc.
