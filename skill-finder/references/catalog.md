# Curated Agent Skills Catalog — snapshot 2026-06-14

The most useful Claude Agent Skills, deduplicated across the official repo, the major awesome-lists, and ranked with skills.sh install telemetry where available. Install via `npx skills add <repo> --skill <name>` or `/plugin marketplace add <repo>` unless noted. Skills are executable instructions — install only from sources you trust.

## Meta — discovery & skill authoring (use these first)
- **find-skills** (vercel-labs/agent-skills) — in-agent discovery + install of other skills. #1 installed (2.0M). The compounding entry point.
- **skill-creator** (anthropics/skills) — scaffold/edit/eval skills, tune descriptions for trigger accuracy. Official.
- **mcp-builder** (anthropics/skills) — build MCP servers (FastMCP Python / TS SDK). Official.
- **SkillCheck-Free** (BehiSecc) — validate SKILL.md structure/naming/semantics.
- **Skill_Seekers** (yusufkaraaslan) — turn docs sites / repos / PDFs into skills.

## Engineering workflow
- **superpowers** (obra) — brainstorm → plan → execute with TDD + subagents. Accepted into Anthropic marketplace. Top community pick.
- **planning-with-files** (OthmanAdi) — crash-proof markdown plans that survive /clear, with completion gates.
- **test-driven-development / tdd** (addyosmani/agent-skills, mattpocock/skills) — tests-first implementation. 243K installs.
- **systematic-debugging / debugging-and-error-recovery** (obra, addyosmani) — root-cause before fixes. 124K installs.
- **code-review-and-quality** + **code-simplification** (addyosmani) — multi-axis review; behavior-preserving cleanup.
- **improve-codebase-architecture** (mattpocock) — 257K installs.
- **spec-driven-development / source-driven-development** (addyosmani) — specs first; ground code in cited official docs.
- **context-engineering** (addyosmani) + **context-mode** (mksglu) — fix degraded output by managing context/noise.
- **grill-me / grill-with-docs** (mattpocock) — interrogates your plan until shared understanding. 315K / 251K installs.
- **handoff** (mattpocock) — compress a session into a resume doc.

## Frontend & design
- **frontend-design** (anthropics/skills) — production-grade UI past "AI slop". Official. 544K installs.
- **vercel-react-best-practices** (vercel-labs) — 57 React/Next perf rules. 476K installs.
- **web-design-guidelines** (vercel-labs) — audit UI vs 100+ a11y/UX rules. 390K installs.
- **composition-patterns** (vercel-labs) — compound components over boolean-prop hell.
- **web-artifacts-builder** (anthropics/skills) — React/Tailwind/shadcn artifacts. Official.
- **shadcn/ui** (shadcn) — component context + pattern enforcement.
- **ui-ux-pro-max-skill** (nextlevelbuilder) — design systems, banners.
- **theme-factory / canvas-design / algorithmic-art** (anthropics/skills) — theming + generative visuals. Official.

## Documents & content
- **document-skills: pdf / docx / pptx / xlsx** (anthropics/skills) — the workhorses; forms, tracked changes, formulas, charts. Official, in nearly every list.
- **baoyu-skills** (JimLiu) — slide decks, article illustration, URL→markdown.
- **drawio-skill** (Agents365-ai) — NL → draw.io diagrams with vision self-check.
- **md2wechat / content-pipeline** — markdown publishing + multi-platform content pipelines.

## Browser, testing & automation
- **webapp-testing** (anthropics/skills) — Playwright testing of local apps. Official.
- **playwright-skill** (lackeyjb) — browser automation, auto-detect dev servers, responsive checks.
- **agent-browser** (vercel-labs) — 449K installs.
- **desktop-commander** (claude-plugins-official) — terminal/process/file ops across many formats.

## Security
- **Trail of Bits skills** (trailofbits/skills) — 21 skills: CodeQL + Semgrep, fuzzing, smart-contract review. Vendor-authored.
- **ffuf-web-fuzzing** (jthack) — expert ffuf fuzzing for pentesting.
- **security-and-hardening** (addyosmani) — input/auth/storage hardening.

## Data, science & viz
- **scientific-agent-skills** (K-Dense-AI) — 147 skills: bio/chem/med, drug discovery, data tools.
- **claude-d3js-skill** (chrisvoncsefalvay) — D3.js dataviz.
- **graphify** (safishamsi) — codebase/docs → queryable knowledge graph.
- **dbt-transformation-patterns** (wshobson/agents) — dbt data transforms.

## Cloud / infra / vendor packs
- **microsoft azure-skills** (microsoft) — foundry, compute, migrate, quotas; 330K–390K installs each.
- Cloudflare, Netlify, Vercel, Terraform/HashiCorp, Neon, Stripe, Sentry, Expo, Figma official skill packs.
- **terraform-module-library / github-actions-templates / saga-orchestration** (wshobson/agents).

## Marketing / PM / business
- **Corey Haines marketing skills** (coreyhaines31) — 32 skills: conversion, copy, SEO, growth.
- **NotFair** (nowork-studio) — SEO/GEO, Google Ads, Meta Ads.
- **Product-Manager-Skills** (deanpeters), **pm-skills** (phuryn) — JTBD, Lean UX, discovery, pricing, full PM lifecycle.
- **career-ops** (santifer) — CVs, offers, job-search tracking.

## Token / session efficiency
- **caveman** (mattpocock / JuliusBrussee) — terse output, ~65-75% fewer tokens. 246K installs.
- **last30days** (mvanhorn) — synthesize recent discourse across Reddit/X/YouTube/HN/GitHub.

## Mega-bundles (browse for more)
- **alirezarezvani/claude-skills** — 330+ skills across engineering/marketing/product/finance.
- **wshobson/agents** — 84-plugin marketplace, broad engineering coverage.
