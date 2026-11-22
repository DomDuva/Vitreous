// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';
import { existsSync, readFileSync } from 'node:fs';

// The API reference pages and their sidebar come from `npm run api` (DocFX + scripts/api-reference.mjs).
// `npm run build:site` builds without .NET, and then the site has no API reference.
const apiSidebarFile = new URL('./src/content/docs/api/_sidebar.json', import.meta.url);
const apiSidebar = existsSync(apiSidebarFile) ? JSON.parse(readFileSync(apiSidebarFile, 'utf8')) : [];

// https://astro.build/config
export default defineConfig({
	// GitHub Pages serves the site from /Vitreous (case-sensitive), so every root-relative
	// link has to include the base. The links validator fails the build when one doesn't.
	site: 'https://domduva.github.io',
	base: '/Vitreous',
	integrations: [
		starlight({
			title: 'Vitreous',
			description:
				'A high-performance, source-generated mediator for .NET 8, 9, 10 and 11, with a Result type, caching, a developer UI, OpenTelemetry and Native AOT support.',
			logo: { src: './src/assets/logo.svg' },
			favicon: '/favicon.svg',
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/DomDuva/Vitreous' }],
			editLink: { baseUrl: 'https://github.com/DomDuva/Vitreous/edit/main/docs/' },
			lastUpdated: true,
			customCss: ['./src/styles/custom.css'],
			components: {
				// Adds a NuGet link next to the GitHub icon; Starlight has no NuGet icon.
				SocialIcons: './src/components/SocialIcons.astro',
			},
			plugins: [starlightLinksValidator()],
			sidebar: [
				{ label: 'Getting started', items: [{ autogenerate: { directory: 'getting-started' } }] },
				{ label: 'Concepts', items: [{ autogenerate: { directory: 'concepts' } }] },
				{ label: 'Guides', items: [{ autogenerate: { directory: 'guides' } }] },
				{ label: 'Reference', items: [{ autogenerate: { directory: 'reference' } }] },
				...(apiSidebar.length ? [{ label: 'API reference', collapsed: true, items: apiSidebar }] : []),
				{ label: 'Troubleshooting', slug: 'troubleshooting' },
			],
		}),
	],
});
