import { notFound } from "next/navigation";
import { MapSearch } from "@/components/MapSearch";
import { INTL_LOCALE, getDictionary, isLocale } from "@/lib/i18n";

/** Find land on a map: every pinned listing in view, with its lowest price, filters and "near me". */
export default async function MapPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
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
        }}
      />
      <p className="hint">{m.note}</p>
    </section>
  );
}
