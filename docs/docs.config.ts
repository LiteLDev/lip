// lip documentation — levimc-kit site config.
// The LeviMC navigation model: every top-level nav entry is a tab with its own sidebar tree.
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { defineDocsConfig, type NavEntry } from './kit/docs/node/config.ts';

const repo = 'https://github.com/LiteLDev/lip';

interface Labels {
  guide: string;
  gettingStarted: string;
  whatIsLip: string;
  installation: string;
  firstServer: string;
  usingInterface: string;
  tour: string;
  // The pages of the interface, named as the interface names them (src/Lip.Cli/Tui/Pages/*/*Text.cs, Title).
  home: string;
  server: string;
  mods: string;
  getMods: string;
  gameConfig: string;
  modConfig: string;
  workspaces: string;
  tools: string;
  settings: string;
  usingCommands: string;
  serverFromCommands: string;
  managingPackages: string;
  configuration: string;
  linuxMacos: string;
  troubleshooting: string;
  faq: string;
  authors: string;
  creatingPackages: string;
  migratingPackages: string;
  concepts: string;
  howLipWorks: string;
  stateFile: string;
  internals: string;
  reference: string;
  cli: string;
  interfaceReference: string;
  files: string;
  commands: string;
}

type Locale = 'root' | 'zh' | 'zh-Hant';

const LABELS: Record<Locale, Labels> = {
  root: {
    guide: 'Guide',
    gettingStarted: 'Getting started',
    whatIsLip: 'What is lip?',
    installation: 'Installation',
    firstServer: 'Your first server',
    usingInterface: 'Using the interface',
    tour: 'A tour of the interface',
    home: 'Home',
    server: 'Server',
    mods: 'Mods',
    getMods: 'Get mods',
    gameConfig: 'Game config',
    modConfig: 'Mod config',
    workspaces: 'Workspaces',
    tools: 'Tools',
    settings: 'Settings',
    usingCommands: 'Using the commands',
    serverFromCommands: 'A server from the commands',
    managingPackages: 'Managing packages',
    configuration: 'Configuration',
    linuxMacos: 'Linux and macOS',
    troubleshooting: 'Troubleshooting',
    faq: 'FAQ',
    authors: 'For package authors',
    creatingPackages: 'Creating packages',
    migratingPackages: 'Migrating packages',
    concepts: 'Concepts',
    howLipWorks: 'How lip works',
    stateFile: 'State file',
    internals: 'Internals',
    reference: 'Reference',
    cli: 'Command line',
    interfaceReference: 'Interface',
    files: 'Files',
    commands: 'Commands',
  },
  zh: {
    guide: '指南',
    gettingStarted: '入门',
    whatIsLip: 'lip 是什么？',
    installation: '安装',
    firstServer: '第一个服务器',
    usingInterface: '使用界面',
    tour: '界面导览',
    home: '主页',
    server: '服务器',
    mods: '模组',
    getMods: '获取模组',
    gameConfig: '游戏配置',
    modConfig: '模组配置',
    workspaces: '工作区',
    tools: '工具',
    settings: '设置',
    usingCommands: '使用命令',
    serverFromCommands: '用命令开服',
    managingPackages: '管理包',
    configuration: '配置',
    linuxMacos: 'Linux 与 macOS',
    troubleshooting: '故障排查',
    faq: '常见问题',
    authors: '写给包作者',
    creatingPackages: '创建包',
    migratingPackages: '迁移包',
    concepts: '概念',
    howLipWorks: 'lip 的工作方式',
    stateFile: '状态文件',
    internals: '内部机制',
    reference: '参考',
    cli: '命令行',
    interfaceReference: '界面',
    files: '文件',
    commands: '命令',
  },
  // Written by hand, with the words of the interface in Traditional Chinese: the conversion of the kit works on
  // characters only (服務器, 設置, 界面), and the pages are made by scripts/zh-hant.mjs (see CONTRIBUTING.md).
  'zh-Hant': {
    guide: '指南',
    gettingStarted: '入門',
    whatIsLip: 'lip 是什麼？',
    installation: '安裝',
    firstServer: '第一個伺服器',
    usingInterface: '使用介面',
    tour: '介面導覽',
    home: '首頁',
    server: '伺服器',
    mods: '模組',
    getMods: '取得模組',
    gameConfig: '遊戲設定',
    modConfig: '模組設定',
    workspaces: '工作區',
    tools: '工具',
    settings: '設定',
    usingCommands: '使用命令',
    serverFromCommands: '用命令開服',
    managingPackages: '管理套件',
    configuration: '設定',
    linuxMacos: 'Linux 與 macOS',
    troubleshooting: '疑難排解',
    faq: '常見問題',
    authors: '寫給套件作者',
    creatingPackages: '建立套件',
    migratingPackages: '移轉套件',
    concepts: '概念',
    howLipWorks: 'lip 的運作方式',
    stateFile: '狀態檔',
    internals: '內部機制',
    reference: '參考',
    cli: '命令列',
    interfaceReference: '介面',
    files: '檔案',
    commands: '命令',
  },
};

