# Pre-Sprint 0 Orchestration Checklist

## Date

2026-03-25

## Purpose

This checklist defines the Pre-Sprint 0 document gate for the current planning pack.

Scope is strictly:
- docs
- planning
- contracts
- gates

Scope does not include:
- broad code implementation
- runtime hardening execution
- Stage 6 feature delivery

Pre-Sprint 0 passes only if PS0-1 through PS0-7 all pass.
If any output contract is ambiguous, missing, or contradicted by another active doc, Sprint 1 is blocked.

## Execution Order

1. PS0-1 planning authority pack
2. PS0-2 sprint-plan gate wording
3. PS0-3 Stage 6 operator contract
4. PS0-4 product-decision closure
5. PS0-5 runtime-topology truth
6. PS0-6 global docs index alignment
7. PS0-7 backlog-status downgrade

## Deliverables

### PS0-1 `docs/planning/README.md`

Output contract:
- declares the planning pack as the sprint-start authority
- points readers first to the Pre-Sprint 0 checklist and sprint plan
- states that `docs/BACKLOG_STATUS.md` is historical status framing, not equal execution authority

Verification:
- reading order includes the checklist and sprint plan before supporting docs
- source-of-truth wording is explicit and non-optional
- archive inputs are marked as supporting context only

Gate:
- fail if this file leaves room to treat `BACKLOG_STATUS.md` as a co-equal sprint authority

### PS0-2 `docs/planning/FULL_IMPLEMENTATION_SPRINT_PLAN_2026-03-25.md`

Output contract:
- maps Pre-Sprint 0 to PS0-1 through PS0-7 deliverables
- states that Sprint 1 cannot start until all PS0 deliverables pass
- keeps Pre-Sprint 0 scoped to planning, topology, and contract cleanup only

Verification:
- the checklist file is referenced as the gate record
- Pre-Sprint 0 wording is pass/fail rather than advisory
- no line implies broad implementation work belongs inside Pre-Sprint 0

Gate:
- fail if Sprint 1 can still be read as allowed before document conflicts are closed

### PS0-3 `docs/planning/STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md`

Output contract:
- defines `dossier` as an operator-facing synthesized artifact
- states what is internal/raw versus operator-facing
- defines first-wave `chat scope` and `case scope` rules

Verification:
- `dossier` is explicitly not a raw dump or reasoning trace
- raw/internal material is allowed as support, not as the default operator artifact
- scope rules default to narrow scope and define when cross-chat scope is allowed

Gate:
- fail if an implementation agent would still need to invent dossier meaning or scope rules

### PS0-4 `docs/planning/PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md`

Output contract:
- records the first-wave product decisions as closed working decisions
- aligns artifact and case scope rules with the PRD
- states that agents should not reopen already-closed first-wave contracts during implementation

Verification:
- first-wave scope and queue/case usage rules are explicit
- the document contains no open-ended wording for required first-wave contracts
- implementation-facing wording points agents to explicit authority docs instead of improvisation

Gate:
- fail if first-wave product semantics still read as open questions

### PS0-5 `docs/planning/RUNTIME_TOPOLOGY_NOTE_2026-03-25.md`

Output contract:
- defines `Ingest`, `Stage 5`, `Stage 6`, `Web`, and `Ops`
- states what is deployable now versus still soft
- states which claims planning docs must not overstate

Verification:
- each runtime area has a distinct meaning
- deployable-now wording separates real, partial, and not-yet-trustworthy surfaces
- non-claims prevent overstatement of runtime isolation or product maturity

Gate:
- fail if another planning doc can still over-claim runtime maturity without contradiction

### PS0-6 `docs/DOCS_INDEX.md`

Output contract:
- identifies the planning pack as the current planning authority
- separates stable reference docs from active sprint-start authority
- downgrades `docs/BACKLOG_STATUS.md` to historical status framing

Verification:
- `BACKLOG_STATUS.md` is not listed under current source-of-truth or current planning authority
- the planning checklist, sprint plan, PRD, gaps interview, and runtime note are grouped together
- documentation hygiene rules do not contradict the planning-authority rule

Gate:
- fail if this file still presents two competing entry points for current execution authority

### PS0-7 `docs/BACKLOG_STATUS.md`

Output contract:
- states at the top that it is historical status framing
- points current execution readers to the planning pack and PS0 checklist
- preserves broad context without claiming sprint-start authority

Verification:
- the warning appears before any status or readiness claims
- the file does not describe itself as the active execution source
- its focus/recommended next steps do not override the sprint plan

Gate:
- fail if this file can still be cited as the active sprint-order authority

## Final Verification

Pre-Sprint 0 is complete only if all of the following are true:
- exactly one current planning authority path exists across active docs
- `BACKLOG_STATUS.md` is downgraded consistently everywhere
- `dossier`, raw/operator-facing boundaries, `chat scope`, and `case scope` are explicit
- runtime-topology wording does not over-claim deployability
- Stage 6 queue/artifact identity and dedupe remains explicit planned work, not implied future cleanup

If any item fails, keep the gate open and resolve the conflicting doc before Sprint 1.
