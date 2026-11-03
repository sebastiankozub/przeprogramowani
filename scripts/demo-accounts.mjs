#!/usr/bin/env node
// Creates 20 permanent demo accounts (<prefix>-01..20@moviedate.test, prefix "demo" by default) through the PUBLIC
// API, so a fresh real user gets several ranked candidates (S-05). No DB access, no demo code inside the app.
// Profiles are hard-coded: clustered Polish cities (several accounts per city) and 9-12 rated titles each from a
// shared pool, rated on both sides of 7 (>= 7 counts as a shared favorite), plus overlapping watchlists.
// Since S-06 every write schedules a background match recompute, so demo accounts appear on each other's lists
// (and on a new user's list) without any extra call.
//
// Usage:
//   DEMO_PASSWORD='...' node scripts/demo-accounts.mjs                              # local (http://localhost:5069)
//   BASE_URL=https://moviedate-sk.fly.dev DEMO_PASSWORD='...' node scripts/demo-accounts.mjs
//   DEMO_EMAIL_PREFIX=demo2 DEMO_PASSWORD='...' node scripts/demo-accounts.mjs      # a fresh set: demo2-01..20
// Idempotent: re-running logs in to existing accounts, re-applies name/city/ratings, tolerates duplicates (409).
// An account that already exists with a different password cannot be updated (the API has no reset without
// e-mail) — re-run with the original password, or pick a new DEMO_EMAIL_PREFIX.
// Exits non-zero on any unexpected status. The password is never printed or stored.

const BASE_URL = (process.env.BASE_URL ?? 'http://localhost:5069').replace(/\/$/, '');
const PASSWORD = process.env.DEMO_PASSWORD;
const PREFIX = process.env.DEMO_EMAIL_PREFIX ?? 'demo';

if (!PASSWORD || PASSWORD.length < 8) {
  console.error('DEMO_PASSWORD env var is required (min 8 characters).');
  process.exit(2);
}
if (!/^[a-z0-9][a-z0-9-]*$/.test(PREFIX)) {
  console.error('DEMO_EMAIL_PREFIX must be lowercase letters, digits and hyphens.');
  process.exit(2);
}

const M = {
  godfather: { title: 'Ojciec chrzestny', year: 1972 },
  godfather2: { title: 'Ojciec chrzestny II', year: 1974 },
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
  matrix: { title: 'Matrix', year: 1999 },
  shawshank: { title: 'Skazani na Shawshank', year: 1994 },
  pulpFiction: { title: 'Pulp Fiction', year: 1994 },
  darkKnight: { title: 'Mroczny rycerz', year: 2008 },
  silence: { title: 'Milczenie owiec', year: 1991 },
  schindler: { title: 'Lista Schindlera', year: 1993 },
  fightClub: { title: 'Podziemny krąg', year: 1999 },
  terminator2: { title: 'Terminator 2: Dzień sądu', year: 1991 },
  interstellar: { title: 'Interstellar', year: 2014 },
  lotr1: { title: 'Władca Pierścieni: Drużyna Pierścienia', year: 2001 },
  lotr3: { title: 'Władca Pierścieni: Powrót króla', year: 2003 },
  starWars: { title: 'Gwiezdne wojny: część IV – Nowa nadzieja', year: 1977 },
  potter1: { title: 'Harry Potter i Kamień Filozoficzny', year: 2001 },
  homeAlone: { title: 'Kevin sam w domu', year: 1990 },
  parasite: { title: 'Parasite', year: 2019 },
  oppenheimer: { title: 'Oppenheimer', year: 2023 },
  departed: { title: 'Infiltracja', year: 2006 },
  lifeIsBeautiful: { title: 'Życie jest piękne', year: 1997 },
  ryan: { title: 'Szeregowiec Ryan', year: 1998 },
  dieHard: { title: 'Szklana pułapka', year: 1988 },
  avengersEndgame: { title: 'Avengers: Koniec gry', year: 2019 },
  braveheart: { title: 'Braveheart. Waleczne serce', year: 1995 },
};

