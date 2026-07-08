# Foundations of Diátaxis

The theory behind the four modes, so you can reason about edge cases rather than memorize rules.

## Why exactly four

Diátaxis rests on one principle: documentation must serve the needs of its users. A user is a
practitioner inside a domain of skill, and skill has a structure. Map it on two axes:

- **Action vs cognition.** Every craft contains both *action* (practical knowledge, knowing
  *how*) and *cognition* (theoretical knowledge, knowing *that*). Distinct, but inseparable.
- **Acquisition vs application.** A practitioner is sometimes *acquiring* skill (studying) and
  sometimes *applying* it (working). These are "counterparts, distinct but bound up with each
  other."

Two binary dimensions produce a 2x2 that covers the whole territory of a craft. That
completeness is the argument for four and only four: "There are only two dimensions ... This is
why there are necessarily four quarters to it, and there could not be three, or five."

| Need (content + state)              | Mode         |
| ----------------------------------- | ------------ |
| action + acquisition (learning to do) | Tutorial     |
| action + application (doing a task) | How-to guide |
| cognition + application (looking up) | Reference    |
| cognition + acquisition (understanding) | Explanation |

## The learning/work cycle

The four are not a menu; they correspond to where the user is in their journey. A newcomer
*studies* by *doing* (tutorial), grows competent and *works* toward goals (how-to), consults
facts while working (reference), and steps back to *understand* the field (explanation). Good
documentation supports the whole cycle, with each mode strong in its quadrant.

## Diátaxis is an approach, not a filing scheme

"Diátaxis is not a scheme into which documentation must be placed - four boxes." It is "a way
of working with documentation, that identifies four different needs and uses them to author and
structure documentation effectively." Do not weaponize the four labels into bureaucracy. Use
them to ask, of any content: whose need, in which state, does this serve, and is it serving it
well.

## Quality: functional vs deep

- **Functional quality** is measurable and objective: accuracy, completeness, consistency,
  usefulness, precision. These are independent constraints (a page can be accurate yet
  incomplete) and they are the floor. Diátaxis does not itself supply functional quality; it
  makes lapses *visible* by clarifying what each page is for.
- **Deep quality** is subjective and interdependent: feeling good to use, having flow, fitting
  human needs, being beautiful, anticipating the user. It is "conditional upon functional
  quality." You cannot reach it while functional quality is broken.

Diátaxis chiefly buys deep quality by preventing disruptive shifts, e.g. "when a digression
into explanation interrupts a how-to guide." When content sits in the right mode, the reader
stops being yanked between needs and the docs start to "feel right."

## Scaling to complex hierarchies

- The four scale by **nesting**. Large sets use landing pages that *introduce and overview*,
  not bare link dumps.
- **Keep lists short.** "Seven items seems to be a comfortable general limit." Longer lists are
  hard to read unless they have an inherent mechanical order (alphabetical, numeric). Past the
  limit, regroup into subsections.
- **Match the product as the user experiences it.** Products serving different audiences
  (developers vs contributors, land/sea/air users, different deployments) may need more than
  one hierarchy. Ask "what *is* the product, as it is for the user," and structure to that.
- **"Documentation should be as complex as it needs to be"**, and no more. Complexity is fine
  when it is logical and serves the reader; schematic purity for its own sake is not the goal.

## The author's honesty

Especially for tutorials, the author cannot fully predict the reader's environment. Test
extensively, then "admit ignorance transparently" and rely on user feedback to close the gaps.
Documentation is never truly finished; it is "complete, not finished" at each step.
