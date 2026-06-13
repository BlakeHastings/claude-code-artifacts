---
name: protostar-docs-deploy
description: >-
  Reference and runbook for the Protostar documentation site (docs.voidprojects.ai),
  built from the public protostar-docs content repo and shipped by the private
  protostar-docs-infra repo via Azure Static Web Apps, Cloudflare DNS, OIDC, and
  Terraform. Use when the docs site is down or 404ing, the build-check or deploy
  or terraform GitHub Actions are failing, a product's docs aren't showing up,
  the custom domain won't validate or has no TLS, adding a new product to the
  docs hub, or running/debugging the Terraform (bootstrap vs site, OIDC, local
  state). Covers the two-repo split, the fetch-docs lift, Azure SWA custom-domain
  cname-delegation, and the Cloudflare grey-cloud + subscription-scope RBAC gotchas.
  Also covers the sibling apex root site voidprojects.ai (swa-voidprojects-site),
  including apex TXT-token validation and Cloudflare CNAME flattening.
allowed-tools: Bash, Read, Grep, Glob, PowerShell
---
# Protostar docs site (docs.voidprojects.ai)

Operational reference for how the Protostar docs site is built, hosted, and recovered.

## Architecture: public content + private deploy

Two repos under `voidprojectssoftware`, split so the public repo carries zero hosting detail:

- **`protostar-docs`** (PUBLIC): Docusaurus content hub. Holds the site shell, `docs.manifest.json`, and `scripts/fetch-docs.mjs`. Its ONLY workflow is a PR/push `build-check` that compiles the site (no secrets, no deploy). Never add a deploy job, secrets, or `pull_request_target` here.
- **`protostar-docs-infra`** (PRIVATE): the deploy + IaC. Holds the `deploy` and `terraform` workflows, Terraform, and the Azure/OIDC config. It checks out the public repo, builds, and ships.

Docs are authored next to code in each product repo (a `docs/` folder) and lifted into the hub at build time.

### How content is lifted (`fetch-docs.mjs`)
- `docs.manifest.json` is the single source of truth for which product repos contribute docs. Each entry: `id`, `label`, `repo`, `ref`, `docsPath` (usually `docs`), `routeBasePath`, `navbarPosition`.
- `scripts/fetch-docs.mjs --remote` blobless+sparse-clones each product's `docsPath` at the pinned `ref` into `external/<id>/docs`. Local sibling checkouts win when present (for authoring) unless `--remote`/CI/`DOCS_SOURCE=remote`.
- **Most common failure:** a manifested product repo has no `docs/` folder yet → `Error: <repo>@<ref> has no 'docs' folder yet` and the script exits 1. This breaks BOTH the public `build-check` AND the private `deploy` (they run the same script). Fix = add the `docs/` folder to the product repo (or remove/repoint the manifest entry), then re-run.
- **Adding a product to the hub:** add an entry to `docs.manifest.json` in `protostar-docs`. Routing, sidebar, and "edit this page" wire up automatically. The product repo must have the `docsPath` folder at the pinned `ref`.

### How it deploys (`deploy` workflow, private repo)
- Triggers: `workflow_dispatch`, `repository_dispatch (docs-content-updated)`, and a daily `schedule` at `cron: '0 7 * * *'` (UTC).
- Checks out the public repo, `npm ci`, `node scripts/fetch-docs.mjs --remote`, `npm run build:no-fetch`, then deploys `./build` to Azure Static Web Apps with `@azure/static-web-apps-cli deploy --env production`.
- Auth is OIDC (`azure/login`); the SWA deployment token is fetched at runtime via `az staticwebapp secrets list`. No stored Azure or SWA secrets.

## Azure / hosting facts
- App: **`swa-protostar-docs`** (Azure Static Web App, Free tier) in resource group **`rg-protostar-docs`**, region **East US 2**.
- SWA default host: **`orange-river-0d06b480f.7.azurestaticapps.net`** (always serves the latest deploy; test here to isolate "is it built?" from "is the custom domain bound?").
- DNS: managed **manually** in Cloudflare (one `docs` CNAME), NOT in Terraform — Cloudflare has no OIDC, so rather than store a long-lived token the CNAME is added by hand. The whole pipeline stays credential-free.
- Custom domain `docs.voidprojects.ai` is bound on the SWA via Terraform (`azurerm_static_web_app_custom_domain`, `validation_type = "cname-delegation"`), gated by the repo variable `ENABLE_CUSTOM_DOMAIN`.

