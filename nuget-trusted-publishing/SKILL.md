---
name: nuget-trusted-publishing
description: >-
  Playbook for releasing a .NET NuGet library (not a CLI) to nuget.org with release-please + MinVer +
  OIDC Trusted Publishing, so no long-lived API key is stored. Use when setting up or debugging NuGet
  package publishing, GitHub Actions release workflows, the NuGet/login OIDC step, a trusted-publishing
  policy, release-please Release PRs, MinVer tag-driven versions, or a multi-package monorepo. Covers the
  hard gotchas: the "Actions can't create pull requests" toggle, first-release 1.0.0 vs Release-As, the
  401 workflow-file mismatch, the NUGET_USER secret, id-token permission, and NU5104.
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
disable-model-invocation: false
---

# NuGet Library Release via Trusted Publishing

Ship one or more NuGet packages from a monorepo with **no stored API key**. Sibling of the
`dotnet-cli-release` skill — that one publishes CLI binaries with an edge channel; this one publishes
**library packages** to nuget.org via OIDC. They share the release-please + MinVer base.

## The composition

1. **release-please** decides the stable version from Conventional Commits, maintains a "Release PR" and
   `CHANGELOG.md`, and on merge creates the `vX.Y.Z` tag + GitHub Release.
2. **MinVer** reads that tag at build time and stamps it into every package. Set `MinVerTagPrefix=v`.
3. **Trusted Publishing**: the publish job requests a GitHub OIDC token, exchanges it at nuget.org via
   `NuGet/login@v1` for a short-lived (1 hour) API key, and `dotnet nuget push`es with that. No secret key.

Keep publish in the **same workflow run** as release-please, gated on its `release_created` output. This
sidesteps the rule that `GITHUB_TOKEN`-created tags/releases do **not** trigger other workflows — a
separate `release:published`-triggered publish workflow would silently never fire unless release-please
runs as a GitHub App or PAT instead of `GITHUB_TOKEN`.

## Files

`release-please-config.json` (manifest-driven, `release-type: simple` so MinVer owns the version — do NOT
use a csproj-editing release type):

```json
{
  "include-component-in-tag": false,
  "bump-minor-pre-major": true,
  "packages": { ".": { "release-type": "simple", "package-name": "your-lib" } }
}
```

`.release-please-manifest.json` — start at `{ ".": "0.0.0" }` for a brand-new repo (see gotcha 2).

Per-package `.csproj`: every **packable** project must reference `MinVer` (PrivateAssets=all). Use central
package management (`Directory.Packages.props`) and shared metadata in `Directory.Build.props`.

`.github/workflows/release-please.yml` — the gated publish job:

```yaml
on: { push: { branches: [main] } }
permissions: { contents: write, issues: write, pull-requests: write }
jobs:
  release-please:
    runs-on: ubuntu-latest
    outputs:
      release_created: ${{ steps.rp.outputs.release_created }}
    steps:
      - uses: googleapis/release-please-action@v4
        id: rp
  publish:
    needs: release-please
    if: ${{ needs.release-please.outputs.release_created }}
    runs-on: ubuntu-latest
    permissions:
      contents: read
      id-token: write          # REQUIRED for the OIDC token
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }   # MinVer needs full history + the new tag
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - run: dotnet build -c Release
      - run: dotnet test -c Release --no-build
      - run: dotnet pack -c Release --no-build --output artifacts
      - name: NuGet login (OIDC -> temp key)
        uses: NuGet/login@v1
        id: login
        with: { user: ${{ secrets.NUGET_USER }} }   # nuget.org ACCOUNT name, not email
      - name: Push
        run: dotnet nuget push "artifacts/*.nupkg" --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## nuget.org side (one-time)

- nuget.org → your username → **Trusted Publishing** → add a policy: Repository Owner, Repository,
  **Workflow File** = the **bare filename** (`release-please.yml`), Environment = empty (unless the job
  uses `environment:`). Public repos activate on first publish; private repos get a 7-day pending window.
- Repo secret **`NUGET_USER`** = your nuget.org account/profile name (NOT your email).

## Gotchas (the expensive ones)

1. **"GitHub Actions is not permitted to create or approve pull requests."** release-please does all its
   work then fails at PR creation. Fix: repo **Settings → Actions → General → Workflow permissions →**
   check **"Allow GitHub Actions to create and approve pull requests."** (Or run release-please as a
   GitHub App / PAT — which also lets you split publish into its own `release:published` workflow.) This
   is an org/repo setting only the owner can flip; can be set via
   `gh api -X PUT repos/OWNER/REPO/actions/permissions/workflow -F default_workflow_permissions=write -F can_approve_pull_request_reviews=true`.
2. **First release proposes `1.0.0`, not `0.1.0`.** release-please jumps to 1.0.0 on the first release
   regardless of `bump-minor-pre-major`. To ship 0.1.0 first, push an empty commit with a `Release-As`
   footer and let it retarget the open Release PR:
   `git commit --allow-empty -m "chore: release 0.1.0" -m "Release-As: 0.1.0"`.
3. **OIDC 401 "Workflow mismatch … expected '.github/release-please.yml', actual 'release-please.yml'."**
   The trusted-publishing policy's **Workflow File** must be the bare filename (`release-please.yml`), with
   no `.github/` or `.github/workflows/` prefix. The repo workflow itself is fine; only the policy string
   is wrong.
4. **"Input required and not supplied: user."** The `NUGET_USER` secret is missing or empty. Its value is
   the nuget.org account name, not an email.
5. **`id-token: write` is required** on the publish job, or the OIDC token is never issued.
6. **`fetch-depth: 0`** on checkout, or MinVer can't see the tag and stamps `0.0.0-alpha.0`.
7. **NU5104 "stable release should not have a prerelease dependency"** at pack time usually means one
   packable project did NOT reference MinVer (so it defaulted to `1.0.0`) while the others are prerelease.
   Add MinVer to every packable project so versions agree. (To pack locally at a chosen version, use
   `-p:MinVerVersionOverride=0.1.0`, not `-p:Version=`, which MinVer overrides.)
8. The OIDC key lives **1 hour** — run `NuGet/login` immediately before the push, not early.
9. After a successful push the log says **"Your package was pushed."** The nuget.org flat-container index
   (`/v3-flatcontainer/<id>/index.json`) lags minutes-to-~15 before the package is listable — a 404 right
   after a green push is normal, not a failure.

## Verify

`gh run watch <id> -R OWNER/REPO --exit-status`; then check the push step log for "Your package was
pushed" rather than waiting on the index. Confirm the tag exists (`git ls-remote --tags origin`).

See `dotnet-cli-release` for the MinVer/release-please base and the CLI/edge-channel variant.
