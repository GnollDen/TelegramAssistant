# Case ID Policy

## Purpose

Fix one canonical meaning for `case_id` so domain services do not drift.

## Rule

`case_id` is the top-level analysis scope identifier for one focal case.

It is not:

- a raw message id
- a chat session id
- an entity id
- a period id

It is:

- the stable container that groups all timeline, state, profile, strategy, clarification, and review artifacts for one analyzed case

## Operational Meaning

Within one `case_id`, the system may reference:

- one primary chat/thread source
- multiple sessions
- multiple periods
- multiple offline events
- multiple clarification and review objects

## Canonical Anchors

When a domain object belongs to a case, it should also reference canonical substrate where relevant:

- `chat_id`
- `source_message_id`
- `source_session_id`
- other canonical source ids where applicable

`case_id` defines scope.
Canonical ids define provenance and linkage.

## Service Rule

Services should:

- accept `case_id` as the primary analysis scope
- resolve canonical source objects within that scope
- avoid inferring a new `case_id` ad hoc from random lower-level objects

## Storage Rule

New domain objects should use `case_id` only when they are true case-scoped artifacts.

Do not introduce extra parallel identifiers when existing canonical ids already express the lower-level reference correctly.

## Future Rule

If multi-case or multi-user support grows later, `case_id` remains the analysis-scope key and should sit under a higher workspace/user partition rather than being redefined.
