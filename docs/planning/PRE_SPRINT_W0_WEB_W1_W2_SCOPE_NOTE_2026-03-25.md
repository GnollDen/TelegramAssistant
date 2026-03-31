# Pre-Sprint W0 Web W1/W2 Scope Lock Note

## Date

2026-03-25

## Purpose

Зафиксировать неизбыточный MVP scope для web track (W1/W2).

## W1 Scope Lock (Web Host and Operator Shell)

Входит в W1:
1. Реальный hosted web runtime поверх существующего web service-layer.
2. Минимальный single-operator access gate для internal использования.
3. Базовый operator shell/navigation.
4. Реальные web entry surfaces для:
- queue
- case detail
- artifact detail
5. Runtime wiring для web role без регрессии текущих verification paths.

Не входит в W1:
- расширенная deep-review UX
- public-product polish
- новые backend reasoning features
- broad redesign existing Stage6 contracts

## W2 Scope Lock (Core Operator Workflow)

Входит в W2:
1. Queue как ежедневный рабочий вход:
- status
- priority
- confidence
- reason
2. Case detail с evidence-first контекстом.
3. Artifact views (минимум):
- dossier
- current_state
- strategy
- draft
- review
4. Core actions в web:
- resolve
- reject
- refresh
- answer clarification

Не входит в W2:
- сложная clarification/deep-review orchestration (это W3)
- usability hardening/polish wave (это W4)
- multi-user/public product surface

## Web MVP Definition (Locked)

Web MVP считается достигнутым после W1+W2, если одновременно выполнено:
1. Hosted web app локально стартует повторяемо.
2. Оператор проходит queue -> case detail -> action loop в web.
3. Базовые artifact views доступны без перехода в bot для core workflow.
4. Clarification answer path доступен из web.

## Explicit MVP Non-Goals

MVP сознательно не включает:
- deep evidence graph exploration as mandatory UX
- high-polish visual system
- external/public exposure
- дублирование backend логики в отдельном frontend domain
