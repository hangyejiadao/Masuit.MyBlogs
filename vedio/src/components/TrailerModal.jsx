import { useEffect } from "react";
export default function TrailerModal({ trailerKey, title, onClose }) {
  useEffect(() => {
    const close = event => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", close);
    return () => window.removeEventListener("keydown", close);
  }, [onClose]);
  return <div className="trailer-overlay" onClick={event => { if (event.target === event.currentTarget) onClose(); }}>
    <div className="trailer-modal" role="dialog" aria-modal="true" aria-label={`${title} 预告片`}>
      <div className="trailer-modal-header"><span>{title} · 预告片</span><button className="btn btn-ghost" onClick={onClose}>关闭</button></div>
      <iframe className="web-player" src={`https://www.youtube-nocookie.com/embed/${encodeURIComponent(trailerKey)}?autoplay=1`}
        title={`${title} 预告片`} allow="autoplay; encrypted-media; fullscreen; picture-in-picture" allowFullScreen />
    </div>
  </div>;
}
