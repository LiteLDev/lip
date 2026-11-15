#!/usr/bin/env node
// Writes the Traditional Chinese pages, docs/zh-Hant/**/*.md, from the Simplified ones, docs/zh/**/*.md, with the
// words that the interface of lip uses in Traditional Chinese. `pnpm build` and `pnpm dev` run it first; the pages
// it writes are not committed (docs/.gitignore). See docs/CONTRIBUTING.md.
//
//   node scripts/zh-hant.mjs [--quiet]
//
// A page is converted in three steps, and only outside what is kept as written (fenced code, inline code, link
// targets, URLs):
//
// 1. A text of the interface in bold or in quotes (**新建服务器…**, 「…」, “…”) becomes the text that the interface
//    shows in Traditional Chinese (**建立伺服器…**): the pairs are read from the L.T(en, zhHans, zhHant) calls of
//    src/Lip.Cli/Tui, so they follow the interface.
// 2. The words of scripts/zh-hant-terms.mjs become their words of Taiwan, the longest match first.
// 3. OpenCC (cn → tw) converts the characters of the rest, as levimc-kit does for a locale with `convertFrom`, and
//    the quotation marks “ ” become 「 」.
//
// In link targets, /screenshots/zh-Hans/ becomes /screenshots/zh-Hant/ and a leading /zh/ becomes /zh-Hant/; a
// #fragment is converted like the heading it points at.
import { existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { TERMS } from './zh-hant-terms.mjs';

const docs = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const from = join(docs, 'zh');
const to = join(docs, 'zh-Hant');
const interfaceSources = resolve(docs, '..', 'src', 'Lip.Cli', 'Tui');
const quiet = process.argv.includes('--quiet');

const OpenCC = await import('opencc-js/cn2t');
const characters = OpenCC.Converter({ from: 'cn', to: 'tw' });

/** Every file below a directory with the extension, as absolute paths. */
function files(directory, extension) {
  const out = [];
  if (!existsSync(directory)) return out;
  for (const entry of readdirSync(directory)) {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) out.push(...files(path, extension));
    else if (path.endsWith(extension)) out.push(path);
  }
  return out.sort();
}

/**
 * The texts of the interface: Simplified → Traditional, for every L.T(en, zhHans, zhHant) whose arguments are
 * string literals without placeholders, and whose Traditional text is not the Simplified one.
 */
