# AGENTS.md

- `protocol/` is the single source of truth.
- Before each commit, fix everything violating `protocol/`.
- Changing `protocol/` requires explicit user approval.
- `protocol/` describes only externally observable behavior: CLI, API, file formats, filesystem and network interactions, etc.
- After changing anything in `protocol/`, bring the text and images of the documentation website ([LiteLDev/liteldev.github.io](https://github.com/LiteLDev/liteldev.github.io), `docs/lip/`; see `CONTRIBUTING.md`) in line with it.
