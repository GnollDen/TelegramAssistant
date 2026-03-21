# Product Decisions

## Purpose

This document captures product decisions already made through Q/A so implementation can proceed against a stable target.

It is not a sprint plan. It is the product doctrine for the system.

## Product Identity

The product is:

- a universal communication intelligence core
- with a relationship-first domain module
- focused on one central relationship case
- while modeling the surrounding social graph

The product is not:

- only a generic chatbot
- only a dossier extractor
- only a draft generator
- only a romance advice engine

## Core Product Shape

The system should support:

- archive analysis
- live Telegram monitoring
- offline event ingestion
- audio ingestion for offline situations
- active clarification through Q/A
- timeline and period reconstruction
- behavioral profiling
- current-state assessment
- next-step strategy
- draft generation and review
- personal adaptation over time

## Relationship Reasoning Model

Relationship quality should not be reduced by universal platform goals.

Architecture principle:

- universal evidence layer
- relationship-specific reasoning layer
- action layer separated from truth-seeking analysis

This separation is mandatory to avoid action advice biasing interpretation.

## Main Analytical Focus

The relationship module must optimize for both:

- accurate analysis of current state and relationship trajectory
- practical help with actions, communication, and reply drafting

Therefore the relationship module must have two explicit loops:

- analytical loop
- action loop

## Social Graph Scope

The product should not model only `me <-> target person`.

It should model:

- self
- focal person
- surrounding people
- helpers
- bridges
- conflict sources
- high-influence third parties
- complicating external actors

Preferred role labels:

- supportive
- neutral
- complicating
- high_influence
- bridge
- information_source
- conflict_source

Avoid framing the model around paranoia-oriented labels like "enemy" as a default product concept.

## State Representation

External UX should be qualitative.

Internal engine should use hidden scoring.

Internal dimensions should include:

- warmth
- reciprocity
- initiative balance
- openness
- ambiguity
- avoidance risk
- escalation readiness
- external influence pressure
- contact stability

External output should show:

- current state label
- key signals
- risks
- missing context
- recommended next move
- confidence in interpretation

The product should not show fake precision like percentage odds of success as a primary UX.

## Clarification and Interview Mode

The system should actively lead clarification, not wait passively.

Default behavior:

- analyze archive
- detect context gaps
- prioritize gaps
- ask targeted questions
- update interpretation after answers
- continue adaptively

This is an interactive reconstruction system, not a one-shot analyzer.

Questions must be:

- evidence-based
- targeted to a period or transition
- high-value
- non-leading
- minimal in count per cycle

## Default Uncertainty Behavior

When signals are weak or conflicting, the default is:

- lower confidence
- show competing interpretations
- ask clarification questions
- defer strong action recommendations

Default path:

- ambiguity
- clarification
- recalculation

Not:

- ambiguity
- forced action advice

## Personal Adaptation

The system must adapt over time to:

- user corrections
- clarification answers
- confirmed facts
- rejected interpretations
- strategy outcomes
- draft feedback

This adaptation should be implemented first through data, memory, ranking, and prompt conditioning.

It should not depend on early fine-tuning of a local model.

## Style Adaptation

Style adaptation is critical.

The product must learn:

- natural message length
- directness
- warmth
- emotional density
- vocabulary
- humor level
- pacing
- over-explaining tendency

The system should support:

- natural style
- slightly improved style
- safer style
- warmer style
- more direct style

Style must remain separate from analysis and strategy.

## Interfaces

The product should ship with:

- Telegram bot
- web interface with simple functional design

Telegram bot is for:

- quick state checks
- next-step requests
- draft requests
- answering clarification questions
- logging offline events

Web UI is for:

- dashboard
- dossier review and editing
- timeline
- periods
- clarifications
- profiles
- strategy review
- draft review
- network review
- history of changes

## Primary Web Entry Point

The main web screen should be a unified dashboard.

Dashboard should include:

- current state
- next-step recommendation
- timeline snapshot
- open clarifications
- recent outcomes
- alerts
- short dossier view

## Offline Workflow

Offline processing should support:

- audio upload
- transcript generation
- user summary after the event
- clarification questions
- integration into dossier, timeline, profiles, and current state

Recommended sequence:

1. raw audio
2. automatic transcript and initial extraction
3. user summary
4. question/answer refinement
5. derived impact on state and periods

For early product iterations, post-event reflection and summary are higher-priority than making audio the only source of truth.

## Truth Layers

The system must explicitly distinguish types of truth.

Minimum truth/source layers:

- observed_from_chat
- observed_from_audio
- user_confirmed
- user_reported
- model_inferred
- model_hypothesis
- contradicted
- obsolete

This provenance must be visible in storage and review UX.

## History and Audit

The system must preserve how understanding changed over time.

Track:

- fact history
- interpretation history
- recommendation history
- outcome history

This is required for:

- rollback
- trust
- debugging
- adaptation
- quality review

## Live Monitoring and Outcomes

Live monitoring should automatically observe outcomes where possible.

Automatically track:

- whether a suggested or similar message was sent
- response latency
- tone shift
- initiative shift
- continuation or drop-off
- invite / decline / avoidance signals

Manual refinement should still be supported for:

- offline outcomes
- ambiguous real-world effects
- subjective evaluation of success

## Action Interpretation

If the user sends a message different from the system draft, the product should:

- observe it
- classify the actual move type
- compare it to recommended strategy
- measure outcome
- learn from it

The product should not only learn from accepted drafts.

It should learn from real user actions.

## Draft Review

The system should support a `review before send` mode.

This is important, but can land after core state, strategy, and draft generation.

Review should classify risks such as:

- too long
- too needy
- too vague
- too cold
- too direct
- good pacing
- safe
- likely over-escalation

## Manual Seeding

The product should support manual pre-seeding of dossier facts and milestones.

This is essential for:

- better grounding
- faster bootstrap
- reduced archive-only distortion

Manual seeds should be marked as explicit user-originated knowledge with strong provenance and trust.

## Model Strategy

Do not prioritize local LLM training early.

Near-term quality should come from:

- structured memory
- better retrieval
- Q/A clarification
- timeline reasoning
- personal adaptation
- style adaptation
- evaluation

If model customization is explored later, it should be a later-stage optimization, not the foundation.

## Product Safety Principles

The product should not:

- push manipulative escalation
- present weak evidence as strong certainty
- confuse warmth with romantic reciprocity automatically
- recommend pressure under ambiguity by default

The product should:

- surface uncertainty clearly
- ask clarification questions when needed
- separate evidence from inference
- separate inference from action advice

## Open Areas Still To Define

These areas are not fully decided yet and should be handled in later Q/A:

- exact web information architecture
- first bot command set and response format
- exact period label taxonomy
- exact social graph schema
- exact state label taxonomy
- exact profile dimensions shown in UX
- evaluation dataset structure
- priority between GraphRAG MVP and minimal web UI expansion

## Dossier and Truth Modeling

The dossier base unit should include:

- entities
- facts
- events
- hypotheses

Hypotheses should appear:

- briefly inside dossier views
- in full detail in dedicated detailed views

User speculative notes should be stored as:

- user context notes with uncertainty

If those notes conflict with observed evidence:

- show conflict
- request clarification

## Web Surface Decisions

The first usable web version should be broad but shallow.

It should include:

- dashboard
- dossier
- timeline
- clarifications
- history
- network
- current state
- profiles
- offline events

Draft authoring does not need a dedicated web screen in the first usable version.

The web should behave as a review console.

Manual editing should exist almost everywhere, including:

- dossier
- periods
- current state
- profiles
- clarifications
- network
- offline events

