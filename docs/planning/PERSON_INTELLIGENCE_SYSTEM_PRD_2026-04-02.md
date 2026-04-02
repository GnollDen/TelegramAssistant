# PRD: Person Intelligence System

## Date

2026-04-02

## Status

Editorial v2

## Purpose

This document defines the target product shape for a self-hosted, AI-first person intelligence system.

It is optimized for:

- product clarity
- architecture handoff
- implementation sequencing
- control-plane discipline

It is not meant to be a giant prompt catalog or a full low-level technical spec.

## Related Documents

- [PRD-Person-Intelligence-System-2026-04-02.md](C:\Users\thrg0\Downloads\TelegramAssistant\PRD-Person-Intelligence-System-2026-04-02.md)
- [docs/PRODUCT_DECISIONS.md](C:\Users\thrg0\Downloads\TelegramAssistant\docs\PRODUCT_DECISIONS.md)
- [STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\STAGE6_SINGLE_OPERATOR_PRD_DRAFT_2026-03-25.md)
- [PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\PRODUCT_IMPLEMENTATION_GAPS_INTERVIEW_2026-03-25.md)
- [RUNTIME_TOPOLOGY_NOTE_2026-03-25.md](C:\Users\thrg0\Downloads\TelegramAssistant\repo_inspect\docs\planning\RUNTIME_TOPOLOGY_NOTE_2026-03-25.md)

## 1. Product Identity and Goals

### 1.1 Product Definition

`Person Intelligence System` is a self-hosted AI-first system for one operator.

Its job is to build and maintain a usable knowledge base about people, relationships, timelines, patterns, and context before any assistant behavior is applied on top.

The product is:

- person-first
- operator-centered
- graph-aware
- timeline-aware
- multi-pass
- reviewable
- rebuildable

The product is not:

- just a chat assistant
- just a romance tool
- just a dossier extractor
- just a rule-heavy interpretation engine

### 1.2 Core Goal

Build a durable, reviewable, AI-assisted knowledge base that helps the operator understand:

- who the tracked people are
- how they behave
- how they relate to the operator and to others
- what happened over time
- what remains uncertain

### 1.3 v1 Goals

- Normalize Telegram archive and realtime data into a rebuildable substrate.
- Build tracked people and linked people around an operator-centered graph.
- Maintain dossiers, pair dynamics, timeline objects, and story arcs with clear truth separation.
- Support offline events and media as first-class sources.
- Use AI for staged reasoning while keeping governance in the application.
- Prepare a strong knowledge base for later assistant behavior.

### 1.4 v1 Non-Goals

- Multi-user SaaS.
- Fully autonomous unreviewed decision-making.
- Local-only reasoning stack.
- Giant one-pass full-context interpretation.
- Psychology as primary truth authority.

### 1.5 Product Boundary

`v1` is primarily a knowledge system, not an assistant-first product.

`v1` includes:

- substrate preparation
- staged intelligence formation
- durable knowledge objects
- review and clarification surfaces
- trigger-based crystallization

`v1` does not require assistant behavior to be the main operator surface.
Assistant capabilities may exist, but they are downstream of the knowledge base.

### 1.6 Terminology Baseline

Use the following terms consistently:

- `tracked person`
  a person intentionally modeled deeply
- `linked person`
  a discovered person with meaningful relevance to a tracked person or story arc
- `candidate identity`
  an unresolved person-like entity that may later become a linked or tracked person
- `mention`
  a lightweight entity reference without enough evidence for durable person modeling
- `profile`
  the person-level behavioral and contextual model
- `dossier`
  the operator-facing structured view of important durable knowledge about a person
- `pair dynamics`
  the model of interaction patterns between two people
- `event`
  a specific thing that happened
- `timeline episode`
  a bounded local temporal segment
- `story arc`
  a broader meaningful history with entry, development, and outcome or fade-out
- `stage`
  a runtime processing stage
- `pass`
  one AI reasoning step within a stage or worker flow
- `run`
  one concrete execution instance of a stage, pass, or worker
- `crystallizer`
  a specialized Stage 8 worker that deepens or reconciles scoped knowledge

## 2. Core Knowledge Model

