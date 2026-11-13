"use client";

import { useCallback, useEffect, useMemo, useRef, useState, type FormEvent } from "react";
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
  scrollWheelZoom: { enable(): void; disable(): void };
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
/** Price tags closer than this (px) would overlap, so they are grouped below the closest zoom. */
const TAG_W = 100;
const TAG_H = 32;
const MAX_ZOOM = 18;

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
  perMonthShort: string;
  placeFirst: string;
  placeNone: string;
  clearPlace: string;
};

/** The place chosen in search, by position: names differ by script and spelling, positions do not. */
type Focus = { name: string; lat: number; lng: number; box: [number, number, number, number] | null };

/** Listings within this distance of a place with no area of its own (our villages) count as in it. */
const NEAR_KM = 3;
/** Panning the map centre this far from the chosen place lets go of it. */
const LET_GO_KM = 15;

function km(aLat: number, aLng: number, bLat: number, bLng: number): number {
  const r = (d: number) => (d * Math.PI) / 180;
  const h = Math.sin(r(bLat - aLat) / 2) ** 2 + Math.cos(r(aLat)) * Math.cos(r(bLat)) * Math.sin(r(bLng - aLng) / 2) ** 2;
  return 12742 * Math.asin(Math.sqrt(h));
}

function toFocus(place: Place): Focus {
  const b = place.boundingBox;
  return { name: place.name.split(",")[0]!.trim(), lat: place.latitude, lng: place.longitude, box: b && b.length === 4 ? [b[0]!, b[1]!, b[2]!, b[3]!] : null };
}