Edits should use:

- draft mode
- confirmation
- a required reason/comment

If an edit affects multiple modules:

- show impacted areas
- require confirmation before recomputation

Bulk review is required for:

- facts
- hypotheses
- gaps/questions
- other high-value reviewable items

Bulk actions should support:

- confirm
- reject
- defer
- edit

A dedicated inbox is required in web.

Inbox sorting should combine:

- importance
- model impact
- freshness

Inbox items should support a `blocking` state.

Bot should not host the full inbox.

Bot should send reminders:

- on schedule
- on trigger conditions

Reminder content should include:

- short summary
- top items
- where to go in web

Global web search is required with:

- text search
- filters
- semantic search

Search should work across the full knowledge surface.

The web should include:

- activity log
- all key system actions
- user actions
- consequences

Saved views should include:

- blocking inbox items
- current period
- conflicts and contradictions

User tags should be supported across core item types.

Visual status colors should exist for:

- confirmed
- hypothesis
- conflict
- blocking
- obsolete

## Bot and Draft Decisions

Core bot surface should prioritize:

- `/state`
- `/draft`
- `/review`
- `/offline`
- `/gaps`
- `/answer`
- `/timeline`

`/state` should include:

- label
- key signals
- next step
- do not do

`/draft` should:

- start by command
- analyze the latest hot session
- accept optional extra context
- accept speculative notes
- accept desired reply goal

Draft output should include:

- one main draft
- two short alternatives

If interpretation is uncertain:

- provide cautious drafts
- ask clarification in parallel

Drafting should optimize for:

- balance between style fit and strategic fit

If style and strategy conflict:

- show a compromise
- explain the tradeoff briefly

The template library should include:

- successful raw messages
- reusable templates by situation

Successful templates should be collected through:

- automatic candidate detection
- user confirmation

Clarification flow should ask:

- one priority question at a time
- 2-3 answer choices
- free input

After answer submission:

- save answer
- update model
- show what changed
- propose next question

## Timeline and Period Decisions

Timeline should be built as:

- periods on top
- nested events inside periods

Periods should use:

- fixed taxonomy
- optional custom labels

Periods should describe:

- relationship status
- period dynamics

Each period should have:

- one main label
- additional traits/signals

Long periods may include:

- visible substages
- collapsed by default

Period transitions should be decided from a combination of:

- pauses
- key events
- changes in dynamics

Events and dynamic shifts matter more than pauses alone.

Periods should include:

- external context
- third-party influence

If an older dynamic returns, new period versus continuation should depend on:

- pause
- status/event change
- tone/behavior change

The current period remains open until:

- a new period emerges
- or long silence makes it stale

If an active conversation shows a strong internal break without pause:

- competing interpretations may coexist

Periods must include:

- label
- dates
- summary
- key signals
- what helped
- what hurt
- open questions
- boundary confidence
- interpretation confidence
- third-party influence
- alternative interpretations
- evidence pack
- review priority
- sensitive flag
- lessons
- strategic patterns

Inside periods, event representation should support both:

- positive/negative effect
- relationship relevance versus context-only role

Periods may contain:

- confirmed events
- hypothetical events marked explicitly

If transition cause is unclear:

- create the period
- create a gap explaining the transition

If neighboring periods likely should merge:

- propose merge

If a period likely should split:

- propose split

Alternative period interpretations should store:

- possible readings
- what evidence would confirm or refute them

Sparse periods should:

- still exist
- use reduced summaries
- raise uncertainty
- optionally generate clarification gaps

Unresolved transitions should appear as:

- timeline markers
- transition cards

Each period should have:

- evidence snippets
- links to full source context

Review priority for periods should depend on a combination of:

- low confidence
- high impact on current situation
- conflicts in data

Lessons should be:

- auto-generated
- manually editable

Strategic patterns should exist:

- inside periods
- and globally across history

## Social Graph Decisions

Social graph is centered on:

- the focal pair

Other nodes attach to:

- the focal pair
- the periods where they mattered

Initial social graph should support:

- people
- groups
- companies
- places

Context-only non-person nodes should be:

- lower-priority contextual nodes

Each node should support:

- one primary role
- additional roles

Roles should support:

- global value
- period-specific overrides

Influence edges should store:

- type
- strength
- evidence

Graph should support:

- direct influence
- indirect influence
- information flow

Unconfirmed influence should be stored as:

- a dedicated influence hypothesis

Information flow should store:

- direction
- evidence

Nodes should support:

- trust/reliability
- global value
- period overrides

If a discovered person has unclear role:

- require manual confirmation before full graph insertion

The system should support:

- review of new nodes
- role suggestion
- evidence for appearance
- bulk review of new nodes

Alias collisions should:

- propose merge

Node importance score should depend on:

- frequency
- influence strength
- relation to key periods

## Offline Audio Decisions

The first iteration of offline audio ingestion should prioritize:

- recordings of meetings or real conversations

If multiple speakers exist:

- attempt speaker separation automatically

If speaker separation is uncertain:

- mark segments as uncertain speaker
- allow targeted manual clarification

After transcription, the system should extract:

- summary
- key moments
- candidate signals

Candidate signals should cover:

- emotions and tension
- facts and events

The system should preserve timestamps for:

- key moments
- candidate signals
- uncertain speaker segments

Offline audio user flow should be:

- auto-summary
- user summary
- clarification Q/A if needed

User summary should be:

- free-form
- with soft prompts

The first soft prompts should focus primarily on:

- key events and facts

Clarification after user summary should cover:

- unclear facts
- unclear signals
- unclear transitions or turning points

Turning points should be prioritized.

Paralinguistic extraction should be attempted early for:

- tension
- confidence
- warmth
- pauses
- harshness

If those signals are weak or uncertain:

- store them as signal hypotheses

The product should include speaker review for:

- speaker confirmation
- key moment correction
- signal correction

Long audio should be:

- chunked
- then recomposed

Recomposed output should include:

- summary
- key moments
- unresolved spots

Unresolved spots should feed directly into clarification Q/A.

Original audio should be stored.

Web should support:

- playback of stored audio

Offline audio must support:

- manual reassignment to another period

If reassignment affects state or timeline:

- show impacted changes
- require confirmation before recomputation

Offline audio should also yield:

- candidate evidence snippets

Those snippets should support:

- automatic extraction
- manual pinning

## State, Profile, and Strategy Decisions

Current state should represent both:

- relationship status
- current dynamics

State should be stored as:

- one primary label
- score dimensions

Required early dimensions:

- initiative
- responsiveness
- openness
- warmth
- reciprocity
- ambiguity
- avoidance risk
- escalation readiness
- external pressure

State taxonomy should be:

- medium-sized

Dynamic labels should focus more on:

- current dynamics

Relationship status should be shown separately and be:

- moderately detailed

Dynamic state should capture both:

- warmth stability change
- initiative change

Current state should be computed from:

- several recent sessions
- weighted influence from the current period

Older periods should influence current state:

- adaptively
- depending on situation similarity

If current signals conflict with historical pattern:

- show the conflict
- lower confidence

Profiles should represent both:

- communication style
- closeness and distance behavior

Profiles should be presented as:

- summary
- traits
- evidence

Trait taxonomy should support:

- fixed traits
- custom traits

Fixed profile dimensions should include:

- communication style
- closeness and distance behavior
- conflict and repair behavior

Profiles should support:

- global view
- period-specific slices

If global and period profile conflict:

- show both
- explain the divergence

The product should also include:

- pair dynamic profile

Pair dynamic profile should include:

