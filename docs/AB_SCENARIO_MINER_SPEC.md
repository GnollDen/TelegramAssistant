# A/B Scenario Miner Spec

## Purpose

Define a semi-automatic way to prepare A/B scenario candidates from the archive.

The miner should not decide the final dataset by itself.
It should produce a candidate pool for human selection.

## Core Principle

The miner should return:

- a manageable list of candidate scenarios
- each with a clear reason for selection
- enough metadata for a human to choose the final 8-10 cases

## Output Goal

The miner should ideally produce:

- `20-40` candidates

From those, a human should choose:

- `8-10` final scenarios

## Candidate Types To Mine

The miner should look for at least these buckets:

## 1. State / Timeline Candidates

- warming slices
- cooling slices
- ambiguous slices
- fragile or post-conflict slices
- re-engagement slices

## 2. Strategy / Draft Candidates

- cases where low-pressure strategy is likely best
- cases where repair is likely relevant
- cases where review/draft quality matters a lot

## 3. Counterexample Candidates

- warm but platonic
- logistics-only or low-signal slices
- emotionally noisy or misleading slices
- third-party-heavy slices that may distort interpretation

## Input Signals To Use

The miner should use existing system artifacts where available:

- periods
- transitions
- unresolved transitions
- conflicts
- clarifications
- state snapshots
- strategy records
- draft/review records
- outcome records
- offline events
- graph/network context

And basic raw substrate metadata:

- message count
- session count
- date range
- chat ids

## Candidate Selection Heuristics

Prefer slices that have one or more of:

- material state shift
- unresolved transition
- high ambiguity
- high review priority
- conflict presence
- strong strategy relevance
- strong draft/review relevance
- strong third-party context

## Counterexample Heuristics

Explicitly mine candidates with:

- warm interaction but low romantic evidence
- mostly logistics / coordination
- low-value chatter that should remain low-signal

These are required to prevent biased A/B results.

## Candidate Boundaries

Each candidate should usually be bounded as:

- one compact period
- one transition-centered slice
- one small cluster of sessions

Avoid giant candidates in the first pass.

## Candidate Metadata

Each candidate should include:

- `candidate_id`
- `title`
- `bucket`
- `date_range`
- `chat_ids`
- `message_count`
- `session_count`
- `source_artifacts`
- `why_selected`
- `risk_of_misread`
- `suggested_expected_state`
- `suggested_expected_risks`

## Suggested Output Shape

The miner can emit results in JSON like:

```json
{
  "candidate_id": "cand_fragile_01",
  "title": "Fragile contact after warm streak",
  "bucket": "state",
  "date_range": {
    "from": "2024-10-01",
    "to": "2024-10-05"
  },
  "chat_ids": [123456],
  "message_count": 240,
  "session_count": 3,
  "source_artifacts": {
    "period_ids": [17],
    "transition_ids": [8],
    "conflict_ids": [4],
    "strategy_record_ids": [12]
  },
  "why_selected": "High ambiguity with fragile reciprocity after a short warm patch.",
  "risk_of_misread": "Could be mislabeled as warming or reopening.",
  "suggested_expected_state": "fragile",
  "suggested_expected_risks": [
    "overpressure",
    "premature_escalation"
  ]
}
```

## Selection Constraints

The miner should try to avoid:

- too many candidates from the same narrow time window
- only high-drama cases
- only romantic-looking cases

## Human In The Loop

The final scenario set must still be selected by a human.

The miner should help the human answer:

- why this slice matters
- what category it belongs to
- what failure mode it can expose

## First Implementation Goal

The first version of the miner should be simple:

- deterministic
- artifact-driven
- good enough to produce candidate pools

It does not need to be perfect.
