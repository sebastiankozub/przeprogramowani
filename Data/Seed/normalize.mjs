// Normalizes the Wikidata SPARQL JSON result (Data/Seed/movies.sparql) into Data/Seed/movies.json.
// Not part of the app build — run by hand when the catalog is regenerated:
//   curl -G https://query.wikidata.org/sparql --data-urlencode query@Data/Seed/movies.sparql \
//     -H "Accept: application/sparql-results+json" -H "User-Agent: MovieDate-seed/1.0" -o result.json
//   node Data/Seed/normalize.mjs result.json Data/Seed/movies.json
import fs from 'node:fs';

// Manual corrections for upstream Wikidata errors, applied after normalization (keyed by QID).
const CORRECTIONS = {
  // Vandalised original title ("…Phantom Menace9999").
  Q165713: { originalTitle: 'Star Wars: Episode I – The Phantom Menace' },
};

const [, , input, output] = process.argv;
if (!input || !output) {
  console.error('Usage: node normalize.mjs <sparql-result.json> <movies.json>');
  process.exit(1);
}
const data = JSON.parse(fs.readFileSync(input, 'utf8'));

const v = (b, k) => (b[k] && b[k].value ? b[k].value.trim() : null) || null;
// Wikidata Polish labels sometimes carry a Wikipedia-style disambiguator, e.g. "Titanic (film 1997)".
const clean = (s) => (s ? s.replace(/\s*\((film|serial|film animowany)[^)]*\)\s*$/i, '').trim() || null : null);

// P1476 may hold several titles ("lang|title" joined by U+001F): prefer the film's original language, then English.
function pickOriginal(b) {
  const raw = v(b, 'originalTitles');
  if (!raw) return null;
  const items = raw
    .split('\u001F')
    .map((s) => {
      const i = s.indexOf('|');
      return { lang: s.slice(0, i), text: s.slice(i + 1).trim() };
    })
    .filter((x) => x.text);
  if (!items.length) return null;
  const langs = (v(b, 'originalLangs') || '').split(',').filter(Boolean);
  for (const l of [...langs, 'en']) {
    const hit = items.find((x) => x.lang === l);
    if (hit) return hit.text;
  }
  return items[0].text;
}

const seen = new Set();
const out = [];
let pl = 0;
let en = 0;
for (const b of data.results.bindings) {
  const id = v(b, 'film').split('/').pop();
  if (seen.has(id)) continue;
  const plT = clean(v(b, 'pl'));
  const enT = clean(v(b, 'en'));
  const title = plT || enT;
  if (!title) continue;
  seen.add(id);
  if (plT) pl++;
  else en++;
  const year = v(b, 'year');
  out.push({
    wikidataId: id,
    title,
    originalTitle: pickOriginal(b),
    year: year ? parseInt(year, 10) : null,
    ...CORRECTIONS[id],
  });
}

fs.writeFileSync(output, JSON.stringify(out, null, 2) + '\n', 'utf8');
console.log(`rows=${data.results.bindings.length} written=${out.length} polish=${pl} englishFallback=${en}`);
