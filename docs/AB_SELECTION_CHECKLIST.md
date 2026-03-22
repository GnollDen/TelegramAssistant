# A/B Selection Checklist

Use this checklist when selecting the first small scenario set from the full archive.

## Scenario Quality

- The slice is understandable by a human in one pass.
- The slice is not too large for first-pass manual review.
- The slice has a clear reason to exist in the dataset.

## Coverage

- The set contains warming or clearly improving contact.
- The set contains cooling or distancing.
- The set contains ambiguity.
- The set contains fragile or post-conflict contact.
- The set contains at least one strategy-sensitive case.
- The set contains at least one draft/review-sensitive case.
- The set contains at least one strong counterexample.

## Counterexamples

- There is at least one warm-but-platonic case.
- There is at least one logistics-only or low-signal case.
- There is at least one misleading/noisy case where the model may overread.

## Annotation Quality

- Each scenario has `why_this_case_matters`.
- Each scenario has `expected_state`.
- Each scenario has `expected_risks`.
- Each scenario has `expected_non_goals`.
- Optional strategy/draft notes are added where useful.

## Balance

- The dataset does not contain only “interesting romantic” cases.
- The dataset does not contain only easy cases.
- The dataset is not dominated by one single period or one single failure mode.

## Practicality

- Raw messages for the scenario are exported or stored.
- Metadata is saved in a stable format.
- A human can explain in one sentence why the scenario belongs in the test set.

## Ready For First Run

The bootstrap set is ready when:

- it has `8-10` scenarios
- it covers the three main buckets
- each scenario has minimal metadata
- the set is small enough to compare baseline vs candidate manually
