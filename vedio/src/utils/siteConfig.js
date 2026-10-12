// Site settings are loaded from the backend and never persisted as visitor preferences.
const defaults = Object.freeze({ enabled: true, configured: false, language: "en-US", playerSource: "videasy", theme: "dark", accentColor: "red", historyEnabled: true });
let config = { ...defaults };
const managedKeys = { tmdbLang: "language", playerSource: "playerSource", theme: "theme", accentColor: "accentColor", historyEnabled: "historyEnabled" };

export const isManagedSetting = key => Object.hasOwn(managedKeys, key);
export const getManagedSetting = key => config[managedKeys[key]];
export const metadataAvailable = () => config.enabled && config.configured;
export async function loadSiteConfig() {
  const response = await fetch("/api/vedio/config", { cache: "no-store", signal: AbortSignal.timeout(15000) });
  if (!response.ok) throw new Error("Configuration unavailable");
  const data = await response.json();
  if (typeof data.enabled !== "boolean" || typeof data.configured !== "boolean") throw new Error("Invalid configuration");
  config = { ...defaults, enabled: data.enabled, configured: data.configured,
    language: ["zh-CN", "zh-TW", "en-US", "ja-JP", "de-DE"].includes(data.language) ? data.language : defaults.language,
    playerSource: ["videasy", "vidsrc", "vidking"].includes(data.playerSource) ? data.playerSource : defaults.playerSource,
    theme: ["dark", "amoled", "mocha", "slate", "light"].includes(data.theme) ? data.theme : defaults.theme,
    accentColor: ["red", "blue", "purple", "green", "orange", "pink"].includes(data.accentColor) ? data.accentColor : defaults.accentColor,
    historyEnabled: data.historyEnabled !== false };
  return { ...config };
}