const COMMANDS = ['cache_clean', 'config', 'daemon', 'install', 'list', 'pkg_init', 'pkg_migrate', 'pkg_pack', 'pkg_view', 'run', 'self_update', 'setup', 'uninstall', 'update'];

/** The tabs of one locale (`p`: its URL prefix, '/', '/zh/' or '/zh-Hant/'), each with the tree of its sidebar. */
const nav = (p: string, t: Labels): NavEntry[] => [
  {
    text: t.guide,
    items: [
      {
        text: t.gettingStarted,
        items: [
          { text: t.whatIsLip, link: `${p}intro/what_is_lip` },
          { text: t.installation, link: `${p}intro/installation` },
          { text: t.firstServer, link: `${p}intro/first_server` },
        ],
      },
      {
        text: t.usingInterface,
        items: [
          { text: t.tour, link: `${p}interface/tour` },
          { text: t.home, link: `${p}interface/home` },
          { text: t.server, link: `${p}interface/server` },
          { text: t.mods, link: `${p}interface/mods` },
          { text: t.getMods, link: `${p}interface/get_mods` },
          { text: t.gameConfig, link: `${p}interface/game_config` },
          { text: t.modConfig, link: `${p}interface/mod_config` },
          { text: t.workspaces, link: `${p}interface/workspaces` },
          { text: t.tools, link: `${p}interface/tools` },
          { text: t.settings, link: `${p}interface/settings` },
        ],
      },
      {
        text: t.usingCommands,
        items: [
          { text: t.serverFromCommands, link: `${p}guides/server_from_commands` },
          { text: t.managingPackages, link: `${p}guides/managing_packages` },
          { text: t.configuration, link: `${p}guides/configuration` },
          { text: t.linuxMacos, link: `${p}guides/linux_macos` },
          { text: t.troubleshooting, link: `${p}guides/troubleshooting` },
          { text: t.faq, link: `${p}faq` },
        ],
      },
      {
        text: t.authors,
        items: [
          { text: t.creatingPackages, link: `${p}authors/creating_packages` },
          { text: t.migratingPackages, link: `${p}authors/migrating_packages` },
        ],
      },
    ],
  },
  {
    text: t.concepts,
    items: [
      { text: t.howLipWorks, link: `${p}concepts/how_lip_works` },
      { text: 'lip.toml', link: `${p}concepts/lip_toml` },
      { text: t.stateFile, link: `${p}concepts/state_file` },
      { text: t.internals, link: `${p}concepts/internals` },
    ],
  },
  {
    text: t.reference,
    items: [
      { text: t.cli, link: `${p}reference/cli` },
      { text: t.interfaceReference, link: `${p}reference/interface` },
      { text: t.configuration, link: `${p}reference/configuration` },
      { text: t.files, link: `${p}reference/files` },
      // A page is named after its command with "_" for " " or "-" (self_update: lip self-update).
      { text: t.commands, items: COMMANDS.map((c) => ({ text: c === 'self_update' ? 'self-update' : c.replace('_', ' '), link: `${p}reference/commands/${c}` })) },
    ],
  },
];

