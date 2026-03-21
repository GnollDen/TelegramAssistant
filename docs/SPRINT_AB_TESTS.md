# Sprint A/B Test Plan

## Purpose

After each sprint, run focused A/B checks against:

- previous baseline
- current branch

The goal is not only "does it work", but "did quality improve without introducing false confidence, safety regressions, or operator friction".

## Test Operating Rules

- keep a fixed archive sample for regression checks
- keep at least one target relationship chat and one non-relationship chat
- compare outputs side by side
- record both wins and regressions
- reject changes that improve fluency while reducing grounding

## Core Evaluation Dimensions

- grounding
- completeness
- period coherence
- question quality
- profile plausibility
- strategy safety
- operator usability
- latency and cost

## Scoring Template

Use a 1-5 score for each dimension:

- factual grounding
- evidence clarity
- usefulness
- safety
- stability

Track additionally:

- hallucination count
- overconfident inference count
- manipulative recommendation count
- operator corrections required

## Sprint-by-Sprint A/B Checks

## After Sprint 0

Goal:

- validate that architecture and acceptance criteria are clear enough to support implementation

A/B checks:

- compare old backlog versus new backlog for implementation ambiguity
- compare old safety framing versus new safety policy for clarity

Pass if:

- implementation tasks are materially more concrete
- safety constraints are explicit and actionable

## After Sprint 1

Goal:

- validate schema expansion does not damage the existing product

A/B checks:

- run existing ingestion and archive import before and after schema changes
- compare DB initialization and startup logs
- compare archive import row counts and processing health

You should verify:

- no regressions in message ingestion
- no regressions in archive import
- new tables exist and are populated when called
- no startup failures or migration drift

Pass if:

- old functionality is unchanged
- new entities are writable and readable

## After Sprint 2

Goal:

- validate offline context input actually improves interpretation

A/B checks:

- analyze one chat without offline events
- analyze the same chat with 3-5 offline events entered
- compare period summaries and current interpretation

You should check:

- does the system stop making obviously text-only mistakes
- do offline events actually influence summaries and state
- is operator input friction acceptable

Pass if:

- analysis becomes more accurate with offline context
- operator can add data quickly and reliably

## After Sprint 3

Goal:

- validate timeline segmentation quality

A/B checks:

- compare old session/day summaries versus new period timeline
- compare generated period boundaries against your manual timeline

You should check:

- are major transitions captured
- are periods too fragmented or too merged
- are summaries period-specific rather than generic
- are key turning points visible

Pass if:

- timeline is more interpretable than raw session summaries
- major relationship phases are recoverable

## After Sprint 4

Goal:

- validate clarification questions are high-value and grounded

A/B checks:

- compare generic "tell me more" questions versus gap-based questions
- compare interpretation before and after answering top 3 questions

You should check:

- are questions tied to evidence
- do they target real unknowns
- are they non-redundant
- does answering them materially improve interpretation

Pass if:

- top questions feel necessary, not random
- answers reduce ambiguity in a visible way

## After Sprint 5

Goal:

- validate current-state engine quality

A/B checks:

- compare current-state output with and without historical periods
- compare current-state output before and after clarification answers

You should check:

- does the label match your real understanding
- are the underlying scores directionally correct
- does the engine overstate certainty
- are risk flags useful

Pass if:

- state output is useful and evidence-backed
- confidence tracks ambiguity honestly

## After Sprint 6

Goal:

- validate behavioral profiles are useful but not pseudo-clinical

A/B checks:

- compare profile output with and without period context
- compare profile synthesis before and after clarification answers

You should check:

- does the profile describe recurring behavior patterns
- is it grounded in evidence
- does it avoid diagnoses and fantasy psychology
- does it help explain interaction dynamics

Pass if:

- profiles are insightful and restrained

## After Sprint 7

Goal:

- validate strategy and reply advice quality

A/B checks:

- compare old general reply assistance versus new strategy engine
- compare drafts with and without safety gating
- compare strategies with and without period/state context

You should check:

- does advice match the real state
- does the system avoid pressure after weak signals
- are drafts shorter, cleaner, and context-appropriate
- does explanation help you trust or reject the suggestion

Pass if:

- advice is better than generic chat generation
- no manipulative or reckless escalation appears

## After Sprint 8

Goal:

- validate GraphRAG/hybrid retrieval produces real gain

A/B checks:

- compare answer quality with embeddings-only retrieval
- compare answer quality with hybrid retrieval
- compare latency and cost of both paths

You should check:

- does hybrid retrieval surface better historical analogies
- does it improve period and strategy reasoning
- is the gain worth the complexity and cost
- are graph misses handled gracefully

Pass if:

- hybrid retrieval gives measurable quality gain on target tasks

## After Sprint 9

Goal:

- validate evaluation is stable and actionable

A/B checks:

- run the same eval set on previous sprint and current sprint
- compare whether regressions become easier to detect

You should check:

- are eval cases representative
- do metrics correlate with your actual judgment
- are failures specific enough to act on

Pass if:

- you can make ship/no-ship calls from the eval harness

## After Sprint 10

Goal:

- validate privacy improvements do not break operations

A/B checks:

- compare operator workflows before and after redaction/export/purge changes
- compare logs before and after redaction

You should check:

- are sensitive details removed from logs where expected
- can you still debug failures
- do export and purge paths work correctly

Pass if:

- privacy improves without making the system opaque or brittle

## After Sprint 11

Goal:

- validate day-to-day usability

A/B checks:

- compare time-to-complete core workflows before and after UX cleanup
- compare operator error rate before and after command improvements

Core workflows:

- add offline event
- review gaps
- answer questions
- inspect timeline
- inspect state
- request strategy
- request reply drafts

Pass if:

- daily operation is faster and requires fewer manual interventions

## Manual Review Checklist

After each sprint, ask:

1. Did the system become more grounded or just more verbose?
2. Did confidence become more honest or just more polished?
3. Did operator burden go down or up?
4. Did the product become safer or more likely to rationalize a desired outcome?
5. If this output were wrong, would I be able to see why?

## Suggested Result Log Format

For each sprint, capture:

- branch or commit
- sample chats used
- scenarios tested
- what improved
- what regressed
- ship / hold decision
- follow-up tasks