// [email suffix, display name, city, voivodeship, watched [[movie, rating]], watchlist [movie]]
// 01-10 keep their original ratings (re-applied as before) and gain more titles; 11-20 are new.
const ACCOUNTS = [
  ['01', 'Marta (demo)', 'Kraków', 'małopolskie',
    [[M.godfather, 9], [M.pianist, 8], [M.inception, 7], [M.titanic, 4], [M.schindler, 9], [M.shawshank, 10],
      [M.lifeIsBeautiful, 8], [M.casablanca, 6], [M.interstellar, 8], [M.homeAlone, 5]],
    [M.joker, M.parasite, M.oppenheimer]],
  ['02', 'Tomek (demo)', 'Kraków', 'małopolskie',
    [[M.godfather, 10], [M.gladiator, 8], [M.jurassic, 6], [M.godfather2, 9], [M.goodfellas, 8], [M.braveheart, 9],
      [M.ryan, 8], [M.dieHard, 7], [M.matrix, 6], [M.titanic, 3]],
    [M.inception, M.basterds, M.departed]],
  ['03', 'Julia (demo)', 'Warszawa', 'mazowieckie',
    [[M.titanic, 9], [M.avatar, 7], [M.pianist, 9], [M.seven, 5], [M.potter1, 9], [M.lotr1, 8], [M.homeAlone, 8],
      [M.lifeIsBeautiful, 9], [M.casablanca, 7], [M.fightClub, 4]],
    [M.godfather, M.interstellar, M.shawshank]],
  ['04', 'Michał (demo)', 'Wrocław', 'dolnośląskie',
    [[M.inception, 10], [M.joker, 8], [M.basterds, 9], [M.avatar, 3], [M.pulpFiction, 10], [M.fightClub, 9],
      [M.darkKnight, 9], [M.matrix, 8], [M.seven, 8], [M.titanic, 4], [M.interstellar, 9]],
    [M.gladiator, M.parasite, M.departed]],
  ['05', 'Zuzia (demo)', 'Gdańsk', 'pomorskie',
    [[M.backToFuture, 9], [M.jurassic, 8], [M.titanic, 7], [M.homeAlone, 9], [M.potter1, 8], [M.starWars, 7],
      [M.avatar, 8], [M.avengersEndgame, 7], [M.seven, 3]],
    [M.pianist, M.lotr1, M.terminator2]],
  ['06', 'Kuba (demo)', 'Rzeszów', 'podkarpackie',
    [[M.gladiator, 9], [M.goodfellas, 8], [M.godfather, 6], [M.braveheart, 9], [M.ryan, 9], [M.dieHard, 8],
      [M.terminator2, 8], [M.departed, 7], [M.titanic, 4]],
    [M.seven, M.inception, M.godfather2]],
  ['07', 'Ola (demo)', 'Zakopane', 'małopolskie',
    [[M.casablanca, 8], [M.pianist, 10], [M.backToFuture, 5], [M.schindler, 9], [M.lifeIsBeautiful, 9],
      [M.shawshank, 8], [M.titanic, 7], [M.parasite, 8], [M.avengersEndgame, 3]],
    [M.titanic, M.oppenheimer, M.godfather]],
  ['08', 'Piotr (demo)', 'Białystok', 'podlaskie',
    [[M.seven, 9], [M.goodfellas, 9], [M.joker, 7], [M.titanic, 2], [M.silence, 9], [M.fightClub, 8],
      [M.pulpFiction, 9], [M.departed, 8], [M.darkKnight, 7], [M.homeAlone, 4]],
    [M.godfather, M.basterds, M.parasite]],
  ['09', 'Agata (demo)', 'Poznań', 'wielkopolskie',
    [[M.avatar, 8], [M.jurassic, 9], [M.inception, 8], [M.interstellar, 9], [M.matrix, 8], [M.starWars, 8],
      [M.terminator2, 7], [M.lotr1, 9], [M.oppenheimer, 7], [M.casablanca, 4]],
    [M.backToFuture, M.lotr3, M.avengersEndgame]],
  ['10', 'Bartek (demo)', 'Łódź', 'łódzkie',
    [[M.basterds, 8], [M.godfather, 7], [M.casablanca, 6], [M.pulpFiction, 9], [M.goodfellas, 8], [M.godfather2, 8],
      [M.departed, 9], [M.parasite, 8], [M.joker, 7], [M.avatar, 4]],
    [M.joker, M.goodfellas, M.seven]],
  ['11', 'Kasia (demo)', 'Kraków', 'małopolskie',
    [[M.lotr1, 10], [M.lotr3, 9], [M.potter1, 9], [M.starWars, 8], [M.backToFuture, 8], [M.homeAlone, 7],
      [M.jurassic, 7], [M.avatar, 6], [M.pulpFiction, 4]],
    [M.interstellar, M.matrix, M.titanic]],
  ['12', 'Paweł (demo)', 'Kraków', 'małopolskie',
    [[M.darkKnight, 10], [M.inception, 9], [M.interstellar, 10], [M.matrix, 9], [M.fightClub, 8], [M.oppenheimer, 9],
      [M.terminator2, 8], [M.seven, 7], [M.titanic, 5], [M.homeAlone, 3]],
    [M.parasite, M.pulpFiction, M.silence]],
  ['13', 'Ania (demo)', 'Warszawa', 'mazowieckie',
    [[M.parasite, 9], [M.pianist, 9], [M.schindler, 8], [M.casablanca, 9], [M.godfather, 8], [M.lifeIsBeautiful, 8],
      [M.shawshank, 9], [M.oppenheimer, 7], [M.avengersEndgame, 4]],
    [M.goodfellas, M.departed, M.silence]],
  ['14', 'Krzysiek (demo)', 'Warszawa', 'mazowieckie',
    [[M.avengersEndgame, 9], [M.starWars, 9], [M.terminator2, 9], [M.dieHard, 8], [M.matrix, 9], [M.jurassic, 8],
      [M.gladiator, 8], [M.avatar, 7], [M.backToFuture, 8], [M.pianist, 4]],
    [M.darkKnight, M.lotr1, M.inception]],
  ['15', 'Magda (demo)', 'Warszawa', 'mazowieckie',
    [[M.titanic, 10], [M.homeAlone, 8], [M.potter1, 8], [M.braveheart, 7], [M.casablanca, 8], [M.lifeIsBeautiful, 9],
      [M.avatar, 8], [M.lotr1, 7], [M.seven, 3], [M.fightClub, 4]],
    [M.pianist, M.shawshank, M.backToFuture]],
  ['16', 'Wojtek (demo)', 'Wrocław', 'dolnośląskie',
    [[M.godfather, 9], [M.godfather2, 10], [M.goodfellas, 9], [M.departed, 8], [M.pulpFiction, 8], [M.seven, 8],
      [M.silence, 8], [M.shawshank, 9], [M.basterds, 7], [M.avatar, 5]],
    [M.joker, M.fightClub, M.oppenheimer]],
  ['17', 'Ewa (demo)', 'Wrocław', 'dolnośląskie',
    [[M.shawshank, 10], [M.interstellar, 8], [M.pianist, 8], [M.schindler, 9], [M.ryan, 8], [M.inception, 8],
      [M.lifeIsBeautiful, 7], [M.parasite, 7], [M.jurassic, 5], [M.dieHard, 4]],
    [M.casablanca, M.lotr3, M.godfather]],
  ['18', 'Szymon (demo)', 'Gdańsk', 'pomorskie',
    [[M.matrix, 10], [M.fightClub, 9], [M.joker, 9], [M.darkKnight, 8], [M.seven, 9], [M.silence, 7],
      [M.pulpFiction, 8], [M.terminator2, 7], [M.titanic, 3]],
    [M.basterds, M.departed, M.interstellar]],
  ['19', 'Natalia (demo)', 'Gdańsk', 'pomorskie',
    [[M.potter1, 10], [M.homeAlone, 9], [M.backToFuture, 8], [M.titanic, 8], [M.lotr1, 8], [M.lotr3, 8],
      [M.avatar, 7], [M.jurassic, 7], [M.goodfellas, 4]],
    [M.starWars, M.avengersEndgame, M.casablanca]],
  ['20', 'Adam (demo)', 'Poznań', 'wielkopolskie',
    [[M.gladiator, 10], [M.braveheart, 9], [M.ryan, 9], [M.dieHard, 8], [M.terminator2, 8], [M.darkKnight, 8],
      [M.oppenheimer, 8], [M.interstellar, 7], [M.lifeIsBeautiful, 5], [M.titanic, 4]],
    [M.godfather2, M.matrix, M.schindler]],
].map(([n, displayName, city, voivodeship, watched, watchlist]) => ({
  email: `${PREFIX}-${n}@moviedate.test`,
  // "(demo)" -> "(demo2)" for another prefix, so a second set is told apart from its identical first set.
  displayName: displayName.replace('(demo)', `(${PREFIX})`),
  city, voivodeship, watched,
  // A movie already rated is never also on the watchlist (the API keeps the two apart).
  watchlist: watchlist.filter((m) => !watched.some(([w]) => w === m)),
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
  const login = await s.req('POST', '/api/auth/login?useCookies=true', { email: u.email, password: PASSWORD }, [200, 401]);
  if (login.status === 401) {
    throw new Error(
      `${u.email} already exists with a different password. Re-run with the original DEMO_PASSWORD, ` +
      `or create a fresh set with DEMO_EMAIL_PREFIX=<new-prefix>.`);
  }
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

console.log(`Upserting ${ACCOUNTS.length} demo accounts (${PREFIX}-NN@moviedate.test) on ${BASE_URL}...`);
const rows = [];
try {
  for (const u of ACCOUNTS) {
    const created = await upsertAccount(u);
    rows.push({
      email: u.email,
      name: u.displayName,
      city: u.city,
      rated: u.watched.length,
      favorites: u.watched.filter(([, r]) => r >= 7).length,
      watchlist: u.watchlist.length,
      account: created ? 'created' : 'existed',
    });
  }
} catch (err) {
  console.error(`\nFAILED: ${err.message}`);
  process.exit(1);
}
console.table(rows);
console.log('Password: the DEMO_PASSWORD value you passed (same for all accounts). Matches refresh in the background within ~10 s.');