## Sibling site: apex root `voidprojects.ai`
A SECOND, independent Azure SWA serves the root/landing site at the apex, separate from the docs app:
- App: **`swa-voidprojects-site`** in resource group **`rg-voidprojects-site`**, region **East US 2**.
- SWA default host: **`proud-hill-01ebd2b0f.7.azurestaticapps.net`**.
- Custom domain `voidprojects.ai` was bound **manually** (Azure portal + `az`), NOT via Terraform (as of 2026-06-11). If/when Terraformed, mirror the docs `terraform/site` pattern but with `validation_type = "dns-txt-token"`.

### Apex domains differ from the docs subdomain
An apex (root) name cannot be a CNAME per the DNS spec, so two things change vs. `docs.`:
1. **Validation = TXT token, not cname-delegation.** `az staticwebapp hostname set ... --validation-method dns-txt-token` returns a token; read it with `... hostname show ... --query validationToken -o tsv`. Put it in a Cloudflare `TXT` record on `@`.
2. **Traffic record = `CNAME` on `@` relying on Cloudflare CNAME flattening.** Cloudflare synthesizes A records at the apex from the SWA default host. Target = `proud-hill-01ebd2b0f.7.azurestaticapps.net`, **DNS only (grey cloud)** during validation (same grey-cloud rule as gotcha 1). The flattened A will be whatever Azure front-end IP Cloudflare resolves the host to (Azure SWA fronts on several anycast IPs, so the apex A and a direct dig of the host can differ; both are valid).
- `www.voidprojects.ai` is NOT configured yet. To add it: a second SWA custom domain (`CNAME www` → the host, grey cloud) or a Cloudflare redirect rule `www → https://voidprojects.ai/$1`.
- **"Apex looks broken locally right after setup" is almost always DNS, not Azure.** The bind, cert, and Cloudflare flattening can all be correct (verify with `curl --resolve voidprojects.ai:443:<azure-ip>` → 200) while the LAN resolver still serves a stale negative answer. Blake's workstation resolves through the homelab Technitium server, which caches NODATA for ~30 min; flush it (see the `technitium-api` skill's negative-cache gotcha). Triage tell: `Resolve-DnsName voidprojects.ai -Server 8.8.8.8` works but the local resolver does not.

## Terraform layout (private repo)
- **`terraform/bootstrap`** — run ONCE, LOCALLY, by an owner. Creates the remote-state storage account, the `rg-protostar-docs` RG, the GitHub OIDC Entra app + federated creds, and the role assignments. Uses **LOCAL gitignored state** (chicken-and-egg: it creates the backend everything else uses) and **Azure-CLI auth** (`az login`), NOT OIDC. CI never touches bootstrap.
- **`terraform/site`** — the SWA + (gated) custom domain. Uses the **azurerm backend** and **OIDC** auth (`use_oidc = true`, `ARM_*` env vars set by the workflow). Runs in CI via the `terraform` workflow: `plan` on PRs, `apply` on push to `main` + `workflow_dispatch`.
- The CI OIDC identity has **Contributor scoped to only `rg-protostar-docs`** + Storage Blob Data Contributor on the state account + (see gotcha 2) **Reader at subscription scope**.

## Gotchas (both bit us bringing the custom domain up)

### 1. cname-delegation needs the Cloudflare CNAME on DNS-only (grey cloud)
`cname-delegation` validation reads public DNS and must see the CNAME pointing at the SWA default host. If the Cloudflare record is **proxied (orange cloud)** it returns Cloudflare anycast IPs and hides the CNAME, so Azure can't validate → `400 BadRequest: CNAME Record is invalid` (ExtendedCode 51021), and the proxied edge serves Azure's default 404 for the unrecognized Host.
- Fix: set the `docs` CNAME to **DNS only (grey cloud)**, target = `orange-river-0d06b480f.7.azurestaticapps.net`. You can re-enable the proxy AFTER validation if desired.
- Check proxy status: `nslookup docs.voidprojects.ai 8.8.8.8` — if it shows the CNAME / an Azure IP it's grey-cloud; Cloudflare anycast (`104.21.x`, `172.67.x`, `2606:4700::`) means it's still proxied.

