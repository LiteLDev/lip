# AGENTS.md

## refs/ is the single source of truth

`refs/` holds the normative specification of lip. It is the one anchor of the whole project: the code, the tests,
the schemas and the documentation all start from `refs/` and must agree with it. When anything else disagrees with
`refs/`, the other thing is wrong.

- **Every change to `refs/` needs the user's explicit approval.** This covers adding, editing, renaming and deleting
  any file under `refs/`, however small (typos and wording included). Propose the change and wait for the user to agree
  before writing it; approval of one change does not extend to another.
- **`refs/` describes only externally observable behavior**: the CLI interface, the API (such as the JSON-RPC
  interface of `lipd`), file formats (`manifest.json`, `tooth.json`, the lock), and what lip does to the workspace and
  reports to the user. It never describes implementation mechanisms: no internal modules, classes, algorithms, data
  structures, caches or file layouts that a user cannot observe.
- **`refs/` is written in English only.** There are no translations of it.
- When a task needs behavior that `refs/` does not define, or that contradicts it, stop and ask the user whether to
  change `refs/` first. Do not work around `refs/` in the code or the docs.
- Section numbers (`§N`) of `refs/` documents are cited from the code; keep them stable, and update the citations when
  an approved change renumbers them.
