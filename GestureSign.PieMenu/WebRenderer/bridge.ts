import i18next from 'i18next';
import { GENERAL_SETTINGS_SCHEMA } from '__UPSTREAM__/common/settings-schemata';

// Electron's preload API is replaced by a fixed-origin WebView2 message bridge.
// The upstream renderer retains its own geometry, selection logic and CSS.
const host = (window as any).chrome.webview;
const config = (window as any).__gestureSignConfig ?? {};
const catalog = fetch('catalog.json').then(r => {
  if (!r.ok) throw new Error('Kando theme catalog unavailable');
  return r.json();
});
const show = new Set<Function>(), trigger = new Set<Function>(), reset = new Set<Function>();
let sessionId = 0;
const send = (type: string, data: object = {}) => host.postMessage({ type, sessionId, ...data });
const noop = () => {};
const buttonBehavior = (value: unknown, fallback: string) =>
  typeof value === 'string' && ['nothing', 'close', 'select-parent', 'select'].includes(value)
    ? value
    : fallback;
const general = GENERAL_SETTINGS_SCHEMA.parse({
  ...config.advanced,
  enableMenuAnimations: config.animations ?? true,
  enableMarkingMode: config.marking ?? true,
  enableTurboMode: config.turbo ?? true,
  hoverModeNeedsConfirmation: config.confirmHover ?? false,
  triggerCenterClickOnKeyRelease: config.closeOnCenterRelease ?? false,
  warpMouse: config.warpPointer ?? true,
  rightMouseButtonBehavior: buttonBehavior(config.rightButton, 'close'),
  middleMouseButtonBehavior: buttonBehavior(config.middleButton, 'select')
});
const theme = async () => {
  const data = await catalog;
  return data.themes[config.theme] ?? data.themes.default;
};
Object.assign(window, {
  commonAPI: {
    getMenuTheme: theme,
    getCurrentMenuThemeColors: async () => {
      const data = await catalog;
      const presets = data.presets[config.theme] ?? data.presets.default;
      return Object.hasOwn(presets, config.palette) ? presets[config.palette] : {};
    },
    getSoundTheme: async () => general.soundTheme === 'none' ? { sounds: {} } : (await catalog).sound,
    getIconThemes: async () => ({
      userIconDirectory: '',
      fileIconThemes: [{ name: 'kando', directory: 'icons', icons: (await catalog).icons }]
    }),
    getSystemIcons: async () => new Map(),
    generalSettings: { get: async () => general, onChange: noop },
    darkModeChanged: noop,
    log: (message: string) => send('log', { message })
  },
  menuAPI: {
    onShowMenu: (fn: Function) => show.add(fn),
    onResetMenu: (fn: Function) => reset.add(fn),
    onTriggerInteraction: (fn: Function) => trigger.add(fn),
    onReloadMenuTheme: noop, onReloadSoundTheme: noop, onReloadIconThemes: noop,
    menuWindowReady: () => send('ready'),
    movePointer: (dist: object) => send('movePointer', { dist }),
    finalizeInteraction: (interaction: string, path: number[]) => send('interaction', { interaction, path }),
    showSettings: () => send('settings')
  }
});
host.addEventListener('message', ({ data }: MessageEvent) => {
  if (data.type === 'show') {
    sessionId = data.sessionId;
    for (const fn of show) fn(data.root, data.options);
    requestAnimationFrame(() => requestAnimationFrame(() => {
      if (sessionId === data.sessionId) send('presented', { pixelRatio: window.devicePixelRatio });
    }));
    return;
  }
  if (data.sessionId !== sessionId) return;
  if (data.type === 'trigger') for (const fn of trigger) fn(data.interaction);
  if (data.type === 'reset') {
    sessionId = 0;
    for (const fn of reset) fn();
  }
});
window.addEventListener('error', e => send('error', { message: e.message }));
window.addEventListener('unhandledrejection', e => send('error', { message: String(e.reason) }));
async function start() {
  const language = config.language ?? 'en';
  const resources = Object.fromEntries(await Promise.all(
    [...new Set(['en', language])].map(async tag => {
      const response = await fetch(`locales/${encodeURIComponent(tag)}/translation.json`);
      if (!response.ok) throw new Error(`Kando locale unavailable: ${tag}`);
      return [tag, { translation: await response.json() }];
    })
  ));
  document.documentElement.lang = language;
  await i18next.init({ lng: language, fallbackLng: 'en', resources });
  await import('__UPSTREAM__/menu-renderer/index');
}
void start();
