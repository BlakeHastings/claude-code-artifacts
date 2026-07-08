# Writing reference (information-oriented)

Reference is the "technical description of the machinery and how to operate it": the
propositional knowledge a user consults while working. It serves *application* through
*cognition*. Think map, encyclopedia entry, or the nutrition label on food: authoritative
facts in a standard format, consulted, not read through.

## The one imperative

**"Describe and only describe. Neutral description is the key imperative."** Reference offers
"truth and certainty, firm platforms on which to stand." The user trusts it the way they trust
a map, so it must be accurate, complete, and free of anything that is not description.

## It must

- **Describe the product factually and completely**: APIs, classes, functions, commands,
  options, parameters, return values, errors, configuration keys.
- **Mirror the structure of the product.** The docs and the product should be navigable in
  parallel, like a map to its territory.
- **Be consistent and predictable.** Same layout, same ordering, same headings for every
  entry, so the reader learns the pattern once.
- **Include examples that illustrate**, without turning into instruction.
- **Be neutral and precise.**

## It must not

- **Explain, instruct, discuss, or opine.** No "why," no recipes, no marketing, no
  interpretation. Those belong in explanation, how-to, or elsewhere.
- **Assume auto-generated output is enough.** Generated API docs are a starting point, not a
  finished reference; they still need structure, completeness, and good examples.
- **Deviate from the standard format** entry to entry.

## Voice and patterns

- Declarative, present tense, describing the thing: "`timeout` is the maximum wait in ms.
  Defaults to 30000. Throws `RangeError` if negative."
- Tables and lists for parameters, options, return values, error codes.
- Cross-link to how-to guides ("to use this, see …") and explanation ("for the rationale,
  see …") rather than embedding them.

## Checklist

- [ ] Every public surface is described; nothing important is missing.
- [ ] Description only: no instructions, no opinions, no rationale inline.
- [ ] Structure mirrors the product and is consistent across entries.
- [ ] Examples demonstrate usage without becoming a tutorial.
- [ ] A working user can find a fact fast and trust it.