### 2. The RG-scoped CI identity can't poll SWA custom-domain async ops
Creating/deleting a SWA custom domain is an async Azure operation. The create runs inside the RG (Contributor covers it), but Terraform then polls the operation RESULT at a subscription/location scope OUTSIDE the RG: `Microsoft.Web/locations/<region>/operationResults/<id>`. Contributor-on-RG can't read it →
`403 AuthorizationFailed ... 'Microsoft.Web/locations/operationResults/read'`.
Azure still CREATES the domain, but the apply errors before recording it in state → permanent drift and `already exists - needs to be imported` on every later apply.
- Fix: grant the CI service principal **Reader at subscription scope** (in `terraform/bootstrap`). Least-privilege alternative: a custom role with `Microsoft.Web/locations/operationResults/read` + `operations/read`. Read-only either way.

## Runbooks

### Site is 404 / not available at docs.voidprojects.ai
1. Is content built? `curl -I https://orange-river-0d06b480f.7.azurestaticapps.net/`. 200 = built; the issue is the custom domain. 404 = the deploy never produced an artifact → check the `deploy` workflow (almost always the `fetch-docs` "no docs folder" failure).
2. Custom-domain 404 with `Server: cloudflare`: check proxy status (gotcha 1) and that the binding exists: `az staticwebapp hostname list --name swa-protostar-docs --resource-group rg-protostar-docs`. Empty `[]` = not bound.
3. `errorMessage`/`status` on the hostname entry tells you where validation is (`Validating` → wait; cname-delegation + managed cert takes a few minutes, longer right after a delete due to ~15-min propagation).

### Build / deploy action failing
- `<repo>@main has no 'docs' folder yet` → add the `docs/` folder to that product repo (or fix the manifest), then re-run. Same root cause hits both `build-check` and `deploy`.

### Recreate a drifted custom domain cleanly (no import)
Done when the resource exists in Azure but not in TF state (gotcha 2). Requires an owner Azure login locally:
1. Land + apply the Reader-role RBAC fix: `terraform -chdir=terraform/bootstrap apply` (LOCAL state, `az login` auth — confirm the plan adds ONLY the role assignment, nothing else).
2. Delete the drifted domain so TF can create fresh: `az staticwebapp hostname delete --name swa-protostar-docs --resource-group rg-protostar-docs --hostname docs.voidprojects.ai --yes` (the site 404s until step 3; deletion edge-propagation can take ~15 min).
3. Re-run the site apply: `gh workflow run terraform --repo voidprojectssoftware/protostar-docs-infra`. With Reader in place and the resource absent, the create polls successfully (~6-7 min for validation + cert) and records state. Verify: `curl -I https://docs.voidprojects.ai/` → 200, and `openssl s_client -connect docs.voidprojects.ai:443 -servername docs.voidprojects.ai </dev/null | openssl x509 -noout -subject` → `CN=docs.voidprojects.ai`.

Alternative to teardown: a config-driven `import {}` block adopts the existing domain without downtime, but it doesn't fix the RBAC gap (future destroy/recreate 403s again), and the import plan may want a replace.

### Bootstrap local-state gotcha
`terraform/bootstrap` state is LOCAL and gitignored — it lives only on the owner's machine that first ran it. Before `terraform -chdir=terraform/bootstrap apply`, confirm `terraform/bootstrap/terraform.tfstate` exists locally; without it Terraform tries to recreate the RG/storage/Entra app and fails on `already exists`. Also note the infra repo's default branch was renamed `master`→`main`; a stale local clone tracking `origin/master` shows a false "diverged" — realign with `git reset --hard origin/main` (the old local commit is a superseded Cloudflare-provider design).

## Non-secret identifiers (handy for ops; none are credentials)
- Subscription: `4d53a45e-234e-451f-b733-df7d7aac9e0b` ("Main")
- OIDC app client id: `2d115dbe-6091-4c8e-9b22-40dad052aa1e`; SP object id: `2edc0699-ca13-47aa-8664-5a73d8cc5f98`
- Tenant: `65c0a7b8-1f48-4b79-8bd2-d13415a790e8`
- State backend: RG `rg-tfstate`, storage `stvptfstate`, container `tfstate`
- Repo var that gates the custom domain: `ENABLE_CUSTOM_DOMAIN` (`true` after the CNAME exists)
