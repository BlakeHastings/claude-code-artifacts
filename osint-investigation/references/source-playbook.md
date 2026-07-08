# Source-Sweep Playbook

A menu of source domains to sweep. Selection and order are user-guided, not all are needed for every subject. Each pass becomes one dated Raw Log entry. For each source, capture a reference (source) note (URL + facts), extract atomic entities, and flag concerns inline.

## 1. The subject's own web properties
- Main site(s), ad landing pages, "About", team, services, locations, contact.
- Capture marketing tells: ad-tracking params (`gclid`, `gad_*`), link shorteners/trackers, lead-gen funnels, claims vs evidence.
- Note what is *not* shown (accreditation, ownership, real staff names) as much as what is.

## 2. Social media
- LinkedIn, Facebook, Instagram, X/Twitter. Capture each account as a reference note; capture *meaningful* posts as `post` notes (skip personal filler unless it bears on the question).
- Use profiles, friends/followers, tagged photos, and "About" details for identity confirmation and relationship mapping. A subject's connections often reveal partners and owners (e.g., a near-empty account whose only friend is a known principal).
- For people: prior roles, education, hometown, family, community.

## 3. Accreditation, licensing, regulatory
- Healthcare: Joint Commission (Quality Check / provider locator), CARF, LegitScript, state licensing and health-department boards, professional-license lookups, CMS.
- Other domains: the relevant regulator/registrar/board.
- Confirm the *specific site or entity* is covered, not just the brand name, and check the level/program of accreditation against the user's actual need.

### Healthcare licensing has two separate systems, check both
- **Individual clinicians** are licensed by the state's health-profession boards (often the Dept of Health). **Facilities** are usually licensed by a *different* agency, the state mental-health / substance-abuse department. A clinic can be missing from the Health Dept's facility list entirely because behavioral-health/SUD facilities are not its jurisdiction. If a facility-license search comes up empty, you are probably querying the wrong agency.
  - TN example (verify the URLs still resolve): clinicians = `internet.health.tn.gov/Licensure` (the old `apps.health.tn.gov/Licensure` path is retired); facilities = TDMHSAS at `cloudmh.tn.gov/LicensureInquiry`. The Health-Dept facility list has no behavioral-health category at all.
- **The facility's state license category list is high-signal and can contradict the federal NPI taxonomy.** A site whose CMS NPI *primary* taxonomy is "Substance Abuse Rehabilitation Facility" can be state-licensed predominantly for *mental health* (e.g. separate "MH Partial Hospitalization" and "MH Outpatient" categories). Do not infer what a facility actually does or is licensed for from the NPI taxonomy alone, pull the state facility license and read its categories.
- **Individual-license records carry a verifiable practice address.** A clinician's license profile and CMS NPI both list a practice address/employer, often more current than a facility's roster. Use an NPI-by-name+state query (see tools-and-tactics) to find where a named clinician actually practices, which is how you catch someone listed at facility A who in fact works at facility B.
- Many state boards expose a per-license **Practitioner Profile** (education, supervising physician, disciplinary actions, criminal offenses) and a sealed **Certification Letter** (license #, status, "no history of disciplinary action") behind the search result, capture both, not just the result row, when discipline history matters.
- **NPI confirms a license number and specialty, not its standing or discipline.** The CMS NPI record gives a clinician's license *number* and taxonomy but says nothing about whether the license is active or whether the board has acted. For standing and disciplinary history, query the state's professional-license board; when that board's portal is down or bot-blocked, **FSMB DocInfo (docinfo.org)** is the cross-state fallback, aggregating the state medical boards and reporting "Board Actions: No actions" (or the actions), the active licenses, and education for a physician. (Observed when a state medical board's own verification portal was down for maintenance, which had first looked like a bot block.)

## 4. Corporate / ownership
- **WHOIS** for the subject's domains: registrant name, email, org, dates (a registrant email on an old brand domain is strong rebrand evidence).
- **Secretary of State** business-entity registries (per state): officers, governors, registered agent, principal address, formation date, status, DBAs / fictitious names, and any parent/holding entity.
- **CMS NPI Registry** (`npiregistry.cms.hhs.gov`) for healthcare orgs: legal name, DBAs, authorized official, primary/secondary taxonomy, addresses.
- SAMHSA treatment locator; business aggregators (OpenCorporates needs an API key).
- Hunt the common threads: a shared registered agent, a shared address, one owner across all entities, a rebrand.
- **Nonprofit subjects have their own verification stack.** For a 501(c)(3), pull the IRS **Form 990** via ProPublica Nonprofit Explorer (total revenue/expenses, net assets vs liabilities, executive compensation, and **Schedule L** related-party transactions), **Charity Navigator** (star rating plus the "no material diversion of assets" accountability flag), and the **state Secretary of State** record (Active/Compliance status, formation date, registered agent). Read net-assets-vs-liabilities and the operating result, not the headline revenue: capital-campaign years spike revenue without signaling instability.
- **For healthcare especially, ownership is per-site, not per-brand.** The day-to-day operator can differ from the name on the door, and the *same* brand can be a genuine nonprofit in one market and a for-profit, chain-operated joint venture in another. Confirm the operating entity for the specific location (CMS NPI authorized official, Secretary of State, the operator's own location list), do not infer it from the logo.

## 5. Reviews, news, press, complaints
- Google reviews, domain-appropriate directories (e.g., rehab.com / recovery.com for treatment), local news, press releases, lawsuits / sanctions / regulatory actions.
- Weigh anonymous reviews accordingly; separate astroturf from substantive complaints; record the *specific* complaints (billing focus, conditions, outcomes).

## 6. Identity & relationship cross-checks
- Reverse-lookup phones, addresses, emails. Who or what else is at that address? Co-located entities reveal a back office or owner.
- Cross-reference names across sources, but require a hard link before equating identities (see `verification.md`).

## What to extract from every pass
People, organizations / legal entities, locations / addresses, identifiers (phones, emails, domains), and the source itself, each as its own back-linked note. The patterns emerge from the collisions, not from prose.
