# Brand Name vs Legal Entity Pattern

When investigating behavioral-health, treatment, or service organizations, the name on marketing materials, directories, or a case-manager referral is often a **brand name** (d/b/a, fictitious name, trade name) that is legally registered to a **different, separate legal entity** (LLC, Inc, PLLC). The brand is what clients know; the legal entity is who holds the license, signs the contract, and bears liability.

This is common across treatment centers, therapy practices, and behavioral-health facilities. Not inherently fraudulent, but a critical distinction for due diligence and a frequent source of confusion.

## The Three Layers

Every subject in this pattern has three names:

1. **Consumer brand** — the name on paid ads, recovery directories, SEO pages, marketing site. What the case manager or client hears. Examples: "Rize OC", "OC Revive", "Thrive Recovery."
2. **Legal entity** — the name registered with the state that holds the license and NPI. Examples: "Zoom Therapy Services LLC", "Orange County Revive Services, Inc."
3. **d/b/a name** (doing business as) — the name the legal entity officially registers as its operating name. Recorded on state filings, CMS NPI registry, DHCS record. Examples: "Rize OC Mental Health" (d/b/a of Zoom Therapy Services LLC).

The d/b/a is the legal bridge between the brand and the legal entity. The consumer brand may or may not match the d/b/a exactly.

## When to Look For It

Always flag this during the **corporate/ownership pass** (source-playbook section 4) and **licensing pass** (source-playbook section 3) when the subject is:

- A behavioral-health, treatment, or therapy facility
- Any service organization where marketing names differ from registered names
- A case-manager referral for a client placement (where the legal entity matters for contracts)

Check these signals during the **website crawl** (source-playbook section 1):

- The about page has a "founded" date that does not match the NPI enumeration or domain registration date
- No legal entity is named anywhere on the site (not in team page, privacy policy, terms, or footer)
- The domain registration date or NPI enumeration date is newer than the claimed founding date
- The "founded" claim predates the domain or NPI by years (strong signal of a d/b/a relationship)

## How to Verify

1. **CMS NPI Registry** — `npiregistry.cms.hhs.gov`. Look up the consumer brand name; the results return the legal entity, d/b/a names, and the authorized official.
2. **State Secretary of State** — search by the legal entity name from the NPI record. Look for DBA/fictitious-name entries, registered agents, and officers.
3. **State licensing boards** — the DHCS facility lookup (for behavioral health in California) or equivalent in other states. The facility name field often shows the d/b/a, while the licensee field shows the legal entity.
4. **Joint Commission Quality Check** — the accrediting body lists the accredited legal entity separately from the care site name.
5. **Domain WHOIS/RDAP** — compare creation dates across all domains. A shared registration day is strong evidence of a single launch batch (Rize OC's `rizeoc.com` and `rizeocmentalhealth.com` were both first registered 2022-09-13).

## How to Structure It in the Vault

### One note per legal entity, one note per consumer brand

**Legal entity note:**
- Title: exact legal entity name from state records (CA SoS, NPI registry)
- Template: Organization
- Overview states: legal entity, d/b/a, consumer brand
- `aliases` field: consumer brand variations

**Consumer brand note:**
- Title: the consumer-facing brand name
- Template: Organization
- Overview explicitly states: legal entity with state filing number and formation date
- `aliases` field: all brand variations the consumer might use

### Link them explicitly

In the legal-entity note:
```
[[Legal Entity LLC]] is the operating legal entity behind [[Consumer Brand]].
```

In the consumer-brand note:
```
**Legal entity:** [[Legal Entity LLC]] (CA SoS #XXXXX, formed MM/DD/YYYY), d/b/a "Consumer Brand Mental Health".
```

### Multi-brand clusters

Some operators run multiple consumer brands under one or several legal entities (the multi-entity pattern: one owner, multiple brands, shared office, shared clinicians). In that case:

- One legal-entity note per registered entity
- Multiple consumer-brand notes linked to the same legal entity(s)
- Use a relationship diagram in the investigation note to show the full mapping

### Affiliated facilities are different

An affiliated residential or detox facility is **not** a d/b/a of the brand — it is a separately owned LLC that the Joint Commission lists as a care site. This is an affiliate relationship, not a brand-alias relationship. Keep affiliated facilities in their own notes and link them separately from the d/b/a mapping.

## State Terminology

| Term | Meaning | Where to Find |
|------|---------|---------------|
| d/b/a | "Doing business as" — registered trade name | CA Secretary of State filings, CMS NPI registry |
| Fictitious name | Same concept, different state terminology | CA SoS, state business registries |
| Trade name | Name under which the entity conducts business | CA SoS, state business registries |
| DBA | Same as d/b/a | NPI registry, state licensing records |

## Why It Matters for Due Diligence

- **Verification targets the legal entity, not the brand.** Licenses, NPI records, state filings, and Joint Commission accreditation are all under the legal entity name.
- **Directories and ads use the brand name.** Case managers and clients encounter the brand in directories, paid ads, and SEO pages. The legal entity is rarely visible to the public.
- **Clients may not know the distinction.** If a client signs a contract with "Rize OC" but the legal entity is "Zoom Therapy Services LLC," that is normal (the d/b/a exists for this). But the operator should disclose this to the client.
- **Multi-brand clusters are the norm.** Many operators run several brands from the same legal infrastructure. Not inherently suspicious, but a pattern to note.

## Cross-References

- `references/source-playbook.md` — sections 3 (licensing) and 4 (corporate/ownership)
- `references/verification.md` — identity discipline and confidence labeling
- `references/tools-and-tactics.md` — NPI lookup, WHOIS/RDAP, state portal tactics

## Worked Example: Rize OC

```
Consumer brand:   Rize OC / Rize Recovery / Rize OC Mental Health
                      │
                      └── d/b/a → Zoom Therapy Services LLC (CA SoS #202252115712, 2022-08-24)
                                  CEO: Levi Sweet · NPI official: Tyler Michaelis
                                  │
                                  ├── d/b/a brand: Rize OC / Rize Recovery
                                  │
                                  └── Joint Commission parent of:
                                          Sullivans Detox and Residential Treatment LLC
                                          (independently owned: Sullivan family)
                                          NOT a d/b/a — affiliate only
```

Vault notes:
- Legal entity: `Zoom Therapy Services LLC`
- Consumer brand: `Rize OC`
- Investigation: `Rize OC Investigation`
