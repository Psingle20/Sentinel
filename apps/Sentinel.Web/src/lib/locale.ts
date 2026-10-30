// The product's display locale, in one place: times, dates and figures are formatted the same on the server and in
// the browser, so a page never renders one way and hydrates another. Components and features take it from here.
export const displayLocale = "en-GB";

const integerFormat = new Intl.NumberFormat(displayLocale);

/** A whole number with the locale's grouping, for example a token count. */
export function formatInteger(value: number): string {
  return integerFormat.format(value);
}
