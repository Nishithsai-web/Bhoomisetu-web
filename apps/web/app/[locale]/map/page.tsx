import { notFound } from "next/navigation";
import { MapSearch } from "@/components/MapSearch";
import { INTL_LOCALE, getDictionary, isLocale } from "@/lib/i18n";

/** Find land on a map: every pinned listing in view, with its lowest price, filters and "near me". */
export default async function MapPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<{ mode?: string }> }) {
  const { locale } = await params;
  const initialMode = (await searchParams).mode === "rent" ? "rent" : "land";
  if (!isLocale(locale)) notFound();
  const t = getDictionary(locale);
  const m = t.landMap;

  return (
    <section className="page">
      <h1>{m.title}</h1>
      <p className="lead">{m.intro}</p>
      <MapSearch
        locale={locale}
        intl={INTL_LOCALE[locale]}
        initialMode={initialMode}
        labels={{
          mapLabel: m.mapLabel,
          nearMe: m.nearMe,
          locating: m.locating,
          locationFailed: m.locationFailed,
          loading: m.loading,
          failed: m.failed,
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
        }}
      />
      <p className="hint">{m.note}</p>
    </section>
  );
}
