import { describePoint, sortPoints, type Point } from "@/lib/compare";

type Props = {
  points: Point[];
  texts: Record<string, string>;
  unit: string;
  kinds: Record<string, string>;
  furnishing: Record<string, string>;
  words: { pro: string; con: string; info: string };
};

/** Pros, cons and neutral facts; each line starts with a word (not only a colour or sign). */
export function PointsList({ points, texts, unit, kinds, furnishing, words }: Props) {
  if (points.length === 0) return null;
  return (
    <ul className="points">
      {sortPoints(points).map((p) => (
        <li key={p.code} className={`point point-${p.kind}`}>
          <span className="point-word">{words[p.kind]}</span> {describePoint(p, texts, { unit, kinds, furnishing })}
        </li>
      ))}
    </ul>
  );
}