- initiative and rhythm
- conflict and repair
- closeness and distance

Pair dynamic profile should support:

- global view
- period-specific slices

Profile UI should support:

- separate view
- comparative view

Each trait should carry:

- confidence
- stability

Low-stability traits should appear as:

- temporary or period-specific traits

Sensitive traits should:

- be shown more cautiously
- require stronger evidence

Profiles should include:

- what tends to work
- what tends to fail

Both:

- globally
- and by period

Strategy output should present:

- several valid moves
- risks
- conditions for use

Strategy actions should use:

- fixed action types
- custom actions when needed

The strategy layer must explicitly support:

- wait / do nothing
- light test
- repair
- boundaries

Strategy should be grounded in:

- current state
- current period
- profile and pair-pattern context

Every action option should include:

- purpose
- risk
- when to use it

Strategy should also state:

- what may go wrong
- how to recognize success
- how to recognize failure

Default strategy posture should be:

- adaptive to current state and uncertainty

When uncertainty is high, strategy should:

- narrow the set of options
- shift toward softer moves and clarification

Strategy should carry:

- its own confidence

The system should explain:

- why rejected alternatives were not preferred

Explanation style should be:

- short by default
- expandable on demand

Strategy should support:

- micro-step guidance
- both as standalone advice
- and within broader action options

Strategy horizon should cover:

- the immediate next step
- a short 2-3 step horizon when confidence is sufficient

Strategy should account for:

- user style and habits

But:

- not at the expense of safety
- not at the expense of clearly bad-fit strategy

## Taxonomy Decisions

### Relationship Status Taxonomy

Relationship status taxonomy should be:

- moderately detailed

It should capture:

- formal status
- relational reality

It should support:

- one primary status
- one alternative status when ambiguity is meaningful

Preferred base set:

- `platonic`
- `warm_platonic`
- `ambiguous`
- `reopening`
- `romantic_history_distanced`
- `fragile_contact`
- `detached`

Relationship status and emotional reality should be split into:

- relationship status
- dynamic state

Relationship status should use:

- one primary label
- one alternative label only when ambiguity is materially high

### Dynamic State Taxonomy

Dynamic state taxonomy should be:

- medium-sized

It should focus on:

- current dynamics

Preferred base set:

- `warming`
- `stable`
- `cooling`
- `fragile`
- `uncertain_shift`
- `low_reciprocity`
- `testing_space`
- `reengaging`

Dynamic labels and period labels should be:

- related
- but separate

Dynamic state should use:

- one primary label

### Period Label Taxonomy

Period label taxonomy should be:

- moderately detailed

It should combine:

- relational phase meaning
- dynamic phase meaning

Preferred base set:

- `initial_warmth`
- `growing_closeness`
- `romantic_phase`
- `post_conflict`
- `cooling`
- `detachment`
- `platonic_stabilization`
- `ambiguous_reconnection`
- `active_reconnection`
- `unstable_contact`

Custom labels should be allowed:

- for periods

Custom labels should not be allowed:

- for relationship status
- for dynamic state

Sensitivity should be represented:

- separately from labels
- through record-level flags

### Profile Trait Taxonomy

Profile trait taxonomy should be:

- medium-sized

Traits should be grouped by:

- behavioral dimensions

Fixed profile dimensions should include:

- communication style
- closeness and distance behavior
- conflict and repair behavior

Communication-style fixed traits should include:

- `directness`
- `verbosity`
- `warmth_expression`
- `playfulness`
- `clarity`
- `emotional_explicitness`
- `response_style`

Closeness and distance fixed traits should include:

- `initiates_closeness`
- `withdraws_under_pressure`
- `needs_space`
- `tolerates_ambiguity`
- `signals_interest_indirectly`
- `pace_preference`

Conflict and repair fixed traits should include:

- `avoids_conflict`
- `addresses_conflict_directly`
- `needs_cooldown`
- `accepts_repair_attempts`
- `reopens_after_distance`
- `holds_grievances`

Traits should use:

- canonical `snake_case` keys

Custom traits should be:

- allowed
- normalized
- reviewable

Profile UI should show:

- top relevant traits first
- full trait set on expansion

Pair dynamic preferred trait set should include:

- `initiative_balance`
- `contact_rhythm`
- `repair_capacity`
- `distance_recovery`
- `escalation_fit`
- `ambiguity_tolerance_pair`
- `pressure_mismatch`
- `warmth_asymmetry`

### Strategy Action Taxonomy

Action taxonomy should be:

- medium-sized

It should use:

- fixed action types
- plus custom actions where needed

Preferred base set:

- `wait`
- `warm_reply`
- `light_test`
- `clarify`
- `invite`
- `repair`
- `boundaries`
- `hold_rapport`
- `check_in`
- `deepen`
- `deescalate`

Action taxonomy structure should be:

- flat list
- plus tags

Preferred action tags:

- `low_pressure`
- `medium_pressure`
- `high_pressure`
- `romantic`
- `platonic`
- `repair`
- `probe`
- `timing_sensitive`
- `requires_reciprocity`
- `safe_under_ambiguity`

### Risk Taxonomy

Risk taxonomy should use:

- fixed labels
- plus free-text comment

Preferred base set:

- `overpressure`
- `overdisclosure`
- `premature_escalation`
- `friendship_misread`
- `neediness_signal`
- `ambiguity_increase`
- `withdrawal_trigger`
- `timing_mismatch`

Risk severity should use:

- `low`
- `medium`
- `high`

Blocking or strongly gated risks should include:

- `overpressure`
- `premature_escalation`
- `withdrawal_trigger`

Sensitive inference safety should remain:

- separate from normal strategic risks

### Gap Taxonomy

Gap taxonomy should use:

- fixed labels
- plus custom gap types when needed

Preferred base set:

- `missing_offline_context`
- `unclear_transition_reason`
- `unclear_relationship_status`
- `unclear_third_party_influence`
- `conflicting_signals`
- `missing_outcome`
- `speaker_uncertainty`
- `weak_period_evidence`

Blocking gap status should be used only when a gap materially affects:

- current state
- current strategy
- major timeline interpretation

### Evidence Taxonomy

Evidence taxonomy should use:

- fixed base types
- optional subtype

Preferred base set:

- `chat_message`
- `audio_segment`
- `offline_summary`
- `clarification_answer`
- `manual_fact`
- `system_derivation`

### Influence and Role Taxonomy

Influence type taxonomy should use:

- fixed labels
- plus custom values when needed

Preferred influence base set:

- `supportive`
- `complicating`
- `mediating`
- `informational`
- `stabilizing`
- `destabilizing`

Node role taxonomy should use:

- fixed labels
- plus custom values when needed

Preferred role base set:

- `friend`
- `close_friend`
- `family`
- `ex_partner`
- `new_interest`
- `bridge`
- `conflict_source`
- `advisor`
- `group`
- `place`
- `work_context`

### Outcome Taxonomy

Outcome tracking should use:

- label
- plus free-text note

Preferred base set:

- `positive`
- `neutral`
- `negative`
- `mixed`
- `unclear`

### Sensitivity, Confidence, Stability

Sensitivity should use:

- boolean
- plus reason

Confidence in UX should support:

- numeric value
- qualitative band

Stability in UX should support:

- numeric value
- qualitative band

Preferred stability bands:

- `unstable`
- `emerging`
- `stable`

### Lessons and Pattern Taxonomy

What-works and what-fails patterns should use:

- category
- plus explanatory text

Preferred categories:

- `tone`
- `timing`
- `pacing`
- `openness`
- `humor`
- `repair`
- `distance_management`
- `invite_style`

