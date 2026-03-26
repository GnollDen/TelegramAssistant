# Initial Chronology Reconstruction Plan

## Date

2026-03-26

## Purpose

This document defines the backlog and sprint track for the missing chronology-first reconstruction layer.

The goal is to make the system capable of:
- running an initial historical pass from the start of available communication to the current moment
- building an explicit temporal baseline before normal incremental Stage 6 operation
- explaining artifacts and cases through chronology, not only through latest-signal heuristics

## Why This Track Exists

The current system already has:
- Stage 5 substrate
- periods and transitions
- Stage 6 artifacts
- Stage 6 cases
- auto-case generation
- bot and web operator surfaces

But the current product gap remains:
- there is no clearly bounded first-pass chronology contract
- the system is still easier to understand as a recent-signal processor than as a chronology-driven reconstruction engine
- case provenance is not yet explained well enough through temporal evolution

This track closes that gap.

## Core Product Requirement

The system must support two distinct modes:

1. Initial chronology reconstruction
- full or bounded historical pass
- from beginning of available history to now
- builds the baseline temporal map and first-wave artifacts

2. Incremental maintenance
- steady-state updates after the baseline exists
- message deltas
- offline events
- clarifications
- artifact refresh
- case reopen/recompute

The second mode should not be treated as a replacement for the first.

## Main Outcomes

After this track, the system should be able to:
- reconstruct periods from history
- explain key transitions and ambiguities
- build initial timeline/dossier/current_state/clarification baseline from chronology
- generate cases that are explainable through temporal development
- estimate and control the cost of large historical passes
- run a safe pilot on a bounded slice such as the first year

## Backlog Blocks

## P0 Chronology Contract

Define:
- what counts as an initial chronology pass
- which data sources are included:
  - messages
  - sessions
  - offline events
  - clarifications
  - outcomes
  - existing artifacts where reusable
- required outputs:
  - period map
  - transition map
  - baseline timeline
  - baseline dossier
  - baseline current_state
  - baseline clarification state

## P0 Two-Mode Runtime Model

Define and implement:
- initial reconstruction mode
- incremental maintenance mode
- switching rules
- rebuild triggers
- partial rebuild rules

## P0 Cost and Runtime Model

Estimate:
- token/call cost for full history
- token/call cost for bounded passes
- latency
- throughput
- chunking windows
- cheap vs expensive path split
- resumability and checkpointing needs

## P1 First-Year Pilot

Run a bounded pilot on:
- the first year of history

Measure:
- runtime cost
- quality of periods
- quality of artifacts
- usefulness of chronology-derived cases
- operator comprehension gain

## P1 Chronological Segmentation

Build or tighten:
- period segmentation from start to now
- transition detection
- key-event extraction
- ambiguity/conflict marking by period

## P1 Baseline Artifact Build

Use chronology pass to build:
- initial timeline
- initial dossier
- initial current_state
- initial clarification_state
- optional bounded behavioral baseline

## P1 Case Provenance via Chronology

Every important case should become explainable through:
- what period or transition it came from
- what changed
- why it matters now
- what evidence moved the system from previous state to current case

## P2 Operator Controls

Operator should eventually be able to run:
- full rebuild
- first-year pilot
- last N months rebuild
- single-period rebuild
- dry run / estimate only
- progress/status inspection

## Recommended Sprint Order

1. Pre-Sprint C0: Chronology Contract and Cost Baseline
2. Sprint C1: Reconstruction Engine and Two-Mode Runtime
3. Sprint C2: First-Year Pilot and Measurement
4. Sprint C3: Chronology-Driven Artifact and Case Provenance
5. Sprint C4: Full-History Rollout Controls and Economics Hardening

## Pre-Sprint C0: Chronology Contract and Cost Baseline

### Goal

Lock the chronology-pass contract before implementation begins.

### Scope

- define initial reconstruction inputs and outputs
- define initial vs incremental mode boundaries
- define which existing Stage 5/Stage 6 artifacts can be reused
- define cost model dimensions
- define first-year pilot as the first required bounded run

### Required outputs

1. Chronology reconstruction contract note
2. Two-mode runtime note
3. Cost-model note
4. First-year pilot definition

### Exit criteria

- chronology pass is explicit
- implementation does not have to invent “what first pass means”
- full-history run is not attempted without cost framing

## Sprint C1: Reconstruction Engine and Two-Mode Runtime

### Goal

Create the bounded execution path for initial chronology reconstruction.

### Scope

- reconstruction runner
- checkpoint/resume rules
- bounded range execution
- initial vs incremental mode wiring
- progress/status model

### Exit criteria

- system can run a bounded chronology pass over a selected date range
- run does not depend on ad hoc manual stitching
- checkpoint/resume semantics are explicit enough for pilot use

## Sprint C2: First-Year Pilot and Measurement

### Goal

Run and evaluate the first bounded chronology pilot.

### Scope

- first-year pilot execution
- cost/latency measurement
- chronology quality review
- artifact usefulness review
- initial case usefulness/noise review
- recommendations for full-history rollout

### Exit criteria

- first-year pass can be executed and inspected
- cost is measured, not guessed
- quality risks are documented before any full-history run

## Sprint C3: Chronology-Driven Artifact and Case Provenance

### Goal

Make artifacts and cases explainable through chronology.

### Scope

- chronology-first artifact provenance
- chronology-first case provenance
- “why now” explanations
- “what changed” explanations
- UI/API fields for human-readable provenance

### Exit criteria

- operator can understand where a case came from in temporal terms
- artifacts can point back to periods, transitions, and key evidence windows

## Sprint C4: Full-History Rollout Controls and Economics Hardening

### Goal

Prepare the system for controlled large-scale chronology runs.

### Scope

- full-history rollout controls
- estimate-only mode
- guardrails on cost/time
- chunking and scheduling policy
- economics hardening

### Exit criteria

- full-history run is operationally controlled
- large pass cost is bounded and observable
- operator can choose between full, partial, and pilot modes

## Pilot-First Rule

Do not start with a blind full-history run.

Preferred order:
1. contract
2. bounded execution engine
3. first-year pilot
4. review quality and economics
5. only then decide whether to run full history

## Manual Review Gates

Manual review is required:

1. After Pre-Sprint C0
- contract and cost sanity

2. After Sprint C2
- first-year pilot quality/cost gate

3. After Sprint C3
- chronology provenance usefulness gate

4. After Sprint C4
- full-history rollout safety gate

## Program Outcome

If completed, this track should produce:
- an explicit chronology-first initialization mode
- better artifact and case explainability
- safer economics for large historical passes
- a bounded pilot path before any expensive full-history run
