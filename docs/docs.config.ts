// lip documentation — levimc-kit site config (replaces .vitepress/config.mts).
// Nav and sidebars are ported verbatim from the former VitePress themeConfig (root + zh locales).
import { defineDocsConfig } from './kit/docs/node/config.ts';

export default defineDocsConfig({
  base: '/lip/',
  title: 'lip',
  description: 'A general package installer',
  // lip has no logo of its own: the header shows the kit's grass block, and the tab icon matches it.
  favicon: 'favicon.svg',
  project: 'lip',
  contentDir: '.',
  repo: 'https://github.com/LiteLDev/lip',
  editLink: 'https://github.com/LiteLDev/lip/edit/main/docs/:path',
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
  nav: {
    root: [
      { text: 'Intro', link: '/intro/quick_start' },
      { text: 'Concepts', link: '/concepts/architecture' },
      { text: 'Guides', link: '/guides/creating_packages' },
      { text: 'CLI', link: '/cli/overview' },
      { text: 'Daemon', link: '/daemon/overview' },
      { text: 'Spec', link: '/spec/package_format' },
    ],
    zh: [
      { text: '介绍', link: '/zh/intro/quick_start' },
      { text: '概念', link: '/zh/concepts/architecture' },
      { text: '指南', link: '/zh/guides/creating_packages' },
      { text: 'CLI', link: '/zh/cli/overview' },
      { text: '守护进程', link: '/zh/daemon/overview' },
      { text: '规范', link: '/zh/spec/package_format' },
    ],
  },
  sidebar: {
    root: {
      '/intro/': [
        {
          text: 'Introduction',
          items: [
            { text: 'Installation', link: '/intro/installation' },
            { text: 'Quick Start', link: '/intro/quick_start' },
          ],
        },
      ],
      '/concepts/': [
        {
          text: 'Concepts',
          items: [
            { text: 'Architecture', link: '/concepts/architecture' },
            { text: 'Package Manifest', link: '/concepts/package_manifest' },
            { text: 'Lockfiles', link: '/concepts/lockfiles' },
          ],
        },
      ],
      '/cli/': [
        {
          text: 'CLI Reference',
          items: [
            { text: 'Overview', link: '/cli/overview' },
            { text: 'Configuration', link: '/cli/configuration' },
            {
              text: 'Commands',
              items: [
                { text: 'cache clean', link: '/cli/commands/cache_clean' },
                { text: 'config', link: '/cli/commands/config' },
                { text: 'init', link: '/cli/commands/init' },
                { text: 'install', link: '/cli/commands/install' },
                { text: 'list', link: '/cli/commands/list' },
                { text: 'migrate', link: '/cli/commands/migrate' },
                { text: 'uninstall', link: '/cli/commands/uninstall' },
                { text: 'update', link: '/cli/commands/update' },
                { text: 'versions', link: '/cli/commands/versions' },
                { text: 'view', link: '/cli/commands/view' },
              ],
            },
          ],
        },
      ],
      '/daemon/': [
        {
          text: 'Daemon (lipd)',
          items: [
            { text: 'Overview', link: '/daemon/overview' },
            { text: 'JSON-RPC Spec', link: '/daemon/json_rpc_spec' },
            {
              text: 'Commands',
              items: [{ text: 'run', link: '/daemon/commands/run' }],
            },
          ],
        },
      ],
      '/guides/': [
        {
          text: 'How-to Guides',
          items: [
            { text: 'Creating Packages', link: '/guides/creating_packages' },
            { text: 'Migrating Packages', link: '/guides/migrating_packages' },
            { text: 'Installing Dependencies', link: '/guides/installing_dependencies' },
            { text: 'Managing Configuration', link: '/guides/managing_configuration' },
            { text: 'Internal Mechanism', link: '/guides/internal_mechanism' },
          ],
        },
      ],
      '/spec/': [
        {
          text: 'Specification',
          items: [{ text: 'Package Format', link: '/spec/package_format' }],
        },
      ],
    },
    zh: {
      '/zh/intro/': [
        {
          text: '入门',
          items: [
            { text: '安装', link: '/zh/intro/installation' },
            { text: '快速开始', link: '/zh/intro/quick_start' },
          ],
        },
      ],
      '/zh/concepts/': [
        {
          text: '概念',
          items: [
            { text: '架构', link: '/zh/concepts/architecture' },
            { text: '包清单', link: '/zh/concepts/package_manifest' },
            { text: '锁文件', link: '/zh/concepts/lockfiles' },
          ],
        },
      ],
      '/zh/cli/': [
        {
          text: 'CLI 参考',
          items: [
            { text: '概览', link: '/zh/cli/overview' },
            { text: '配置', link: '/zh/cli/configuration' },
            {
              text: '命令',
              items: [
                { text: 'cache clean', link: '/zh/cli/commands/cache_clean' },
                { text: 'config', link: '/zh/cli/commands/config' },
                { text: 'init', link: '/zh/cli/commands/init' },
                { text: 'install', link: '/zh/cli/commands/install' },
                { text: 'list', link: '/zh/cli/commands/list' },
                { text: 'migrate', link: '/zh/cli/commands/migrate' },
                { text: 'uninstall', link: '/zh/cli/commands/uninstall' },
                { text: 'update', link: '/zh/cli/commands/update' },
                { text: 'versions', link: '/zh/cli/commands/versions' },
                { text: 'view', link: '/zh/cli/commands/view' },
              ],
            },
          ],
        },
      ],
      '/zh/daemon/': [
        {
          text: '守护进程（lipd）',
          items: [
            { text: '概览', link: '/zh/daemon/overview' },
            { text: 'JSON-RPC 规范', link: '/zh/daemon/json_rpc_spec' },
            {
              text: '命令',
              items: [{ text: 'run', link: '/zh/daemon/commands/run' }],
            },
          ],
        },
      ],
      '/zh/guides/': [
        {
          text: '实践指南',
          items: [
            { text: '创建包', link: '/zh/guides/creating_packages' },
            { text: '迁移包', link: '/zh/guides/migrating_packages' },
            { text: '安装依赖', link: '/zh/guides/installing_dependencies' },
            { text: '管理配置', link: '/zh/guides/managing_configuration' },
            { text: '内部机制', link: '/zh/guides/internal_mechanism' },
          ],
        },
      ],
      '/zh/spec/': [
        {
          text: '规范',
          items: [{ text: '包格式', link: '/zh/spec/package_format' }],
        },
      ],
    },
  },
  social: [{ icon: 'github', link: 'https://github.com/LiteLDev/lip', label: 'GitHub' }],
});
