// Turns the markdown that `docfx metadata` writes to api/obj/md into Starlight pages under
// src/content/docs/api, plus src/content/docs/api/_sidebar.json for astro.config.mjs. Starlight
// skips files starting with an underscore, so the sidebar sits next to the pages it lists and
// the two are always regenerated together. Plain Node, no dependencies.
//
// DocFX's markdown output needs four fixes for Starlight:
//   1. Starlight wants the title in frontmatter, not as a `# Heading`.
//   2. File names such as `Vitreous.Result-1.md` would lose their dots to Astro's slugger, so
//      each page moves to a nested lowercase path: `vitreous/result-1.md`.
//   3. Links between pages point at those `.md` files and have to become site URLs.
//   4. `<see cref>` in XML docs comes out as unresolved `<xref href="uid">` tags.

import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const docsRoot = join(dirname(fileURLToPath(import.meta.url)), '..');
const inputDir = join(docsRoot, 'api/obj/md');
const outputDir = join(docsRoot, 'src/content/docs/api');
const baseUrl = '/Vitreous/api/';

// Learn has no pages for enum fields: they are anchors on the enum's page.
const externalEnums = new Set(['Microsoft.Extensions.DependencyInjection.ServiceLifetime']);

if (!existsSync(inputDir)) {
	throw new Error(`${inputDir} not found. Run \`dotnet docfx metadata api/docfx.json\` first.`);
}

