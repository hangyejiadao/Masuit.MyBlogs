import test from "node:test";
import assert from "node:assert/strict";
import { parsePlayerProgress, progressKey, normalizeMedia } from "../src/utils/browserPlayer.js";

const frame = {};
const url = "https://www.vidking.net/embed/movie/123";
const movie = { id: 123, type: "movie" };
const event = overrides => ({ source: frame, origin: new URL(url).origin,
  data: { type: "PLAYER_EVENT", data: { id: 123, currentTime: 50, duration: 100 } }, ...overrides });
test("accepts matching provider progress as object or JSON", () => {
  assert.equal(parsePlayerProgress(event(), frame, url, movie), 50);
  assert.equal(parsePlayerProgress(event({ data: JSON.stringify(event().data) }), frame, url, movie), 50);
});
test("rejects wrong origins, windows, titles, malformed messages and invalid timing", () => {
  for (const overrides of [{ origin: "https://attacker.example" }, { source: {} }, { data: "invalid JSON" }, { data: null },
    { data: { type: "PLAYER_EVENT", data: { id: 999, currentTime: 50, duration: 100 } } },
    { data: { type: "PLAYER_EVENT", data: { id: 123, currentTime: -1, duration: 100 } } },
    { data: { type: "PLAYER_EVENT", data: { id: 123, currentTime: 50, duration: 0 } } }]) {
    assert.equal(parsePlayerProgress(event(overrides), frame, url, movie), null);
  }
});
test("requires matching season and episode for TV", () => {
  const data = { type: "PLAYER_EVENT", data: { id: 123, season: 2, episode: 3, currentTime: 50, duration: 100 } };
  assert.equal(parsePlayerProgress(event({ data }), frame, url, { id: 123, type: "tv", season: 2, episode: 3 }), 50);
  assert.equal(parsePlayerProgress(event({ data }), frame, url, { id: 123, type: "tv", season: 2, episode: 4 }), null);
});
test("normalizes TMDB results and separates episode history", () => {
  assert.equal(normalizeMedia({ id: 1, name: "Show", first_air_date: "2020-01-01" }).media_type, "tv");
  assert.equal(progressKey({ id: 123, media_type: "tv", season: 2, episode: 3 }), "tv_123_s2e3");
  assert.equal(progressKey({ id: 123, media_type: "movie" }), "movie_123");
});
