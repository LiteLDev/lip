// lip documentation — levimc-kit site config (replaces .vitepress/config.mts).
// The LeviMC navigation model (kit 2.19): every top-level nav entry is a section with its own sidebar tree.
import { defineDocsConfig, type NavEntry } from './kit/docs/node/config.ts';

const repo = 'https://github.com/LiteLDev/lip';

interface Labels {
  guide: string;
  gettingStarted: string;
  installation: string;
  quickStart: string;
  howTo: string;
  creatingPackages: string;
  migratingPackages: string;
  installingDependencies: string;
  managingConfiguration: string;
  help: string;
  faq: string;
  concepts: string;
  architecture: string;
  workspaceFile: string;
  internalMechanism: string;
  cli: string;
  overview: string;
  configuration: string;
  commands: string;
}

const LABELS: Record<'root' | 'zh', Labels> = {
  root: {
    guide: 'Guide',
    gettingStarted: 'Getting Started',
    installation: 'Installation',
    quickStart: 'Quick Start',
    howTo: 'How-to Guides',
    creatingPackages: 'Creating Packages',
    migratingPackages: 'Migrating Packages',
    installingDependencies: 'Installing Dependencies',
    managingConfiguration: 'Managing Configuration',
    help: 'Help',
    faq: 'FAQ',
    concepts: 'Concepts',
    architecture: 'Architecture',
    workspaceFile: 'Workspace File',
    internalMechanism: 'Internal Mechanism',
    cli: 'CLI Reference',
    overview: 'Overview',
    configuration: 'Configuration',
    commands: 'Commands',
  },
  zh: {
    guide: '指南',
    gettingStarted: '入门',
    installation: '安装',
    quickStart: '快速开始',
    howTo: '实践指南',
    creatingPackages: '创建包',
    migratingPackages: '迁移包',
    installingDependencies: '安装依赖',
    managingConfiguration: '管理配置',
    help: '帮助',
    faq: '常见问题',
    concepts: '概念',
    architecture: '架构',
    workspaceFile: '工作区文件',
    internalMechanism: '内部机制',
    cli: 'CLI 参考',
    overview: '概览',
    configuration: '配置',
    commands: '命令',
  },
};

const COMMANDS = ['cache_clean', 'config', 'init', 'install', 'list', 'migrate', 'pack', 'uninstall', 'update', 'versions', 'view'];

/** The sections of one locale (`p`: its URL prefix, '/' or '/zh/'); zh-Hant is converted from zh by the kit. */
const nav = (p: string, t: Labels): NavEntry[] => [
  {
    text: t.guide,
    items: [
      {
        text: t.gettingStarted,
        items: [
          { text: t.installation, link: `${p}intro/installation` },
          { text: t.quickStart, link: `${p}intro/quick_start` },
        ],
      },
      {
        text: t.howTo,
        items: [
          { text: t.creatingPackages, link: `${p}guides/creating_packages` },
          { text: t.migratingPackages, link: `${p}guides/migrating_packages` },
          { text: t.installingDependencies, link: `${p}guides/installing_dependencies` },
          { text: t.managingConfiguration, link: `${p}guides/managing_configuration` },
        ],
      },
      { text: t.help, items: [{ text: t.faq, link: `${p}faq` }] },
    ],
  },
  {
    text: t.concepts,
    items: [
      { text: t.architecture, link: `${p}concepts/architecture` },
      { text: 'lip.yaml', link: `${p}concepts/lip_yaml` },
      { text: t.workspaceFile, link: `${p}concepts/lockfiles` },
      { text: t.internalMechanism, link: `${p}guides/internal_mechanism` },
    ],
  },
  {
    text: t.cli,
    items: [
      { text: t.overview, link: `${p}cli/overview` },
      { text: t.configuration, link: `${p}cli/configuration` },
      { text: t.commands, items: COMMANDS.map((c) => ({ text: c.replace('_', ' '), link: `${p}cli/commands/${c}` })) },
    ],
  },
];

// Pages removed in lip v1 (JSON-RPC, tooth.json → lip.yaml): their old URLs, in every locale, open the closest page.
const MOVED: Record<string, string> = {
  'rpc/overview': 'cli/overview.html',
  'rpc/json_rpc_spec': 'cli/overview.html',
  'cli/commands/rpc': 'cli/overview.html',
  'concepts/tooth_json': 'concepts/lip_yaml.html',
};
const redirects = Object.fromEntries(
  ['', 'zh/', 'zh-Hant/'].flatMap((p) => Object.entries(MOVED).map(([from, to]) => [p + from, p + to])),
);

export default defineDocsConfig({
  base: '/lip/',
  title: 'lip',
  description: 'A general package installer',
  // the same mark as lip's tile in the LeviMC Projects menu (levimc.org/logos/lip.webp)
  logo: { src: '/logo.webp', alt: 'lip' },
  favicon: '/logo.webp',
  project: 'lip',
  contentDir: '.',
  repo,
  editLink: `${repo}/edit/main/docs/:path`,
  hostname: 'https://docs.levimc.org',
  sitemap: { hostname: 'https://docs.levimc.org' },
  i18n: {
    mode: 'folder',
    defaultLocale: 'root',
    locales: [
      { code: 'root', label: 'English', lang: 'en', prefix: '' },
      { code: 'zh', label: '简体中文', lang: 'zh-CN', prefix: 'zh/' },
      // Traditional Chinese, generated from the zh pages (levimc-kit convertFrom)
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
  nav: { root: nav('/', LABELS.root), zh: nav('/zh/', LABELS.zh) },
  redirects,
});