### Taxonomy Language

Internal taxonomy keys should use:

- English canonical keys

UI should display:

- human-readable labels

Custom extension should be:

- allowed
- reviewable
- normalized

But custom extension should not override:

- core relationship status labels
- core dynamic state labels

## Dossier and Data Model Decisions

### Overall Model Shape

Data model shape should be:

- typed entities
- typed domain records
- unified review and event layer

Rather than one giant undifferentiated knowledge table.

Core record families must include:

- facts
- events
- hypotheses
- periods
- outcomes

### Fact Model

Facts should be stored as:

- entity-scoped typed records
- with provenance
- with lifecycle fields

Fact lifecycle should support:

- `tentative`
- `confirmed`
- `contradicted`
- `obsolete`

Facts and important interpretations should support:

- time validity

### Event Model

Events should be stored as:

- typed events
- with participants
- with time
- with source
- with impact

### Hypothesis Model

Hypotheses should be stored as:

- separate entities
- not as normal facts with lower confidence

Hypotheses should support:

- conflict fields
- validation fields

Hypothesis lifecycle should support:

- `open`
- `supported`
- `rejected`
- `unresolved`

### Manual Overrides

Manual corrections should be stored as:

- overlays
- with reason
- with version linkage

Not as destructive overwrite of model output.

### Effective Truth Resolution

Effective truth should be resolved by:

- source priority
- validity
- overrides

Not by naive last-write-wins logic.

### Provenance

Provenance should store:

- source type
- source id
- evidence refs
- actor

### Conflict Model

Conflicts should exist as:

- computable runtime logic
- explicit conflict records for review

### Clarification Binding

Clarification answers should bind to:

- question
- period
- affected objects

### Offline Event Structure

Offline events should be stored as:

- event
- attached assets
- derived records
- user review state

### Audio Asset Structure

Audio assets should store:

- file
- transcript
- segments
- speaker review
- evidence snippets

### Period Storage

Periods should be stored as:

- first-class entities
- with separate transition objects

### State Storage

State should be stored as:

- snapshots over time
- plus diff/change records

### Profile Storage

Profiles should be stored as:

- profile snapshots
- trait-level history

### Strategy Storage

Strategy should be stored as:

- recommendation records
- with outcome links
- with feedback links

### Draft Storage

Drafts should be stored as:

- generated variants
- chosen/sent linkage when known
- matched outcome when known

### Review Layer

Review should be represented as:

- current statuses inside entities
- review events for history

### Inbox Model

Inbox should use:

- computed projections
- materialized queue items where useful

### Social Graph Alias Model

Aliases should be stored as:

- separate alias records
- with merge suggestions

### Outcome Model

Outcomes should link:

- strategy or draft
- actual action
- observed consequence

### Pattern and Lesson Model

Patterns should be stored as:

- structured records
- with supporting evidence links

### Tagging Model

Tags should use:

- normalized tags
- scoped tag types

### Recompute Dependency Model

Recompute logic should support:

- explicit dependency links
- or affected object map

So targeted recalculation is possible.

### Privacy, Visibility, and Deletion

Records should support:

- visibility
- sensitivity
- review restrictions

Deletion should use:

- soft delete
- archival tombstone

### Event Log

System history should use:

- typed event log
- domain event categories

## Bot Interaction Decisions

### Overall Bot Style

Bot interaction style should be:

- hybrid

Meaning:

- command-driven where structure matters
- guided where completion rate and clarity matter

Core first-wave bot surface should include:

- `/state`
- `/draft`
- `/review`
- `/offline`
- `/gaps`
- `/answer`
- `/timeline`
- `/profile`
- `/next`

Commands should remain:

- the primary interaction contract

But:

- guided flows should be heavily used inside key scenarios

### `/state`

`/state` should be:

- medium-depth by default

It should include:

- state label
- relationship status
- key signals
- main risk
- next step
- do not do

Details should be:

- expandable on demand

### `/draft`

`/draft` should use:

- the latest hot session
- current state context

It should accept optional:

- extra context
- speculative notes
- desired reply goal

Output should include:

- one main draft
- two short alternatives

Explanation should be:

- short by default

If interpretation is uncertain:

- provide cautious drafts
- ask clarification in parallel

### `/review`

`/review` should:

- assess risk
- propose rewrites

Preferred output:

- brief assessment
- one or two risks
- safer rewrite
- more natural rewrite

### `/offline`

`/offline` should accept:

- text
- audio

Preferred flow:

- accept input
- generate auto-summary
- request user addition/correction
- ask clarification questions
- update model

### `/gaps` and `/answer`

`/gaps` should operate as:

- guided one-by-one clarification

Preferred answer format:

- 2-3 answer options
- free input

After answer submission:

- save answer
- update model
- show what changed
- offer next question

`/answer` should exist as:

- fallback/manual entry

### `/timeline`

`/timeline` in bot should show:

- current period
- a few previous periods

Rather than:

- full history

### `/profile`

`/profile` should show:

- summary
- key patterns
- evidence snippets

### `/next`

`/next` should present:

- multiple action options
- risks
- conditions for use

### Reminders and Pushes

Bot reminders should work:

- on schedule
- on trigger conditions

Reminder content should include:

- short summary
- top items
- where to go in web

Bot should not host the full inbox.

Bot should act mainly as:

- reminder and action-entry surface

Additional limited pushes may include:

- blocking inbox alert
- stale current state
- unanswered high-impact clarification
- important contradiction
- strong outcome shift after action

### Buttons, Length, and Sessions

Inline buttons should be used:

- moderately

Especially for:

- clarification
- offline flows
- review flows

Message length policy should:

- depend on the command

Bot should support:

- short-lived flow session memory

If a user starts another command during a flow:

- ask whether to interrupt the current flow

### Sensitive Output and Recovery

Sensitive output should be:

- softer in tone
- less categorical

If bot input is not understood:

- ask for clarification
- suggest likely next scenarios

### Discoverability and Persistence

Bot should support:

- help/menu command

Help format should be:

- command list
- plus short descriptions

Bot outputs should be persisted:

- selectively

Examples worth persisting:

- drafts
- strategies
- reviews
- answers

Non-substantive utility outputs do not need persistence.

## Evaluation and Quality Loop Decisions

### Evaluation Strategy

Evaluation should be:

- manual
- and automatic

Core evaluation units should include:

- individual outputs
- end-to-end scenarios

Main eval surfaces should include:

- timeline
- state
- profiles
- strategy
- drafts
- clarification questions

Ground truth should be based on:

- user judgment
- confirmed facts
- known history

Not only on raw observed data.

### Evaluation Dataset

Evaluation dataset should include:

- relationship cases
- non-relationship counterexamples

Golden-case coverage should include:

- warming case
- cooling case
- ambiguous case
- fragile contact case
- false-positive romance risk case
- strong offline-context case
- third-party influence case

Counterexample coverage should include:

- pure logistics friendship
- warm but platonic chat
- emotionally intense but non-romantic context
- externally stressed contact without relationship degradation
- misleading third-party mention

### Surface-Specific Quality Criteria

Timeline quality should evaluate:

- boundary quality
- label quality
- summary quality
- usefulness for later reasoning

State quality should evaluate:

- label quality
- signal quality
- confidence honesty

Profile quality should evaluate:

- plausibility
- usefulness
- grounding in evidence

Strategy quality should evaluate:

- appropriateness
- safety
- usefulness

Draft quality should evaluate:

- naturalness
- strategic fit
- sendability

Clarification quality should evaluate:

- relevance
- non-leading quality
- impact on certainty

