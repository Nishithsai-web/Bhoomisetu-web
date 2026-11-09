"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { MapParcel } from "@/lib/api";
import { formatRupeesShort, type AmountWords } from "@/lib/money";

// Leaflet is loaded from unpkg with a fixed version and integrity hash, so no map package is bundled.
const LEAFLET_CSS = { href: "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css", integrity: "sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=" };
const LEAFLET_JS = { src: "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js", integrity: "sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=" };

/* Minimal typing of the Leaflet calls used here. */
type LatLngBounds = { getSouth(): number; getWest(): number; getNorth(): number; getEast(): number };
type LeafletMap = {
  setView(center: [number, number], zoom: number): LeafletMap;
  getBounds(): LatLngBounds;
  on(event: string, handler: () => void): void;
  remove(): void;
};
type LayerGroup = { clearLayers(): void; addTo(map: LeafletMap): LayerGroup };
type Leaflet = {
  map(element: HTMLElement, options?: Record<string, unknown>): LeafletMap;
  tileLayer(url: string, options: Record<string, unknown>): { addTo(map: LeafletMap): void };
  layerGroup(): LayerGroup;
  circleMarker(at: [number, number], options: Record<string, unknown>): { bindPopup(content: HTMLElement): { addTo(layer: LayerGroup): void } };
};

declare global {
  interface Window {
    L?: Leaflet;
  }
}

let loading: Promise<Leaflet> | null = null;

function loadLeaflet(): Promise<Leaflet> {
  if (window.L) return Promise.resolve(window.L);
  loading ??= new Promise<Leaflet>((resolve, reject) => {
    const css = document.createElement("link");
    css.rel = "stylesheet";
    css.href = LEAFLET_CSS.href;
    css.integrity = LEAFLET_CSS.integrity;
    css.crossOrigin = "";
    document.head.appendChild(css);
    const script = document.createElement("script");
    script.src = LEAFLET_JS.src;
    script.integrity = LEAFLET_JS.integrity;
    script.crossOrigin = "";
    script.async = true;
    script.onload = () => (window.L ? resolve(window.L) : reject(new Error("Leaflet missing")));
    script.onerror = () => {
      loading = null;
      reject(new Error("Leaflet could not load"));
    };
    document.head.appendChild(script);
  });
  return loading;
}

// Land types told apart by colour and by the label in every popup and list row (never colour alone).
const COLOURS: Record<string, string> = { agricultural: "#1a7f45", non_agricultural: "#b5620a", converted: "#1d5fa8", unknown: "#5c6b6d" };

export type MapLabels = {
  mapLabel: string;
  nearMe: string;
  locating: string;
  locationFailed: string;
  loading: string;
  failed: string;
  none: string;
  truncated: string;
  count: string;
  filterLabel: string;
  all: string;
  types: Record<string, string>;
  classNames: Record<string, string>;
  fromPrice: string;
  noPrice: string;
  offers: string;
  offersOne: string;
  survey: string;
  open: string;
  listTitle: string;
  amounts: AmountWords;
};

const fill = (text: string, values: Record<string, string | number>) => text.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key] ?? ""));

/**
 * Interactive land map: pan and zoom, filter by land type, "near me". Every pin in view is also listed below
 * the map, so the page works with a screen reader, a slow phone, or when the map tiles do not load.
 */
