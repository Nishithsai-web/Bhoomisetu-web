"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import type { MapParcel, Place, Rental } from "@/lib/api";
import { compactRupees, isNew } from "@/lib/compactPrice";
import { formatRupees, formatRupeesShort, type AmountWords } from "@/lib/money";

// Leaflet is loaded from unpkg with a fixed version and integrity hash, so no map package is bundled.
const LEAFLET_CSS = { href: "https://unpkg.com/leaflet@1.9.4/dist/leaflet.css", integrity: "sha256-p4NxAoJBhIIN+hmNHrzRCf9tD/miZyoHS5obTRR9BMY=" };
const LEAFLET_JS = { src: "https://unpkg.com/leaflet@1.9.4/dist/leaflet.js", integrity: "sha256-20nQCchB9co0qIjJZRGuk2/Z9VM+kNiyxNV1lvTlZBo=" };

/* Minimal typing of the Leaflet calls used here. */
type LatLng = { lat: number; lng: number };
type Point = { x: number; y: number };
type LatLngBounds = { getSouth(): number; getWest(): number; getNorth(): number; getEast(): number };
type Popup = unknown;
type Marker = { bindPopup(content: HTMLElement): Marker; addTo(layer: LayerGroup): Marker; openPopup(): Marker; on(event: string, handler: () => void): Marker };
type LeafletMap = {
  setView(center: [number, number], zoom: number): LeafletMap;
  flyTo(center: [number, number], zoom?: number, options?: Record<string, unknown>): LeafletMap;
  fitBounds(bounds: [[number, number], [number, number]], options?: Record<string, unknown>): LeafletMap;
  getBounds(): LatLngBounds;
  getCenter(): LatLng;
  getZoom(): number;
  latLngToContainerPoint(at: [number, number]): Point;
  invalidateSize(): void;
  on(event: string, handler: () => void): void;
  remove(): void;
};
type LayerGroup = { clearLayers(): void; addTo(map: LeafletMap): LayerGroup };
type Leaflet = {
  map(element: HTMLElement, options?: Record<string, unknown>): LeafletMap;
  tileLayer(url: string, options: Record<string, unknown>): { addTo(map: LeafletMap): void };
  layerGroup(): LayerGroup;
  divIcon(options: { html: HTMLElement; className: string; iconSize: null | [number, number] }): unknown;
  marker(at: [number, number], options: Record<string, unknown>): Marker;
  popup(): Popup;
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
const RENT_COLOUR = "#7b2d8b";

/** Pins closer than this many screen pixels are grouped when zoomed out. */
const CLUSTER_PX = 56;
const CLUSTER_BELOW_ZOOM = 14;

export type MapLabels = {
  mapLabel: string;
  mapLabelRent: string;
  nearMe: string;
  locating: string;
  locationFailed: string;
  loading: string;
  failed: string;
  dataFailed: string;
  none: string;
  noneRent: string;
  truncated: string;
  count: string;
  countRent: string;
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
  searchLabel: string;
  searchPlaceholder: string;
  searchButton: string;
  placesTitle: string;
  placeListings: string;
  placeNoListings: string;
  noPlaces: string;
  newOnly: string;
  newBadge: string;
  showOnMap: string;
  expand: string;
  collapse: string;
  clusterLabel: string;
};

export type MapStart = { mode: "land" | "rent"; lat: number; lng: number; zoom: number; landClass: string; bedrooms: number; newOnly: boolean; q: string };

const fill = (text: string, values: Record<string, string | number>) => text.replace(/\{(\w+)\}/g, (_, key: string) => String(values[key] ?? ""));

type Item =
  | { kind: "land"; id: string; lat: number; lng: number; data: MapParcel }
  | { kind: "rent"; id: string; lat: number; lng: number; data: Rental };

/**
 * Live map explorer. Search any place (Telugu, Hindi or English) and fly there; pins show their price, new
 * listings are tagged; pins group when zoomed out; the list beside the map follows the view; the view and
 * filters live in the address bar so it can be shared. Every pin in view is also in the list, so the page
 * works with a screen reader, a slow phone, or when the map tiles do not load.
 */
export function MapSearch({ locale, intl, labels, start, tiles }: { locale: string; intl: string; labels: MapLabels; start: MapStart; tiles: { url: string; attribution: string } }) {
  const element = useRef<HTMLDivElement | null>(null);
  const map = useRef<LeafletMap | null>(null);
  const layer = useRef<LayerGroup | null>(null);
  const markers = useRef<Map<string, Marker>>(new Map());
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const loadRef = useRef<() => Promise<void>>(async () => {});
  const inflight = useRef<AbortController | null>(null);
  const fallbackArea = useRef({ south: start.lat - 0.2, west: start.lng - 0.25, north: start.lat + 0.2, east: start.lng + 0.25 });

  const [mode, setMode] = useState<"land" | "rent">(start.mode);
  const [landClass, setLandClass] = useState(start.landClass);
  const [bedrooms, setBedrooms] = useState(start.bedrooms);
  const [newOnly, setNewOnly] = useState(start.newOnly);
  const [items, setItems] = useState<Item[]>([]);
  const [truncated, setTruncated] = useState(false);
  const [status, setStatus] = useState(labels.loading);
  const [query, setQuery] = useState(start.q);
  const [places, setPlaces] = useState<Place[] | null>(null);
  const [expanded, setExpanded] = useState(false);
  const now = useRef(new Date());

  const landPrice = useCallback(
    (p: MapParcel) => (p.lowestPriceRupees === null ? labels.noPrice : fill(labels.fromPrice, { price: formatRupeesShort(p.lowestPriceRupees, intl, labels.amounts) })),
    [intl, labels],
  );
  const homeTitle = useCallback(
    (h: Rental) => (h.bedrooms === 0 ? (labels.homeTypes[h.propertyType] ?? h.propertyType) : fill(labels.homeHeadline, { bedrooms: h.bedrooms, type: labels.homeTypes[h.propertyType] ?? h.propertyType })),
    [labels],
  );

  /** Popup content from DOM nodes with textContent: names typed by listers are never parsed as HTML. */
  const popupFor = useCallback(
    (item: Item) => {
      const box = document.createElement("div");
      const title = document.createElement("strong");
      const line1 = document.createElement("div");
      const line2 = document.createElement("div");
      const link = document.createElement("a");
      if (item.kind === "land") {
        const p = item.data;
        title.textContent = `${p.village}, ${p.mandal}`;
        line1.textContent = `${labels.classNames[p.landClass] ?? p.landClass} · ${fill(labels.survey, { number: p.surveyNumber })}`;
        line2.textContent = `${landPrice(p)} · ${p.activeOffers === 1 ? labels.offersOne : fill(labels.offers, { count: p.activeOffers })}`;
        link.href = `/${locale}/parcels/${p.id}`;
      } else {
        const h = item.data;
        title.textContent = homeTitle(h);
        line1.textContent = h.locality;
        line2.textContent = `${fill(labels.perMonth, { rent: formatRupees(h.rentRupees, intl) })} · ${fill(labels.perSqft, { value: formatRupees(h.rentPerSqft, intl) })}`;
        link.href = `/${locale}/rentals/${h.id}`;
      }
      link.textContent = labels.open;
      box.append(title, line1, line2, link);
      return box;
    },
    [homeTitle, intl, labels, landPrice, locale],
  );

  /** Price tags for single pins; numbered circles for groups of nearby pins when zoomed out. */
  const draw = useCallback(
    (list: Item[]) => {
      const m = map.current;
      const L = window.L;
      if (!m || !L || !layer.current) return;
      layer.current.clearLayers();
      markers.current.clear();
      const zoom = m.getZoom();
      const groups = new Map<string, Item[]>();
      for (const item of list) {
        const key =
          zoom < CLUSTER_BELOW_ZOOM && list.length > 25
            ? (() => {
                const pt = m.latLngToContainerPoint([item.lat, item.lng]);
                return `${Math.floor(pt.x / CLUSTER_PX)}:${Math.floor(pt.y / CLUSTER_PX)}`;
              })()
            : item.id;
        groups.set(key, [...(groups.get(key) ?? []), item]);
      }
      for (const group of groups.values()) {
        if (group.length > 1) {
          const lat = group.reduce((s, i) => s + i.lat, 0) / group.length;
          const lng = group.reduce((s, i) => s + i.lng, 0) / group.length;
          const bubble = document.createElement("span");
          bubble.className = "cluster-pin";
          bubble.textContent = String(group.length);
          bubble.setAttribute("aria-label", fill(labels.clusterLabel, { count: group.length }));
          L.marker([lat, lng], { icon: L.divIcon({ html: bubble, className: "pin-wrap", iconSize: null }), keyboard: true, title: fill(labels.clusterLabel, { count: group.length }) })
            .on("click", () => m.flyTo([lat, lng], Math.min(zoom + 2, 18)))
            .addTo(layer.current);
          continue;
        }
        const item = group[0]!;
        const tag = document.createElement("span");
        const fresh = isNew(item.data.createdAt, now.current);
        tag.className = `price-pin${fresh ? " is-new" : ""}`;
        tag.style.borderColor = item.kind === "land" ? (COLOURS[item.data.landClass] ?? COLOURS.unknown!) : RENT_COLOUR;
        tag.textContent =
          item.kind === "land"
            ? item.data.lowestPriceRupees === null
              ? "₹ –"
              : compactRupees(item.data.lowestPriceRupees)
            : `${compactRupees(item.data.rentRupees)}/mo`;
        if (fresh) {
          const badge = document.createElement("b");
          badge.textContent = labels.newBadge;
          tag.prepend(badge);
        }
        const marker = L.marker([item.lat, item.lng], { icon: L.divIcon({ html: tag, className: "pin-wrap", iconSize: null }), keyboard: true, title: tag.textContent ?? "" })
          .bindPopup(popupFor(item))
          .addTo(layer.current);
        markers.current.set(item.id, marker);
      }
    },
    [labels, popupFor],
  );

  const load = useCallback(async () => {
    const m = map.current;
    const b = m?.getBounds();
    const area = b ? { south: b.getSouth(), west: b.getWest(), north: b.getNorth(), east: b.getEast() } : fallbackArea.current;
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
    if (newOnly) params.set("newDays", "7");

    // Keep the view and filters in the address, so it can be shared or reopened.
    if (m) {
      const c = m.getCenter();
      const url = new URLSearchParams({ mode, lat: c.lat.toFixed(4), lng: c.lng.toFixed(4), z: String(m.getZoom()) });
      if (landClass) url.set("landClass", landClass);
      if (bedrooms) url.set("bedrooms", String(bedrooms));
      if (newOnly) url.set("new", "1");
      window.history.replaceState(null, "", `${window.location.pathname}?${url}`);
    }

    // A newer request replaces an older one, so a slow answer can never overwrite the current view.
    inflight.current?.abort();
    const controller = new AbortController();
    inflight.current = controller;
    try {
      const response = await fetch(`/map-data?${params}`, { cache: "no-store", signal: controller.signal });
      if (!response.ok) throw new Error(String(response.status));
      let list: Item[];
      let more: boolean;
      if (mode === "rent") {
        const data = (await response.json()) as { items: Rental[]; truncated: boolean };
        list = data.items.map((h) => ({ kind: "rent", id: h.id, lat: h.latitude, lng: h.longitude, data: h }));
        more = data.truncated;
      } else {
        const data = (await response.json()) as { items: MapParcel[]; truncated: boolean };
        list = data.items.map((p) => ({ kind: "land", id: p.id, lat: p.latitude, lng: p.longitude, data: p }));
        more = data.truncated;
      }
      setItems(list);
      setTruncated(more);
      draw(list);
      setStatus(
        list.length === 0 ? (mode === "rent" ? labels.noneRent : labels.none) : fill(mode === "rent" ? labels.countRent : labels.count, { count: list.length }),
      );
    } catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") return;
      setStatus(labels.dataFailed);
    }
  }, [bedrooms, draw, labels, landClass, mode, newOnly]);

  // Map set-up once, at the place and zoom from the address (or Hyderabad).
  useEffect(() => {
    let cancelled = false;
    loadLeaflet()
      .then((L) => {
        if (cancelled || !element.current || map.current) return;
        const m = L.map(element.current, { scrollWheelZoom: true, zoomControl: true }).setView([start.lat, start.lng], start.zoom);
        L.tileLayer(tiles.url, { maxZoom: 19, attribution: tiles.attribution }).addTo(m);
        layer.current = L.layerGroup().addTo(m);
        map.current = m;
        m.on("moveend", () => {
          if (timer.current) clearTimeout(timer.current);
          timer.current = setTimeout(() => void loadRef.current(), 250);
        });
        void loadRef.current();
        if (start.q) void searchPlaces(start.q, true);
      })
      .catch(() => {
        // Without the map library, the list still loads for the starting area.
        setStatus(labels.failed);
        void loadRef.current();
      });
    return () => {
      cancelled = true;
      if (timer.current) clearTimeout(timer.current);
      inflight.current?.abort();
      map.current?.remove();
      map.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // A new mode or filter reloads the same area, and is what panning uses from now on.
  useEffect(() => {
    loadRef.current = load;
    void load();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [load]);

  // The map changes size when expanded; Leaflet must be told.
  useEffect(() => {
    const id = setTimeout(() => map.current?.invalidateSize(), 50);
    return () => clearTimeout(id);
  }, [expanded]);

  const goTo = (place: Place) => {
    const m = map.current;
    if (!m) return;
    if (place.boundingBox && place.boundingBox.length === 4) {
      const [s, n, w, e] = place.boundingBox as [number, number, number, number];
      m.fitBounds(
        [
          [s, w],
          [n, e],
        ],
        { maxZoom: 15 },
      );
    } else {
      m.flyTo([place.latitude, place.longitude], 14);
    }
  };

  async function searchPlaces(text: string, flyToFirst = false) {
    const q = text.trim();
    if (q.length < 2) return;
    try {
      const response = await fetch(`/places?q=${encodeURIComponent(q)}&lang=${encodeURIComponent(locale)}`);
      const found = response.ok ? ((await response.json()) as Place[]) : [];
      setPlaces(found);
      if (flyToFirst && found[0]) goTo(found[0]);
    } catch {
      setPlaces([]);
    }
  }

  const onSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void searchPlaces(query, true);
  };

  const showOnMap = (item: Item) => {
    const m = map.current;
    if (!m) return;
    m.flyTo([item.lat, item.lng], Math.max(m.getZoom(), 15));
    setTimeout(() => markers.current.get(item.id)?.openPopup(), 600);
  };

  const nearMe = () => {
    if (!map.current) {
      setStatus(labels.failed);
      return;
    }
    if (!("geolocation" in navigator)) {
      setStatus(labels.locationFailed);
      return;
    }
    setStatus(labels.locating);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        map.current?.flyTo([position.coords.latitude, position.coords.longitude], 14);
        setStatus("");
      },
      () => setStatus(labels.locationFailed),
      { enableHighAccuracy: false, timeout: 15000, maximumAge: 300000 },
    );
  };

  return (
    <div className={expanded ? "explorer is-expanded" : "explorer"}>
      <form className="explorer-search" role="search" onSubmit={onSearch}>
        <label htmlFor="place-q" className="visually-hidden">
          {labels.searchLabel}
        </label>
        <input id="place-q" type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder={labels.searchPlaceholder} maxLength={100} autoComplete="off" />
        <button type="submit">{labels.searchButton}</button>
        <button type="button" className="secondary" onClick={nearMe}>
          {labels.nearMe}
        </button>
      </form>
      {places && (
        <div className="place-results">
          {places.length === 0 ? (
            <p className="hint">{labels.noPlaces}</p>
          ) : (
            <ul aria-label={labels.placesTitle}>
              {places.map((p) => (
                <li key={`${p.source}-${p.name}-${p.latitude}-${p.longitude}`}>
                  <button type="button" className="place-button" onClick={() => goTo(p)}>
                    <strong>{p.name}</strong> <span className="muted">{p.detail}</span>{" "}
                    <span className={p.listings > 0 ? "tag" : "tag tag-plain"}>{p.listings > 0 ? fill(labels.placeListings, { count: p.listings }) : labels.placeNoListings}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <div className="pills" role="group" aria-label={labels.modeLabel}>
        {(["land", "rent"] as const).map((m) => (
          <button key={m} type="button" aria-pressed={mode === m} className={mode === m ? "pill active" : "pill"} onClick={() => setMode(m)}>
            {m === "land" ? labels.landMode : labels.rentMode}
          </button>
        ))}
        <button type="button" aria-pressed={newOnly} className={newOnly ? "pill active" : "pill"} onClick={() => setNewOnly(!newOnly)}>
          {labels.newOnly}
        </button>
        <button type="button" className="secondary" onClick={() => setExpanded(!expanded)}>
          {expanded ? labels.collapse : labels.expand}
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

      <div className="explorer-body">
        <div ref={element} className="land-map" role="region" aria-label={mode === "rent" ? labels.mapLabelRent : labels.mapLabel} />
        <div className="explorer-list">
          <p role="status" className="hint">
            {status}
            {truncated && ` ${labels.truncated}`}
          </p>
          {items.length > 0 && (
            <>
              <h2>{mode === "rent" ? labels.homesTitle : labels.listTitle}</h2>
              <ul className="parcel-list">
                {items.map((item) => {
                  const fresh = isNew(item.data.createdAt, now.current);
                  return (
                    <li key={item.id} className="result">
                      <ul className="tags">
                        {item.kind === "land" ? (
                          <li className="tag tag-class">
                            <span aria-hidden="true" className="dot" style={{ background: COLOURS[item.data.landClass] ?? COLOURS.unknown }} />{" "}
                            {labels.classNames[item.data.landClass] ?? item.data.landClass}
                          </li>
                        ) : (
                          <li className="tag tag-class">{labels.homeTypes[item.data.propertyType] ?? item.data.propertyType}</li>
                        )}
                        {fresh && <li className="tag tag-new">{labels.newBadge}</li>}
                      </ul>
                      <h3>
                        {item.kind === "land" ? (
                          <a href={`/${locale}/parcels/${item.id}`}>
                            {item.data.village}, {item.data.mandal}
                          </a>
                        ) : (
                          <a href={`/${locale}/rentals/${item.id}`}>
                            {homeTitle(item.data)}, {item.data.locality}
                          </a>
                        )}
                      </h3>
                      <p className="price">
                        {item.kind === "land" ? (
                          <>
                            {landPrice(item.data)}{" "}
                            <span className="muted">· {item.data.activeOffers === 1 ? labels.offersOne : fill(labels.offers, { count: item.data.activeOffers })}</span>
                          </>
                        ) : (
                          <>
                            {fill(labels.perMonth, { rent: formatRupees(item.data.rentRupees, intl) })}{" "}
                            <span className="muted">· {fill(labels.perSqft, { value: formatRupees(item.data.rentPerSqft, intl) })}</span>
                          </>
                        )}
                      </p>
                      <button type="button" className="link-button" onClick={() => showOnMap(item)}>
                        {labels.showOnMap}
                      </button>
                    </li>
                  );
                })}
              </ul>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
