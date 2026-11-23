/**
 * A name reduced to what a search should compare: lower case, diacritics
 * removed, every run of punctuation and whitespace collapsed to one space.
 *
 * The list spells names the way the legal act does — `Aleksandr`, `Łukašenka`,
 * `AL-TIKRITI`, `“Aqua Fleet Management”` — while the name being checked comes
 * from a contract or an invoice. `LIKE` already ignores case; this also makes
 * `lukasenka`, `al tikriti` and `aqua fleet management` match. It does not
 * transliterate between scripts and does not guess at spelling variants: those
 * are what the list's own name variants are for, and one row exists per
 * variant.
 *
 * The same function is what a future list's rows must use, so one search
 * string means the same thing against every list.
 */
export function normalizeName(name: string): string {
  return name
    .normalize('NFKD')
    .replace(/\p{M}+/gu, '')
    .toLowerCase()
    .replace(/[łøđħıßæœþ]/g, (letter) => UNDECOMPOSABLE[letter] ?? letter)
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim();
}

/**
 * Latin letters whose accent is part of the letter itself, so NFKD leaves
 * them whole — `Łukašenka` would otherwise normalize to `łukasenka`.
 */
const UNDECOMPOSABLE: Record<string, string> = {
  ł: 'l',
  ø: 'o',
  đ: 'd',
  ħ: 'h',
  ı: 'i',
  ß: 'ss',
  æ: 'ae',
  œ: 'oe',
  þ: 'th',
};
