---
name: jpl-horizons-ephemeris
description: Query NASA/JPL HORIZONS for real spacecraft and planet position/velocity vectors over time via its public REST API. Use when you need actual trajectory or ephemeris data — where a probe (Voyager, Pioneer, New Horizons, Cassini, Parker, Juno, etc.) or a planet was at given dates, heliocentric/barycentric coordinates, an orbit or flight-path to plot, or "position of X relative to the Sun/Earth over time". Covers the ssd.jpl.nasa.gov/api/horizons.api endpoint, VECTORS + CSV params, SPICE/NAIF body ids, the ecliptic X-Y (north-pole) projection, and the coverage-bound error + clamp-and-retry gotcha.
---

# Querying JPL HORIZONS for trajectories

HORIZONS is NASA/JPL's authoritative ephemeris service. Its REST API returns state
vectors (position/velocity) for any solar-system body or tracked spacecraft over a
date range. No key required.

## Minimal working request

`GET https://ssd.jpl.nasa.gov/api/horizons.api` with a query string. Values that
contain special characters (the SPICE id, the `@`, dates, `1 MO`) must be wrapped in
**single quotes** and URL-encoded.

Node (Node 18+ has global `fetch`):

```js
const params = new URLSearchParams({
  format: 'text',
  COMMAND: "'-31'",       // Voyager 1 (SPICE id)
  OBJ_DATA: 'NO',
  MAKE_EPHEM: 'YES',
  EPHEM_TYPE: 'VECTORS',  // state vectors
  CENTER: "'500@10'",     // observer at the Sun's body center (see below)
  REF_PLANE: 'ECLIPTIC',  // X-Y is the ecliptic plane
  START_TIME: "'1977-09-06'",
  STOP_TIME: "'2000-01-01'",
  STEP_SIZE: "'1 MO'",
  VEC_TABLE: '1',         // 1 = position only; 2 adds velocity
  OUT_UNITS: 'AU-D',      // AU and days
  CSV_FORMAT: 'YES'
});
const text = await (await fetch(`https://ssd.jpl.nasa.gov/api/horizons.api?${params}`)).text();
```

## Body ids

`COMMAND` and `CENTER` take SPICE/NAIF ids. Spacecraft are **negative**:

| Probe | id | | Probe | id |
|---|---|---|---|---|
| Voyager 1 | -31 | | Parker Solar Probe | -96 |
| Voyager 2 | -32 | | Juno | -61 |
| Pioneer 10 | -23 | | Cassini | -82 |
| Pioneer 11 | -24 | | Galileo | -77 |
| New Horizons | -98 | | MESSENGER | -236 |
| Ulysses | -55 | | Dawn | -203 |

Planets/Sun use NAIF ids: Sun `10`, Earth `399`, Jupiter `599`, etc. (barycenters
are `0`–`9`; e.g. solar-system barycenter `0`). These id assignments are stable.

`CENTER='500@<id>'` means "observer at the body center of `<id>`". `500@10` =
heliocentric (Sun center). `500@0` = solar-system barycentric. `500@399` = geocentric.

## Parsing the result

Data sits between the `$$SOE` and `$$EOE` markers. With `CSV_FORMAT=YES` and
`VEC_TABLE=1`, each row is: `JDTDB, Calendar Date, X, Y, Z,` (trailing comma).

```js
const soe = text.indexOf('$$SOE'), eoe = text.indexOf('$$EOE');
for (const line of text.slice(soe + 5, eoe).trim().split('\n')) {
  const c = line.split(',').map(s => s.trim());
  const date = c[1];                       // "A.D. 1977-Sep-06 00:00:00.0000"
  const [x, y, z] = [c[2], c[3], c[4]].map(Number);
}
```

For a "viewed down the north ecliptic pole" plot (the classic Voyager/Pioneer swirl),
plot X vs Y and drop Z. Note Z is not negligible for craft that leave the ecliptic
(e.g. Voyager 1 climbs tens of AU above the plane after Saturn) — dropping it is a
deliberate top-down projection, not an approximation of a flat trajectory.

## Coverage bounds: the clamp-and-retry gotcha

Each target has its own coverage window. If `START_TIME`/`STOP_TIME` fall outside it,
there is **no `$$SOE` block**; instead the body says:

```
No ephemeris for target "Voyager 1 (spacecraft)" prior to A.D. 1977-SEP-05 13:59:24.3830 TDB
```

(or `... after A.D. ...`). Handle this by parsing the reported bound and retrying
clamped to it, rather than hardcoding windows:

```js
const prior = text.match(/prior to A\.D\.\s+(\d{4}-[A-Za-z]{3}-\d{2})/);
const after = text.match(/after A\.D\.\s+(\d{4}-[A-Za-z]{3}-\d{2})/);
```

**Intra-day epoch gotcha:** the reported bound carries a time-of-day (e.g.
`13:59:24`). A clamp to that *date* at 00:00 is still before the epoch, so it loops.
Advance the start bound by one whole day (and pull the stop bound in by one) when
clamping. In practice, seeding `START_TIME` at launch-day + 1 avoids the first hit.

World-state note: which craft cover which dates changes as JPL reconstructs/extends
trajectories (defunct probes like Pioneer 11 still have reconstructed arcs to ~1995;
Voyagers have long predicted arcs). Do not assert a fixed window — let the API report
it and clamp. Verify by inspecting the response text.

## Etiquette

Add a small delay (~300–500 ms) between requests when fetching many bodies. Commit the
resulting JSON into your project so runtime/builds never depend on the live API;
re-fetch only when you need to change bodies, range, or step.
