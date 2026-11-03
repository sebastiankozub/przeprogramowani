#!/usr/bin/env node
// Seeds 20 manual-test accounts through the PUBLIC API (no DB access, no demo code in the app):
//   5x zachodniopomorskie, 5x wielkopolskie, 5x małopolskie, 5x without a city.
// Movie overlap is shaped against the reference account (sebastiankozub@o2.pl):
//   favorites (rated >= 7): Casablanca (1942), Chłopcy z ferajny (1990)
//   watchlist:              Operacja Piorun (1965), Smerfy (2011)
//   rated < 7:              Siedem (1995), Halloween (1978)  — never a shared favorite
// Each voivodeship gets one user per overlap tier (shuffled with a fixed seed), so the reference user
// sees strong matches, watchlist-only matches and zero-overlap top-up neighbours in every region.
// Users without a city get strong overlap but must NOT appear on anyone's list (city required).
//
// Usage:
//   TEST_PASSWORD='...' node scripts/seed-test-users.mjs [--recompute]
//   BASE_URL=http://localhost:5069 TEST_PASSWORD='...' node scripts/seed-test-users.mjs
// --recompute: also press "Szukaj dopasowań" for every seeded user with a city (mirrors rows onto the
//              reference user's list without them clicking). Off by default so the flow can be tested by hand.
// Idempotent: re-running logs in to existing accounts and re-applies ratings.
// Remove everything with scripts/cleanup-test-users.sql. The password is never printed or stored.

const BASE_URL = (process.env.BASE_URL ?? 'https://moviedate-sk.fly.dev').replace(/\/$/, '');
const PASSWORD = process.env.TEST_PASSWORD;
const RECOMPUTE = process.argv.includes('--recompute');
const EMAIL_PREFIX = 'mdtest-';
const EMAIL_DOMAIN = '@moviedate.test';

if (!PASSWORD || PASSWORD.length < 8) {
  console.error('TEST_PASSWORD env var is required (min 8 characters).');
  process.exit(2);
}

