#!/usr/bin/env node
// find-skills.mjs — query live Agent Skill registries and print a ranked, deduplicated list.
//
// Usage:
//   node find-skills.mjs "<query>" [--limit 25] [--sources skillsmp,official,guild] [--json]
//
// Sources:
//   skillsmp  (default) — https://skillsmp.com/api/skills  searchable REST, no auth, GitHub-sourced, has star counts
//   official  (default) — anthropics/skills + anthropics/claude-plugins-official marketplace.json (raw GitHub)
//   guild     (opt-in)  — https://guildskills.com/data/guildskills.json  ~75MB cross-agent catalog, schema-rich,
//                         security_grade + agent_compat. Only fetched when explicitly requested (heavy).
//
// No external deps. Node 18+ (global fetch).

const args = process.argv.slice(2);
const flags = {};
const positional = [];
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a.startsWith("--")) {
    const key = a.slice(2);
    const next = args[i + 1];
    if (next && !next.startsWith("--")) { flags[key] = next; i++; }
    else flags[key] = true;
  } else positional.push(a);
}

const query = (positional.join(" ") || "").trim();
const limit = parseInt(flags.limit || "25", 10);
const asJson = !!flags.json;
const sources = (flags.sources
  ? String(flags.sources).split(",").map(s => s.trim())
  : ["skillsmp", "official"]); // guild is opt-in: heavy 75MB download

if (!query && !sources.includes("official")) {
  console.error('Provide a query, e.g.  node find-skills.mjs "pdf forms"');
  process.exit(1);
}

const q = query.toLowerCase();
const hit = (text) => !q || (text || "").toLowerCase().includes(q);

async function fetchJson(url, opts = {}) {
  const res = await fetch(url, { headers: { "User-Agent": "skill-finder" }, ...opts });
  if (!res.ok) throw new Error(`${res.status} ${url}`);
  return res.json();
}

// ---- SkillsMP: searchable, GitHub-sourced, star counts -----------------------
async function fromSkillsMp() {
  const url = `https://skillsmp.com/api/skills?q=${encodeURIComponent(query)}&limit=${Math.max(limit, 50)}`;
  const data = await fetchJson(url);
  return (data.skills || []).map(s => ({
    name: s.name,
    author: s.author,
    description: (s.description || "").replace(/\s+/g, " ").trim(),
    url: s.githubUrl,
    stars: s.stars ?? 0,
    install: `npx skills add ${s.githubUrl}`,
    source: "skillsmp",
    trust: "community",
  }));
}

// ---- Official Anthropic marketplaces -----------------------------------------
const OFFICIAL = [
  "https://raw.githubusercontent.com/anthropics/skills/main/.claude-plugin/marketplace.json",
  "https://raw.githubusercontent.com/anthropics/claude-plugins-official/main/.claude-plugin/marketplace.json",
];
async function fromOfficial() {
  const out = [];
  for (const url of OFFICIAL) {
    let mp;
    try { mp = await fetchJson(url); } catch { continue; }
    const market = mp.name || "marketplace";
    for (const p of mp.plugins || []) {
      const skills = p.skills || [];
      const text = `${p.name} ${p.description} ${skills.join(" ")}`;
      if (!hit(text)) continue;
      out.push({
        name: p.name,
        author: mp.owner?.name || "Anthropic",
        description: (p.description || "").replace(/\s+/g, " ").trim(),
        url: url.replace("/raw.githubusercontent.com/", "/github.com/").replace("/main/.claude-plugin/marketplace.json", ""),
        stars: Infinity,
        install: `/plugin marketplace add ${market === "anthropic-agent-skills" ? "anthropics/skills" : "anthropics/claude-plugins-official"} ; /plugin install ${p.name}@${market}`,
        source: "official",
        trust: "official",
        skills,
      });
    }
  }
  return out;
}

// ---- GuildSkills: heavy cross-agent catalog (opt-in) -------------------------
async function fromGuild() {
  console.error("[guild] downloading ~75MB cross-agent catalog, this is slow...");
  const data = await fetchJson("https://guildskills.com/data/guildskills.json");
  return (data.skills || [])
    .filter(s => hit(`${s.name} ${s.description} ${(s.tags || []).join(" ")}`))
    .filter(s => (s.agent_compat || []).includes("claude-code"))
    .map(s => ({
      name: s.name,
      author: s.author || "unknown",
      description: (s.description || "").replace(/\s+/g, " ").trim(),
      url: s.source_url,
      stars: 0,
      grade: s.security_grade,
      install: `see ${s.source_url}`,
      source: "guild",
      trust: "community",
    }));
}

const RUNNERS = { skillsmp: fromSkillsMp, official: fromOfficial, guild: fromGuild };

(async () => {
  const results = [];
  for (const src of sources) {
    const fn = RUNNERS[src];
    if (!fn) { console.error(`unknown source: ${src}`); continue; }
    try { results.push(...await fn()); }
    catch (e) { console.error(`[${src}] failed: ${e.message}`); }
  }

  // dedupe by github repo root (keep highest signal)
  const seen = new Map();
  for (const r of results) {
    const key = (r.url || r.name).replace(/\/tree\/.*$/, "").toLowerCase();
    const prev = seen.get(key);
    if (!prev || rank(r) > rank(prev)) seen.set(key, r);
  }
  const merged = [...seen.values()].sort((a, b) => rank(b) - rank(a)).slice(0, limit);

  if (asJson) { console.log(JSON.stringify(merged, null, 2)); return; }

  if (!merged.length) { console.log(`No skills found for "${query}".`); return; }
  console.log(`\nTop ${merged.length} skills for "${query || "(official catalog)"}":\n`);
  for (const r of merged) {
    const trust = r.trust === "official" ? "[OFFICIAL]" : (r.grade ? `[${r.grade}]` : "");
    const stars = Number.isFinite(r.stars) && r.stars > 0 ? `  ★${r.stars}` : "";
    console.log(`• ${r.name}  ${trust}${stars}  (${r.source})`);
    console.log(`    ${r.description.slice(0, 160)}`);
    console.log(`    ${r.url}`);
    console.log(`    install: ${r.install}\n`);
  }
})();

function rank(r) {
  let s = 0;
  if (r.trust === "official") s += 1e9;          // official always floats up
  if (Number.isFinite(r.stars)) s += r.stars;    // GitHub stars
  if (r.grade === "A") s += 50;                   // GuildSkills security grade
  return s;
}