/** Inside the place's area (OpenStreetMap gives one), or within NEAR_KM of its centre. */
function inPlace(item: Item, f: Focus): boolean {
  if (f.box) {
    const [s, n, w, e] = f.box;
    if (item.lat >= s && item.lat <= n && item.lng >= w && item.lng <= e) return true;
  }
  return km(item.lat, item.lng, f.lat, f.lng) <= NEAR_KM;
}

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
  /** The pin whose popup is open; reopened after every redraw so panning never closes it. */
  const selected = useRef<string | null>(null);
  const redrawing = useRef(false);
  /** "Show on map" keeps its pin selected through the flight, even if a redraw on the way still groups it. */
  const holdSelectionUntil = useRef(0);
  /** A place chosen before the map finished loading; the map goes there once it is ready. */
  const pendingPlace = useRef<Place | null>(null);
  const placeSearch = useRef(0);
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
  /** The place last chosen from search: its listings lead the list until cleared. */
  const [focus, setFocus] = useState<Focus | null>(null);
  const focusRef = useRef<Focus | null>(null);
  focusRef.current = focus;
  /** Read out by screen readers when "Show on map" moves the map. */
  const [announce, setAnnounce] = useState("");
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
      redrawing.current = true;
      layer.current.clearLayers();
      redrawing.current = false;
      markers.current.clear();
      const zoom = m.getZoom();
      const groups = new Map<string, Item[]>();
      // Pins whose marks would touch are grouped: wide boxes when zoomed out over many pins, otherwise just the
      // size of a price tag, until the closest zoom, where every pin stands alone. Each group sits on its first
      // pin, so two groups are never closer than the box and never cover each other.
      const dense = zoom < CLUSTER_BELOW_ZOOM && list.length > 25;
      const [w, h] = dense ? [CLUSTER_PX + 8, CLUSTER_PX] : [TAG_W, TAG_H];
      const anchors: { x: number; y: number; key: string }[] = [];
      for (const item of list) {
        const pt = m.latLngToContainerPoint([item.lat, item.lng]);
        let key = item.id;
        // At the closest zoom only listings at the very same point (homes share a rounded ~110 m point) stay
        // together, and their group opens a list instead of zooming.
        const [bw, bh] = zoom < MAX_ZOOM ? [w, h] : [4, 4];
        {
          const near = anchors.find((a) => Math.abs(a.x - pt.x) < bw && Math.abs(a.y - pt.y) < bh);
          if (near) key = near.key;
          else anchors.push({ x: pt.x, y: pt.y, key });
        }
        groups.set(key, [...(groups.get(key) ?? []), item]);
      }
      for (const group of groups.values()) {
        if (group.length > 1) {
          const { lat, lng } = group[0]!;
          const bubble = document.createElement("span");
          bubble.className = "cluster-pin";
          bubble.textContent = String(group.length);
          bubble.setAttribute("aria-label", fill(labels.clusterLabel, { count: group.length }));
          const marker = L.marker([lat, lng], { icon: L.divIcon({ html: bubble, className: "pin-wrap", iconSize: null }), keyboard: false, title: fill(labels.clusterLabel, { count: group.length }) });
          const samePoint = group.every((i) => Math.abs(i.lat - lat) < 1e-6 && Math.abs(i.lng - lng) < 1e-6);
          if (samePoint || zoom >= MAX_ZOOM) {
            // Zooming cannot pull these apart: list them all in one popup.
            const box = document.createElement("div");
            box.className = "group-popup";
            for (const i of group) box.append(popupFor(i));
            marker.bindPopup(box);
            for (const i of group) markers.current.set(i.id, marker);
          } else {
            marker.on("click", () => m.flyTo([lat, lng], Math.min(zoom + 2, MAX_ZOOM)));
          }
          marker.addTo(layer.current);
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
            : fill(labels.perMonthShort, { rent: compactRupees(item.data.rentRupees) });
        if (fresh) {
          const badge = document.createElement("b");
          badge.textContent = labels.newBadge;
          tag.prepend(badge);
        }
        // Every pin is also in the list beside the map, which is the keyboard and screen-reader path, so pins
        // stay out of the tab order (up to 500 of them would come before the list).
        const place = item.kind === "land" ? `${item.data.village}, ${item.data.mandal}` : item.data.locality;
        const name = `${place}: ${tag.lastChild?.textContent ?? ""}${fresh ? ` (${labels.newBadge})` : ""}`;
        const marker = L.marker([item.lat, item.lng], { icon: L.divIcon({ html: tag, className: "pin-wrap", iconSize: null }), keyboard: false, title: name })
          .bindPopup(popupFor(item))
          .on("popupopen", () => {
            selected.current = item.id;
          })
          .on("popupclose", () => {
            if (!redrawing.current) selected.current = null;
          })
          .addTo(layer.current);
        markers.current.set(item.id, marker);
      }
      // A selected pin that is now grouped or out of view is let go, so zooming back never pans the map by itself.
      if (selected.current) {
        const again = markers.current.get(selected.current);
        if (again) again.openPopup();
        else if (Date.now() > holdSelectionUntil.current) selected.current = null;
        else {
          // "Show on map" chose a pin that is still inside a group: go closer until it stands alone.
          const chosen = list.find((i) => i.id === selected.current);
          if (chosen && zoom < MAX_ZOOM) {
            holdSelectionUntil.current = Date.now() + 6000;
            m.flyTo([chosen.lat, chosen.lng], Math.min(zoom + 2, MAX_ZOOM));
          }
        }
      }
    },
    [labels, popupFor],
  );

  const load = useCallback(async () => {
    const m = map.current;
    const f = focusRef.current;
    if (m && f && km(m.getCenter().lat, m.getCenter().lng, f.lat, f.lng) > LET_GO_KM) setFocus(null);
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
        const m = L.map(element.current, { scrollWheelZoom: false, zoomControl: true }).setView([start.lat, start.lng], start.zoom);
        L.tileLayer(tiles.url, { maxZoom: 19, attribution: tiles.attribution }).addTo(m);
        layer.current = L.layerGroup().addTo(m);
        map.current = m;
        // The wheel scrolls the page until the map is tapped, so the map never traps scrolling.
        m.on("click", () => m.scrollWheelZoom.enable());
        m.on("mouseout", () => m.scrollWheelZoom.disable());
        m.on("moveend", () => {
          if (timer.current) clearTimeout(timer.current);
          timer.current = setTimeout(() => void loadRef.current(), 250);
        });
        void loadRef.current();
        if (pendingPlace.current) goTo(pendingPlace.current);
        else if (start.q) void searchPlaces(start.q, true);
      })
      .catch(() => {
        // Without the map library, the list still loads for the starting area.
        setStatus(labels.failed);
        // Without the map, a place in the address still chooses the area the list shows.
        if (start.q) void searchPlaces(start.q, true);
        else void loadRef.current();
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
    setFocus(toFocus(place));
    const m = map.current;
    if (!m) {
      // Map still loading: go there when it is ready. Map failed: the list shows the area around the place.
      pendingPlace.current = place;
      const d = 0.15;
      fallbackArea.current = { south: place.latitude - d, west: place.longitude - d, north: place.latitude + d, east: place.longitude + d };
      void loadRef.current();
      return;
    }
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
    // Only the newest search may move the map.
    const ticket = ++placeSearch.current;
    try {
      const response = await fetch(`/places?q=${encodeURIComponent(q)}&lang=${encodeURIComponent(locale)}`);
      const found = response.ok ? ((await response.json()) as Place[]) : [];
      if (ticket !== placeSearch.current) return;
      setPlaces(found);
      if (found.length === 0) setFocus(null);
      if (flyToFirst && found[0]) goTo(found[0]);
    } catch {
      if (ticket === placeSearch.current) setPlaces([]);
    }
  }

  const onSearch = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void searchPlaces(query, true);
  };

  // Listings in the chosen place first, then the rest in view, each keeping the order the server gave.
  const inFocus = useMemo(() => (focus ? items.filter((i) => inPlace(i, focus)) : []), [focus, items]);
  const listed = useMemo(() => (focus ? [...inFocus, ...items.filter((i) => !inPlace(i, focus))] : items), [focus, inFocus, items]);

  const showOnMap = (item: Item) => {
    const m = map.current;
    if (!m) return;
    // The redraw after the flight reopens this pin's popup (at zoom 15 it is never inside a group).
    selected.current = item.id;
    setAnnounce(`${labels.showOnMap}: ${item.kind === "land" ? `${item.data.village}, ${item.data.mandal}` : `${homeTitle(item.data)}, ${item.data.locality}`}`);
    holdSelectionUntil.current = Date.now() + 6000;
    markers.current.get(item.id)?.openPopup();
    m.flyTo([item.lat, item.lng], Math.max(m.getZoom(), 15));
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
        <button type="button" className="secondary" aria-expanded={expanded} onClick={() => setExpanded(!expanded)}>
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
          <p className="visually-hidden" aria-live="polite">
            {announce}
          </p>
          <p role="status" className="hint">
            {status}
            {truncated && ` ${labels.truncated}`}
          </p>
          {focus && (
            <p className="place-focus" aria-live="polite">
              <span>{inFocus.length > 0 ? fill(labels.placeFirst, { count: inFocus.length, place: focus.name }) : fill(labels.placeNone, { place: focus.name })}</span>{" "}
              <button type="button" className="link-button" onClick={() => setFocus(null)}>
                {labels.clearPlace}
              </button>
            </p>
          )}
          {items.length > 0 && (
            <>
              <h2>{mode === "rent" ? labels.homesTitle : labels.listTitle}</h2>
              <ul className="parcel-list">
                {listed.map((item) => {
                  const fresh = isNew(item.data.createdAt, now.current);
                  return (
                    <li key={item.id} className={focus && inPlace(item, focus) ? "result in-place" : "result"}>
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
