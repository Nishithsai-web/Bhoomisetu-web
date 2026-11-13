import { notFound } from "next/navigation";
import { MapSearch, type MapStart } from "@/components/MapSearch";
import { INTL_LOCALE, getDictionary, isLocale } from "@/lib/i18n";

type Search = { mode?: string; q?: string; lat?: string; lng?: string; z?: string; landClass?: string; bedrooms?: string; new?: string };

const num = (value: string | undefined, min: number, max: number, fallback: number) => {
  const n = Number(value);
  return value !== undefined && value !== "" && Number.isFinite(n) && n >= min && n <= max ? n : fallback;
};

/**
 * Explore land and homes on a live map: search any place, price tags on pins, new listings marked, filters.
 * The view and filters come from the address, so a shared link opens the same view. Bounds are India's.
 */
export default async function MapPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Search> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  const sp = await searchParams;
  const start: MapStart = {
    mode: sp.mode === "rent" ? "rent" : "land",
    lat: num(sp.lat, 6, 38, 17.385),
    lng: num(sp.lng, 68, 98, 78.4867),
    zoom: Math.round(num(sp.z, 5, 18, 11)),
    landClass: ["agricultural", "non_agricultural", "converted"].includes(sp.landClass ?? "") ? (sp.landClass as string) : "",
    bedrooms: Math.round(num(sp.bedrooms, 0, 4, 0)),
    newOnly: sp.new === "1",
    q: (sp.q ?? "").slice(0, 100),
  };
  // A licensed tile provider is set by environment before launch; OpenStreetMap's own tiles are for light use.
  const tiles = {
    url: process.env.BHOOMISETU_MAP_TILES_URL || "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
    attribution: process.env.BHOOMISETU_MAP_TILES_ATTRIBUTION || "&copy; OpenStreetMap contributors",
  };
  const t = getDictionary(locale);
  const m = t.landMap;

  return (
    <section className="page">
      <h1>{m.title}</h1>
      <p className="lead">{m.intro}</p>
      <MapSearch
        locale={locale}
        intl={INTL_LOCALE[locale]}
        start={start}
        tiles={tiles}
        labels={{
          mapLabel: m.mapLabel,
          nearMe: m.nearMe,
          locating: m.locating,
          locationFailed: m.locationFailed,
          loading: m.loading,
          failed: m.failed,
          dataFailed: m.dataFailed,
          noneRent: m.noneRent,
          countRent: m.countRent,
          mapLabelRent: m.mapLabelRent,
          none: m.none,
          truncated: m.truncated,
          count: m.count,
          filterLabel: t.parcels.filterLabel,
          all: t.parcels.filterAll,
          types: { agricultural: t.home.types.agricultural.title, non_agricultural: t.home.types.non_agricultural.title, converted: t.home.types.converted.title },
          classNames: t.sell.landClasses as Record<string, string>,
          fromPrice: t.parcels.fromPrice,
          noPrice: t.parcels.noPrice,
          offers: t.parcels.offers,
          offersOne: t.parcels.offersOne,
          survey: t.parcels.survey,
          open: t.parcels.viewLand,
          listTitle: m.listTitle,
          amounts: t.amounts,
          modeLabel: m.modeLabel,
          landMode: m.landMode,
          rentMode: m.rentMode,
          bedroomsLabel: m.bedroomsLabel,
          anyBedrooms: m.anyBedrooms,
          homeHeadline: t.rent.headline,
          homeTypes: t.rent.types as Record<string, string>,
          perMonth: t.rent.perMonth,
          perSqft: t.rent.perSqft,
          homesTitle: m.homesTitle,
          rentOut: t.rent.rentOut,
          searchLabel: m.searchLabel,
          searchPlaceholder: m.searchPlaceholder,
          searchButton: m.searchButton,
          placesTitle: m.placesTitle,
          placeListings: m.placeListings,
          placeNoListings: m.placeNoListings,
          noPlaces: m.noPlaces,
          newOnly: m.newOnly,
          newBadge: m.newBadge,
          showOnMap: m.showOnMap,
          expand: m.expand,
          collapse: m.collapse,
          clusterLabel: m.clusterLabel,
          perMonthShort: m.perMonthShort,
          levelMandal: m.levelMandal,
          levelDistrict: m.levelDistrict,
          clusterListLabel: m.clusterListLabel,
          placeFirst: m.placeFirst,
          placeNone: m.placeNone,
          clearPlace: m.clearPlace,
        }}
      />
      <p className="hint">{m.note}</p>
    </section>
  );
}