function interfaceTexts() {
  const literal = String.raw`\$?"(?:[^"\\\n]|\\.)*"`;
  const argument = String.raw`(${literal}(?:\s*\+\s*${literal})*)`;
  const call = new RegExp(String.raw`\bL\.T\(\s*${argument}\s*,\s*${argument}\s*,\s*${argument}\s*\)`, 'g');
  const value = (code) =>
    [...code.matchAll(new RegExp(literal, 'g'))]
      .map((m) => m[0].replace(/^\$?"/, '').replace(/"$/, '').replace(/\\(.)/g, (_, c) => (c === 'n' ? '\n' : c)))
      .join('');
  const texts = new Map();
  for (const file of files(interfaceSources, '.cs')) {
    for (const m of readFileSync(file, 'utf8').matchAll(call)) {
      const interpolated = [m[2], m[3]].some((code) => /\$"/.test(code) && /\{/.test(code));
      const [hans, hant] = [value(m[2]), value(m[3])];
      if (!interpolated && hans !== hant) texts.set(hans, hant);
    }
  }
  return texts;
}

const texts = interfaceTexts();
if (texts.size === 0) {
  console.warn(`zh-hant: no text of the interface was found in ${interfaceSources}; only the term table is used.`);
}

// The longest term that matches at a place wins.
const terms = Object.keys(TERMS).sort((a, b) => b.length - a.length);
const termPattern = new RegExp(terms.map((t) => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|'), 'g');

/** Prose without any text of the interface: the terms, then the characters of what is left. */
function words(text) {
  let out = '';
  let last = 0;
  for (const m of text.matchAll(termPattern)) {
    out += characters(text.slice(last, m.index)) + TERMS[m[0]];
    last = m.index + m[0].length;
  }
  return out + characters(text.slice(last));
}

// A text of the interface as a page marks it: in bold, or in quotes.
const MARKED = /(\*\*)([^*\n]+)(\*\*)|(「)([^」\n]+)(」)|(“)([^”\n]+)(”)/g;

/** Prose: marked texts of the interface as the interface has them, and the rest by words. */
function prose(text) {
  let out = '';
  let last = 0;
  for (const m of text.matchAll(MARKED)) {
    const [open, inner, close] = m.slice(1).filter((part) => part !== undefined);
    const shown = texts.get(inner);
    if (shown === undefined) continue;
    out += words(text.slice(last, m.index)) + open + shown + close;
    last = m.index + m[0].length;
  }
  return out + words(text.slice(last));
}

/** Prose as Taiwan punctuates it: the quotation marks “ ” are 「 」. */
const punctuated = (text) => prose(text).replace(/“/g, '「').replace(/”/g, '」');

/** A link target: its path with the pictures and the pages of the locale, its fragment as the heading it names. */
function target(link) {
  // A link to another site is kept whole, its fragment too.
  if (/^[a-z][a-z0-9+.-]*:/i.test(link)) return link;
  const hash = link.indexOf('#');
  const path = (hash < 0 ? link : link.slice(0, hash))
    .replace('/screenshots/zh-Hans/', '/screenshots/zh-Hant/')
    .replace(/^\/zh\//, '/zh-Hant/');
  return hash < 0 ? path : path + '#' + words(link.slice(hash + 1));
}

// What is kept as written, as levimc-kit keeps it (kit/docs/node/convert.ts): fenced code, inline code, link
// targets (`](…`, href="…", src="…"), autolinks and bare URLs.
const KEEP =
  /(?:^|\n)[ \t]*(`{3,}|~{3,})[^\n]*\n(?:[\s\S]*?\n)?[ \t]*\1[ \t]*(?=\n|$)|`[^`\n]+`|\]\([^)\s]*|\b(?:href|src)\s*=\s*(["'])[^"']*\2|<https?:[^>]*>|https?:\/\/[^\s)<>"']+/g;

/** A page in Traditional Chinese. */
export function convertPage(markdown) {
  // The links of the front matter of a home page (`link: /zh/intro/…`) are no Markdown links.
  const md = markdown.replace(/^(\s*link:\s*)\/zh\//gm, '$1/zh-Hant/');
  // What is kept is taken out first, each span for a character that no page has, so that a text in bold is
  // found whole whatever code or link stands before it in its line; then the prose is converted, and the spans
  // are put back.
  const kept = [];
  const held = md.replace(KEEP, (span) => {
    let text = span;
    if (span.startsWith('](')) text = '](' + target(span.slice(2));
    else if (/^(href|src)/.test(span)) text = span.replace(/(["'])([^"']*)\1$/, (_, q, link) => q + target(link) + q);
    kept.push(text);
    return `\uE000${kept.length - 1}\uE001`;
  });
  return punctuated(held).replace(/\uE000(\d+)\uE001/g, (_, index) => kept[Number(index)]);
}

// Write the pages that changed, and delete those whose Simplified page is gone: the directory is this script's.
const wanted = new Set();
let written = 0;
for (const source of files(from, '.md')) {
  const page = join(to, relative(from, source));
  wanted.add(page);
  const text = convertPage(readFileSync(source, 'utf8'));
  if (existsSync(page) && readFileSync(page, 'utf8') === text) continue;
  mkdirSync(dirname(page), { recursive: true });
  writeFileSync(page, text);
  written++;
}
let deleted = 0;
for (const page of files(to, '.md')) {
  if (wanted.has(page)) continue;
  rmSync(page);
  deleted++;
}
if (!quiet) {
  console.log(
    `zh-hant: ${wanted.size} pages in ${relative(docs, to)}/ (${written} written, ${deleted} deleted),` +
      ` with ${texts.size} texts of the interface and ${terms.length} terms`,
  );
}