### Safety Evaluation

Safety must be evaluated explicitly through violation categories.

Base safety violations should include:

- `manipulative_escalation`
- `overconfidence_under_ambiguity`
- `friendship_as_romance_overread`
- `pressure_after_withdrawal`
- `unsupported_psychological_claim`
- `unsafe_sensitive_inference`

Confidence calibration should be evaluated:

- as a dedicated dimension

### Feedback Taxonomy

General output feedback should use:

- labels
- plus free-text comment

Preferred labels:

- `correct`
- `partially_correct`
- `incorrect`
- `too_optimistic`
- `too_negative`
- `missing_context`
- `unsafe`
- `useful`
- `not_useful`

Draft-specific feedback should use:

- `sounds_like_me`
- `does_not_sound_like_me`
- `too_much`
- `too_cold`
- `too_needy`
- `good_tone`
- `good_pacing`
- `sendable`
- `not_sendable`

### Scoring and Release Gates

Evaluation scoring should use:

- `1-5` scale

Ship/hold decisions should use:

- numeric thresholds on key metrics
- plus human review gate

Important releases should require:

- passing tests
- eval pass
- manual review

### Regression Structure

Regression should be:

- layered

Including:

- smoke suite
- focused suites

Smoke suite should include at least:

- archive import sanity
- one timeline scenario
- one state scenario
- one strategy scenario
- one draft scenario
- one clarification scenario

Focused suites should exist for:

- timeline
- state
- profiles
- strategy
- drafts
- safety
- graph retrieval later

### Persistence and Review

Eval runs should be:

- fully persisted

Post-sprint review should compare:

- metrics
- side-by-side output diffs

Manual review should happen:

- after each sprint

### Outcome and Offline Evaluation

Outcome-based learning should be evaluated:

- from the beginning
- but in a limited form

Clarification success should measure:

- certainty increase
- interpretation improvement

Inbox/review efficiency should measure:

- review time
- error rate
- backlog burn

Offline audio evaluation should include:

- transcript quality
- extraction quality
- impact on the model

### Graph and Canary Evaluation

Hybrid or graph retrieval should be evaluated on:

- quality
- cost
- latency

Online canary evaluation is desirable:

- later
- after base stabilization

## Deployment and Operations Decisions

### Runtime Topology

Runtime topology should be:

- hybrid

Meaning:

- one main application
- plus supporting services

Required service set should include:

- app
- db
- redis
- web
- optional graph service

Graph runtime should be:

- optional

Not a blocker for the first production-capable deployment.

### Deployment Shape

Deployment should use:

- Docker Compose

Storage should use:

- persistent mounted volumes

Rather than:

- ephemeral in-container state

### Storage Strategy

Audio should be stored:

- locally on VPS first
- with a future path to object storage

Structured derived artifacts should live:

- in the database

Raw and heavier artifacts should live:

- in files

### Background Processing

Workers should use:

- hybrid processing model

Meaning:

- small work may stay close to main app
- heavy or unstable work should move to dedicated workers

Heavy recomputation should use:

- async queue-based processing

Small updates may remain:

- synchronous

Queue backbone should use:

- Redis for runtime queueing
- DB for durable state and review state

### Configuration and Secrets

Configuration should use:

- appsettings
- environment overrides

Secrets should use:

- `.env` now
- with future move toward stronger server-side secret handling

### Product Scope and Isolation

The system should be:

- single-user first
- extensible later

Data should support:

- case-scoped partitioning

Rather than:

- full multi-user architecture now

### Cost Control

Cost control should use:

- alerts
- soft budgets
- hard budgets where practical

Tracked cost dimensions should include:

- text LLM usage
- audio usage
- embeddings

### Reprocessing and Recovery

System must support:

- targeted re-runs
- full rebuilds

Backups should cover:

- database
- audio
- derived artifacts

Backup cadence should include:

- daily backups
- extra backup before risky migrations

Retention should be:

- configurable

Not:

- hard-coded infinite retention
- or aggressive deletion by default

### Privacy and Controls

Operational privacy controls should include:

- redaction
- retention control
- purge tooling
- export tooling

Records should support:

- visibility
- sensitivity
- review restriction

Deletion should support:

- whole-case purge
- per-data-type purge

### Observability

Observability should cover:

- technical metrics
- domain metrics

Important domain metrics should include:

- open gaps
- blocking inbox items
- stale state snapshots
- unresolved transitions
- review backlog
- strategy generation count
- draft usage and outcome match rate

Alerts should flow to:

- Grafana/Prometheus
- bot notifications

### Failure Handling

Heavy tasks should support:

- retry
- quarantine
- escalation

Audio processing should be isolated:

- in dedicated worker logic

### Migration and Release Workflow

Migrations should run:

- as an explicit deployment step

Not only:

- implicitly at app startup

Release workflow should be:

- migrate
- deploy
- smoke
- eval

New domain capabilities should use:

- feature flags

### Admin and Recovery Surface

Admin actions are required for:

- rebuild timeline
- rerun clarification impact
- recompute state
- recompute profiles
- replay offline event
- re-evaluate strategy outcomes
- review node merges

Recovery should support:

- backup restore
- partial rebuild

### Environment and Access

Environment layout should use:

- dev
- prod

Web auth should start with:

- simple single-user auth

Bot-to-web handoff should support:

- text pointers
- deep links

### Export

Export should support:

- raw data
- reviewed knowledge
- all major derived layers

### Production Posture

Default production posture should be:

- balanced
- with safety bias

## Exact Schema Field Decisions

### Fact Schema

Required fact fields should include:

- `id`
- `entity_id`
- `category`
- `key`
- `value`
- `source_type`
- `source_id`
- `confidence`
- `trust_factor`
- `status`
- `valid_from`
- `valid_until`
- `is_current`
- `evidence_refs`
- `created_at`
- `updated_at`

Optional fact fields should include:

- `period_id`
- `notes`
- `review_status`
- `sensitivity_reason`
- `user_confirmed`

### Event Schema

Required event fields should include:

- `id`
- `event_type`
- `title`
- `summary`
- `timestamp_start`
- `timestamp_end`
- `source_type`
- `source_id`
- `confidence`
- `impact_type`
- `relationship_relevance`
- `evidence_refs`
- `created_at`
- `updated_at`

Event linking should support:

- `period_id`
- `participant_entity_ids`
- `related_person_ids`
- `related_place_ids`

### Hypothesis Schema

Required hypothesis fields should include:

- `id`
- `hypothesis_type`
- `subject_type`
- `subject_id`
- `statement`
- `confidence`
- `status`
- `source_type`
- `source_id`
- `evidence_refs`
- `conflict_refs`
- `validation_targets`
- `created_at`
- `updated_at`

### Period Schema

Required period fields should include:

- `id`
- `chat_id` or `case_id`
- `label`
- `custom_label`
- `start_at`
- `end_at`
- `is_open`
- `summary`
- `key_signals`
- `what_helped`
- `what_hurt`
- `open_questions_count`
- `boundary_confidence`
- `interpretation_confidence`
- `review_priority`
- `is_sensitive`
- `status_snapshot`
- `dynamic_snapshot`
- `created_at`
- `updated_at`

Optional period fields should include:

- `lessons`
- `strategic_patterns`
- `manual_notes`
- `user_override_summary`

### Transition Schema

Required transition fields should include:

- `id`
- `from_period_id`
- `to_period_id`
- `transition_type`
- `summary`
- `is_resolved`
- `confidence`
- `gap_id`
- `evidence_refs`
- `created_at`
- `updated_at`

