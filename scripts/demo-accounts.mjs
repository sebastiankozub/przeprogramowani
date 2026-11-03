#!/usr/bin/env node
// Creates 10 permanent demo accounts (demo-01..10@moviedate.test) through the PUBLIC API, so a fresh real user
// in production gets more than one ranked candidate (S-05). No DB access, no demo code inside the app.
// Profiles are hard-coded: varied Polish cities and overlapping popular titles, rated on both sides of 7
// (>= 7 counts as a shared favorite), plus watchlists that overlap too.
// Since S-06 every write schedules a background match recompute, so demo accounts appear on each other's lists
// (and on a new user's list) without any extra call.
//
// Usage:
//   DEMO_PASSWORD='...' node scripts/demo-accounts.mjs                              # local (http://localhost:5069)
//   BASE_URL=https://moviedate-sk.fly.dev DEMO_PASSWORD='...' node scripts/demo-accounts.mjs
// Idempotent: re-running logs in to existing accounts, re-applies name/city/ratings, tolerates duplicates (409).
// Exits non-zero on any unexpected status. The password is never printed or stored.

const BASE_URL = (process.env.BASE_URL ?? 'http://localhost:5069').replace(/\/$/, '');
const PASSWORD = process.env.DEMO_PASSWORD;

if (!PASSWORD || PASSWORD.length < 8) {
  console.error('DEMO_PASSWORD env var is required (min 8 characters).');
  process.exit(2);
}

const M = {
  godfather: { title: 'Ojciec chrzestny', year: 1972 },
  titanic: { title: 'Titanic', year: 1997 },
  gladiator: { title: 'Gladiator', year: 2000 },
  inception: { title: 'Incepcja', year: 2010 },
  pianist: { title: 'Pianista', year: 2002 },
  joker: { title: 'Joker', year: 2019 },
  avatar: { title: 'Avatar', year: 2009 },
  jurassic: { title: 'Jurassic Park', year: 1993 },
  backToFuture: { title: 'Powrót do przyszłości', year: 1985 },
  basterds: { title: 'Bękarty wojny', year: 2009 },
  casablanca: { title: 'Casablanca', year: 1942 },
  goodfellas: { title: 'Chłopcy z ferajny', year: 1990 },
  seven: { title: 'Siedem', year: 1995 },
};

// [email suffix, display name, city, voivodeship, watched [[movie, rating]], watchlist [movie]]
const ACCOUNTS = [
  ['01', 'Marta (demo)', 'Kraków', 'małopolskie',
    [[M.godfather, 9], [M.pianist, 8], [M.inception, 7], [M.titanic, 4]], [M.casablanca, M.joker]],
  ['02', 'Tomek (demo)', 'Kraków', 'małopolskie',
    [[M.godfather, 10], [M.gladiator, 8], [M.jurassic, 6]], [M.inception, M.basterds]],
  ['03', 'Julia (demo)', 'Warszawa', 'mazowieckie',
    [[M.titanic, 9], [M.avatar, 7], [M.pianist, 9], [M.seven, 5]], [M.godfather]],
  ['04', 'Michał (demo)', 'Wrocław', 'dolnośląskie',
    [[M.inception, 10], [M.joker, 8], [M.basterds, 9], [M.avatar, 3]], [M.gladiator]],
  ['05', 'Zuzia (demo)', 'Gdańsk', 'pomorskie',
    [[M.backToFuture, 9], [M.jurassic, 8], [M.titanic, 7]], [M.pianist, M.avatar]],
  ['06', 'Kuba (demo)', 'Rzeszów', 'podkarpackie',
    [[M.gladiator, 9], [M.goodfellas, 8], [M.godfather, 6]], [M.seven, M.inception]],
  ['07', 'Ola (demo)', 'Zakopane', 'małopolskie',
    [[M.casablanca, 8], [M.pianist, 10], [M.backToFuture, 5]], [M.titanic]],
  ['08', 'Piotr (demo)', 'Białystok', 'podlaskie',
    [[M.seven, 9], [M.goodfellas, 9], [M.joker, 7], [M.titanic, 2]], [M.godfather, M.basterds]],
  ['09', 'Agata (demo)', 'Poznań', 'wielkopolskie',
    [[M.avatar, 8], [M.jurassic, 9], [M.inception, 8]], [M.backToFuture]],
  ['10', 'Bartek (demo)', 'Łódź', 'łódzkie',
    [[M.basterds, 8], [M.godfather, 7], [M.casablanca, 6]], [M.joker, M.goodfellas]],
].map(([n, displayName, city, voivodeship, watched, watchlist]) => ({
  email: `demo-${n}@moviedate.test`, displayName, city, voivodeship, watched, watchlist,
}));

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

async function upsertAccount(u) {
  const s = new Session();
  const reg = await s.req('POST', '/api/auth/register', { email: u.email, password: PASSWORD }, [200, 400]);
  const created = reg.status === 200;
  if (!created && !JSON.stringify(reg.json).includes('DuplicateUserName')) {
    throw new Error(`register ${u.email} -> 400 ${JSON.stringify(reg.json)}`);
  }
  await s.req('POST', '/api/auth/login?useCookies=true', { email: u.email, password: PASSWORD });
  await s.req('PUT', '/api/profile/display-name', { displayName: u.displayName });
  await s.req('PUT', '/api/profile/city', { cityId: await resolveCity(s, u.city, u.voivodeship) });
  for (const [m, rating] of u.watched) {
    const movieId = await resolveMovie(s, m);
    const r = await s.req('POST', '/api/watched', { movieId, rating }, [201, 409]);
    if (r.status === 409) await s.req('PUT', `/api/watched/${movieId}`, { rating });
  }
  for (const m of u.watchlist) {
    await s.req('POST', '/api/watchlist', { movieId: await resolveMovie(s, m) }, [201, 409]);
  }
  return created;
}

console.log(`Upserting ${ACCOUNTS.length} demo accounts on ${BASE_URL}...`);
const rows = [];
try {
  for (const u of ACCOUNTS) {
    const created = await upsertAccount(u);
    rows.push({
      email: u.email,
      name: u.displayName,
      city: `${u.city} (${u.voivodeship})`,
      favorites: u.watched.filter(([, r]) => r >= 7).map(([m]) => m.title).join(', '),
      watchlist: u.watchlist.map((m) => m.title).join(', '),
      account: created ? 'created' : 'existed',
    });
  }
} catch (err) {
  console.error(`\nFAILED: ${err.message}`);
  process.exit(1);
}
console.table(rows);
console.log('Password: the DEMO_PASSWORD value you passed (same for all accounts). Matches refresh in the background within ~10 s.');
