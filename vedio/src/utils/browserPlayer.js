// Only accept progress events from the actual embedded player and matching title.
// Cross-origin video DOM access and Electron script injection are unavailable.
export function parsePlayerProgress(event, frameWindow, playerUrl, expected) {
  if (!frameWindow || event.source !== frameWindow) return null;
  if (event.origin !== new URL(playerUrl).origin) return null;
  let payload = event.data;
  if (typeof payload === "string") {
    try { payload = JSON.parse(payload); } catch { return null; }
  }
  if (!payload || typeof payload !== "object") return null;
  if (payload.type !== "PLAYER_EVENT" && payload.type !== "MEDIA_DATA") return null;
  const data = payload.data;
  if (!data || typeof data !== "object") return null;
  if (String(data.id ?? data.tmdbId ?? "") !== String(expected.id)) return null;
  if (expected.type === "tv" && (
    Number(data.season) !== Number(expected.season) ||
    Number(data.episode) !== Number(expected.episode)
  )) return null;
  const currentTime = Number(data.currentTime ?? data.current_time);
  const duration = Number(data.duration);
  if (!Number.isFinite(currentTime) || !Number.isFinite(duration) || duration <= 0 || currentTime < 0) return null;
  return Math.min(100, Math.max(0, (currentTime / duration) * 100));
}

export const progressKey = (item) => item.media_type === "tv"
  ? `tv_${item.id}_s${item.season}e${item.episode}`
  : `movie_${item.id}`;

export function normalizeMedia(item) {
  return { ...item, title: item.title || item.name || "", media_type: item.media_type || (item.first_air_date ? "tv" : "movie") };
}