const unescapeMarkdown = (text) => text.replace(/\\([\\`*_{}[\]()#+\-.!<>|~])/g, '$1');
const escapeHtml = (text) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
// `Vitreous.Result-1` → `vitreous/result-1`
const slugOf = (name) =>
	name
		.split('.')
		.map((part) => part.toLowerCase().replace(/[^a-z0-9-]/g, '-'))
		.join('/');
const urlOf = (slug, anchor) => `${baseUrl}${slug}/${anchor ? `#${anchor}` : ''}`;
// DocFX's anchor ids are the uid with every non-alphanumeric character replaced by `_`.
const anchorOf = (uid) => uid.replace(/[^A-Za-z0-9]/g, '_');
// `Bind<TOut\>\(Func<T, Result<TOut\>\>\)` → `Bind<TOut>`
const memberName = (heading) => unescapeMarkdown(heading).replace(/\(.*$/, '').trim();

// ── Pass 1: read every page, collect titles and anchors ─────────────────────────────────────

const pages = [];
const anchors = new Map(); // anchor id → { page, name, isPage }
const slugs = new Map();

for (const file of readdirSync(inputDir).filter((f) => f.endsWith('.md')).sort()) {
	const name = file.slice(0, -'.md'.length);
	const source = readFileSync(join(inputDir, file), 'utf8');
	const heading = source.match(/^# <a id="([^"]+)"><\/a> (\w+) (.+)\n/);
	if (!heading) throw new Error(`${file}: no "# <a id></a> Kind Name" heading on the first line`);

	const [line, id, kind, title] = heading;
	const slug = slugOf(name);
	if (slugs.has(slug)) throw new Error(`${file} and ${slugs.get(slug)} both map to ${slug}`);
	slugs.set(slug, file);

	const page = {
		name,
		slug,
		kind,
		displayName: unescapeMarkdown(title),
		assembly: source.match(/^Assembly: (\S+)\.dll/m)?.[1],
		body: source.slice(line.length),
	};

	// Enum fields have no anchors of their own (`Parallel = 1`), so add them for cref links.
	if (kind === 'Enum') {
		page.body = page.body.replace(/^`(\w+) = [^`]+` $/gm, (field, fieldName) => {
			const fieldId = `${id}_${fieldName}`;
			anchors.set(fieldId, { page, name: fieldName });
			return `<a id="${fieldId}"></a>${field}`;
		});
	}

	anchors.set(id, { page, name: page.displayName, isPage: true });
	for (const [, memberId, text] of page.body.matchAll(/^#{2,6} <a id="([^"]+)"><\/a> (.+)$/gm)) {
		anchors.set(memberId, { page, name: memberName(text) });
	}
	pages.push(page);
}

const namespaces = pages.filter((p) => p.kind === 'Namespace');
const types = pages.filter((p) => p.kind !== 'Namespace');
const namespaceOf = (page) =>
	namespaces
		.filter((ns) => page.name.startsWith(`${ns.name}.`))
		.reduce((longest, ns) => (ns.name.length > longest.name.length ? ns : longest));

// ── Pass 2: rewrite links and crefs, write the pages ───────────────────────────────────────

const unresolved = new Set();

function resolveCref(uid, page) {
	const id = anchorOf(uid);
	const target = anchors.get(id);
	if (target) {
		const anchor = target.isPage ? '' : id;
		return target.page === page && target.isPage
			? escapeHtml(target.name)
			: `[${escapeHtml(target.name)}](${urlOf(target.page.slug, anchor)})`;
	}

	// `Task.WhenAll(IEnumerable{Task})` → `Task.WhenAll`, `IAsyncEnumerable`1` → `IAsyncEnumerable<T>`
	const path = uid.replace(/\(.*$/, '');
	const last = path.slice(path.lastIndexOf('.') + 1);
	const arity = Number(last.match(/`(\d+)$/)?.[1] ?? 0);
	const typeParams = arity === 1 ? ['T'] : Array.from({ length: arity }, (_, i) => `T${i + 1}`);
	const text = escapeHtml(last.replace(/`\d+$/, '') + (arity ? `<${typeParams.join(', ')}>` : ''));

	if (/^(System|Microsoft)\./.test(uid)) {
		const parent = path.slice(0, path.lastIndexOf('.'));
		const learnPath = (p) => p.toLowerCase().replace(/`/g, '-');
		const href = externalEnums.has(parent)
			? `https://learn.microsoft.com/dotnet/api/${learnPath(parent)}#${learnPath(path).replace(/\./g, '-')}`
			: `https://learn.microsoft.com/dotnet/api/${learnPath(path)}`;
		return `[${text}](${href})`;
	}

	// Third-party types, and Vitreous types the docs leave out (internal, or hidden from
	// IntelliSense with [EditorBrowsable(Never)]).
	if (uid.startsWith('Vitreous.')) unresolved.add(uid);
	return `<code>${text}</code>`;
}

function rewrite(page) {
	return (
		page.body
			// <xref href="Vitreous.Result%601.Error" data-throw-if-not-resolved="false"></xref>
			.replace(/<xref href="([^"]+)"[^>]*><\/xref>/g, (_, uid) =>
				resolveCref(decodeURIComponent(uid).replace(/\?.*$/, ''), page),
			)
			// [Error](Vitreous.Error.md) and [CollectStream](Vitreous.StreamQueryExtensions.md\#Vitreous\_…)
			.replace(/\]\(([A-Za-z0-9.\\-]+)\.md(?:\\?#([A-Za-z0-9_\\]+))?\)/g, (link, file, anchor) => {
				const slug = slugOf(unescapeMarkdown(file));
				if (!slugs.has(slug)) throw new Error(`${page.name}.md: link to a missing page: ${link}`);
				return `](${urlOf(slug, anchor && unescapeMarkdown(anchor))})`;
			})
	);
}

function frontmatter(title, description) {
	// Generated pages have no source file to edit and no git history.
	const lines = ['---', `title: ${JSON.stringify(title)}`];
	if (description) lines.push(`description: ${JSON.stringify(description)}`);
	lines.push('editUrl: false', 'lastUpdated: false', '---', '');
	return lines.join('\n');
}

rmSync(outputDir, { recursive: true, force: true });

for (const page of pages) {
	const title = `${page.displayName} ${page.kind.toLowerCase()}`;
	const file = join(outputDir, `${page.slug}.md`);
	mkdirSync(dirname(file), { recursive: true });
	writeFileSync(file, frontmatter(title) + rewrite(page).replace(/^\n+/, '\n'));
}

// ── The index page and the sidebar ─────────────────────────────────────────────────────────

const byName = (a, b) => a.displayName.localeCompare(b.displayName, 'en');
const packages = new Map(); // package → Set of namespaces with types from it
for (const type of types) {
	const ns = namespaceOf(type);
	if (!packages.has(type.assembly)) packages.set(type.assembly, new Set());
	packages.get(type.assembly).add(ns);
}

const index = [
	frontmatter('API reference', 'Every public type in the Vitreous packages, generated from the XML docs.'),
	'Generated from the XML documentation of the public API with DocFX, for `net11.0`. The public API is the same on every target framework.',
	'',
	'| Package | Namespaces |',
	'|---|---|',
	...[...packages]
		.sort(([a], [b]) => a.localeCompare(b, 'en'))
		.map(
			([pkg, nss]) =>
				`| [\`${pkg}\`](https://www.nuget.org/packages/${pkg}) | ${[...nss]
					.sort(byName)
					.map((ns) => `[${ns.displayName}](${urlOf(ns.slug)})`)
					.join('<br />')} |`,
		),
	'',
].join('\n');
writeFileSync(join(outputDir, 'index.md'), index);

const sidebar = [
	{ label: 'Overview', slug: 'api' },
	...namespaces.sort(byName).map((ns) => ({
		label: ns.displayName,
		collapsed: true,
		items: [
			{ label: 'Overview', slug: `api/${ns.slug}` },
			...types
				.filter((type) => namespaceOf(type) === ns)
				.sort(byName)
				.map((type) => ({ label: type.displayName, slug: `api/${type.slug}` })),
		],
	})),
];
writeFileSync(join(outputDir, '_sidebar.json'), `${JSON.stringify(sidebar, null, '\t')}\n`);

console.log(`api-reference: ${pages.length} pages written to ${outputDir}`);
if (unresolved.size) {
	console.log(`api-reference: crefs to undocumented Vitreous APIs, shown as code: ${[...unresolved].sort().join(', ')}`);
}
