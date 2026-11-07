// WhatsApp click-to-chat links. Only Indian mobile numbers in +91 form are accepted, so a bad value
// from anywhere can never turn into a link to some other site or number format.

const INDIAN_MOBILE = /^\+91[6-9]\d{9}$/;

/** https://wa.me link with a first message already typed, or null when the number is not an Indian mobile. */
export function whatsappLink(e164: string, message: string): string | null {
  if (!INDIAN_MOBILE.test(e164)) return null;
  return `https://wa.me/${e164.slice(1)}?text=${encodeURIComponent(message)}`;
}

/** "+91 98480 12345" for display. */
export function formatIndianMobile(e164: string): string {
  return INDIAN_MOBILE.test(e164) ? `+91 ${e164.slice(3, 8)} ${e164.slice(8)}` : e164;
}

/** File size in KB or MB, with the reader's digits. */
export function formatFileSize(bytes: number, intl: string): string {
  const mb = bytes / (1024 * 1024);
  const nf = new Intl.NumberFormat(intl, { maximumFractionDigits: 1 });
  return mb >= 1 ? `${nf.format(mb)} MB` : `${nf.format(Math.max(1, Math.round(bytes / 1024)))} KB`;
}
