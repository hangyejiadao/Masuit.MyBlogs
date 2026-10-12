import { useState, useEffect, useRef } from "react";
import { tmdbFetch, imgUrl, PLAYER_SOURCES, getSourceUrl } from "../utils/api";
import { storage } from "../utils/storage";
import { ACCENT_PRESETS } from "../utils/appearance";
import { progressKey, parsePlayerProgress } from "../utils/browserPlayer";
import MediaCard from "../components/MediaCard";
import TrailerModal from "../components/TrailerModal";
import { PlayIcon, BookmarkIcon, BookmarkFillIcon, BackIcon } from "../components/Icons";

export default function MediaPage({ item, apiKey, onBack, onSave, isSaved, onHistory,
  progress, saveProgress, watched, onMarkWatched, onMarkUnwatched, onSelect }) {
  const tv = item.media_type === "tv";
  const [details, setDetails] = useState(null);
  const [error, setError] = useState(null);
  const [revision, setRevision] = useState(0);
  const [season, setSeason] = useState(Number(item.season ?? 1));
  const [episode, setEpisode] = useState(Number(item.episode ?? 1));
  const [episodes, setEpisodes] = useState([]);
  const [seasonLoading, setSeasonLoading] = useState(false);
  const [seasonError, setSeasonError] = useState(null);
  const [playing, setPlaying] = useState(false);
  const [trailer, setTrailer] = useState(false);
  const [source, setSource] = useState(() => {
    const saved = storage.get("playerSource");
    return PLAYER_SOURCES.some(value => value.id === saved) ? saved : PLAYER_SOURCES[0].id;
  });
  const frame = useRef(null);
  const player = useRef(null);

  useEffect(() => {
    let active = true;
    setError(null); setDetails(null);
    tmdbFetch(`/${item.media_type}/${item.id}?append_to_response=videos,recommendations,credits`, apiKey)
      .then(data => {
        if (!active) return;
        setDetails(data);
        if (tv && !data.seasons?.some(value => value.season_number === season)) {
          setSeason(data.seasons?.find(value => value.season_number > 0)?.season_number ?? data.seasons?.[0]?.season_number ?? 1);
          setEpisode(1);
        }
      }).catch(() => { if (active) setError("无法加载详情，请检查 Token 和网络后重试。"); });
    return () => { active = false; };
  }, [item.id, item.media_type, apiKey, revision]);

  useEffect(() => {
    if (!tv || !details) return;
    let active = true;
    setSeasonLoading(true); setSeasonError(null); setEpisodes([]);
    tmdbFetch(`/tv/${item.id}/season/${season}`, apiKey)
      .then(data => {
        if (!active) return;
        const list = data.episodes || [];
        setEpisodes(list);
        if (!list.some(value => value.episode_number === episode)) setEpisode(list[0]?.episode_number ?? 1);
      }).catch(() => { if (active) setSeasonError("无法加载剧集，请重试。"); })
      .finally(() => { if (active) setSeasonLoading(false); });
    return () => { active = false; };
  }, [tv, details, item.id, apiKey, season, revision]);

  const selectedEpisode = episodes.find(value => value.episode_number === episode);
  const media = { ...item, title: details?.title || details?.name || item.title,
    poster_path: details?.poster_path || item.poster_path,
    ...(tv ? { season, episode, episodeName: selectedEpisode?.name } : {}) };
  const key = progressKey(media);
  const accent = ACCENT_PRESETS.find(value => value.id === (storage.get("accentColor") || "red"))?.color;
  const url = getSourceUrl(source, item.media_type, item.id, season, episode, {}, accent, storage.get("subtitleLang"));
  const trailerVideo = details?.videos?.results?.find(value => value.site === "YouTube" && value.type === "Trailer");

  useEffect(() => {
    if (!playing) return;
    const receive = event => {
      const percent = parsePlayerProgress(event, frame.current?.contentWindow, url,
        { id: item.id, type: item.media_type, season, episode });
      if (percent !== null) saveProgress(key, Math.round(percent * 10) / 10);
    };
    window.addEventListener("message", receive);
    return () => window.removeEventListener("message", receive);
  }, [playing, url, key, item.id, item.media_type, season, episode, saveProgress]);

  useEffect(() => { setPlaying(false); }, [season, episode]);

  const play = () => { onHistory(media); setPlaying(true); };
  const chooseEpisode = number => { setPlaying(false); setEpisode(number); };
  if (error) return <div className="web-empty"><p role="alert">{error}</p><button className="btn btn-primary" onClick={() => setRevision(value => value + 1)}>重试</button><button className="btn btn-ghost" onClick={onBack}>返回</button></div>;
  if (!details) return <div className="web-empty">正在加载详情…</div>;
  return <article className="web-media fade-in">
    <div className="web-media-hero" style={{ backgroundImage: details.backdrop_path ? `linear-gradient(0deg, var(--bg), rgba(0,0,0,.45)), url(${imgUrl(details.backdrop_path, "w1280")})` : undefined }}>
      <button className="btn btn-ghost" onClick={onBack}><BackIcon /> 返回</button>
      <div className="web-media-info">
        {details.poster_path && <img className="web-poster" src={imgUrl(details.poster_path)} alt={media.title} />}
        <div>
          <div className="web-kicker">{tv ? "电视剧 / TV SERIES" : "电影 / MOVIE"}</div>
          <h1>{media.title}</h1>
          <p className="web-muted">{(details.release_date || details.first_air_date || "").slice(0, 4)} · ★ {details.vote_average?.toFixed(1)} · {details.genres?.map(value => value.name).join(" / ")}{details.runtime ? ` · ${details.runtime} 分钟` : ""}</p>
          <p className="web-overview">{details.overview || "暂无剧情介绍"}</p>
          <div className="web-actions">
            <button className="btn btn-primary" onClick={play} disabled={tv && (!selectedEpisode || seasonLoading)}><PlayIcon /> {playing ? "正在播放" : "播放"}</button>
            <button className="btn btn-ghost" onClick={onSave}>{isSaved ? <BookmarkFillIcon /> : <BookmarkIcon />}{isSaved ? "取消收藏" : "收藏"}</button>
            {trailerVideo && <button className="btn btn-ghost" onClick={() => setTrailer(true)}>预告片</button>}
            <button className="btn btn-ghost" onClick={() => watched[key] ? onMarkUnwatched(key) : onMarkWatched(key)}>{watched[key] ? "取消已看标记" : "标记已看"}</button>
          </div>
        </div>
      </div>
    </div>

    {tv && <section className="web-section">
      <label className="web-label">选择季
        <select aria-label="选择季" value={season} onChange={event => { setSeason(Number(event.target.value)); setEpisode(1); setPlaying(false); }}>
          {details.seasons?.filter(value => value.episode_count > 0).map(value => <option key={value.season_number} value={value.season_number}>{value.name}</option>)}
        </select>
      </label>
      {seasonLoading && <p className="web-muted">正在加载剧集…</p>}
      {seasonError && <p role="alert">{seasonError} <button className="btn btn-ghost" onClick={() => setRevision(value => value + 1)}>重试</button></p>}
      <div className="web-episodes">{episodes.map(value => {
        const episodeKey = progressKey({ ...item, season, episode: value.episode_number });
        return <button key={value.episode_number} className={`web-episode ${episode === value.episode_number ? "selected" : ""}`}
          onClick={() => chooseEpisode(value.episode_number)} aria-pressed={episode === value.episode_number}>
          <span>E{value.episode_number} {watched[episodeKey] ? "✓" : ""}</span><span>{value.name}</span>
        </button>;
      })}</div>
    </section>}

    <section className="web-section" ref={player}>
      <div className="web-player-toolbar">
        <label className="web-label">播放源 <select aria-label="播放源" value={source} onChange={event => { setSource(event.target.value); storage.set("playerSource", event.target.value); }}>
          {PLAYER_SOURCES.map(value => <option key={value.id} value={value.id}>{value.label}</option>)}
        </select></label>
        {playing && <>
          <button className="btn btn-ghost" onClick={() => setPlaying(false)}>停止播放</button>
          <button className="btn btn-ghost" onClick={async () => { try { await player.current?.requestFullscreen(); } catch { /* Browser may deny fullscreen. */ } }}>全屏</button>
          <a className="btn btn-ghost" href={url} target="_blank" rel="noopener noreferrer" onClick={() => { onHistory(media); setPlaying(false); }}>新窗口打开</a>
        </>}
      </div>
      {playing ? <iframe key={url} ref={frame} className="web-player" src={url} title={`${media.title}${tv ? ` S${season}E${episode}` : ""} 播放器`}
        allow="autoplay; fullscreen; encrypted-media; picture-in-picture" allowFullScreen
        sandbox="allow-scripts allow-same-origin allow-forms allow-presentation" referrerPolicy="strict-origin-when-cross-origin" />
        : <div className="web-player-placeholder"><PlayIcon /><p>点击「播放」开始观看{tv ? ` S${season}E${episode}` : ""}</p></div>}
      <p className="web-muted web-player-note">第三方播放源的可用性由提供方决定。加载失败时请切换播放源或在新窗口打开。仅记录播放源主动提供的进度；浏览器无法读取跨站播放器。</p>
      {progress[key] > 0 && <p className="web-muted">已记录进度：{Math.round(progress[key])}%</p>}
    </section>
    {details.credits?.cast?.length > 0 && <section className="web-section"><h2>主演</h2><p className="web-muted">{details.credits.cast.slice(0, 10).map(value => value.name).join(" · ")}</p></section>}
    {details.recommendations?.results?.length > 0 && <section className="web-section"><h2>相似推荐</h2><div className="web-recommendations">
      {details.recommendations.results.slice(0, 12).map(value => <MediaCard key={value.id} item={{ ...value, media_type: item.media_type }} onClick={() => onSelect({ ...value, media_type: item.media_type })}
        watched={watched} onMarkWatched={onMarkWatched} onMarkUnwatched={onMarkUnwatched} />)}
    </div></section>}
    {trailer && <TrailerModal trailerKey={trailerVideo.key} title={media.title} onClose={() => setTrailer(false)} />}
  </article>;
}
