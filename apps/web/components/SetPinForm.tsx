"use client";

import { useState } from "react";
import { setPinAction } from "@/app/[locale]/actions";

type Labels = {
  title: string;
  help: string;
  useLocation: string;
  locating: string;
  latitude: string;
  longitude: string;
  save: string;
  denied: string;
  unavailable: string;
  onLand: string;
};

/**
 * Pin the land on the map. The easy way for villagers: stand on the land and tap "Use my current location".
 * Coordinates can also be typed (for example copied from Google Maps).
 */
export function SetPinForm({ locale, parcelId, labels }: { locale: string; parcelId: string; labels: Labels }) {
  const [latitude, setLatitude] = useState("");
  const [longitude, setLongitude] = useState("");
  const [source, setSource] = useState<"gps" | "map">("map");
  const [status, setStatus] = useState<string>("");
  const [onLand, setOnLand] = useState(false);

  const locate = () => {
    if (!("geolocation" in navigator)) {
      setStatus(labels.unavailable);
      return;
    }
    setStatus(labels.locating);
    navigator.geolocation.getCurrentPosition(
      (position) => {
        setLatitude(position.coords.latitude.toFixed(6));
        setLongitude(position.coords.longitude.toFixed(6));
        setSource("gps");
        setStatus("");
      },
      (error) => setStatus(error.code === error.PERMISSION_DENIED ? labels.denied : labels.unavailable),
      { enableHighAccuracy: true, timeout: 20000, maximumAge: 0 },
    );
  };

  return (
    <form action={setPinAction} className="card form">
      <h3>{labels.title}</h3>
      <p className="hint">{labels.help}</p>
      <input type="hidden" name="locale" value={locale} />
      <input type="hidden" name="parcelId" value={parcelId} />
      {/* A phone location only counts as "from the land" when the person confirms they are standing on it now. */}
      <input type="hidden" name="source" value={source === "gps" && onLand ? "gps" : "map"} />
      <label className="choice">
        <input type="checkbox" checked={onLand} onChange={(e) => setOnLand(e.target.checked)} /> {labels.onLand}
      </label>
      <button type="button" className="secondary" onClick={locate} disabled={!onLand}>
        {labels.useLocation}
      </button>
      <p role="status" className="hint">
        {status}
      </p>
      <div className="field">
        <label htmlFor="pin-lat">{labels.latitude}</label>
        <input id="pin-lat" name="latitude" inputMode="decimal" value={latitude} onChange={(e) => { setLatitude(e.target.value); setSource("map"); }} />
      </div>
      <div className="field">
        <label htmlFor="pin-lng">{labels.longitude}</label>
        <input id="pin-lng" name="longitude" inputMode="decimal" value={longitude} onChange={(e) => { setLongitude(e.target.value); setSource("map"); }} />
      </div>
      <button type="submit">{labels.save}</button>
    </form>
  );
}
