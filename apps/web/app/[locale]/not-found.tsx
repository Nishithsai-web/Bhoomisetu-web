import Link from "next/link";

// Rendered for unknown locales and unknown pages. Kept language-neutral because the locale itself may be invalid.
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
