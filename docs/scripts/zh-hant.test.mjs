#!/usr/bin/env node
// Checks the converter of scripts/zh-hant.mjs: what a Simplified Chinese page becomes in Traditional Chinese,
// and what is kept as written. `pnpm test` runs it; it exits with 1 when a case fails.
//
//   node scripts/zh-hant.test.mjs
import { convertPage } from './zh-hant.mjs';

/** The cases: a name, a Simplified page, and the Traditional page it becomes. */
const CASES = [
  ['terms, the longest first', '服务器的设置和缓存，以及代理服务器。', '伺服器的設定和快取，以及代理伺服器。'],
  ['characters of the rest', '这里什么都没有。', '這裡什麼都沒有。'],
  ['inline code is kept', '运行 `lip 设置 服务器` 即可。', '執行 `lip 设置 服务器` 即可。'],
  [
    'fenced code is kept',
    '服务器：\n\n```sh\necho 服务器 "设置"\n```\n\n设置完成。',
    '伺服器：\n\n```sh\necho 服务器 "设置"\n```\n\n設定完成。',
  ],
  [
    'a fence with tildes, indented',
    '1. 步骤：\n\n   ~~~\n   服务器\n   ~~~\n\n   文件夹',
    '1. 步驟：\n\n   ~~~\n   服务器\n   ~~~\n\n   資料夾',
  ],
  [
    'the text of a link is converted, its target is not',
    '参见[服务器页面](../interface/server.md)。',
    '參見[伺服器頁面](../interface/server.md)。',
  ],
  [
    'a link below /zh/ leads to /zh-Hant/, and its fragment is converted as the heading',
    '参见[设置](/zh/interface/settings.md#代理服务器)。',
    '參見[設定](/zh-Hant/interface/settings.md#代理伺服器)。',
  ],
  [
    'a link to another site is kept whole',
    '参见[文档](https://example.com/zh/文档#设置)。',
    '參見[文件](https://example.com/zh/文档#设置)。',
  ],
  [
    'a picture: the alternative text is converted, the path names the Traditional picture',
    '![“服务器”页面。](/screenshots/zh-Hans/server-01-stopped.svg)',
    '![「伺服器」頁面。](/screenshots/zh-Hant/server-01-stopped.svg)',
  ],
  [
    'a bare URL is kept whole, its Chinese characters too, and ends before Chinese punctuation',
    '打开 https://example.com/设置 之前，先看 https://proxy.example/。然后设置服务器。',
    '開啟 https://example.com/设置 之前，先看 https://proxy.example/。然後設定伺服器。',
  ],
  [
    'a bare URL ends before a comma, a bracket or a quote of Chinese text',
    '（见 https://example.com/a）、“https://example.com/b”，以及 https://example.com/c：服务器',
    '（見 https://example.com/a）、「https://example.com/b」，以及 https://example.com/c：伺服器',
  ],
  [
    'a bare URL in an alternative text, right before Chinese punctuation, does not take the path with it',
    '![对话框里填着 https://proxy.example/。服务器的设置](/screenshots/zh-Hans/settings-03-proxy.svg)',
    '![對話方塊裡填著 https://proxy.example/。伺服器的設定](/screenshots/zh-Hant/settings-03-proxy.svg)',
  ],
  [
    'a bare URL as the text of a link ends before the bracket',
    '[https://example.com/a](/zh/intro/a.md)，服务器',
    '[https://example.com/a](/zh-Hant/intro/a.md)，伺服器',
  ],
  ['an autolink is kept', '见 <https://example.com/服务器>。', '見 <https://example.com/服务器>。'],
  [
    'href and src are targets',
    '<a href="/zh/faq.md">文件</a> <img src="/screenshots/zh-Hans/a.svg" alt="服务器">',
    '<a href="/zh-Hant/faq.md">檔案</a> <img src="/screenshots/zh-Hant/a.svg" alt="伺服器">',
  ],
  [
    'a text of the interface in bold is what the interface shows',
    '按 **新建服务器…**，然后选 **应用**。',
    '按 **建立伺服器…**，然後選 **套用**。',
  ],
  ['a text of the interface in corner quotes', '打开「主页」。', '開啟「首頁」。'],
  ['a text of the interface in curly quotes, which become corner quotes', '选择“应用”。', '選擇「套用」。'],
  ['bold that is no text of the interface is converted by words', '**服务器的文件**很重要。', '**伺服器的檔案**很重要。'],
  [
    'a text in bold after code and a link in the same line',
    '运行 `lip`，见[说明](a.md)，再按 **应用**。',
    '執行 `lip`，見[說明](a.md)，再按 **套用**。',
  ],
  ['the link of the front matter of a home page', '---\n  link: /zh/intro/\n---\n', '---\n  link: /zh-Hant/intro/\n---\n'],
];

let failed = 0;
for (const [name, hans, hant] of CASES) {
  const got = convertPage(hans);
  if (got === hant) continue;
  failed++;
  console.error(`zh-hant.test: ${name}\n  page:     ${JSON.stringify(hans)}\n  expected: ${JSON.stringify(hant)}\n  got:      ${JSON.stringify(got)}`);
}
if (failed > 0) {
  console.error(`zh-hant.test: ${failed} of ${CASES.length} cases failed`);
  process.exit(1);
}
console.log(`zh-hant.test: ${CASES.length} cases passed`);
