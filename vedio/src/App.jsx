import { useState, useEffect, useCallback, useMemo, lazy, Suspense } from "react";
import ErrorBoundary from "./components/ErrorBoundary";
import Sidebar from "./components/Sidebar";
import SearchModal from "./components/SearchModal";
import { storage } from "./utils/storage";
import { loadSiteConfig } from "./utils/siteConfig";
import { tmdbFetch, setApiErrorHandlers, clearTmdbCache } from "./utils/api";
import { applyAccentColor, applyTheme } from "./utils/appearance";
import { normalizeMedia, progressKey } from "./utils/browserPlayer";

const HomePage = lazy(() => import("./pages/HomePage"));
const MediaPage = lazy(() => import("./pages/MediaPage"));
const LibraryPage = lazy(() => import("./pages/LibraryPage"));

function usePersistentState(key, fallback) {
  const [value, setValue] = useState(() => storage.get(key) ?? fallback);
  useEffect(() => storage.set(key, value), [key, value]);
  return [value, setValue];
}

export default function App() {
  const [apiKey, setApiKey] = useState(null);
  const [ready, setReady] = useState(false);
  const [status, setStatus] = useState(null);
  const [route, setRoute] = useState({ page: "home", item: null });
  const [stack, setStack] = useState([]);
  const [showSearch, setShowSearch] = useState(false);
  const [showHelp, setShowHelp] = useState(false);
  const [saved, setSaved] = usePersistentState("saved", {});
  const [savedOrder, setSavedOrder] = usePersistentState("savedOrder", []);
  const [history, setHistory] = usePersistentState("history", []);
  const [progress, setProgress] = usePersistentState("progress", {});
  const [watched, setWatched] = usePersistentState("watched", {});
  const [trending, setTrending] = useState([]);
  const [trendingTV, setTrendingTV] = useState([]);
  const [loading, setLoading] = useState(false);
  const [offline, setOffline] = useState(!navigator.onLine);
  const [languageRevision, setLanguageRevision] = useState(0);

  const refreshConfiguration = useCallback(async () => {
    setLoading(true);
    try {
      const config = await loadSiteConfig();
      clearTmdbCache();
      applyAccentColor(config.accentColor);
      applyTheme(config.theme);
      setApiKey(config.enabled && config.configured);
      setStatus(!config.enabled ? "disabled" : !config.configured ? "unconfigured" : null);
      setLanguageRevision(value => value + 1);
    } catch { setApiKey(false); setStatus("configuration"); }
    finally { setReady(true); setLoading(false); }
  }, []);

  useEffect(() => {
    refreshConfiguration();
    const online = () => setOffline(false);
    const offline = () => setOffline(true);
    const languageChanged = () => setLanguageRevision(value => value + 1);
    window.addEventListener("online", online);
    window.addEventListener("offline", offline);
    window.addEventListener("streambert:tmdb-lang-changed", languageChanged);
    setApiErrorHandlers(() => setStatus("invalid"), () => setStatus("unreachable"));
    return () => {
      window.removeEventListener("online", online);
      window.removeEventListener("offline", offline);
      window.removeEventListener("streambert:tmdb-lang-changed", languageChanged);
      setApiErrorHandlers(null, null);
    };
  }, []);

  useEffect(() => {
    if (!apiKey || offline) return;
    let active = true;
    setLoading(true);
    Promise.all([tmdbFetch("/trending/movie/week", apiKey), tmdbFetch("/trending/tv/week", apiKey)])
      .then(([movies, tv]) => {
        if (!active) return;
        setTrending(movies.results || []);
        setTrendingTV(tv.results || []);
        setStatus(null);
      })
      .catch(() => { if (active) setStatus(previous => previous || "unreachable"); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [apiKey, offline, languageRevision]);

  const navigate = useCallback((page, item = null) => {
    setStack(previous => [...previous, route].slice(-30));
    setRoute({ page, item: item ? normalizeMedia(item) : null });
    setShowSearch(false);
  }, [route]);
  const back = useCallback(() => {
    if (!stack.length) return;
    setRoute(stack[stack.length - 1]);
    setStack(previous => previous.slice(0, -1));
  }, [stack]);
  const select = item => { const media = normalizeMedia(item); navigate(media.media_type, media); };

  useEffect(() => {
    const handler = event => {
      if (["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName) || event.target.isContentEditable) return;
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "f") {
        event.preventDefault(); setShowSearch(true);
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z") {
        event.preventDefault(); back();
      }
      if (event.key === "?") setShowHelp(true);
      if (event.key === "Escape") { setShowSearch(false); setShowHelp(false); }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [back]);

  const toggleSave = item => {
    const media = normalizeMedia(item);
    const key = `${media.media_type}_${media.id}`;
    const removing = !!saved[key];
    setSaved(previous => { const next = { ...previous }; if (removing) delete next[key]; else next[key] = media; return next; });
    setSavedOrder(previous => removing ? previous.filter(value => value !== key) : [...previous.filter(value => value !== key), key]);
  };
  const savedList = useMemo(() => {
    const keys = [...new Set([...savedOrder, ...Object.keys(saved)])];
    return keys.map(key => saved[key]).filter(Boolean);
  }, [saved, savedOrder]);
  const markWatched = key => setWatched(previous => ({ ...previous, [key]: true }));
  const markUnwatched = key => setWatched(previous => { const next = { ...previous }; delete next[key]; return next; });
  const saveProgress = useCallback((key, percent) => setProgress(previous => ({ ...previous, [key]: percent })), [setProgress]);
  const addHistory = useCallback(item => {
    if (storage.get("historyEnabled") === false) return;
    const entry = { ...normalizeMedia(item), watchedAt: Date.now() };
    setHistory(previous => [entry, ...previous.filter(value => progressKey(value) !== progressKey(entry))].slice(0, 100));
  }, [setHistory]);
  const inProgress = history.map(item => ({ ...item, _pk: progressKey(item) }))
    .filter(item => !watched[item._pk] && progress[item._pk] > 2 && progress[item._pk] < 98);

  if (!ready) return <div className="web-empty">正在加载…</div>;
  return <ErrorBoundary>
    <Sidebar page={route.page} onNavigate={navigate} onSearch={() => setShowSearch(true)} savedList={savedList}
      onReorderSaved={setSavedOrder} onRemoveSaved={toggleSave} canGoBack={stack.length > 0} onBack={back}
      onShowShortcuts={() => setShowHelp(true)} />
    <main className="main">
      {(!apiKey || status) && <div className="api-status-banner api-status-warn" role="status">
        <span>{status === "disabled" ? "影视服务暂时关闭。" : status === "unconfigured" ? "影视服务尚未配置，请联系管理员。" : status === "configuration" ? "无法获取影视配置，请稍后重试。" : status === "invalid" ? "影视资料服务配置异常，请联系管理员。" : "暂时无法连接影视资料服务，请稍后重试。"}</span>
        <button className="api-status-btn" onClick={refreshConfiguration}>重试</button>
      </div>}
      <Suspense fallback={<div className="web-empty">正在加载…</div>}>
        {route.page === "home" && <HomePage key={languageRevision} trending={trending} trendingTV={trendingTV} loading={loading}
          onSelect={select} progress={progress} inProgress={inProgress} offline={offline} onRetry={() => setLanguageRevision(value => value + 1)}
          watched={watched} onMarkWatched={markWatched} onMarkUnwatched={markUnwatched} history={history} apiKey={apiKey} />}
        {["movie", "tv"].includes(route.page) && route.item && <MediaPage key={`${route.page}_${route.item.id}`} item={route.item} apiKey={apiKey}
          onBack={back} onSave={() => toggleSave(route.item)} isSaved={!!saved[`${route.page}_${route.item.id}`]}
          onHistory={addHistory} progress={progress} saveProgress={saveProgress} watched={watched}
          onMarkWatched={markWatched} onMarkUnwatched={markUnwatched} onSelect={select} />}
        {route.page === "history" && <LibraryPage history={history} inProgress={inProgress} saved={savedList} progress={progress}
          onSelect={select} watched={watched} onMarkWatched={markWatched} onMarkUnwatched={markUnwatched}
          onRemoveHistory={item => setHistory(previous => previous.filter(value => progressKey(value) !== progressKey(item)))} />}
      </Suspense>
    </main>
    {showSearch && <SearchModal apiKey={apiKey} offline={offline} onSelect={select} onClose={() => setShowSearch(false)} />}
    {showHelp && <div className="modal-overlay" onClick={event => { if (event.target === event.currentTarget) setShowHelp(false); }}>
      <div className="web-help" role="dialog" aria-modal="true" aria-label="使用帮助">
        <h2>Streambert 网页版</h2>
        <p>Ctrl / ⌘ + F 搜索 · Ctrl / ⌘ + Z 返回 · Esc 关闭弹窗。</p>
        <p>收藏和历史保存在此浏览器，影视配置由网站统一管理。播放器由第三方提供，无法加载时可切换播放源或在新窗口打开。</p>
        <p>网页版不提供桌面端的多线程下载、AllManga 抓取、广告拦截和跨站视频控制。仅保存播放源主动提供的观看进度，也可手动标记已看。</p>
        <a href={import.meta.env.BASE_URL + "streambert-source.tar.gz"}>下载此版本源码（GPL-3.0）</a>
        <button className="btn btn-primary" onClick={() => setShowHelp(false)}>关闭</button>
      </div>
    </div>}
  </ErrorBoundary>;
}
