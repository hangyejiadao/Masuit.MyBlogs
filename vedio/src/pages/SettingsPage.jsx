import { useState } from "react";
import { storage, clearAppCaches } from "../utils/storage";
import { PLAYER_SOURCES, clearTmdbCache } from "../utils/api";
import { ACCENT_PRESETS, THEME_PRESETS, applyAccentColor, applyTheme } from "../utils/appearance";
import { collectBackupData, restoreBackupData } from "../utils/backup";

export default function SettingsPage({ apiKey, onChangeApiKey }) {
  const [language, setLanguage] = useState(storage.get("tmdbLang") || "en-US");
  const [source, setSource] = useState(storage.get("playerSource") || PLAYER_SOURCES[0].id);
  const [accent, setAccent] = useState(storage.get("accentColor") || "red");
  const [theme, setTheme] = useState(storage.get("theme") || "dark");
  const [history, setHistory] = useState(storage.get("historyEnabled") !== false);
  const [message, setMessage] = useState("");
  const exportBackup = () => {
    const url = URL.createObjectURL(new Blob([JSON.stringify({ version: 1, data: collectBackupData() }, null, 2)], { type: "application/json" }));
    const link = document.createElement("a"); link.href = url; link.download = "streambert-backup.json"; link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  };
  const importBackup = async event => {
    const file = event.target.files?.[0]; if (!file) return;
    try {
      if (file.size > 5 * 1024 * 1024) throw new Error("备份文件过大");
      const backup = JSON.parse(await file.text());
      if (backup.version !== 1 || !backup.data || typeof backup.data !== "object") throw new Error("备份格式无效");
      restoreBackupData(backup.data); window.location.reload();
    } catch { setMessage("导入失败，请选择 Streambert 导出的有效备份文件。"); }
    event.target.value = "";
  };
  return <div className="web-settings fade-in">
    <h1>设置</h1>
    <section><h2>TMDB Token</h2><p className="web-muted">{apiKey ? "已配置" : "未配置"}。Token 保存在此浏览器的本地存储，不包含在发布文件或备份中。浏览器存储不提供操作系统密钥链加密，请在信任的设备上使用。</p><button className="btn btn-primary" onClick={onChangeApiKey}>更换 Token</button></section>
    <section><h2>影视与播放</h2>
      <label className="web-label">影视资料语言<select value={language} onChange={event => {
        const value = event.target.value; setLanguage(value); storage.set("tmdbLang", value); clearTmdbCache(); window.dispatchEvent(new Event("streambert:tmdb-lang-changed"));
      }}><option value="zh-CN">简体中文</option><option value="zh-TW">繁體中文</option><option value="en-US">English</option><option value="ja-JP">日本語</option><option value="de-DE">Deutsch</option></select></label>
      <label className="web-label">默认播放源<select value={source} onChange={event => { setSource(event.target.value); storage.set("playerSource", event.target.value); }}>
        {PLAYER_SOURCES.map(value => <option key={value.id} value={value.id}>{value.label}</option>)}
      </select></label>
      <label className="web-label"><input type="checkbox" checked={history} onChange={event => { setHistory(event.target.checked); storage.set("historyEnabled", event.target.checked); }} /> 保存观看历史</label>
    </section>
    <section><h2>外观</h2>
      <label className="web-label">主题<select aria-label="主题" value={theme} onChange={event => { setTheme(event.target.value); storage.set("theme", event.target.value); applyTheme(event.target.value); }}>
        {THEME_PRESETS.filter(value => value.id !== "custom").map(value => <option key={value.id} value={value.id}>{value.label}</option>)}
      </select></label>
      <label className="web-label">强调色<select value={accent} onChange={event => { setAccent(event.target.value); storage.set("accentColor", event.target.value); applyAccentColor(event.target.value); }}>
        {ACCENT_PRESETS.map(value => <option key={value.id} value={value.id}>{value.label}</option>)}
      </select></label>
    </section>
    <section><h2>数据</h2><p className="web-muted">收藏、观看历史和设置只保存在当前浏览器，可导出备份后在另一台设备导入。</p>
      <div className="web-actions"><button className="btn btn-ghost" onClick={exportBackup}>导出备份</button>
        <label className="btn btn-ghost">导入备份<input aria-label="导入备份" type="file" accept="application/json,.json" onChange={importBackup} hidden /></label>
        <button className="btn btn-ghost" onClick={async () => { await clearAppCaches(); clearTmdbCache(); setMessage("缓存已清除"); }}>清除缓存</button>
      </div><p role="status">{message}</p>
    </section>
    <section><h2>关于</h2><p className="web-muted">Streambert 2.6.0 浏览器移植版。影视资料来自 TMDB，播放由第三方提供。网页版支持搜索、收藏、历史和浏览器播放，不提供桌面下载器、AllManga 抓取、广告拦截或跨站脚本控制。</p>
      <div className="web-actions"><a href="https://github.com/truelockmc/streambert" target="_blank" rel="noopener noreferrer">上游项目</a>
        <a href={import.meta.env.BASE_URL + "LICENSE.txt"}>GPL-3.0 许可证</a><a href={import.meta.env.BASE_URL + "streambert-source.tar.gz"}>下载此版本源码</a><a href="/">返回博客</a></div>
    </section>
  </div>;
}
