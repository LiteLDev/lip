# AGENTS.md

- MUST ensure that `protocol/` is the single source of truth.
- MUST check if any code, doc, test, or any other thing in the project violates `protocol/` and auto fix before any commit.
- MUST require the user's explicit approval for every change to `protocol/`.
- MUST only describe externally observable behavior in `protocol/`: CLI interface, API, file formats, filesystem interaction behavior, network interaction behavior, etc.
