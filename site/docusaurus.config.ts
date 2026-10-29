import {themes as prismThemes} from 'prism-react-renderer';
import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';

const config: Config = {
  title: 'Preferis',
  tagline: 'Open every link in the right browser.',
  favicon: 'img/favicon.svg',
  url: 'https://cysharp.github.io',
  baseUrl: '/Preferis/',
  organizationName: 'Cysharp',
  projectName: 'Preferis',
  trailingSlash: true,
  onBrokenLinks: 'throw',
  i18n: {
    defaultLocale: 'en',
    locales: ['en', 'ja'],
    localeConfigs: {
      en: {label: 'English', htmlLang: 'en'},
      ja: {label: '日本語', htmlLang: 'ja-JP'},
    },
  },
  presets: [
    [
      'classic',
      {
        docs: {
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          editUrl: ({locale, docPath}) => {
            const contentPath = locale === 'en'
              ? 'docs'
              : `i18n/${locale}/docusaurus-plugin-content-docs/current`;
            return `https://github.com/Cysharp/Preferis/edit/main/site/${contentPath}/${docPath}`;
          },
        },
        blog: false,
        pages: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],
  themeConfig: {
    navbar: {
      title: 'Preferis',
      items: [
        {
          type: 'docSidebar',
          sidebarId: 'docsSidebar',
          position: 'left',
          label: 'Documentation',
        },
        {type: 'localeDropdown', position: 'right'},
        {
          href: 'https://github.com/Cysharp/Preferis',
          label: 'GitHub',
          position: 'right',
        },
      ],
    },
    footer: {
      style: 'dark',
      copyright: 'Copyright © Cysharp, Inc. Built with Docusaurus.',
    },
    prism: {
      theme: prismThemes.github,
      darkTheme: prismThemes.dracula,
      additionalLanguages: ['csharp', 'powershell', 'json'],
    },
  } satisfies Preset.ThemeConfig,
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },
};

export default config;