### 2.1 Operator-Centered Graph

The operator should be treated as the primary operational root of the graph.

Rules:

- the operator remains the main operational root
- tracked people are added around the operator
- linked people usually appear through tracked people
- the system may maintain strong subgraphs around tracked people when their local network has high importance
- new tracked people do not replace the operator as the primary operational root

### 2.2 Main Knowledge Objects

The core knowledge model includes:

- `Person`
- `OperatorModel`
- `PairDynamics`
- `SourceObject`
- `EvidenceItem`
- `Event`
- `TimelineEpisode`
- `RelationshipEdge`
- `DossierField`
- `ClarificationCase`

### 2.3 Truth Model

Truth must be separated into layers.

Core layers:

- `canonical truth`
  source objects, normalized evidence, deterministic bindings, reviewed state
- `derived but durable`
  reviewed dossier fields, reviewed operator model fields, reviewed pair dynamics, accepted reconstructions
- `proposal layer`
  model hypotheses, candidate identities, candidate merges, candidate patterns, candidate clarifications
- `operator-supplied but unreconciled`
  memory notes, long-form context, user-reported offline facts, external facts not yet checked against corpus
- `conflicted or obsolete`
  contradicted claims, superseded interpretations, expired episodes, old identity candidates

Rule:

- truth is promoted by the control plane, not by model eloquence
- assistant output may explain, summarize, and recommend, but never directly promote truth-bearing state

### 2.4 Behavioral and Psychological Model

The system should model:

- repeated behavioral patterns
- stimulus/reaction tendencies
- pair-specific dynamics
- contextual vs global tendencies

Psychology is a helper layer, not a primary truth layer.

Interpretation priority:

1. observed facts and events
2. repeated behavioral patterns
3. stimulus/reaction tendencies
4. cautious psychological interpretation

Guardrails:

- repeated reaction patterns may create trait candidates
- they must not directly become stable traits without cross-context evidence
- the system should prefer behavioral language over diagnostic language

### 2.5 Profile and Dossier Model

Profiles should support:

- global profile
- contextual profile
- pair-specific profile behavior

Profiles should track:

- confidence
- coverage
- stability
- freshness

Profiles should treat traits as grouped families rather than flat unstructured output.

`Profile` and `dossier` are not the same thing.

- `profile` is the underlying person model
- `dossier` is the structured operator-facing presentation of durable person knowledge

### 2.6 Pair Dynamics Model

`PairDynamics` is a first-class object.

It should capture:

- initiative balance
- response rhythm
- closeness-distance cycle
- conflict and repair cycle
- emotional safety
- planning and followthrough
- pressure and withdrawal
- playfulness and lightness
- logistics and real-world coordination
- topic sensitivity

### 2.7 Timeline Model

Timeline must distinguish:

- global timeline
- local episodes
- open dynamics
- closed dynamics
- decayed relevance
- story arcs

Rules:

- event confidence and boundary confidence are separate
- story arcs may be closed, semi-closed, or open
- old patterns should not override recent evidence without freshness logic

## 3. Runtime Stage Model

Stages in this section are runtime processing stages.
They are not delivery milestones and not architecture review phases.

### 3.1 Stage 5: Substrate Preparation

`Stage 5` is the normalized evidence substrate.

Responsibilities:

- import and parsing
- transcription
- media handling
- source binding
- normalized evidence creation
- provenance assignment
- embeddings as retrieval infrastructure when cheap enough
- rebuildable corpus preparation

It is `corpus-first`, not `person-first`.

It should not own:

- high-level person interpretation
- durable dossier synthesis
- strategy logic
- draft/review logic
- hidden semantic arbitration

### 3.2 Stage 6: Initial Bootstrap Run

`Stage 6` is the first person-first pass over a prepared corpus.

Trigger:

- new tracked person
- and/or new chat corpus

Responsibilities:

- initial graph assembly
- initial linked-person discovery
- bootstrap candidate slices
- ambiguity and contradiction discovery
- rough person-first map of the corpus

Important constraint:

- Stage 6 does not pretend to already know the full timeline structure
- slices at this stage are bootstrap candidates, not durable semantic periods

Exit criteria:

- operator-centered graph initialized
- tracked person attached to graph
- initial linked-person candidates discovered
- bootstrap slices created
- initial contradiction and ambiguity pools created
- no durable psychological claims required
- no specialized crystallizer outputs required

### 3.3 Stage 7: First Durable Knowledge Formation

`Stage 7` is the second intelligence run over the prepared corpus and Stage 6 outputs.

Responsibilities:

- reconcile Stage 6 outputs
- build first stable knowledge objects
- form first durable dossiers
- form first operator model
- form first pair dynamics object
- form reviewed timeline and durable knowledge layers
- promote durable knowledge where justified

Stage 7 owns:

- first stable knowledge assembly
- first durable object formation
- first durable promotion decisions

Stage 7 does not own:

- specialized crystallizer workers
- ongoing trigger-based refinement

### 3.4 Stage 8: Trigger-Based Crystallization

`Stage 8` owns all specialized crystallizers and selective refinement after the first durable knowledge system exists.

Responsibilities:

- trigger-based crystallization
- incremental refresh from new data
- scoped reruns
- profile crystallization
- history crystallization
- post-change refinement after edits, deletes, operator answers, story closures, and other high-signal changes

Stage 8 should maintain and deepen the knowledge base without forcing global reruns by default.

## 4. Control Plane and Governance

### 4.1 Core Principle

The system must be:

- strict on governance
- flexible on reasoning

The app should orchestrate, validate, normalize, promote, review, and recompute.
The app should not contain the main semantic brain.

### 4.2 Progressive Context Retrieval

Thinking models must not receive the entire corpus by default.

The intended flow is:

1. thin context pack
2. reasoning pass
3. structured request for additional data if needed
4. targeted retrieval by control plane
5. follow-up reasoning pass
6. result or clarification

Rule:

- models are free in reasoning, but constrained in protocol

### 4.3 AI Pass Protocol

Each AI pass should run under a small explicit protocol.

Minimum input envelope:

- protocol version
- run type
- target object type
- target object id
- input scope
- source refs
- truth layer summary
- known conflicts
- known unknowns
- normalization constraints
- output schema

Allowed output statuses:

- `result_ready`
- `need_more_data`
- `need_operator_clarification`
- `blocked_invalid_input`

The system should keep these statuses explicit, but should not turn every reasoning variation into a separate contract.

This protocol governs interaction between:

- the control plane
- the AI reasoning layer
- the normalization layer

It should remain small, explicit, and stable.

### 4.4 Normalization Layer

Normalization is a distinct mechanism.

Its purpose is to:

- keep thinking prompts flexible
- convert rich semantic output into typed objects
- separate fact, inference, hypothesis, and contradiction
- downscope unsafe claims
- ensure storage-safe outputs

Rules:

- rich reasoning output must not directly enter truth-bearing layers
- normalized output may become candidate knowledge
- normalization does not equal promotion

### 4.5 Clarification Discipline

Clarification should be minimal and high-value.

Rules:

- do not ask the operator too early
- Stage 6 may create candidate ambiguity, but not a final question pool
- final operator question pool must not be created after pass 1
- operator questions should be created only after synthesis
- ask the operator only when operator information is the cheapest and highest-value resolver

### 4.5.1 Clarification Blocking Policy

Clarification should not block the whole system by default.

Rule:

- questions should block the smallest safe scope by default
- but the system may escalate to wider blocking when missing context would materially degrade global knowledge quality or force expensive low-value reruns later

Expected behavior:

- unresolved scope may remain non-durable or lower-confidence
- unrelated processing may continue
- other profiles, arcs, or windows may continue to update
- only hard clarification gates should block a critical local branch

Escalation to broader blocking is justified when:

- the missing answer affects identity resolution at graph-root or high-impact subgraph level
- the missing answer changes how large portions of the corpus should be interpreted
- the missing answer is required before Stage 6 or Stage 7 can safely continue
- continuing without the answer would likely create low-quality knowledge that later requires expensive broad recompute

Preference rule:

- it is acceptable to block more aggressively if that prevents a degraded pass and a costly rerun

### 4.6 Review and Promotion

Knowledge should move through a lightweight promotion ladder:

- raw source
- normalized evidence
- candidate object
- reviewable object
- durable knowledge object
- historical or obsolete object

Promotion should depend on:

- confidence
- coverage
- contradiction pressure
- source diversity
- temporal stability
- sensitivity
- operator approval where required

Persistent objects and transient AI outputs must remain distinct.

Persistent objects:

- reviewed knowledge objects
- durable dossiers
- durable profiles
- durable pair dynamics
- reviewed timeline objects

Transient AI outputs:

- hypotheses
- candidate objects
- scoped reasoning output
- temporary summaries
- pre-normalization synthesis

### 4.7 Scoped Recompute

Scoped recompute should be the default.

Typical scopes:

- one person profile
- one pair dynamics object
- one story arc
- one timeline zone
- one contradiction cluster
- one linked-personality neighborhood

Rule:

- recompute from the smallest sufficient scope

### 4.8 Minimal Dependency Model

Dependencies should exist only where they materially affect recompute behavior.

Keep dependencies minimal, especially around:

- person profile -> pair dynamics
- person profile -> linked-person importance
- timeline or story arc -> dossier conclusions
- contradiction cluster -> clarification queue
- merge or split -> affected profiles and graph edges

Rule:

- dependency model should be sufficient for scoped recompute, not theoretically complete

## 5. Runtime Policies

### 5.1 Identity Resolution

Identity resolution is a distinct concern.

The system should distinguish:

- same person
- probably same person
- unresolved but related
- different people

Strong signals:

- stable alias overlap
- direct source binding
- repeated co-reference
- shared anchors
- temporal continuity
- operator confirmation

Anti-merge signals:

- contradictory role or timeline
- incompatible source bindings
- different social neighborhoods
- conflicting stable attributes
- operator rejection

Rule:

- alias overlap alone is insufficient for merge

Linked-person promotion signals should stay lightweight but explicit:

- repeated mentions across independent contexts
- direct timeline impact
- pair-dynamics impact
- frequent appearance in important arcs
- operator relevance
- explicit operator promotion

### 5.2 Graph Expansion

Tracked people should be modeled more deeply than automatically discovered people.

Graph expansion should be importance-weighted.

Depth should depend on:

- proximity to tracked person
- frequency of appearance
- influence on timeline
- influence on pair dynamics
- impact on communication strategy

### 5.3 Media Policy

Media should enter the system as source objects first.

Stage 5 media flow:

- save media
- gather lightweight signals
- classify candidate type
- assign candidate importance
- escalate only important or ambiguous media for deeper analysis

Media analysis should be multi-stage:

1. compressed preview
2. cheap triage
3. context-aware recheck
4. deep analysis only when justified

Burst-aware rule:

- large photo bursts should be treated as batch objects first
- single photos and small groups should usually get more individual attention

### 5.3.1 Offline Event Policy

Offline follow-up should stay explicit but lightweight.

Rules:

- fresh meetings -> default optional follow-up
- historical meetings -> ranked optional enrichment only

This keeps offline capture useful without forcing heavy retrospective reconstruction everywhere.

### 5.4 Timeline and Story Arc Detection

Initial slicing should not be random.

Recommended bootstrap logic:

- coarse baseline slicing across the corpus
- signal-aware refinement
- local context expansion around anchors
- bootstrap candidate slices

Story arcs should be detected from meaningful continuity, not just dense windows.

Typical arc signals:

- recurring topic cluster
- recurring people around one issue
- explicit start or end markers
- conflict with aftermath
- health, work, travel, or problem threads
- offline event with a long tail in chat

### 5.5 Incremental Updates

The system should not run full intelligence passes on every new message.

Realtime policy:

- ingest messages immediately
- update hot windows incrementally
- run intelligence by windows and triggers, not by every message

Trigger classes:

- normal incremental window
- high-signal message
- edit event
- delete event
- operator request
- scheduled refresh

Edit and delete events must be treated as semantic events when they affect important facts, timeline, or strategy.

### 5.6 Negative Evidence and Freshness

The system should track negative evidence as a real signal family.

Examples:

- does not respond to a certain kind of stimulus
- does not confirm plans
- does not return to a topic
- does not initiate contact