// Deterministic PRNG so a re-run produces the same profiles.
function mulberry32(seed) {
  return () => {
    seed |= 0;
    seed = (seed + 0x6d2b79f5) | 0;
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
const rand = mulberry32(20261103);
const int = (min, max) => min + Math.floor(rand() * (max - min + 1));
const pick = (arr) => arr[Math.floor(rand() * arr.length)];
const shuffle = (arr) => {
  const a = [...arr];
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(rand() * (i + 1));
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
};
const sample = (arr, n) => shuffle(arr).slice(0, n);

const M = {
  casablanca: { title: 'Casablanca', year: 1942 },
  goodfellas: { title: 'Chłopcy z ferajny', year: 1990 },
  seven: { title: 'Siedem', year: 1995 },
  halloween: { title: 'Halloween', year: 1978 },
  thunderball: { title: 'Operacja Piorun', year: 1965 },
  smurfs: { title: 'Smerfy', year: 2011 },
};
const REF_FAVORITES = [M.casablanca, M.goodfellas];
const REF_WATCHLIST = [M.thunderball, M.smurfs];
const FILLER = [
  { title: 'Titanic', year: 1997 },
  { title: 'Ojciec chrzestny', year: 1972 },
  { title: 'Avatar', year: 2009 },
  { title: 'Gladiator', year: 2000 },
  { title: 'Powrót do przyszłości', year: 1985 },
  { title: 'Jurassic Park', year: 1993 },
  { title: 'Incepcja', year: 2010 },
  { title: 'Bękarty wojny', year: 2009 },
  { title: 'Pianista', year: 2002 },
  { title: 'Joker', year: 2019 },
];

// Overlap tiers vs. the reference user. "strong" = at least one shared favorite or shared watchlist movie.
const TIERS = {
  'two-favorites': () => ({
    watched: REF_FAVORITES.map((m) => [m, int(7, 10)]),
    watchlist: sample(REF_WATCHLIST, 1),
  }),
  'one-favorite': () => ({ watched: [[pick(REF_FAVORITES), int(7, 10)]], watchlist: [] }),
  'watchlist-only': () => ({ watched: [], watchlist: sample(REF_WATCHLIST, int(1, 2)) }),
  // Same movies, but no shared favorite: they rate Casablanca low, and the reference rated Siedem 6.
  'low-ratings': () => ({ watched: [[M.casablanca, int(3, 6)], [M.seven, int(8, 10)]], watchlist: [] }),
  none: () => ({ watched: [], watchlist: [] }),
};

const REGIONS = [
  { code: 'zp', voivodeship: 'zachodniopomorskie', cities: ['Szczecin', 'Koszalin', 'Stargard', 'Kołobrzeg', 'Świnoujście'],
    names: ['Ania', 'Bartek', 'Celina', 'Damian', 'Ewa'] },
  { code: 'wlkp', voivodeship: 'wielkopolskie', cities: ['Poznań', 'Kalisz', 'Piła', 'Konin', 'Gniezno'],
    names: ['Filip', 'Gosia', 'Henryk', 'Iza', 'Jacek'] },
  { code: 'mlp', voivodeship: 'małopolskie', cities: ['Kraków', 'Tarnów', 'Nowy Sącz', 'Oświęcim', 'Bochnia'],
    names: ['Kasia', 'Leszek', 'Magda', 'Norbert', 'Ola'] },
  { code: 'nocity', voivodeship: null, cities: [null, null, null, null, null],
    names: ['Paweł', 'Renata', 'Szymon', 'Tamara', 'Wiktor'] },
];

function buildUsers() {
  const users = [];
  for (const region of REGIONS) {
    const tiers = region.voivodeship ? shuffle(Object.keys(TIERS)) : Array(5).fill('two-favorites');
    region.names.forEach((name, i) => {
      const tier = tiers[i];
      const shaped = TIERS[tier]();
      const used = new Set([...shaped.watched.map(([m]) => m.title), ...shaped.watchlist.map((m) => m.title)]);
      const fillers = sample(FILLER.filter((m) => !used.has(m.title)), int(2, 4));
      const watched = [...shaped.watched, ...fillers.slice(0, -1).map((m) => [m, int(1, 10)])];
      const watchlist = [...shaped.watchlist, fillers.at(-1)];
      users.push({
        email: `${EMAIL_PREFIX}${region.code}-${i + 1}${EMAIL_DOMAIN}`,
        displayName: `${name} (test)`,
        city: region.cities[i],
        voivodeship: region.voivodeship,
        tier,
        watched,
        watchlist,
      });
    });
  }
  return users;
}

class Session {
  cookies = new Map();

  async req(method, path, body, okStatuses = [200, 201, 204]) {
    const res = await fetch(`${BASE_URL}${path}`, {
      method,
      headers: {
        ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
        ...(this.cookies.size ? { Cookie: [...this.cookies].map(([k, v]) => `${k}=${v}`).join('; ') } : {}),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    for (const c of res.headers.getSetCookie()) {
      const [pair] = c.split(';');
      const eq = pair.indexOf('=');
      this.cookies.set(pair.slice(0, eq), pair.slice(eq + 1));
    }
    const text = await res.text();
    if (!okStatuses.includes(res.status)) {
      throw new Error(`${method} ${path} -> ${res.status} ${text.slice(0, 300)}`);
    }
    return { status: res.status, json: text ? safeJson(text) : null };
  }
}
const safeJson = (t) => { try { return JSON.parse(t); } catch { return null; } };

const movieCache = new Map();
async function resolveMovie(s, m) {
  const key = `${m.title}|${m.year}`;
  if (!movieCache.has(key)) {
    const { json } = await s.req('GET', `/api/movies?q=${encodeURIComponent(m.title)}`);
    const hit = json.find((x) => x.title === m.title && x.year === m.year);
    if (!hit) throw new Error(`Movie not found in catalog: ${m.title} (${m.year})`);
    movieCache.set(key, hit.id);
  }
  return movieCache.get(key);
}

async function resolveCity(s, name, voivodeship) {
  const { json } = await s.req('GET', `/api/cities?q=${encodeURIComponent(name)}`);
  const hit = json.find((c) => c.name === name && c.voivodeship === voivodeship);
  if (!hit) throw new Error(`City not found: ${name} (${voivodeship})`);
  return hit.id;
}

async function seedUser(u) {
  const s = new Session();
  const reg = await s.req('POST', '/api/auth/register', { email: u.email, password: PASSWORD }, [200, 400]);
  const created = reg.status === 200;
  if (!created && !JSON.stringify(reg.json).includes('DuplicateUserName')) {
    throw new Error(`register ${u.email} -> 400 ${JSON.stringify(reg.json)}`);
  }
  await s.req('POST', '/api/auth/login?useCookies=true', { email: u.email, password: PASSWORD });
  await s.req('PUT', '/api/profile/display-name', { displayName: u.displayName });
  if (u.city) {
    await s.req('PUT', '/api/profile/city', { cityId: await resolveCity(s, u.city, u.voivodeship) });
  }
  for (const [m, rating] of u.watched) {
    const movieId = await resolveMovie(s, m);
    const r = await s.req('POST', '/api/watched', { movieId, rating }, [201, 409]);
    if (r.status === 409) await s.req('PUT', `/api/watched/${movieId}`, { rating });
  }
  for (const m of u.watchlist) {
    await s.req('POST', '/api/watchlist', { movieId: await resolveMovie(s, m) }, [201, 409]);
  }
  if (RECOMPUTE && u.city) await s.req('POST', '/api/matches/recompute');
  return created;
}

const users = buildUsers();
console.log(`Seeding ${users.length} test users on ${BASE_URL}${RECOMPUTE ? ' (with recompute)' : ''}...`);
const rows = [];
for (const u of users) {
  const created = await seedUser(u);
  const titles = (list) => list.map((m) => m.title);
  const sharedFav = u.watched.filter(([m, r]) => r >= 7 && REF_FAVORITES.includes(m)).map(([m]) => m.title);
  const sharedWl = titles(u.watchlist.filter((m) => REF_WATCHLIST.includes(m)));
  rows.push({
    email: u.email,
    name: u.displayName,
    city: u.city ?? '— (brak)',
    tier: u.tier,
    sharedFav: sharedFav.join(', ') || '—',
    sharedWl: sharedWl.join(', ') || '—',
    new: created ? 'yes' : 'existed',
  });
  process.stdout.write('.');
}
console.log('\n');
console.table(rows);
console.log('Password: the TEST_PASSWORD value you passed (same for all accounts).');
