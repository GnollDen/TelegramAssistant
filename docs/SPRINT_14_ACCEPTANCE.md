# Sprint 14 Acceptance

## Purpose

Validate that Sprint 14 added a usable social graph and network layer.

## Acceptance Checklist

## Node Coverage

- people can appear in the network
- groups/places/work-context nodes are supported where relevant
- roles are visible

## Edge Coverage

- influence edges exist
- information-flow direction is supported where seeded
- evidence linkage or confidence is visible

## Web Usefulness

- network page exists
- node detail is inspectable
- linked context is visible

## Verification

- build passes
- startup passes
- network smoke passes

## Hold Conditions

Hold Sprint 14 if any of these are true:

- network is mostly placeholder
- influence is not represented meaningfully
- roles are too weak to support later reasoning
- the web surface is too thin to inspect the graph

## Pass Condition

Sprint 14 passes if:

- the product now has a usable graph/network layer ready for later influence and external-context work