Profiles should also distinguish:

- stable trait
- recurrent pattern
- episode-bound pattern
- weak signal

And:

- active
- recent but unconfirmed
- stale
- historical only

### 5.7 Conflict Handling

Conflicts should first be:

- recorded
- scoped
- checked by one more AI pass if useful

Only materially important unresolved conflicts should go to operator review.

Rule:

- weak conflicts may coexist as hypotheses
- strong conflicts must not silently auto-resolve

## 6. Data and Knowledge Object Model

### 6.1 Main Entities

- `Person`
- `OperatorModel`
- `PairDynamics`
- `SourceObject`
- `EvidenceItem`
- `Event`
- `TimelineEpisode`
- `RelationshipEdge`
- `DossierField`
- `ClarificationCase`
- `ModelPassRun`
- `NormalizationRun`

### 6.2 Person

`Person` should include:

- identity basics
- aliases
- source bindings
- maturity level
- graph role

### 6.3 Operator Model

`OperatorModel` should include:

- style profile
- reaction patterns
- blind spots
- preferred strategy bounds

### 6.4 Pair Dynamics

`PairDynamics` should include:

- global summary
- pattern families
- current dynamic state
- confidence

### 6.5 Knowledge Metadata

Important fields across knowledge objects:

- confidence
- coverage
- freshness
- stability
- promotion state
- evidence refs
- contradiction markers where relevant

## 7. Delivery Scope and Milestones

### 7.1 v1 Knowledge Scope

The first durable product scope should include:

- Stage 5 substrate
- Stage 6 bootstrap
- Stage 7 first durable knowledge formation
- Stage 8 trigger-based crystallization
- tracked people
- linked people
- operator model
- pair dynamics
- timeline and story arcs
- review and clarification surfaces

Assistant behavior can be layered on top later, but the knowledge base is the primary foundation.

### 7.1.1 Why This Architecture Is Better

This architecture is better than a chat-first or rule-heavy Stage 6 design because it:

- keeps Stage 5 narrow and rebuildable
- moves semantic reasoning into staged AI passes instead of hidden app rules
- separates truth promotion from model prose
- makes durable knowledge formation explicit before assistant behavior
- supports selective refinement instead of global reruns

### 7.2 Milestone Outline

1. Evidence substrate hardening
2. Initial bootstrap run
3. First durable knowledge formation
4. Trigger-based crystallization
5. Review and merge control
6. Assistant layer on top of durable knowledge

## 8. Appendices

### Appendix A: Starting Trait Families

- communication style
- closeness-distance behavior
- initiative and rhythm
- conflict and repair
- planning and reliability
- emotional expression
- boundaries and sensitivity
- social environment dependence
- stimulus-reaction patterns
- operator-specific pair patterns

### Appendix B: Starting Pair Dynamics Families

- initiative balance
- response rhythm
- closeness-distance cycle
- conflict and repair cycle
- emotional safety
- planning and followthrough
- pressure and withdrawal
- playfulness and lightness
- logistics and real-world coordination
- topic sensitivity map

### Appendix C: Starting Coverage Areas

- identity
- communication style
- preferences
- boundaries
- important people
- recurring events
- reaction patterns
- pair dynamics relevance

### Appendix D: Starting Confidence Factors

- evidence volume
- evidence diversity
- temporal spread
- cross-context consistency
- contradiction level
- source quality
- recency
- operator confirmation

### Appendix E: Example Review Threshold Baseline

- lower baseline:
  - communication style
  - initiative and rhythm
  - planning and reliability
- medium baseline:
  - conflict and repair
  - closeness-distance behavior
  - stimulus-reaction patterns
- higher baseline:
  - emotional expression
  - boundaries and sensitivity
  - social environment dependence
- very high baseline:
  - high-level psychological interpretations
  - sensitive motives
  - claims that materially change strategy under risk

## 9. Editorial Principles

This PRD should continue evolving under these rules:

- do not lose control-plane discipline
- do not collapse back into giant prompts
- do not move semantic brain back into the app layer
- keep governance strict and reasoning flexible
- move heuristics and examples to appendices when the main body gets too dense