export function MapSearch({ locale, intl, labels }: { locale: string; intl: string; labels: MapLabels }) {
  const element = useRef<HTMLDivElement | null>(null);
  const map = useRef<LeafletMap | null>(null);
  const layer = useRef<LayerGroup | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [landClass, setLandClass] = useState("");
  const [items, setItems] = useState<MapParcel[]>([]);
  const [truncated, setTruncated] = useState(false);
  const [status, setStatus] = useState(labels.loading);

  const price = useCallback(
    (p: MapParcel) => (p.lowestPriceRupees === null ? labels.noPrice : fill(labels.fromPrice, { price: formatRupeesShort(p.lowestPriceRupees, intl, labels.amounts) })),
    [intl, labels],
  );

  const load = useCallback(async () => {
    const m = map.current;
    const L = window.L;
    if (!m || !L || !layer.current) return;
    const b = m.getBounds();
    const params = new URLSearchParams({
      south: b.getSouth().toFixed(5),
      west: b.getWest().toFixed(5),
      north: b.getNorth().toFixed(5),
      east: b.getEast().toFixed(5),
    });
    if (landClass) params.set("landClass", landClass);
    try {
      const response = await fetch(`/map-data?${params}`, { cache: "no-store" });
      if (!response.ok) throw new Error(String(response.status));
      const data = (await response.json()) as { items: MapParcel[]; truncated: boolean };
      layer.current.clearLayers();
      for (const p of data.items) {
        // Popup built from DOM nodes with textContent: names typed by listers are never parsed as HTML.
        const box = document.createElement("div");
        const title = document.createElement("strong");
        title.textContent = `${p.village}, ${p.mandal}`;
        const kind = document.createElement("div");
        kind.textContent = `${labels.classNames[p.landClass] ?? p.landClass} · ${fill(labels.survey, { number: p.surveyNumber })}`;
        const cost = document.createElement("div");
        cost.textContent = `${price(p)} · ${p.activeOffers === 1 ? labels.offersOne : fill(labels.offers, { count: p.activeOffers })}`;
        const link = document.createElement("a");
        link.href = `/${locale}/parcels/${p.id}`;
        link.textContent = labels.open;
        box.append(title, kind, cost, link);
        L.circleMarker([p.latitude, p.longitude], { radius: 9, color: "#ffffff", weight: 2, fillColor: COLOURS[p.landClass] ?? COLOURS.unknown, fillOpacity: 0.95 })
          .bindPopup(box)
          .addTo(layer.current);
      }
      setItems(data.items);
      setTruncated(data.truncated);
      setStatus(data.items.length === 0 ? labels.none : fill(labels.count, { count: data.items.length }));
    } catch {
      setStatus(labels.failed);
    }
  }, [landClass, labels, locale, price]);

  // Map set-up once; Hyderabad region first, the reader can move anywhere in India.
  useEffect(() => {
    let cancelled = false;
    loadLeaflet()
      .then((L) => {
        if (cancelled || !element.current || map.current) return;
        const m = L.map(element.current, { scrollWheelZoom: false }).setView([17.385, 78.4867], 10);
        L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
          maxZoom: 19,
          attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
        }).addTo(m);
        layer.current = L.layerGroup().addTo(m);
        map.current = m;
        m.on("moveend", () => {
          if (timer.current) clearTimeout(timer.current);
          timer.current = setTimeout(() => void load(), 300);
        });
        void load();
      })
      .catch(() => setStatus(labels.failed));
    return () => {
      cancelled = true;
      if (timer.current) clearTimeout(timer.current);
      map.current?.remove();
      map.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A new land-type filter reloads the pins for the same area.
  useEffect(() => {
    void load();
  }, [load]);

  const nearMe = () => {
    if (!("geolocation" in navigator)) {
      setStatus(labels.locationFailed);
      return;
    }
    setStatus(labels.locating);
    navigator.geolocation.getCurrentPosition(
      (position) => map.current?.setView([position.coords.latitude, position.coords.longitude], 13),
      () => setStatus(labels.locationFailed),
      { enableHighAccuracy: false, timeout: 15000, maximumAge: 300000 },
    );
  };

  return (
    <div className="map-search">
      <div className="pills" role="group" aria-label={labels.filterLabel}>
        <span>{labels.filterLabel}</span>
        {["", "agricultural", "non_agricultural", "converted"].map((c) => (
          <button key={c || "all"} type="button" aria-pressed={landClass === c} className={landClass === c ? "pill active" : "pill"} onClick={() => setLandClass(c)}>
            {c === "" ? labels.all : (labels.types[c] ?? c)}
          </button>
        ))}
        <button type="button" className="secondary" onClick={nearMe}>
          {labels.nearMe}
        </button>
      </div>
      <div ref={element} className="land-map" role="region" aria-label={labels.mapLabel} />
      <p role="status" className="hint">
        {status}
        {truncated && ` ${labels.truncated}`}
      </p>
      {items.length > 0 && (
        <>
          <h2>{labels.listTitle}</h2>
          <ul className="parcel-list">
            {items.map((p) => (
              <li key={p.id} className="result">
                <ul className="tags">
                  <li className="tag tag-class">
                    <span aria-hidden="true" className="dot" style={{ background: COLOURS[p.landClass] ?? COLOURS.unknown }} /> {labels.classNames[p.landClass] ?? p.landClass}
                  </li>
                </ul>
                <h3>
                  <a href={`/${locale}/parcels/${p.id}`}>
                    {p.village}, {p.mandal}, {p.district}
                  </a>
                </h3>
                <p className="price">
                  {price(p)} <span className="muted">· {p.activeOffers === 1 ? labels.offersOne : fill(labels.offers, { count: p.activeOffers })}</span>
                </p>
                <p className="muted">{fill(labels.survey, { number: p.surveyNumber })}</p>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
