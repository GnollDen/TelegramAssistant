# Stage 5 Micro Sprint Acceptance

Micro sprint принимается, если выполнено следующее:

## 1. Voice path

- warning-noise materially reduced or clearly bounded
- sticky voice tail behavior is improved or clearly explained
- no new instability introduced into media/audio path

## 2. Session semantics

- `is_finalized` behavior is understood
- if it was a defect, a minimal fix is in place
- if it is expected behavior, that is now explicit and coherent

## 3. Extraction quality

- false positives on jokes/questions are reduced in practical cases
- weak micro-claims are reduced
- duplicate signals are reduced

## 4. Summary quality

- no regression in summary usefulness
- truncation/fallback-like outcomes are reduced
- over-interpretive relational phrasing is reduced

## 5. Operational safety

- build passes
- stage5 smoke passes
- no obvious regression in Stage 5 runtime flow

## Reject conditions

Micro sprint should be rejected if:

- it introduces a new Stage 5 blocker
- it meaningfully regresses extraction coverage
- it makes summaries less useful in practice
- it expands into broad redesign instead of targeted polish

