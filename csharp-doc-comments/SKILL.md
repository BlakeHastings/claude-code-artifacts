---
name: csharp-doc-comments
description: >-
  House standard for writing C# XML documentation comments (/// triple-slash
  doc comments) to Microsoft's conventions. Use when writing, reviewing, or
  tightening <summary>, <remarks>, <param>, <returns>, <typeparam>, or
  <exception> tags on C# types and members, or when doc comments feel too
  verbose. Covers what belongs in summary vs remarks, when to use each tag,
  and the project rule that summaries stay short.
---
# C# XML Doc Comment Standard

How to write `///` documentation comments for C#. The governing idea: **`<summary>` is a short description of *what* a thing is; everything longer or different has a dedicated tag.** Most verbosity problems are misplaced content, not bad writing.

Authoritative source: Microsoft C# reference, [Recommended XML documentation tags](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/recommended-tags) and [XML documentation comments](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/xmldoc/).

## Rules

1. **`<summary>` is one declarative sentence: the "what."** It appears in IntelliSense at every call site, so keep it tight. Microsoft: *"Use the `<summary>` tag to describe a type or a type member."* Do not pack rationale, design history, or parameter detail into it.

2. **The "why" goes in `<remarks>`.** Design intent, the role a type plays in the larger system, trade-offs, "this is the seam shared by X and Y" — all `<remarks>`. Microsoft: `<remarks>` *"add information ... supplementing the information specified with `<summary>`. This tag can include more lengthy explanations."*

3. **Per-parameter and return detail goes in `<param>` / `<returns>`, never folded into the summary.** The compiler validates `<param name="...">` against the actual signature and warns on mismatch, so you get a free correctness check. One `<param>` per parameter.

4. **Use the code-reference tags.** `<see cref="Type"/>` for cross-references (DocFX/Sandcastle turn these into links and the compiler verifies the target exists), `<paramref name="x"/>` to refer to a parameter inside prose, `<c>text</c>` for inline code/identifiers, `<code>` for multi-line blocks.

5. **Write complete sentences ending in a full stop.** Microsoft states this explicitly. Applies to every tag's text, including `<param>` and `<returns>`.

6. **Don't restate the signature or document the obvious.** A summary of "Gets or sets the name." on a `Name` property adds nothing. Document the non-obvious: units, nullability meaning, invariants, side effects, ordering guarantees.

7. **Use `<inheritdoc/>` instead of copy-pasting, and inherit in the right direction.** The base owns the canonical docs; the derived member inherits. The *interface* (or base type) carries the real `<summary>`/`<param>`/`<returns>`; the *implementation*, override, or sync/async twin gets a bare `<inheritdoc/>`. A bare `<inheritdoc/>` on an implementation auto-resolves to the interface member it implements, so no `cref` is needed. Putting the prose on the implementation and pointing the interface at it with `<inheritdoc cref="TheImplementation"/>` is backwards: the contract should document itself, and if the implementation is undocumented the interface inherits nothing. Reserve `<inheritdoc cref="..."/>` for links the compiler can't infer, such as a sync/async twin.

8. **Generic and exception tags when they apply.** `<typeparam name="T">` for each type parameter; `<exception cref="...">` for exceptions a caller can reasonably expect and handle.

## Tag cheat-sheet

| Tag | Holds |
|-----|-------|
| `<summary>` | One sentence: what this type/member is. |
| `<remarks>` | Why it exists, design intent, longer explanation. |
| `<param name="x">` | What one parameter means. Compiler-validated. |
| `<returns>` | What the return value represents. |
| `<value>` | What a property's value represents. |
| `<typeparam name="T">` | What a generic type parameter is for. |
| `<exception cref="E">` | An exception the member can throw. |
| `<see cref="M"/>` | Inline link to another code element. |
| `<paramref name="x"/>` | Refers to a parameter inside prose. |
| `<c>` / `<code>` | Inline / multi-line code formatting. |
| `<inheritdoc/>` | Inherit comments from base/interface/sync-twin. |

## Examples

### Good

```csharp
/// <summary>
/// Resolves the target harness(es), runs their skill discovery, and returns the merged, ordered set.
/// </summary>
/// <remarks>
/// The reusable read-side seam shared by the <c>skills</c> listing command and skill sync, so neither
/// re-implements harness resolution or ordering. <see cref="ISkillCapability"/> is the data source;
/// this orchestrates over it and returns data only, never console markup.
/// </remarks>
internal sealed class SkillService { }

/// <summary>Discovers skills across the selected harness(es), ordered for stable display.</summary>
/// <param name="harnessId">Limit the query to one harness; <see langword="null"/> queries every harness that supports discovery.</param>
/// <param name="harnessHome">Overrides the config root.</param>
/// <param name="projectStart">Where to begin the project-scope walk-up; <see langword="null"/> skips project scope.</param>
/// <returns>The discovered skills ordered by scope then name, or a typed failure.</returns>
public SkillDiscoveryResult Discover(string? harnessId, string? harnessHome, string? projectStart) { }
```

### Bad

```csharp
/// <summary>
/// The read side of "push a local skill": resolve which harnesses to ask, fan out their skill
/// discovery, and return the merged, ordered set. This is the reusable logic seam shared by the
/// <c>skills</c> listing command and (later) skill sync, so neither re-implements harness resolution
/// or ordering. The harness capability is the data source; this is the orchestration over it.
/// Returns data only, never console markup.
/// </summary>
// ^ Rationale crammed into <summary>. IntelliSense shows this whole wall at every call site.
//   Split: one-line <summary>, the rest to <remarks>.

/// <summary>
/// Discovers skills. harnessId limits to one harness (null = all); harnessHome overrides the config
/// root; projectStart is where to begin the project walk-up.
/// </summary>
// ^ Parameters described in prose instead of <param> tags — no compiler validation, harder to read.
```

## Applying this

When asked to tighten verbose doc comments: keep the existing wording, just **relocate** it. Move design rationale from `<summary>` to `<remarks>`, lift per-parameter prose into `<param>` tags, and trim the summary to its first declarative sentence. Information is preserved, not deleted.
