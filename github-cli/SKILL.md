---
name: github-cli
description: GitHub CLI utilities for repository management, branch protection setup, CI workflows, release tagging, and deployment automation. Handles repo operations via gh API calls with proper JSON payload formatting. Use when adjusting PR scope, isolating commits to specific files, or creating a clean focused pull request from an existing branch.
allowed-tools: Bash, Read, Glob, Grep
disable-model-invocation: false
---

# GitHub CLI Skill

This skill provides expertise in using the GitHub CLI (`gh`) to automate common repository management tasks including:
- Branch protection configuration
- Repository settings updates (visibility, templates, etc.)
- Issue and PR automation
- Release creation and tagging
- Workflow triggers and actions
- Webhook configuration

## Trigger Phrases
"Use the GitHub CLI", "configure branch protection via gh api", "set up repo settings with gh", "automate releases using GitHub CLI", "manage repository with gh command".

---

## Known Issue: JSON Payload Formatting

**Problem**: The `gh api` command has limitations when passing nested JSON objects for certain endpoints. For complex payloads like branch protection, it's more reliable to use a file-based approach.

**Solution Pattern**:
1. Create a JSON configuration file with the full payload structure
2. Use `--input <file>` to pass the complete JSON object
3. Avoid using `-f/--raw-field` for nested objects

### Example: Branch Protection Setup

```bash
# 1. Create the protection config
cat > /tmp/protection.json << 'EOF'
{
  "enforce_admins": true,
  "required_status_checks": {
    "strict": true,
    "contexts": ["ci"]
  },
  "allow_deletions": false,
  "required_pull_request_reviews": {
    "require_code_owner_reviews": false,
    "dismiss_stale_reviews_by_mergers": false,
    "dismiss_stale_reviews": false,
    "require_last_pushers_can_review": false,
    "required_approving_review_count": 1
  },
  "restrictions": null
}
EOF

# 2. Apply the configuration
gh api repos/{owner}/{repo}/branches/master/protection --method PUT \
  --input /tmp/protection.json
```

### Example: Repository Settings Update

For simple field updates, you can use `-f`:
```bash
gh api repos/{owner}/{repo} --method PATCH -F visibility=private
```

For complex nested structures (branches protection, labels, etc.), always use `--input <file>`.

## Common Operations

### Branch Protection

| Setting | Field Path | Example Value |
|---------|-----------|---------------|
| Enforce admins | `enforce_admins` | `true` |
| Required status checks | `required_status_checks.strict` | `true` |
| Status check contexts | `required_status_checks.contexts[]` | `["ci", "lint"]` |
| Allow deletions | `allow_deletions` | `false` |
| PR review count | `required_pull_request_reviews.required_approving_review_count` | `1` |

### Repository Operations

```bash
# Make repo private (org repos only)
gh api repos/{owner}/{repo} --method PATCH -F visibility=private

# Enable issues
gh api repos/{owner}/{repo}/enable-issues

# Create a tag
gh api repos/{owner}/{repo}/git/refs/tags/v1.0.0 --method PUT \
  -f sha=$(git rev-parse HEAD)
```

### Releases

```bash
# Create release from tag
gh release create v1.0.0 --title "Release 1.0.0" --notes "Changelog..."

# Upload assets
gh release upload v1.0.0 ./binary.exe -n binary.exe
```

## Best Practices

### 1. Use Files for Complex Payloads
Never try to inline complex JSON structures in command-line flags. Use heredocs or temporary files.

### 2. Verify Before Apply
When using `--method PUT` or `PATCH`, consider testing with `GET` first:
```bash
# Check current state
gh api repos/{owner}/{repo}/branches/master/protection

# Then apply changes
gh api repos/{owner}/{repo}/branches/master/protection --method PUT \
  --input /tmp/protection.json
```

### 3. Handle Personal vs Organization Repos
Some settings (like `restrictions`) are only available for organization repositories. For personal repos, omit or set to `null`.

### 4. Error Handling
Always check the HTTP status code in scripts:
```bash
if ! gh api ...; then
    echo "Failed to update branch protection" >&2
    exit 1
fi
```

## Validation Checklist

When setting up production repositories, verify:
- [ ] Branch protection is enabled on default branch
- [ ] Required status checks include CI pipeline
- [ ] Force pushes are disabled (`allow_force_pushes` not explicitly true)
- [ ] Pull request reviews required
- [ ] Admin enforcement enabled for critical settings

---

## Gotchas (hard-won)

### Actions can't create or approve PRs by default

Workflows that open PRs (release-please, dependabot-style bots, "create-pull-request" actions) fail
with:

```
GitHub Actions is not permitted to create or approve pull requests.
```

This is a **separate setting** from the workflow's `permissions:` block — granting
`pull-requests: write` in YAML does not cover it. Enable the repo toggle **Settings → Actions →
General → Workflow permissions → "Allow GitHub Actions to create and approve pull requests"**:

```bash
# Read current state (note: NO leading slash — see Windows quirk below)
gh api repos/{owner}/{repo}/actions/permissions/workflow
# Enable PR creation, keeping least-privilege default token at read
gh api -X PUT repos/{owner}/{repo}/actions/permissions/workflow \
  -f default_workflow_permissions=read -F can_approve_pull_request_reviews=true
```

An **org-level** setting (`gh api orgs/{org}/actions/permissions/workflow`, needs `admin:org`) can
override the repo. On a public repo this is low-risk: fork PRs get a read-only token with no secrets,
and the capability is only usable by workflows triggered from trusted refs.

### Pin Actions to commit SHAs (supply-chain)

On public repos, pin every `uses:` to a full 40-char commit SHA with a version comment, so a moving
tag can't be repointed at malicious code:

```yaml
- uses: actions/checkout@<40-char-sha> # v4
```

Resolve the SHA for a tag (dereferences annotated tags to the commit):

```bash
gh api repos/actions/checkout/commits/v4 --jq .sha
```

Pin to the **current major** you already use; don't silently jump majors inside a security pin.

### `gh api` leading slash on Git Bash (Windows)

On Windows Git Bash / MSYS, a leading-slash path is rewritten to a filesystem path:

```
invalid API endpoint: "C:/Program Files/Git/repos/owner/repo/..."
```

Fix: **drop the leading slash** (`gh api repos/owner/repo/...`, not `/repos/...`), or prefix with
`MSYS_NO_PATHCONV=1`.

### Renamed repos redirect

After a repo rename, the old `owner/oldname` URL still works via redirect, but `gh repo view --json
nameWithOwner` shows the canonical new name. Use the canonical name for `gh api repos/...` calls to
avoid surprises.

---

## Scoping a PR to Specific Files

When a branch has commits that touch too many things and you want a focused PR containing only certain files:

**The Problem:** `git cherry-pick <commit> -- <files>` is not valid syntax and will fail with `fatal: bad revision`.

**The Solution:** Use `git show <commit>:<file> > <file>` to extract specific files from a commit, then create a clean commit manually.

### Step-by-Step: Extract Files from a Commit

```bash
# 1. Identify the commit containing the files you want
git log --oneline origin/<branch>

# 2. Create a new focused branch from master/main
git checkout master
git checkout -b feature/focused-change

# 3. Extract only the files you want from the source commit
git show <commit-sha>:LICENSE > LICENSE
git show <commit-sha>:CONTRIBUTING.md > CONTRIBUTING.md
git show <commit-sha>:CHANGELOG.md > CHANGELOG.md

# 4. Commit only those files
git add LICENSE CONTRIBUTING.md CHANGELOG.md
git commit -m "docs: add repo production elements"

# 5. Push and create PR
git push origin feature/focused-change --set-upstream
gh pr create --title "..." --base master --head feature/focused-change --body "..."
```

### Auditing What a PR Contains

Before creating or adjusting a PR, audit what files are actually in the branch vs master:

```bash
# See all unique files changed across all commits in the PR branch
git diff <base-branch>...HEAD --name-only | sort -u

# See what each individual commit changed
git log --oneline origin/<pr-branch>
git show <commit-sha> --stat

# See full diff of a single commit
git diff <commit-sha>^..<commit-sha> --stat
```

### Checking Remote Branch State

```bash
# List all remote branches
git branch -r | grep <pattern>

# See commits on remote branch not yet on master
git log --oneline master..origin/<branch>
```

### Creating a PR with Body via Heredoc

Always use a heredoc to avoid quoting issues in PR body:

```bash
gh pr create --title "Add production files" --base master --head feature/my-branch --body "$(cat <<'EOF'
## Summary

- LICENSE - MIT License
- CONTRIBUTING.md - Contribution guidelines
- CHANGELOG.md - Release tracking

## Why

Makes the repo safer and more professional for contributors.
EOF
)"
```

---

## Related Skills
- `dotnet-cli-release`: Full release pipeline for a .NET CLI (MinVer + release-please + edge channel); the gotchas above came from that work.
- `litellm`: For OpenCode plugin development and LiteLLM configuration
- `orchestrator`: For complex multi-step deployment tasks involving GitHub operations