### State Snapshot Schema

Required state snapshot fields should include:

- `id`
- `case_id`
- `as_of`
- `dynamic_label`
- `relationship_status`
- `alternative_status`
- `initiative_score`
- `responsiveness_score`
- `openness_score`
- `warmth_score`
- `reciprocity_score`
- `ambiguity_score`
- `avoidance_risk_score`
- `escalation_readiness_score`
- `external_pressure_score`
- `confidence`
- `key_signal_refs`
- `risk_refs`
- `created_at`

### Profile Schema

Profile snapshots should include:

- `id`
- `subject_type`
- `subject_id`
- `period_id`
- `summary`
- `confidence`
- `stability`
- `created_at`

Profile traits should be stored separately with:

- `id`
- `profile_snapshot_id`
- `trait_key`
- `value_label`
- `confidence`
- `stability`
- `is_sensitive`
- `evidence_refs`

### Clarification Schema

Clarification question fields should include:

- `id`
- `question_text`
- `question_type`
- `priority`
- `status`
- `period_id`
- `related_hypothesis_id`
- `affected_outputs`
- `why_it_matters`
- `expected_gain`
- `answer_options`
- `created_at`
- `updated_at`

Clarification answer fields should include:

- `id`
- `question_id`
- `answer_type`
- `answer_value`
- `answer_confidence`
- `source_class`
- `affected_objects`
- `created_at`

### Offline and Audio Schema

Offline event fields should include:

- `id`
- `case_id`
- `event_type`
- `title`
- `user_summary`
- `auto_summary`
- `timestamp_start`
- `timestamp_end`
- `period_id`
- `review_status`
- `impact_summary`
- `created_at`
- `updated_at`

Audio asset fields should include:

- `id`
- `offline_event_id`
- `file_path`
- `duration_seconds`
- `transcript_status`
- `transcript_text`
- `speaker_review_status`
- `processing_status`
- `created_at`
- `updated_at`

Audio should also support separate:

- segments
- snippets

### Strategy and Draft Schema

Strategy record fields should include:

- `id`
- `case_id`
- `period_id`
- `state_snapshot_id`
- `strategy_confidence`
- `recommended_goal`
- `why_not_others`
- `created_at`

Strategy options should be stored separately with:

- `strategy_record_id`
- `action_type`
- `summary`
- `purpose`
- `risk`
- `when_to_use`
- `success_signs`
- `failure_signs`
- `is_primary`

Draft record fields should include:

- `id`
- `strategy_record_id`
- `source_session_id`
- `main_draft`
- `alt_draft_1`
- `alt_draft_2`
- `style_notes`
- `confidence`
- `created_at`

Draft outcomes should include:

- `draft_id`
- `actual_message_id`
- `match_score`
- `outcome_label`
- `notes`

### Inbox, Conflict, Outcome, and Review Schema

Inbox item fields should include:

- `id`
- `item_type`
- `source_object_type`
- `source_object_id`
- `priority`
- `is_blocking`
- `title`
- `summary`
- `period_id`
- `status`
- `created_at`
- `updated_at`

Conflict record fields should include:

- `id`
- `conflict_type`
- `object_a_type`
- `object_a_id`
- `object_b_type`
- `object_b_id`
- `summary`
- `severity`
- `status`
- `created_at`
- `updated_at`

Outcome fields should include:

- `id`
- `strategy_record_id`
- `draft_id`
- `actual_action_type`
- `actual_message_id`
- `observed_outcome_label`
- `user_outcome_label`
- `outcome_confidence`
- `summary`
- `created_at`
- `updated_at`

Review event fields should include:

- `id`
- `object_type`
- `object_id`
- `action`
- `old_value_ref`
- `new_value_ref`
- `reason`
- `actor`
- `created_at`

### Tag, Activity, Alias, and Dependency Schema

Tags should use:

- `Tag`
- `TagAssignment`

Activity log should use fields:

- `id`
- `event_category`
- `event_type`
- `object_type`
- `object_id`
- `summary`
- `payload_ref`
- `actor`
- `created_at`

Aliases should use:

- separate alias records
- merge suggestion records

Dependency tracking should support:

- `upstream_type`
- `upstream_id`
- `downstream_type`
- `downstream_id`
- `link_reason`

### JSON vs Table Policy

Schema policy should be:

- core entities and important fields in typed tables
- small explainers/options/payloads allowed in JSON

## Clarification Orchestration Decisions

### Clarification Model

Clarifications should be represented as:

- prioritized queue
- plus dependency graph

Not as:

- flat independent question list

Priority levels should be:

- `blocking`
- `important`
- `optional`

### Waves and Channels

Clarification should be asked in waves.

Preferred wave size:

- `3-5` questions

Bot should ask:

- one question at a time

Web should show:

- current wave
- plus backlog

### Triggering Policy

The system should ask only questions that:

- materially affect the model

Priority should be computed from impact on:

- timeline
- current state
- strategy

### Deduplication and Dependencies

Similar questions should be:

- collapsed under parent question logic where possible

If answering one question resolves others:

- automatically close or lower priority of dependent questions

### Question Formats

Clarification formats should support:

- yes/no
- answer choice
- short text
- free text

When the model has a tentative interpretation, questions should be:

- neutral
- with options when useful

Not strongly leading.

### Answer Confidence and Source

Answer confidence should be:

- optional for the user

Clarification answers should be treated:

- according to answer type

Meaning:

- some become user-confirmed truth
- some remain user-reported context

### Stopping and Re-Asking

Clarification should stop when:

- certainty is sufficient
- or only optional items remain

Skipped questions should:

- return later
- with lower priority

### Contradictions and Review

If a new clarification answer conflicts with previous answers:

- create conflict

Do not:

- silently overwrite

Answers should go to review:

- selectively

### User Motivation and Timing

Clarification UI should show:

- why the question matters
- what it may change

Clarification should begin:

- after initial period/state assembly

Not:

- immediately at raw archive import

Live-mode clarification should continue:

- in a limited way

### Binding and Recompute

Clarification items should bind to:

- period
- hypothesis
- affected outputs

After answer submission, recompute should be:

- local
- plus affected dependent layers

### Clarification Success

Clarification success should be measured by:

- certainty increase
- quality improvement
- conflict reduction

## Bot Response Formatting Decisions

### `/state` Formatting

`/state` should default to:

- `State`
- `Status`
- `Signals`
- `Risk`
- `Next`
- `Do not`

Preferred response length:

- `6-10` lines

Confidence should be shown:

- briefly

Example style:

- `Confidence: medium`

### `/draft` Formatting

`/draft` should default to:

- `Main`
- main draft text
- `Alt 1`
- alternative text
- `Alt 2`
- alternative text
- `Why`
- short explanation

Each draft should use:

- natural sendable length

Explanations should be:

- short
- tied to each option

### `/review` Formatting

`/review` should default to:

- `Assessment`
- `Risks`
- `Safer`
- `More natural`

Risk count should be:

- `1-2`

### `/gaps` Formatting

`/gaps` should default to:

- `Question`
- `Why this matters`
- `Options`
- `Your answer`

Answer options should be:

- max `2-3`

Progress display should show:

- wave progress

Example:

- `Question 1 of 4`

### `/offline` Formatting

After audio upload, `/offline` should show:

- `Auto summary`
- `What to fix/add`

Then continue into:

- clarification prompts

### `/timeline`, `/profile`, `/next`

`/timeline` should default to:

- current period
- short summary
- `2-3` previous periods

`/profile` should default to:

- summary
- patterns
- evidence

With:

- `3-5` key patterns maximum by default

