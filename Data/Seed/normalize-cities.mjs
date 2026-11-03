// Normalizes the Wikidata SPARQL JSON result (Data/Seed/cities.sparql) into Data/Seed/cities.json.
// Not part of the app build — run by hand when the city catalog is regenerated:
//   curl -G https://query.wikidata.org/sparql --data-urlencode query@Data/Seed/cities.sparql \
//     -H "Accept: application/sparql-results+json" -H "User-Agent: MovieDate-seed/1.0" -o result.json
//   node Data/Seed/normalize-cities.mjs result.json Data/Seed/cities.json
// One row per QID; rows without a Polish label, coordinates or a current voivodeship are dropped.
// Population = the latest-dated P1082 value (preferred rank wins a tie; undated values only when no dated
// one exists). Villages (no town rights) are kept only when that population is >= 10,000.
import fs from 'node:fs';

// Manual corrections for upstream Wikidata errors, applied after normalization (keyed by QID).
// `null` drops the row (an entity that still matches the query but is not a current town).
const CORRECTIONS = {
  // Medieval town merged into Wrocław in the 13th century, still typed as "town" without an end date.
  Q9378128: null,
  // Village (gmina seat) without town rights, upstream also typed as "town".
  Q745635: null,
};

const VILLAGE_MIN_POPULATION = 10000;

const [, , input, output] = process.argv;
if (!input || !output) {
  console.error('Usage: node normalize-cities.mjs <sparql-result.json> <cities.json>');
  process.exit(1);
}
const data = JSON.parse(fs.readFileSync(input, 'utf8'));

const v = (b, k) => (b[k] && b[k].value ? b[k].value.trim() : null) || null;
// Wikidata Polish labels sometimes carry a Wikipedia-style disambiguator, e.g. "Brzeg (miasto)".
const clean = (s) => (s ? s.replace(/\s*\([^)]*\)\s*$/, '').trim() || null : null);
const round = (x) => Math.round(x * 1e5) / 1e5;

// "Point(lon lat)" -> { latitude, longitude }
function parseCoords(s) {
  const m = s && /^Point\(\s*(-?[\d.]+)\s+(-?[\d.]+)\s*\)$/.exec(s);
  return m ? { latitude: round(parseFloat(m[2])), longitude: round(parseFloat(m[1])) } : null;
}

// "województwo mazowieckie" -> "mazowieckie"
const voivodeshipName = (label) => label.replace(/^województwo\s+/i, '').trim().toLowerCase();

// "value|date|rank" joined by "," -> latest-dated value (preferred rank wins a tie)
function pickPopulation(raw) {
  if (!raw) return null;
  const items = raw
    .split(',')
    .map((s) => {
      const [value, date, rank] = s.split('|');
      return { value: Math.round(parseFloat(value)), date: date || '', preferred: rank === 'PreferredRank' };
    })
    .filter((x) => Number.isFinite(x.value) && x.value > 0);
  if (!items.length) return null;
  items.sort(
    (a, b) => b.date.localeCompare(a.date) || Number(b.preferred) - Number(a.preferred) || b.value - a.value,
  );
  return items[0].value;
}

const byId = new Map();
const dropped = { noName: 0, noCoords: 0, smallVillage: 0, corrections: 0 };
const multiVoivodeship = [];
for (const b of data.results.bindings) {
  const id = v(b, 'item').split('/').pop();
  const name = clean(v(b, 'name'));
  const coords = parseCoords(v(b, 'coords'));
  const voivodeships = [...new Set((v(b, 'voivodeships') || '').split(',').filter(Boolean).map(voivodeshipName))];
  const population = pickPopulation(v(b, 'populations'));
  const isTown = v(b, 'kind') === 'town';

  if (!name) { dropped.noName++; continue; }
  if (!coords) { dropped.noCoords++; continue; }
  if (!voivodeships.length) continue;
  if (voivodeships.length > 1) multiVoivodeship.push(`${id} ${name}: ${voivodeships.join('/')}`);

  const prev = byId.get(id);
  // An item can match as both a town and a village; town rights win.
  if (prev && (prev.isTown || !isTown)) continue;
  byId.set(id, { isTown, row: { wikidataId: id, name, voivodeship: voivodeships[0], ...coords, population } });
}

const out = [];
for (const { isTown, row } of byId.values()) {
  if (!isTown && (row.population ?? 0) < VILLAGE_MIN_POPULATION) { dropped.smallVillage++; continue; }
  if (CORRECTIONS[row.wikidataId] === null) { dropped.corrections++; continue; }
  out.push({ ...row, ...CORRECTIONS[row.wikidataId] });
}
out.sort((a, b) => a.name.localeCompare(b.name, 'pl') || a.wikidataId.localeCompare(b.wikidataId));

fs.writeFileSync(output, JSON.stringify(out, null, 2) + '\n', 'utf8');
console.log(
  `rows=${data.results.bindings.length} written=${out.length} villages=${out.filter((r) => !byId.get(r.wikidataId).isTown).length}` +
    ` noPopulation=${out.filter((r) => r.population == null).length} dropped=${JSON.stringify(dropped)}`,
);
if (multiVoivodeship.length) console.warn(`multiple voivodeships (first kept):\n  ${multiVoivodeship.join('\n  ')}`);
