import Link from "next/link";

// Rendered when a page inside a valid language calls notFound() (for example an unknown parcel id).
// Unknown languages never reach the layout because dynamicParams is false. Text is in all three languages.
export default function NotFound() {
  return (
    <section className="page">
      <h1>404</h1>
      <p lang="en">This page does not exist.</p>
      <p lang="te">ఈ పేజీ లేదు.</p>
      <p lang="hi">यह पेज मौजूद नहीं है।</p>
      <p>
        <Link href="/en">English</Link> · <Link href="/te">తెలుగు</Link> · <Link href="/hi">हिंदी</Link>
      </p>
    </section>
  );
}