`/next` should default to:

- `Best option`
- `Alternative`
- `Risks`
- `Use when`
- `Avoid when`

### Tone and Presentation

Default bot tone should be:

- businesslike
- concise

Formatting richness should be:

- moderate

Emoji use should be:

- none

### Buttons and Long Output Handling

Inline buttons should be used mainly for:

- clarification answers
- offline flow continuation
- show more / next question
- open web / inbox links

When bot output becomes too long, prefer:

- summary
- plus pointer to web for details

Rather than:

- long multi-message reports by default

### Errors, Sensitive Output, and Help

Errors should be:

- short
- actionable

Sensitive output should be:

- softer
- explicitly uncertain
- less categorical

Reminders should format as:

- `Inbox`
- `Blocking: N`
- `Top items`
- `Open in web`

Help/menu should be:

- one line per command
- short descriptions

### Language Policy

Bot UI language should be:

- Russian

Internal keys should:

- never be shown directly in normal UX

## Web Information Architecture Decisions

### Dashboard

Preferred dashboard block order:

1. current state
2. next step
3. open clarifications
4. current period
5. alerts
6. dossier snapshot
7. recent changes

Above the fold should show:

- current state
- next step
- blocking clarifications or alerts

Collapsed by default:

- dossier snapshot
- recent changes
- deeper history snippets

### Primary Navigation

Primary web navigation should include:

- dashboard
- dossier
- timeline
- clarifications
- current state
- profiles
- offline events
- network
- history
- inbox

### Dossier

Dossier should default to three sections:

- confirmed
- hypotheses
- conflicts

Filters should include:

- source
- period
- sensitivity
- review status

Default sorting should be:

- confirmed by importance and freshness
- hypotheses by impact and uncertainty
- conflicts by severity

### Timeline

Timeline should default to:

- vertical period feed
- current period highlighted
- transition markers between period cards

Period detail should include:

- summary
- events
- what helped
- what hurt
- open questions
- evidence
- lessons
- history

### Clarifications

Clarifications should be organized into:

- current wave
- backlog
- resolved

Clarification cards should include:

- question
- why it matters
- related period
- related hypothesis or output
- answer choices
- free input
- expected impact

### Inbox

Inbox should use:

- list view by default
- filters above or to the side
- detail pane or drawer

Default grouping should be:

- blocking
- high impact
- everything else

### Current State

Current State screen should include:

- state summary
- scores
- key signals
- risks
- next step
- do not do
- history trend

### Profiles

Profiles should default to:

- comparative view

Subjects:

- me
- other
- pair

Each should show:

- summary
- top traits
- evidence
- what works
- what fails

### Offline Events

Offline Events should include:

- event list
- selected event detail
- transcript
- snippets
- model impact
- speaker review section

### Network

Network should use:

- node list with filters
- simple graph view
- selected node detail panel

### History

History should default to:

- chronological feed

With filters for:

- object type
- impact

Detailed diffs should open:

- in drawer or detail panel

### Search and Saved Views

Search should be:

- global
- always available
- grouped by object type in results
- blended semantic and keyword

Saved views should be:

- accessible from sidebar or top navigation
- linked from inbox and search flows

### Review UX

Review should use a unified card pattern with:

- object summary
- provenance
- suggested change
- confirm
- reject
- defer
- edit
- reason field when needed

Bulk review should use:

- list or table
- checkboxes
- batch action bar
- impact preview before apply

Edit mode should use:

- inline editing for simple changes
- drawer or modal for larger edits
- required reason field
- impacted modules preview when recomputation matters

### Sensitive Content

Sensitive content should be:

- visible but softened
- marked with warning chip
- reveal-gated for especially sensitive details

### Cross-Linking

The web should support cross-linking between:

- object and history
- conflict and both sides
- period and evidence
- inbox item and source object

### Visual Direction

The first web version should be:

- simple
- quiet
- analytical
- light theme first

Visual hierarchy should rely on:

- spacing
- cards
- tabs
- chips

## Launch Scope Decisions

### First Usable Release Shape

The first usable release should be:

- archive analysis
- plus live assistant behavior

Not:

- archive-only
- and not an attempt at the full maximum product

Core promise of the first usable release should include:

- archive import
- clarification-driven reconstruction
- period timeline
- current state
- live monitoring
- draft and review in bot
- web review console

### First Release Web Scope

Must-have screens:

- dashboard
- dossier
- timeline
- clarifications
- current state
- profiles
- offline events
- inbox

Can wait until later:

- polished network visualization
- advanced history diff UX
- saved-view polish
- deeper search tuning

### First Release Bot Scope

Must-have bot commands:

- `/state`
- `/draft`
- `/review`
- `/offline`
- `/gaps`
- `/answer`
- `/timeline`

Can be lighter in first release:

- richer `/profile`
- extra `/next` polish
- advanced help/navigation polish

### First Release Backend Scope

Must-have backend capabilities:

- archive import
- live Telegram ingestion
- offline audio ingestion
- clarification orchestration
- periodization
- state engine
- draft/review engine
- inbox/review layer
- history/audit layer

Can wait until post-v1:

- GraphRAG dependency
- advanced pair-profile sophistication
- deep outcome learning
- canary experimentation
- heavy adaptation tuning

### Graph and Audio in First Release

GraphRAG in first release should be:

- optional only

Offline audio in first release should be:

- basic but real

Meaning:

- upload
- transcript
- summary
- user summary
- clarifications
- impact on model

### Profile, Strategy, Review, Search, History

Profile depth in first release should be:

- moderate

Strategy depth in first release should be:

- strong immediate step
- light short horizon

Review scope in first release should cover critical objects first:

- facts
- hypotheses
- periods
- gaps
- social graph nodes
- state-changing interpretations

Search in first release should be:

- basic text search
- plus filters

History in first release should be:

- basic but usable

Inbox in first release should be:

- basic prioritized inbox

### Security and Ops in First Release

First release must include:

- basic auth
- redaction baseline
- retention config
- export
- purge

Basic admin actions must exist for:

- recompute period
- recompute state
- recompute profile
- replay offline event
- review merges
- rebuild selected case

### Release Readiness Rule

First release is ready only if it has:

- working end-to-end archive flow
- working live flow
- clarification loop
- usable bot
- usable web
- reviewable model outputs
- eval baseline
- basic privacy controls

### Post-v1 Bucket

Post-v1 items should explicitly include:

- graph-first reasoning dependency
- advanced semantic search
- advanced network visualization
- richer strategy horizon
- stronger automatic adaptation
- deep style lab
- polished analytics dashboards

## Security and Retention Defaults

### Retention Defaults

Raw audio should be retained:

- for a limited period by default

Preferred default range:

- `30-90 days`

Transcripts should be retained:

- longer than raw audio by default

Derived snippets should be retained:

- roughly as long as transcripts
- or slightly longer

Application logs should be retained:

- briefly

Preferred default:

- `7-14 days`

Temporary artifacts should auto-delete:

- quickly

Preferred default:

- `1-7 days`

### Redaction and Sensitivity

Log redaction should be:

- aggressive by default

Sensitive content in web should use:

- warning
- reveal step

Sensitive defaults should apply especially to:

- raw audio
- full transcripts
- sensitive profile traits
- sensitive period notes
- contradiction-heavy private notes
- intimate manual context notes

### Export and Purge

Export should default to:

- layer-based selection

Purge should support:

- whole case
- per data type

Auto-delete should be enabled by default only for:

- logs
- temporary artifacts

### Rebuild and Access Safety

If raw audio is deleted, rebuild should:

- continue from transcript and derived layers where possible

Sensitive access should require:

- extra confirmation

Sensitive objects should be excluded by default from:

- ordinary bulk views

Unless:

- explicitly requested

### Bot and Backup Policy

Bot should show sensitive material:

- only in reduced or summarized form by default

Backups should be:

- encrypted

### Configuration and Incident Readiness

Retention controls should exist first in:

- config

And may later appear in:

- web admin

Privacy incident readiness should include:

- dedicated runbook

### First Release Privacy Minimum

First release must include:

- log redaction
- retention configuration
- export
- purge
- encrypted backups
- warning and reveal behavior for sensitive materials

## Review Threshold Decisions

### Auto-Apply Philosophy

Auto-apply should be limited to:

- low-risk
- structured
- sufficiently grounded outputs

### Facts and Events

Facts may auto-apply when:

- confidence is high
- sensitivity is low
- no conflict exists

Events may auto-apply when:

- they are clearly grounded
- sensitivity is low
- no major downstream ambiguity exists

### Hypotheses

Hypotheses may auto-apply only as:

- internal working objects

They should not automatically become:

- fully surfaced reviewed knowledge

### Periods and State

Periods may be created automatically.

But:

- high-uncertainty periods should enter review or inbox

Period merge and split should:

- never auto-apply
- be proposed for review

State snapshots should:

- auto-apply
- but be flagged when confidence is low

### Profiles and Strategy

Profiles should auto-apply as:

- draft profiles

Not as:

- silently final trusted profiles

Strategy should auto-apply as:

- ephemeral recommendation layer

Not as:

- durable truth layer

### Social Graph

New graph nodes may auto-apply when:

- role is clear

Influence edges may auto-apply only when:

- evidence is strong

### Clarification Answers

Clarification answers may auto-apply when:

- answer type is low-risk

More sensitive or conflicting answers should:

- enter review logic

### Inbox Admission

Inbox should admit mainly:

- uncertain items
- high-impact items

Blocking should be reserved for items that materially affect:

- current state
- strategy
- major timeline interpretation

### Mandatory Review Zones

Manual review should always be required for:

- period merge or split
- strong third-party influence claims
- sensitive profile traits
- major relationship status shifts with weak confidence
- contradictory clarification answers
- ambiguous alias merges

### Threshold Model

Thresholds should be:

- object-type specific

Suggested threshold severity by object family:

- facts: moderate-high
- events: moderate
- period boundaries: moderate
- profiles: high
- influence: high
- sensitive traits: very high

### Escalation and Hidden Internal State

If an object fails threshold:

- it should either enter inbox
- or remain hidden/internal depending on type

Hidden/internal handling is preferred for:

- weak hypotheses
- low-confidence profile candidates
- weak influence candidates
- weak paralinguistic signal candidates

### Review Priority and Defer

Review priority should combine:

- impact
- uncertainty
- sensitivity
- contradiction
- recency

Deferred items should:

- return later
- with lower priority

### Re-Review and Overrides

Reviewed items should re-enter review only when:

- new strong conflicting evidence appears

User override should:

- normally take precedence
- but may enter conflict state against strong new evidence

### Sensitive Review and Channel Split

Sensitive materials should have:

- stronger review gate

Review channel split should be:

- bot for simple and flow-bound items
- web for serious review

## Score-to-Label Mapping Decisions

### Mapping Philosophy

Label mapping should use:

- hybrid logic

Meaning:

- score heuristics
- plus model interpretation

Not:

- only rigid rules
- or only free-form model inference

### Dynamic State Inputs

Dynamic labels should be driven primarily by:

- warmth
- reciprocity
- initiative
- responsiveness
- ambiguity
- avoidance risk

### Relationship Status Inputs

Relationship status should be driven primarily by:

- period context
- relationship history
- explicit status evidence
- current closeness signals
- ambiguity

### Dynamic Label Rules

`warming` should require:

- warmth above baseline
- responsiveness improving
- initiative stable or rising
- avoidance not dominant
- ambiguity not overwhelming

`stable` should require:

- no strong upward or downward movement
- acceptable reciprocity
- initiative not collapsing
- ambiguity low or moderate

`cooling` should require:

- reduced warmth
- weaker responsiveness
- worsening initiative balance
- rising avoidance risk

`fragile` should require:

- active contact
- weak or unstable reciprocity
- noticeable ambiguity
- easy breakability of momentum

`uncertain_shift` should require:

- mixed signals
- conflicting cues
- recent movement
- insufficient certainty for stronger directional label

`low_reciprocity` should require:

- persistent initiative asymmetry
- contact still present
- reciprocity clearly weaker than interaction volume suggests

`testing_space` should require:

- lighter or intentionally lower-pressure interaction
- more room-giving behavior
- no strong evidence of outright cooling

`reengaging` should require:

- prior cooler or distant phase
- renewed interaction energy
- improving contact quality

### Dynamic Label Priority

When multiple dynamic labels are plausible, priority should use:

- priority order
- plus ambiguity fallback

Preferred priority order:

1. `uncertain_shift`
2. `fragile`
3. `low_reciprocity`
4. `cooling`
5. `reengaging`
6. `warming`
7. `testing_space`
8. `stable`

Alternative dynamic label may be shown:

- when a competing label is genuinely close

### Relationship Status Rules

`platonic` should mean:

- non-romantic baseline
- no strong reopening evidence

`warm_platonic` should mean:

- platonic baseline
- genuine warmth
- insufficient reopening evidence

`ambiguous` should mean:

- plausible romantic and platonic interpretations remain alive

`reopening` should mean:

- romantic history exists or is strongly implied
- renewed closeness signals exist
- reopening interpretation is stronger than baseline platonic reading

`romantic_history_distanced` should mean:

- prior romantic relationship history
- current distance from that prior state

`fragile_contact` should mean:

- contact exists
- continuity is weak
- rupture risk is meaningful

`detached` should mean:

- low engagement
- low reciprocity
- little active closeness

Alternative relationship status should be shown:

- only when ambiguity is above threshold
- and competing interpretation is genuinely plausible

### Confidence and Ambiguity

Confidence should be computed from:

- score coherence
- evidence quality
- conflict level

Confidence bands should be:

- `low`
- `medium`
- `high`

If ambiguity is high and signals are mixed:

- favor ambiguous or uncertain interpretation
- do not bias toward optimistic reading

### Avoidance and External Pressure

If avoidance risk is high:

- dynamic interpretation should not become strongly optimistic without strong counter-evidence

If external pressure is high:

- reduce confidence in relationship-only explanations
- increase contextual interpretation weight

### Historical and Pair-Pattern Modulation

Pair profile should modulate labels.

Examples:

- good repair history can soften one weak reciprocity moment
- repeated collapse after brief warming can reduce optimism for warming spikes

Current period should dominate mapping.

Historical periods should affect mapping:

- adaptively
- based on similarity

### Overrides, Explanations, and Hysteresis

Manual overrides should:

- not erase score computation
- but take precedence in displayed interpretation until strong conflict emerges

For every label, the system should be able to explain:

- why the label won
- what alternative was close
- what would need to change to flip the interpretation

Label transitions should use:

- hysteresis

Meaning:

- labels do not flip on every small score change
- borderline movement first raises ambiguity or conflict

### Mapping-Based Review Triggers

Mapping should trigger review or inbox when:

- label changed materially
- confidence dropped sharply
- status and dynamic state conflict increased
- ambiguity spiked
- current label contradicts recent reviewed interpretation

### First Release Mapping Complexity

First release mapping should be:

- medium complexity
