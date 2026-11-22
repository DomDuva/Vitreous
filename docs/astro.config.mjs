// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';
import starlightLinksValidator from 'starlight-links-validator';

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
				{ label: 'Troubleshooting', slug: 'troubleshooting' },
			],
		}),
	],
});
