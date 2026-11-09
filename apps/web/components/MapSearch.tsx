"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import type { MapParcel, Rental } from "@/lib/api";
import { formatRupees, formatRupeesShort, type AmountWords } from "@/lib/money";

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
  dataFailed: string;
  none: string;
  noneRent: string;
  countRent: string;
  mapLabelRent: string;
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
  modeLabel: string;
  landMode: string;
  rentMode: string;
  bedroomsLabel: string;
  anyBedrooms: string;
  homeHeadline: string;
  homeTypes: Record<string, string>;
  perMonth: string;
  perSqft: string;
  homesTitle: string;
  rentOut: string;
};

const fill = (text: string, values: Record<string, string | number>) => text.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key] ?? ""));

/**
 * Interactive land map: pan and zoom, filter by land type, "near me". Every pin in view is also listed below
 * the map, so the page works with a screen reader, a slow phone, or when the map tiles do not load.
 */
export function MapSearch({ locale, intl, labels, initialMode }: { locale: string; intl: string; labels: MapLabels; initialMode: "land" | "rent" }) {
  const [mode, setMode] = useState<"land" | "rent">(initialMode);
  const [bedrooms, setBedrooms] = useState(0);
  const [homes, setHomes] = useState<Rental[]>([]);
  const element = useRef<HTMLDivElement | null>(null);
  const map = useRef<LeafletMap | null>(null);
  const layer = useRef<LayerGroup | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  // The map's move handler is attached once; it always calls the latest load (current mode and filters).
  const loadRef = useRef<() => Promise<void>>(async () => {});
  const inflight = useRef<AbortController | null>(null);
  // Area used before the map library loads (or if it never does): around Hyderabad.
  const fallbackArea = { south: 17.2, west: 78.2, north: 17.6, east: 78.7 };
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
    const b = m?.getBounds();
    const area = b ? { south: b.getSouth(), west: b.getWest(), north: b.getNorth(), east: b.getEast() } : fallbackArea;
    const params = new URLSearchParams({
      south: area.south.toFixed(5),
      west: area.west.toFixed(5),
      north: area.north.toFixed(5),
      east: area.east.toFixed(5),
    });
    if (mode === "land" && landClass) params.set("landClass", landClass);
    if (mode === "rent") {
      params.set("mode", "rent");
      if (bedrooms > 0) params.set("bedrooms", String(bedrooms));
    }
    // A newer request replaces an older one, so a slow answer can never overwrite the current view.
    inflight.current?.abort();
    const controller = new AbortController();
    inflight.current = controller;
    try {
      const response = await fetch(`/map-data?${params}`, { cache: "no-store", signal: controller.signal });
      if (!response.ok) throw new Error(String(response.status));
      layer.current?.clearLayers();
      if (mode === "rent") {
        const data = (await response.json()) as { items: Rental[]; truncated: boolean };
        for (const h of data.items) {
          const box = document.createElement("div");
          const title = document.createElement("strong");
          title.textContent = (h.bedrooms === 0 ? (labels.homeTypes[h.propertyType] ?? h.propertyType) : fill(labels.homeHeadline, { bedrooms: h.bedrooms, type: labels.homeTypes[h.propertyType] ?? h.propertyType }));
          const where = document.createElement("div");
          where.textContent = h.locality;
          const cost = document.createElement("div");
          cost.textContent = `${fill(labels.perMonth, { rent: formatRupees(h.rentRupees, intl) })} · ${fill(labels.perSqft, { value: formatRupees(h.rentPerSqft, intl) })}`;
          const link = document.createElement("a");
          link.href = `/${locale}/rentals/${h.id}`;
          link.textContent = labels.open;
          box.append(title, where, cost, link);
          if (L && layer.current) {
            L.circleMarker([h.latitude, h.longitude], { radius: 9, color: "#ffffff", weight: 2, fillColor: "#7b2d8b", fillOpacity: 0.95 }).bindPopup(box).addTo(layer.current);
          }
        }
        setHomes(data.items);
        setItems([]);
        setTruncated(data.truncated);
        setStatus(data.items.length === 0 ? labels.noneRent : fill(labels.countRent, { count: data.items.length }));
        return;
      }
      const data = (await response.json()) as { items: MapParcel[]; truncated: boolean };
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
        if (L && layer.current) {
          L.circleMarker([p.latitude, p.longitude], { radius: 9, color: "#ffffff", weight: 2, fillColor: COLOURS[p.landClass] ?? COLOURS.unknown, fillOpacity: 0.95 })
            .bindPopup(box)
            .addTo(layer.current);
        }
      }
      setItems(data.items);
      setHomes([]);
      setTruncated(data.truncated);
      setStatus(data.items.length === 0 ? labels.none : fill(labels.count, { count: data.items.length }));
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") return;
      // The map itself may be showing; only the listings for this area could not be fetched.
      setStatus(labels.dataFailed);
    }
  }, [landClass, labels, locale, price, mode, bedrooms, intl]);

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
          timer.current = setTimeout(() => void loadRef.current(), 300);
        });
        void loadRef.current();
      })
      .catch(() => {
        // Without the map library, the list below still loads for the Hyderabad area.
        setStatus(labels.failed);
        void loadRef.current();
      });
    return () => {
      cancelled = true;
      if (timer.current) clearTimeout(timer.current);
      map.current?.remove();
      map.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A new mode or filter reloads the pins for the same area, and becomes what panning uses from now on.
  useEffect(() => {
    loadRef.current = load;
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
      <div className="pills" role="group" aria-label={labels.modeLabel}>
        {(["land", "rent"] as const).map((m) => (
          <button key={m} type="button" aria-pressed={mode === m} className={mode === m ? "pill active" : "pill"} onClick={() => setMode(m)}>
            {m === "land" ? labels.landMode : labels.rentMode}
          </button>
        ))}
        <button type="button" className="secondary" onClick={nearMe}>
          {labels.nearMe}
        </button>
      </div>
      {mode === "land" ? (
        <div className="pills" role="group" aria-label={labels.filterLabel}>
          <span>{labels.filterLabel}</span>
          {["", "agricultural", "non_agricultural", "converted"].map((c) => (
            <button key={c || "all"} type="button" aria-pressed={landClass === c} className={landClass === c ? "pill active" : "pill"} onClick={() => setLandClass(c)}>
              {c === "" ? labels.all : (labels.types[c] ?? c)}
            </button>
          ))}
        </div>
      ) : (
        <div className="pills" role="group" aria-label={labels.bedroomsLabel}>
          <span>{labels.bedroomsLabel}</span>
          {[0, 1, 2, 3, 4].map((b) => (
            <button key={b} type="button" aria-pressed={bedrooms === b} className={bedrooms === b ? "pill active" : "pill"} onClick={() => setBedrooms(b)}>
              {b === 0 ? labels.anyBedrooms : `${b}+`}
            </button>
          ))}
          <a href={`/${locale}/rent/new`} className="button-link">
            {labels.rentOut}
          </a>
        </div>
      )}
      <div ref={element} className="land-map" role="region" aria-label={mode === "rent" ? labels.mapLabelRent : labels.mapLabel} />
      <p role="status" className="hint">
        {status}
        {truncated && ` ${labels.truncated}`}
      </p>
      {homes.length > 0 && (
        <>
          <h2>{labels.homesTitle}</h2>
          <ul className="parcel-list">
            {homes.map((h) => (
              <li key={h.id} className="result">
                <h3>
                  <a href={`/${locale}/rentals/${h.id}`}>
                    {(h.bedrooms === 0 ? (labels.homeTypes[h.propertyType] ?? h.propertyType) : fill(labels.homeHeadline, { bedrooms: h.bedrooms, type: labels.homeTypes[h.propertyType] ?? h.propertyType }))}, {h.locality}
                  </a>
                </h3>
                <p className="price">
                  {fill(labels.perMonth, { rent: formatRupees(h.rentRupees, intl) })}{" "}
                  <span className="muted">· {fill(labels.perSqft, { value: formatRupees(h.rentPerSqft, intl) })}</span>
                </p>
                <p className="muted">
                  {h.mandal}, {h.district}
                </p>
              </li>
            ))}
          </ul>
        </>
      )}
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
