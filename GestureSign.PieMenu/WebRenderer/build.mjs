import { build } from 'esbuild';
import { compileString } from 'sass';
import JSON5 from 'json5';
import { cp, mkdir, readFile, readdir, writeFile } from 'node:fs/promises';
import { basename, dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const root = dirname(fileURLToPath(import.meta.url));
const input = resolve(process.argv[2] ?? '');
const output = resolve(process.argv[3] ?? join(root, '../KandoWeb'));
const revision = '6df468a66695ac504afebfff2c581a42296511b4';
const actual = execFileSync('git', ['-C', input, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
if (actual !== revision) throw new Error('Kando source revision mismatch: ' + actual);
const changed = execFileSync('git', ['-C', input, 'status', '--porcelain', '--untracked-files=no'], { encoding: 'utf8' });
if (changed.trim()) throw new Error('Kando reference checkout has local changes.');

const src = join(input, 'src');
const assets = join(input, 'assets');
const modules = join(root, 'node_modules');
await mkdir(output, { recursive: true });
await cp(join(input, 'locales'), join(output, 'locales'), { recursive: true });
const keySource = await readFile(join(src, 'common/key-codes.ts'), 'utf8');
const properKeyNames = new Map([...keySource.matchAll(/\['(\w+)', '(\w+)'\]/g)].map(([, key, name]) => [key, name]));
const windowsScanCodes = Object.fromEntries([...keySource.matchAll(/\[\s*'(\w+)',\s*\{\s*windows:\s*(0x[0-9a-f]+),/gi)]
  .map(([, key, scan]) => [properKeyNames.get(key), Number(scan)]));
if (Object.keys(windowsScanCodes).length < 100 || 'undefined' in windowsScanCodes) throw new Error('Could not read the Kando Windows key table.');
await writeFile(join(output, 'windows-key-codes.json'), JSON.stringify(windowsScanCodes));

const scss = (await readFile(join(src, 'menu-renderer/index.scss'), 'utf8'))
  .replace(/^\$material-symbols-font-path:.*$/m, '')
  .replace(/^@import 'material-symbols\/rounded';$/m, '')
  .replace(/^@import '\.\.\/\.\.\/node_modules\/simple-icons-font\/font\/simple-icons.css';$/m, '');
const material = join(modules, 'material-symbols');
const simple = join(modules, 'simple-icons-font/font');
await cp(join(material, 'material-symbols-rounded.woff2'), join(output, 'material-symbols-rounded.woff2'));
await cp(join(simple, 'SimpleIcons.woff2'), join(output, 'SimpleIcons.woff2'));
const materialCss = await readFile(join(material, 'rounded.css'), 'utf8');
const simpleCss = (await readFile(join(simple, 'simple-icons.css'), 'utf8'))
  .replace(/src\s*:[^;]+;/g, 'src:url("./SimpleIcons.woff2") format("woff2");');
await writeFile(join(output, 'renderer.css'), materialCss + '\n' + simpleCss + '\n' + compileString(scss).css);
const html = (await readFile(join(src, 'menu-renderer/index.html'), 'utf8')).replace('</head>', `
    <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self'; media-src 'self' blob:">
    <link rel="stylesheet" href="renderer.css">
    <script defer src="renderer.js"></script>
  </head>`);
await writeFile(join(output, 'index.html'), html);
for (const [source, target] of [['menu-themes', 'themes'], ['sound-themes/simple-clicks', 'sounds/simple-clicks'], ['icon-themes/kando', 'icons']]) {
  await cp(join(assets, source), join(output, target), { recursive: true });
}
execFileSync(process.env.PYTHON ?? 'python', [join(root, 'export-native-icons.py'), modules, output], { stdio: 'inherit' });
const themes = {};
const presets = {};
for (const dir of await readdir(join(output, 'themes'), { withFileTypes: true })) {
  if (!dir.isDirectory()) continue;
  const description = JSON5.parse(await readFile(join(output, 'themes', dir.name, 'theme.json5'), 'utf8'));
  themes[dir.name] = { drawCenterText: true, drawWedgeSeparators: false, ...description, id: dir.name, directory: 'themes' };
  const folder = join(output, 'themes', dir.name, 'presets');
  const files = await readdir(folder).catch(error => { if (error.code !== 'ENOENT') throw error; return []; });
  presets[dir.name] = Object.fromEntries(await Promise.all(files.filter(file => file.endsWith('.json')).map(async file =>
    [file.slice(0, -5), JSON.parse(await readFile(join(folder, file), 'utf8')).colors])));
}
const sound = JSON5.parse(await readFile(join(output, 'sounds/simple-clicks/theme.json5'), 'utf8'));
await writeFile(join(output, 'catalog.json'), JSON.stringify({
  themes, presets, sound: { ...sound, id: 'simple-clicks', directory: 'sounds' },
  icons: (await readdir(join(output, 'icons'))).filter(name => name.endsWith('.svg'))
}));
const bridge = (await readFile(join(root, 'bridge.ts'), 'utf8')).replaceAll('__UPSTREAM__', src.replaceAll('\\', '/'));
await build({
  stdin: { contents: bridge, resolveDir: root, loader: 'ts', sourcefile: 'bridge.ts' },
  outfile: join(output, 'renderer.js'), bundle: true, format: 'iife', platform: 'browser',
  target: 'chrome131', minify: true, legalComments: 'eof', nodePaths: [modules],
  define: { cIsWindows: 'true', cIsMac: 'false', cAPIVersion: '1' },
  plugins: [{
    name: 'webview-local-resources',
    setup(bundler) {
      bundler.onResolve({ filter: /index\.scss$/ }, () => ({ path: 'renderer.css', namespace: 'css-already-built' }));
      bundler.onLoad({ filter: /.*/, namespace: 'css-already-built' }, () => ({ contents: '' }));
      bundler.onResolve({ filter: /^!!raw-loader!material-symbols\/index\.d\.ts$/ }, () => ({ path: 'material-symbols', namespace: 'raw-symbols' }));
      bundler.onLoad({ filter: /.*/, namespace: 'raw-symbols' }, async () => ({
        contents: 'export default ' + JSON.stringify(await readFile(join(material, 'index.d.ts'), 'utf8'))
      }));
      bundler.onLoad({ filter: /menu-renderer[\\/]index\.ts$/ }, async ({ path }) => {
        const original = await readFile(path, 'utf8');
        const hook = '  window.menuAPI.onShowMenu(';
        if (!original.includes(hook)) throw new Error('Missing menu reset integration point: ' + path);
        const resetHook = `  window.menuAPI.onResetMenu(() => {
    menu.clear();
    settingsButton.hide();
  });

`;
        return { contents: original.replace(hook, resetHook + hook), loader: 'ts', resolveDir: dirname(path) };
      });
      bundler.onLoad({ filter: /(menu-theme|sound-theme|file-icon-theme)\.ts$/ }, async ({ path }) => {
        const original = await readFile(path, 'utf8');
        if (!original.includes('file://')) throw new Error('Missing asset path adaptation: ' + path);
        return {
          contents: original.replaceAll('file://', './'), loader: 'ts',
          resolveDir: dirname(path)
        };
      });
    }
  }]
});
await cp(join(input, 'LICENSES'), join(output, 'Kando-Licenses'), { recursive: true });
await cp(join(input, 'LICENSE.md'), join(output, 'Kando-LICENSE.md'));
const notices = join(output, 'Dependency-Notices');
await mkdir(notices, { recursive: true });
for (const name of ['events', 'emojilib', 'howler', 'i18next', 'match-sorter', 'material-symbols', 'remove-accents', '@babel/runtime', 'simple-icons-font', 'zod']) {
  let folder = join(modules, name);
  if (name === 'remove-accents' || name === '@babel/runtime') folder = join(modules, 'match-sorter', '..', name);
  try {
    for (const file of await readdir(folder)) {
      if (/^(licen[sc]e|notice|ofl)/i.test(file)) await cp(join(folder, file), join(notices, name.replaceAll('/', '-') + '-' + basename(file)));
    }
  } catch (error) { if (error.code !== 'ENOENT') throw error; }
}
await writeFile(join(output, 'NOTICE.txt'),
  'Kando renderer and artworks from https://github.com/kando-menu/kando/tree/' + revision +
  '\nUpstream TypeScript, CSS, assets and MIT-licensed localizations are retained. Electron preload calls are adapted to WebView2.\nNative icon paths derived from the same pinned Material Symbols and Simple Icons fonts.\nSee Kando-Licenses, EditorAssets notices, per-theme SPDX files and Dependency-Notices.\n');
console.log('Kando renderer built from ' + revision);
