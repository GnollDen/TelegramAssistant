# PRD: Operator Interaction Layer

Date: `2026-04-03`  
Status: draft  
Role: product PRD for operator-facing Telegram and Web surfaces on top of the current person-intelligence core

## 1. Goal

Build the operator interaction layer for the current core so an operator can:

- inspect person-centered outputs
- resolve clarification and review blockers
- add offline communication context
- receive workflow-critical alerts
- interact with the system through:
  - Telegram bot
  - Web workspace

This layer does not replace the core pipeline and does not restore the old legacy Stage6 bot model.

## 2. Core Product Position

The current backend/core already provides:

- Stage5 substrate
- Stage6 bootstrap
- Stage7 durable formation
- Stage8 scoped recompute and outcome gating
- bounded baseline proof on a real seeded scope

The operator interaction layer is the product surface over that core.

## 3. Main Priority

`P0` for the operator interaction layer is:

- **Resolution system**

Everything else is secondary in delivery order:

- assistant mode
- offline communication capture
- alerts expansion
- richer web workspace

The PRD is full-scope, but implementation priority is explicitly resolution-first.

## 4. Shared Principles

### 4.1 Resolution is not just workflow status

Every resolution action should:

1. present the problem
2. collect operator decision
3. collect operator context if needed
4. optionally ask follow-up questions
5. save the result as new system input
6. trigger bounded recompute in the affected scope
7. automatically re-evaluate related conflicts

### 4.2 Deep analysis belongs to web

Telegram is compact and action-oriented.
Web is the primary deep-analysis workspace.

### 4.3 Trust must be explicit

The system must explicitly distinguish:

- `Fact`
- `Inference`
- `Hypothesis`
- `Recommendation`

Trust factor is shown in **percent**.

### 4.4 No MCP dependency

The operator layer should not depend on MCP.

Memory and context should come from:

- durable read models
- short-term session state
- retrieval/context assembly
- bounded tools/actions in the application layer

## 5. Telegram Bot

## 5.1 Role

Telegram bot is a compact operator cockpit.
It is not a free-form consumer chatbot and not the old legacy Stage6 bot.

Its responsibilities:

- live operator assistant dialogue
- compact resolution cards and actions
- offline event capture
- workflow-critical alerts

## 5.2 Start UX

The default Telegram entry is a **mode selection card**.

Modes:

- `Assistant`
- `Resolution`
- `Offline Event`
- `Alerts`

The bot does not start directly in free chat mode.

## 5.3 Scope Model

The bot works in the context of one active tracked person.

Rules:

- operator selects one tracked person from those fixed for analysis
- that person stays active until explicitly changed
- if only one tracked person exists, it is auto-selected
- operator identity is not treated as the tracked person target

Session state should store:

- active tracked person
- active mode
- recent cards shown
- unfinished operator workflow step

## 5.4 Assistant Mode

Purpose:

- answer operator questions about the active tracked person
- provide analysis
- explain system conclusions
- help with communication strategy

Assistant response structure:

1. `Short Answer`
2. `What Is Known`
3. `What It Means`
4. `Recommendation`
5. `Trust: NN%`

The answer must explicitly mark content as:

- Fact
- Inference
- Hypothesis
- Recommendation

If deeper inspection is needed:

- provide `Open in Web`

Telegram should stay compact.

## 5.5 Resolution Mode

This is the P0 Telegram workflow.

It must support:

- clarification items
- review items
- contradictions
- blocked branches
- missing-data items

Each resolution card should include:

- title
- item type
- short summary
- why it matters
- trust factor
- status
- evidence count
- last updated

Actions:

- `Approve`
- `Reject`
- `Defer`
- `Clarify`
- `Evidence`
- `Open Web`

Telegram handles compact action.
Deep analysis goes to web.

## 5.6 Offline Event Mode

Purpose:

- capture offline communication not present in chat logs

Flow:

1. operator presses `Offline Event`
2. operator enters a text summary
3. optionally attaches recording/audio
4. system analyzes the summary
5. system builds a pool of clarification questions with importance
6. system selects the most important questions
7. asks them one by one
8. stops when information gain is exhausted
9. saves a structured offline event record

Stopping rule:

- if operator repeats themselves
- if operator no longer adds new context
- if answers become "I don't know / don't remember"

then the system should stop the clarification loop and offer partial-confidence save.

## 5.7 Alerts

Telegram should proactively push only workflow-critical alerts.

P0 automatic alerts:

- critical clarification blocks
- review items blocking workflow
- runtime degraded state affecting active workflow
- materialization failures that stop progression
- control-plane stops on active tracked person

Telegram should not push all state changes by default.

## 6. Web

## 6.1 Role

Web is the primary deep-analysis workspace.

It is used for:

- detailed person inspection
- dossier/profile review
- pair/timeline analysis
- evidence drilldown
- rich resolution workflows
- bounded runtime/control visibility

## 6.2 Home

Web home is **navigation-first** with a small operational dashboard.

Home should contain:

- main navigation buttons
- unresolved counts on buttons
- critical workflow blockers
- system status
- recent important changes

Suggested buttons:

- `Resolution (N)`
- `Persons (N)`
- `Alerts (N)`
- `Offline Events`
- `Assistant`

Dashboard should stay light:

- `System status: normal / degraded`
- `Critical unresolved items`
- `Active tracked persons`
- `Recent significant updates`

## 6.3 Main Web Sections

### Resolution

This is the P0 section.

Contains:

- resolution queue
- filters and sorting
- resolution item detail
- evidence panel
- clarification side panel
- bounded operator actions
- recompute feedback

