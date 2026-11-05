# AGENTS.md

- `protocol/` is the single source of truth.
- Before each commit, fix everything violating `protocol/`.
- Changing `protocol/` requires explicit user approval.
- `protocol/` describes only externally observable behavior: CLI, API, file formats, filesystem and network interactions, etc.
