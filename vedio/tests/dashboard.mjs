// Run after npm run build --prefix front. Exercises the built Dashboard with mocked admin APIs.
import { chromium, expect } from "@playwright/test";
import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { resolve, extname } from "node:path";

const root = resolve("../src/Masuit.MyBlogs.Core/wwwroot/dashboard");
const server = createServer(async (req, res) => {
  try {
    const path = new URL(req.url, "http://localhost").pathname.replace(/^\/dashboard\/?/, "");
    const file = path.includes(".") ? resolve(root, path) : resolve(root, "index.html");
    if (!file.startsWith(root)) { res.writeHead(400).end(); return; }
    res.setHeader("Content-Type", ({ ".js": "application/javascript", ".css": "text/css", ".html": "text/html" })[extname(file)] || "application/octet-stream");
    res.end(await readFile(file));
  } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const browser = await chromium.launch();
try {
  const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
  const errors = [];
  page.on("pageerror", error => errors.push(error.message));
  await page.route("**/UEditorPlus/**", route => route.fulfill({ body: "", contentType: "application/javascript" }));
  await page.route("**/pagead/**", route => route.abort());
  await page.route("**/passport/getuserinfo", route => route.fulfill({ json: { IsLogin: true, Success: true, Data: { NickName: "Test Admin", IsAdmin: true, Roles: ["admin"] } } }));
  const config = { Enabled: true, HasToken: true, Language: "zh-CN", PlayerSource: "videasy", Theme: "slate", AccentColor: "blue", HistoryEnabled: true };
  const saves = [];
  let loadFails = false;
  await page.route("**/system/getvediosettings", route => route.fulfill({ json: loadFails ? { Success: false } : { Success: true, Data: config } }));
  await page.route("**/system/savevediosettings", route => { saves.push(route.request().postDataJSON()); return route.fulfill({ json: { Success: true } }); });
  await page.goto(`http://127.0.0.1:${server.address().port}/dashboard/system/vedio`);
  const save = page.getByRole("button", { name: "保存配置", exact: true });
  await expect(save).toBeEnabled({ timeout: 60000 });
  const token = page.locator('.vedio-settings input[type="password"]');
  await expect(token).toHaveValue("");
  await expect(page.getByText("Token 已配置", { exact: true })).toBeVisible();
  await page.locator('.q-select').filter({ hasText: "影视资料语言" }).click();
  await page.getByRole("option").filter({ hasText: "Deutsch" }).click();
  await save.click();
  await expect.poll(() => saves.length).toBe(1);
  expect(saves[0].Language).toBe("de-DE");
  expect(saves[0].TmdbToken).toBe("");
  expect(saves[0].ClearToken).toBe(false);
  expect(saves[0].HasToken).toBeUndefined();
  await token.fill("test-server-token");
  await save.click();
  await expect.poll(() => saves.length).toBe(2);
  expect(saves[1].TmdbToken).toBe("test-server-token");
  await expect(token).toHaveValue("");
  await page.getByRole("checkbox", { name: "清除已保存的 Token" }).click();
  await save.click();
  await expect.poll(() => saves.length).toBe(3);
  expect(saves[2].ClearToken).toBe(true);
  await expect(page.getByText("Token 未配置", { exact: true })).toBeVisible();
  await page.screenshot({ path: "test-results/dashboard-vedio.png", fullPage: true });
  loadFails = true;
  await page.getByRole("button", { name: "重新加载" }).click();
  await expect(save).toBeDisabled();
  await expect(page.getByText("加载影视配置失败，请检查登录状态后重试。", { exact: true })).toBeVisible();
  expect(errors).toEqual([]);
  console.log("Dashboard settings: load, blank-token preservation, update, clear, and load failure passed.");
} finally {
  await browser.close();
  await new Promise(resolve => server.close(resolve));
}