### Persons

Tracked-person list with:

- search
- quick entry into person workspace
- unresolved badges
- recent update signals

### Person Workspace

Main deep-analysis surface for one tracked person.

Subsections:

- `Summary`
- `Dossier`
- `Profile`
- `Pair Dynamics`
- `Timeline`
- `Evidence`
- `Revisions`
- `Resolution`

### Alerts

Grouped list of workflow-critical alerts with more context than Telegram.

### Offline Events

Inspection and refinement of captured offline events.

## 6.4 Resolution in Web

Resolution items must be sorted by importance:

- `critical`
- `high`
- `medium`
- `low`

Within the same priority:

- newer or more active items first

Each resolution item should show:

- title
- type
- summary
- why it matters
- affected object/family
- trust factor
- evidence count
- last updated
- recommended next action if available

Supported item types:

- clarification
- review
- contradiction
- missing_data
- blocked_branch

Resolution actions:

- approve
- reject
- defer
- request clarification
- inspect evidence
- open related object

## 6.5 Clarification Side Panel

Interactive clarification should open in a **side panel / drawer**.

This keeps:

- the original item visible
- the operator in context
- the workflow compact

Follow-up questions should be asked only when justified:

- ambiguity is high
- operator chooses reject/defer/clarify
- operator response lacks required information
- expected information gain is high

## 6.6 Recompute After Resolution

After a resolution action, the system should:

1. save the operator decision and explanation
2. save structured clarification payload if any
3. trigger bounded recompute for the affected scope
4. re-evaluate related conflicts
5. automatically close conflicts that are no longer relevant
6. surface the updated state in UI

The UI should show:

- recompute running / done
- auto-resolved related conflicts
- remaining unresolved conflicts
- newly emerged conflicts if any

## 6.7 Person Workspace

### Summary

Default tab.

Shows:

- tracked person identity
- current summary state
- top conclusions
- trust signals
- unresolved issues
- recent changes

### Dossier

Durable, evidence-backed person record.

Contains:

- stable facts
- durable traits
- relevant states
- contradictions
- provenance links
- revision awareness

### Profile

Readable operator-facing synthesis.

Contains:

- concise narrative profile
- communication-relevant synthesis
- behavior/state interpretation
- trust factor
- open uncertainties

Boundary:

- dossier = durable record
- profile = readable synthesis

### Pair Dynamics

Shows:

- operator ↔ tracked relation state
- recent interaction dynamics
- direction of change
- important shifts
- trust factor
- unresolved ambiguity

### Timeline

Shows:

- meaningful events
- timeline episodes
- story arcs
- chronology of relevant changes

Should support:

- summary view
- drilldown
- evidence access

### Evidence

Should allow inspection of:

- source messages
- source media
- evidence links
- why a durable object exists

### Revisions

Should show:

- how durable objects changed
- revision numbers
- what triggered change
- history relevant to operator analysis

## 6.8 Offline Events in Web

Telegram handles quick capture.
Web handles:

- inspection
- refinement
- review
- correction
- timeline linkage

An offline event should expose:

- operator summary
- optional recording reference
- extracted interpretation
- clarification history
- confidence/trust
- affected person/scope
- timeline linkage

## 6.9 Alerts in Web

Web should display:

- critical workflow blockers
- pending resolution items
- degraded states affecting active workflow
- important model/state transitions

Web alerts may be grouped, filtered, and linked to related objects.

## 7. Memory and Context Model

The operator layer should use:

### Durable application memory

- dossier read models
- profile read models
- pair dynamics read models
- timeline read models
- resolution/clarification read models
- offline event records

### Session memory

- active tracked person
- active mode
- recent cards/actions
- unfinished workflow step

### Retrieval/context assembly

For every answer, card, or review item:

- determine active scope
- assemble bounded context
- produce compact operator-facing output

## 8. Security and Access

Requirements:

- operator-authenticated only
- explicit operator identity
- auditable actions
- no silent destructive changes
- bounded action scope
- Telegram access restricted by operator identity allowlist

## 9. Non-Goals

This PRD does not include:

- public-facing UI
- raw DB admin surface
- broad infra debugging console
- restoration of old legacy Stage6 bot UX
- broad uncontrolled operator actions

## 10. Delivery Order

### P0

- Telegram Resolution mode
- Web Resolution queue
- Resolution item detail
- Evidence panel
- Clarification side panel
- Resolution actions
- Recompute-after-resolution feedback

### P1

- Telegram Assistant mode
- Telegram Offline Event mode
- Persons list in web
- Web person workspace
- Summary/Dossier/Profile/Pair/Timeline views
- Revision access

### P2

- richer alerts
- richer control/analytics views
- more advanced comparison/history tooling

## 11. Open Decisions

The following still need final product decisions:

1. Which web resolution actions require mandatory operator explanation
2. Timeline presentation model:
   - chronology-first
   - episode-first
   - arc-first
   - hybrid
3. Evidence drilldown depth:
   - summarized panel only
   - raw message/media viewer
   - both
4. Persons list information model
5. Whether web gets an assistant mode in the first implementation wave or later
6. To what extent offline event structures can be edited in web after capture

## 12. Success Criteria

The operator interaction layer is successful when an operator can:

1. choose a tracked person context
2. inspect dossier/profile/pair/timeline outputs
3. understand trust and uncertainty
4. resolve critical workflow blockers
5. add new context through offline events or clarification input
6. trigger bounded downstream reconciliation after resolution
7. move smoothly between Telegram compact flow and Web deep-analysis flow
