# A/B Dataset Bootstrap

## Purpose

Define how to prepare a small but useful A/B evaluation dataset from the full archive without processing the entire history at once.

## Core Principle

Do not start with a random slice of the archive.

Start with:

- a curated scenario set
- small enough to inspect manually
- broad enough to cover the main failure modes

## Recommended First Dataset Size

For the first serious A/B run:

- `8-10 scenarios`
- total roughly `1k-3k messages`

This is enough to reveal:

- extraction drift
- false optimism
- weak ambiguity handling
- bad strategy choices
- poor draft/review behavior

## Recommended Scenario Buckets

## Set A — Timeline / State Cases

Use `4` scenarios:

- warming period
- cooling period
- ambiguous contact
- fragile or post-conflict contact

## Set B — Strategy / Draft Cases

Use `3` scenarios:

- one case where soft strategy is correct
- one case where repair is correct
- one case where draft/review quality matters a lot

## Set C — Counterexamples

Use `2-3` scenarios:

- warm but platonic
- pure logistics / low-signal chat
- misleading third-party or emotionally noisy case

These are critical to prevent romantic overreading and generic over-interpretation.

## Size of One Scenario

A scenario should usually be one of:

- `100-500 messages`
- `1-3 sessions`
- one compact period-sized slice

Do not make the first scenarios too large.

## How To Choose Scenarios From The Archive

Choose slices that are:

- clearly bounded
- understandable by a human in one pass
- representative of a meaningful dynamic

Prefer slices around:

- visible shift points
- conflict or repair moments
- re-engagement phases
- meaningful offline-context periods
- situations where the model could misread intent

## Metadata To Store Per Scenario

Each scenario should have:

- `scenario_id`
- `title`
- `bucket`
- `date_range`
- `chat_ids`
- `message_count`
- `why_this_case_matters`

## Minimal Human Annotation Per Scenario

For the first A/B cycle, store only compact annotations:

- `expected_state`
- `expected_risks`
- `expected_non_goals`
- `notes_on_context`

Optional:

- `expected_strategy_shape`
- `expected_draft_tone`

## What To Avoid

Do not start with:

- one giant month-long slice
- only romantic-looking cases
- only easy cases
- only one type of dynamic

## First A/B Targets

The first dataset should be usable for comparing:

- Stage 5 extraction behavior
- state and ambiguity handling
- strategy generation
- draft/review behavior

## Suggested File Structure

You can store the bootstrap dataset like this:

- `data/ab/scenarios/<scenario_id>/messages.json`
- `data/ab/scenarios/<scenario_id>/meta.json`
- `data/ab/scenarios/<scenario_id>/notes.md`

## Suggested `meta.json` Shape

```json
{
  "scenario_id": "fragile_contact_01",
  "title": "Fragile contact after a warm streak",
  "bucket": "state",
  "date_range": {
    "from": "2024-10-01",
    "to": "2024-10-05"
  },
  "chat_ids": [123456],
  "message_count": 240,
  "why_this_case_matters": "Tests fragile vs warming misclassification.",
  "expected_state": "fragile",
  "expected_risks": [
    "overpressure",
    "premature_escalation"
  ],
  "expected_non_goals": [
    "should_not_read_as_clear_reopening"
  ],
  "notes_on_context": "There was a recent warm patch but reciprocity became unstable."
}
```

## Recommended First Pass

Build the first dataset in this order:

1. pick 8-10 scenarios
2. export/store raw message slices
3. add minimal metadata
4. run baseline
5. run candidate
6. compare scenario-by-scenario

## Success Criterion

The bootstrap dataset is good enough when:

- a human can quickly explain why each scenario exists
- the scenarios cover both positive cases and traps
- failures are interpretable without reading the entire archive