// Pages that moved or were removed: their old URLs, in every locale, open the page that took their place.
const MOVED: Record<string, string> = {
  // The documentation was rebuilt around the interface (lip 0.41).
  'intro/quick_start': 'intro/first_server.html',
  'guides/running_server': 'guides/server_from_commands.html',
  'guides/installing_dependencies': 'guides/managing_packages.html',
  'guides/managing_configuration': 'guides/configuration.html',
  'guides/creating_packages': 'authors/creating_packages.html',
  'guides/migrating_packages': 'authors/migrating_packages.html',
  'guides/internal_mechanism': 'concepts/internals.html',
  'concepts/architecture': 'concepts/how_lip_works.html',
  'concepts/lockfiles': 'concepts/state_file.html',
  'cli/overview': 'reference/cli.html',
  'cli/tui': 'reference/interface.html',
  'cli/configuration': 'reference/configuration.html',
  ...Object.fromEntries(COMMANDS.map((c) => [`cli/commands/${c}`, `reference/commands/${c}.html`])),
  // Since lip v1: JSON-RPC, tooth.json → lip.toml, the command group pkg.
  'rpc/overview': 'reference/cli.html',
  'rpc/json_rpc_spec': 'reference/cli.html',
  'cli/commands/rpc': 'reference/cli.html',
  'concepts/tooth_json': 'concepts/lip_toml.html',
  'concepts/lip_yaml': 'concepts/lip_toml.html',
  'cli/commands/init': 'reference/commands/pkg_init.html',
  'cli/commands/pack': 'reference/commands/pkg_pack.html',
  'cli/commands/view': 'reference/commands/pkg_view.html',
  'cli/commands/versions': 'reference/commands/pkg_view.html',
  'cli/commands/migrate': 'reference/commands/pkg_migrate.html',
};
// A page that is still where it was needs no redirect, and the kit refuses one: the old URL of a page redirects
// from the build in which its file is gone. The Traditional Chinese pages are those of zh/ (scripts/zh-hant.mjs).
const isPage = (folder: string, page: string) => existsSync(fileURLToPath(new URL(`./${folder}${page}.md`, import.meta.url)));
const redirects = Object.fromEntries(
  (
    [
      ['', ''],
      ['zh/', 'zh/'],
      ['zh-Hant/', 'zh/'],
    ] as const
  ).flatMap(([p, folder]) =>
    Object.entries(MOVED)
      .filter(([from]) => !isPage(folder, from))
      .map(([from, to]) => [p + from, p + to]),
  ),
);

export default defineDocsConfig({
  base: '/lip/',
  title: 'lip',
  // The NAME line of lip(1) (protocol/lip.md).
  description: 'Run a LeviLamina server and manage its packages',
  // the same mark as lip's tile in the LeviMC Projects menu (levimc.org/logos/lip.webp)
  logo: { src: '/logo.webp', alt: 'lip' },
  favicon: '/logo.webp',
  project: 'lip',
  contentDir: '.',
  // For writers, not pages of the site: how the screenshots of the interface are made, and how the site is built.
  exclude: ['SCREENSHOTS.md', 'CONTRIBUTING.md'],
  repo,
  // The Traditional Chinese pages are made from the Simplified ones (scripts/zh-hant.mjs): those are the ones to edit.
  editLink: { pattern: (source) => `${repo}/edit/main/docs/${source.replace(/^zh-Hant\//, 'zh/')}` },
  hostname: 'https://levimc.org',
  sitemap: { hostname: 'https://levimc.org' },
  i18n: {
    mode: 'folder',
    defaultLocale: 'root',
    locales: [
      { code: 'root', label: 'English', lang: 'en', prefix: '' },
      { code: 'zh', label: '简体中文', lang: 'zh-CN', prefix: 'zh/' },
      // Traditional Chinese: scripts/zh-hant.mjs writes zh-Hant/ from zh/ before every build, with the words of the
      // interface. A zh page that it did not write is still converted by the kit, character by character.
      { code: 'zh-Hant', label: '繁體中文', lang: 'zh-Hant', prefix: 'zh-Hant/', convertFrom: 'zh' },
    ],
  },
  // Existing URLs are /lip/<page>.html (VitePress without cleanUrls); keep them.
  urlStyle: 'html',
  cleanUrls: false,
  slug: 'vitepress',
  markdown: { compat: 'commonmark' },
  // VitePress default (the old config set no outline): "On this page" lists h2 headings only.
  outline: 2,
  sidebar: 'tabs',
  nav: { root: nav('/', LABELS.root), zh: nav('/zh/', LABELS.zh), 'zh-Hant': nav('/zh-Hant/', LABELS['zh-Hant']) },
  redirects,
});
