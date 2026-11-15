# Writing the documentation

This page is for whoever writes the documentation. It is not a page of the site (`docs.config.ts` excludes it).
[SCREENSHOTS.md](SCREENSHOTS.md) says how the pictures of the interface are made.

## Building the site

The site is built by levimc-kit, which `pnpm install` fetches into `docs/kit/`. From `docs/`:

```
pnpm install --frozen-lockfile
pnpm typecheck      # docs.config.ts
pnpm build          # writes zh-Hant/, then dist/
pnpm linkcheck      # every link, anchor and picture of dist/
pnpm dev            # the site on a local server, rebuilt as you edit
```

When the pnpm of your machine refuses the version that `package.json` pins, run `npx pnpm@10.34.7 -C docs <script>`
from the root of the repository.

`docs.config.ts` holds what is not a page: the tabs and their sidebars, the labels in each language, and the
redirects of the pages that moved. A new page needs an entry in `nav` there; a page that moves needs one in `MOVED`.

## The three languages

| Language | Pages | Pictures |
|---|---|---|
| English | `docs/**/*.md` | `/screenshots/en/` |
| Simplified Chinese | `docs/zh/**/*.md` | `/screenshots/zh-Hans/` |
| Traditional Chinese | `docs/zh-Hant/**/*.md`, **written by a script** | `/screenshots/zh-Hant/` |

English and Simplified Chinese are written by hand, together: the same pages, the same headings in the same
order, the same pictures in their language.

### Traditional Chinese is generated

`scripts/zh-hant.mjs` writes `docs/zh-Hant/` from `docs/zh/`. `pnpm build` and `pnpm dev` run it first, and
`pnpm zh-hant` runs it alone. **Do not edit `docs/zh-Hant/`, and do not commit it**: it is in `.gitignore`, and the
script deletes what it did not write. The "Edit this page" link of a Traditional Chinese page opens the Simplified
page it was made from. A Traditional page has no "Last updated" date, since git does not know it.

While `pnpm dev` runs, run `pnpm zh-hant` in another terminal after you changed a Simplified page, to see its
Traditional page change.

The script exists because the interface of lip does not say in Traditional Chinese what a conversion of characters
makes of its Simplified text: 服务器 is 伺服器 there, not 服務器; 设置 is 設定, 缓存 is 快取, 界面 is 介面. For a
page, outside its code, its link targets and its URLs, the script does three things:

1. **Texts of the interface.** A text in bold or in quotes (`**新建服务器…**`, `「主页」`, `“应用”`) that is a text
   of the interface in Simplified Chinese becomes the text that the interface shows in Traditional Chinese
   (`**建立伺服器…**`, `「首頁」`, `「套用」`: the quotation marks “ ” become 「 」 everywhere). The pairs are read from the `L.T(en, zhHans, zhHant)` calls of
   `src/Lip.Cli/Tui/`, so they follow the interface without anybody copying them.
2. **Terms.** The words of `scripts/zh-hant-terms.mjs` become their words of Taiwan, the longest match first.
3. **Characters.** OpenCC (`cn` → `tw`) converts the rest, as the kit does for a page that has no file of its own.

In link targets, `/screenshots/zh-Hans/` becomes `/screenshots/zh-Hant/` and a leading `/zh/` becomes `/zh-Hant/`;
a `#fragment` is converted as the heading it names.

So, in a Simplified Chinese page:

- Write what the interface shows **in bold** (or in 「」), exactly as `…Text.cs` has it in Simplified Chinese, and
  alone: `**应用**`, not `**应用按钮**`. Then the Traditional page shows what the Traditional interface shows.
  Do not write it as code: code is kept as it is.
- When a Traditional page reads wrong, add the word to `scripts/zh-hant-terms.mjs`, and build again. A word that
  maps to itself keeps a longer word whole (`包含` is not `包`, 套件, and `含`).
- The labels of the tabs and the sidebar in Traditional Chinese are written by hand, in `LABELS['zh-Hant']` of
  `docs.config.ts`.

## Writing

- The name of the program is `lip`, in lower case, also at the start of a sentence.
- A page starts with one or two sentences that say what the reader can do after it.
- The protocol (`protocol/`) is right where a page and the protocol disagree. The texts of the interface are
  taken from the `…Text.cs` files of `src/Lip.Cli/Tui/`, in the language of the page.
- Link to another page by its relative path with `.md` (`../interface/server.md`); refer to a picture by its
  path below `public/`, with a leading slash (`/screenshots/en/home-01-empty.svg`).
- No page recommends a GitHub proxy by name.
