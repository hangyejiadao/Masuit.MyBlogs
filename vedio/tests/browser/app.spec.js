import { test, expect } from "@playwright/test";

const movie = { id: 123, title: "Test Movie", media_type: "movie", release_date: "2020-01-01", vote_average: 8, overview: "A test movie.", genre_ids: [18], poster_path: null };
const tv = { id: 456, name: "Test Series", media_type: "tv", first_air_date: "2020-01-01", vote_average: 8, genre_ids: [18], poster_path: null };
const config = { enabled: true, configured: true, language: "zh-CN", playerSource: "videasy", theme: "slate", accentColor: "blue", historyEnabled: true };
async function mockApis(page) {
  await page.route("**/api/vedio/config", route => route.fulfill({ json: config }));
  await page.route("**/api/vedio/tmdb/**", async route => {
    const url = new URL(route.request().url());
    const pathname = url.pathname;
    let data = { results: [] };
    if (pathname.endsWith("/configuration")) data = { images: {} };
    else if (pathname.includes("/trending/movie")) data = { results: [movie] };
    else if (pathname.includes("/trending/tv")) data = { results: [tv] };
    else if (pathname.includes("/search/multi")) data = { results: url.searchParams.get("query").includes("Series") ? [tv] : [movie] };
    else if (pathname === "/api/vedio/tmdb/movie/123") data = { ...movie, genres: [{ name: "Drama" }], runtime: 120, videos: { results: [] }, recommendations: { results: [] }, credits: { cast: [] } };
    else if (pathname === "/api/vedio/tmdb/tv/456") data = { ...tv, genres: [], seasons: [{ season_number: 1, episode_count: 2, name: "Season 1" }, { season_number: 2, episode_count: 2, name: "Season 2" }], videos: { results: [] }, credits: { cast: [] } };
    else if (pathname.includes("/tv/456/season/")) data = { episodes: [{ episode_number: 1, name: "First Episode" }, { episode_number: 2, name: "Second Episode" }] };
    await route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(data) });
  });
  await page.route(/https:\/\/(player\.videasy\.to|vsembed\.su|www\.vidking\.net)\/.*/, route => route.fulfill({
    contentType: "text/html", body: "<!doctype html><html><body>Test Player</body></html>",
  }));
}
async function start(page) {
  await mockApis(page);
  await page.addInitScript(() => {
    localStorage.setItem("streambert_apikey", JSON.stringify("obsolete-token"));
    localStorage.setItem("streambert_theme", JSON.stringify("light"));
    localStorage.setItem("streambert_playerSource", JSON.stringify("vidking"));
  });
  await page.goto("/vedio/");
}
async function search(page, query) {
  await page.locator('.sidebar-btn').filter({ hasText: "Search" }).click();
  await page.locator(".search-input").fill(query);
  await page.locator(".search-result").first().click();
}
test("unconfigured service has no visitor token or settings controls", async ({ page }) => {
  await page.route("**/api/vedio/config", route => route.fulfill({ json: { ...config, configured: false } }));
  await page.goto("/vedio/");
  await expect(page.getByText("影视服务尚未配置，请联系管理员。", { exact: true })).toBeVisible();
  await expect(page.getByLabel("TMDB Read Access Token", { exact: true })).toHaveCount(0);
  await expect(page.locator('.sidebar-btn').filter({ hasText: "Settings" })).toHaveCount(0);
});
test("configuration failure can be retried", async ({ page }) => {
  await mockApis(page);
  let failed = true;
  await page.route("**/api/vedio/config", route => failed ? route.fulfill({ status: 503, json: {} }) : route.fulfill({ json: config }));
  await page.goto("/vedio/");
  await expect(page.getByText("无法获取影视配置，请稍后重试。", { exact: true })).toBeVisible();
  failed = false;
  await page.getByRole("button", { name: "重试", exact: true }).first().click();
  await search(page, "Movie");
  await expect(page.getByRole("heading", { name: "Test Movie", exact: true })).toBeVisible();
});
test("movie search, watchlist, playback progress and history persist after reload", async ({ page }) => {
  const errors = []; page.on("pageerror", error => errors.push(error.message));
  await start(page);
  await search(page, "Movie");
  await expect(page.getByRole("heading", { name: "Test Movie", exact: true })).toBeVisible();
  await page.getByRole("button", { name: "收藏", exact: true }).click();
  await page.getByLabel("播放源", { exact: true }).selectOption("vidking");
  await page.getByRole("button", { name: "播放", exact: true }).click();
  await expect(page.locator("iframe.web-player")).toHaveAttribute("src", /vidking\.net\/embed\/movie\/123/);
  const iframe = await page.locator("iframe.web-player").elementHandle();
  const player = await iframe.contentFrame();
  await expect(player.locator("body")).toHaveText("Test Player");
  await player.evaluate(() => parent.postMessage({ type: "PLAYER_EVENT", data: { id: 123, currentTime: 40, duration: 100 } }, "*"));
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem("streambert_progress"))?.movie_123)).toBe(40);
  await page.reload();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem("streambert_saved"))?.movie_123?.title)).toBe("Test Movie");
  await page.locator('.sidebar-btn').filter({ hasText: "Library & History" }).click();
  await expect(page.locator(".history-row")).toContainText("Test Movie");
  await expect(page.locator(".saved-thumb")).toHaveCount(1);
  expect(errors).toEqual([]);
  await page.screenshot({ path: `test-results/library-${test.info().project.name}.png`, fullPage: true });
});
test("TV season and episode selection updates player and stops the old episode", async ({ page }) => {
  const errors = []; page.on("pageerror", error => errors.push(error.message));
  await start(page); await search(page, "Series");
  await expect(page.getByRole("heading", { name: "Test Series", exact: true })).toBeVisible();
  await page.getByLabel("选择季", { exact: true }).selectOption("2");
  await page.getByRole("button", { name: "E2 Second Episode", exact: true }).click();
  await page.getByRole("button", { name: "播放", exact: true }).click();
  await expect(page.locator("iframe.web-player")).toHaveAttribute("src", /\/tv\/456\/2\/2/);
  await page.getByRole("button", { name: "E1 First Episode", exact: true }).click();
  await expect(page.locator("iframe.web-player")).toHaveCount(0);
  await page.getByRole("button", { name: "播放", exact: true }).click();
  await expect(page.locator("iframe.web-player")).toHaveAttribute("src", /\/tv\/456\/2\/1/);
  await page.getByRole("button", { name: "标记已看", exact: true }).click();
  await expect.poll(() => page.evaluate(() => JSON.parse(localStorage.getItem("streambert_watched"))?.tv_456_s2e1)).toBe(true);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > innerWidth);
  expect(overflow).toBe(false); expect(errors).toEqual([]);
  await page.screenshot({ path: `test-results/tv-${test.info().project.name}.png`, fullPage: true });
});
test("all player sources support the provider embed compatibility check", async ({ page }) => {
  await start(page);
  await page.route(/https:\/\/(player\.videasy\.to|vsembed\.su|www\.vidking\.net)\/.*/, route => route.fulfill({
    contentType: "text/html", body: `<!doctype html><html><body>
      <button id="check">Check player compatibility</button><p id="status">Waiting</p>
      <script>
        document.getElementById('check').onclick = () => {
          const popup = window.open('about:blank', '_blank');
          document.getElementById('status').textContent = popup ? 'Player ready' : 'Iframe Sandbox Detected';
          if (popup) popup.close();
        };
      </script></body></html>`,
  }));
  await search(page, "Movie");
  await page.getByRole("button", { name: "播放", exact: true }).click();
  for (const source of ["videasy", "vidsrc", "vidking"]) {
    await page.getByLabel("播放源", { exact: true }).selectOption(source);
    const provider = page.frameLocator("iframe.web-player");
    await provider.getByRole("button", { name: "Check player compatibility" }).click();
    await expect(provider.locator("#status")).toHaveText("Player ready");
  }
});
test("server settings override legacy preferences and personal backups remain available", async ({ page }) => {
  const requests = [];
  page.on("request", request => { if (request.url().includes("/api/vedio/tmdb/")) requests.push(request); });
  await start(page);
  await search(page, "Movie");
  await expect(page.getByLabel("播放源", { exact: true })).toHaveValue("videasy");
  expect(await page.evaluate(() => document.documentElement.style.getPropertyValue("--red"))).toBe("#2563eb");
  expect(await page.evaluate(() => document.documentElement.style.getPropertyValue("--bg"))).not.toBe("#f5f5f5");
  expect(requests.length).toBeGreaterThan(0);
  for (const request of requests) {
    expect(request.headers().authorization).toBeUndefined();
    expect(new URL(request.url()).searchParams.get("language")).toBe("zh-CN");
  }
  await expect(page.locator('.sidebar-btn').filter({ hasText: "Settings" })).toHaveCount(0);
  await page.locator('.sidebar-btn').filter({ hasText: "Library & History" }).click();
  const pending = page.waitForEvent("download");
  await page.getByRole("button", { name: "导出备份" }).click();
  expect((await pending).suggestedFilename()).toBe("streambert-backup.json");
});
test("disabled history is controlled by server configuration", async ({ page }) => {
  await start(page);
  await page.route("**/api/vedio/config", route => route.fulfill({ json: { ...config, historyEnabled: false } }));
  await page.reload();
  await search(page, "Movie");
  await page.getByRole("button", { name: "播放", exact: true }).click();
  expect(await page.evaluate(() => JSON.parse(localStorage.getItem("streambert_history")))).toEqual([]);
});
