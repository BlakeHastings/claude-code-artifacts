---
name: dotnet-cli-release
description: >-
  Playbook for versioning and releasing a .NET CLI from git tags: MinVer for tag-driven versions,
  release-please for automated stable Release PRs, and a rolling "edge"/nightly prerelease channel.
  Use when setting up or debugging release automation, version numbering, GitHub Actions release
  workflows, prerelease/nightly channels, or self-contained binary publishing for a dotnet CLI.
  Covers the non-obvious gotchas (first-release 1.0.0, Actions PR permission, tag-prefix matching,
  fetch-depth, GITHUB_TOKEN downstream limits).
allowed-tools: Read, Write, Edit, Bash, Glob, Grep
disable-model-invocation: false
---

# .NET CLI Release Engineering

A proven pipeline for shipping a self-contained .NET CLI with **two channels**:

| Channel | Trigger | Version | GitHub Release | Source of truth |
|---|---|---|---|---|
| **stable** | merge the release-please Release PR | `0.1.0` | `vX.Y.Z`, normal release (`releases/latest`) | git tag, created by release-please |
| **edge** | every code change to `main` | `0.1.0-alpha.0.N` | moving `edge` tag, `prerelease`, assets replaced | MinVer commit height |

**The composition that makes it work:** release-please *decides the stable version and creates the
`vX.Y.Z` tag*; **MinVer** *reads that tag at build time and stamps it into the binary*. The edge tag
does not start with `v`, so MinVer ignores it and the channels never collide. The version is read at
runtime from the assembly — never hardcoded.

Use this skill when standing up releases for a new .NET CLI, adding a prerelease channel, or
debugging why a version/release came out wrong.

---

## Part 1 — MinVer (tag-driven version)

`Directory.Build.props` at the repo root (applies to all projects):

```xml
<Project>
  <PropertyGroup>
    <MinVerTagPrefix>v</MinVerTagPrefix>
    <!-- Floor untagged builds so edge reports 0.1.0-alpha.0.N, not 0.0.0-alpha, before the first tag. -->
    <MinVerMinimumMajorMinor>0.1</MinVerMinimumMajorMinor>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MinVer" Version="7.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Read the version at runtime** (trim/AOT-safe — assembly attributes survive trimming). Never keep a
hardcoded `const Version`:

```csharp
using System.Reflection;
internal static class CliInfo
{
    public static string Version { get; } = Resolve();
    private static string Resolve()
    {
        var info = typeof(CliInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "0.0.0";
        var plus = info.IndexOf('+');      // strip "+<git-sha>" build metadata
        return plus >= 0 ? info[..plus] : info;
    }
}
```

## Part 2 — release-please (stable channel)

Three files at the repo root:

`release-please-config.json`
```json
{
  "$schema": "https://raw.githubusercontent.com/googleapis/release-please/main/schemas/config.json",
  "include-component-in-tag": false,
  "bump-minor-pre-major": true,
  "packages": { ".": { "release-type": "simple", "package-name": "<name>" } }
}
```
`.release-please-manifest.json` → `{ ".": "0.0.1" }` and `version.txt` → `0.0.1` (see Gotcha 1).

Workflow: release-please job + a build job gated on its `release_created` output (see Gotcha 5),
which builds the RID matrix and attaches binaries. Day-to-day flow: conventional-commit PR titles →
**squash-merge** → release-please opens a Release PR → merge it to ship.

## Part 3 — edge channel (rolling prerelease)

A separate `edge.yml`, triggered `on: push: branches: [main]` with a `paths:` filter (`src/**`,
`**/*.csproj`, `Directory.Build.props`) so docs/CI-only merges don't rebuild. Build the RID matrix,
then one job re-points the moving `edge` tag and replaces its prerelease assets via the **gh CLI**
(no third-party action needed). Add `concurrency: { group: edge, cancel-in-progress: true }`.

```bash
gh release delete edge --cleanup-tag --yes || true
gh release create edge dist/* --prerelease --target "$GITHUB_SHA" \
  --title "Edge (latest main)" --notes "Rolling prerelease from the tip of main."
```

## Part 4 — channel-aware installer

Same asset names in both releases; only the URL path differs:
- stable → `https://github.com/<repo>/releases/latest/download/<asset>`
- edge → `https://github.com/<repo>/releases/download/edge/<asset>`

Give the installer a `--channel stable|edge` (default stable) flag. **Prereleases are excluded from
`releases/latest`**, which is exactly why edge needs its own fixed-tag URL.

---

## Gotchas checklist (each cost real debugging)

1. **release-please's first release defaults to `1.0.0`.** Pre-major options are *ignored* when the
   manifest is `0.0.0` (their issue #2087). Fix: set manifest + `version.txt` to **`0.0.1`** and add
   `"bump-minor-pre-major": true` → first release computes as `0.1.0`, and breaking changes stay in
   `0.x`. (Alternative one-off: a `Release-As: 0.1.0` commit footer.)
2. **release-please needs a repo/org toggle, not just workflow `permissions:`.** It fails with
   *"GitHub Actions is not permitted to create or approve pull requests."* Enable **Settings →
   Actions → General → Allow GitHub Actions to create and approve pull requests**
   (`can_approve_pull_request_reviews: true`). An org-level setting can override the repo.
3. **Tag prefix must match.** release-please defaults to `include-component-in-tag: true` →
   `name-v1.2.3`, which MinVer's `v` prefix won't read. Set `include-component-in-tag: false` →
   `v1.2.3`.
4. **MinVer in CI needs `fetch-depth: 0`.** The default shallow `actions/checkout` strips tags/history
   and MinVer falls back to `0.0.0-alpha.0`.
5. **`GITHUB_TOKEN` does not trigger downstream workflows.** So a *separate* tag-triggered build won't
   fire when release-please tags via `GITHUB_TOKEN`. Keep the build job in the **same** workflow run,
   gated on `needs.release-please.outputs.release_created`. (A PAT is the alternative.)
6. **The edge tag must not start with `v`** or MinVer will try to parse it. `edge`/`nightly`/`canary`
   are safe.
7. **Prereleases are excluded from `releases/latest`.** Channel-aware installers must hit the fixed
   `edge` tag URL directly, not `latest`.
8. **Pin Actions to commit SHAs** on public repos (`uses: actions/checkout@<40-char-sha> # v4`).
   Supply-chain hardening; this is a *different* setting than `default_workflow_permissions`.
9. **`MinVerMinimumMajorMinor`** keeps untagged/edge builds from sitting at `0.0.0-alpha` before the
   first real tag.
10. **Stable tags belong on `main`** (release-please handles this). A tag left on a feature branch is
    orphaned by squash/rebase merges and MinVer stops seeing it.

## Verify

```bash
dotnet build -c Release
dotnet run --project <proj> -- --version   # untagged -> 0.1.0-alpha.0.N (proves MinVer, not a const)
# tag a commit v0.1.0, rebuild from it -> reports clean 0.1.0
```

## Related

- `github-cli` — the Actions PR-permission toggle, SHA-pinning, and `gh api` Windows quirk.
- MinVer: https://github.com/adamralph/minver — release-please: https://github.com/googleapis/release-please
