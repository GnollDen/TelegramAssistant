# Stage 5 Micro Sprint

Это не новый большой Stage 5 redesign.
Это маленький polish/stabilization sprint после успешного baseline run.

## Контекст

Текущий Stage 5 baseline:

- baseline practically finished
- core Stage 5 usable for Stage 6
- quality verdict: acceptable / usable with caveats

Во время прогона были найдены точечные дефекты и шум:

- voice/paralinguistics warning-noise и sticky tail
- `finalized_sessions=0` semantics needs clarification/fix
- false positives на jokes/questions
- weak micro-claims
- duplicated signals between truth layers
- occasional summary truncation / slightly over-interpreted relational phrasing

## Главная цель

Сделать Stage 5 заметно чище и устойчивее без нового полного redesign и без тяжелого rerun-first мышления.

## Что нужно сделать

### 1. Voice/paralinguistics stabilization

Нужно:

- убрать лишний warning-noise where practical
- улучшить handling для response-format / non-json fallback path
- проверить sticky pending tail behavior
- не делать broad rewrite всей audio pipeline

Ожидаемый результат:

- меньше шумных повторов в логах
- меньше бессмысленного retry-churn
- более предсказуемый tail completion

### 2. Session finalization semantics

Нужно:

- проверить, почему `chat_sessions.is_finalized` остался 0
- определить:
  - expected operational behavior in current profile
  - или real lifecycle gap
- если это real defect, сделать минимальный safe fix

Важно:

- не включать обратно большой cold-path, если это не нужно
- не делать большой timeline/session redesign

### 3. Extraction polish

Нужно:

- снизить false positives на шутках, вопросах, полушутливых коротких репликах
- подрезать weak micro-claims
- уменьшить duplication между facts/claims/observations внутри одного message extraction

Ожидаемый фокус:

- no heavy prompt rewrite from scratch
- targeted heuristics / post-filter / dedup improvements

### 4. Summary polish

Нужно:

- уменьшить fallback/truncation-like summary outcomes
- снизить лишнюю relational interpretation, если evidence слабый
- сохранить useful compactness

### 5. Verification

Нужно проверить:

- build
- runtime startup sanity
- stage5 smoke
- no regression in current Stage 5 paths

Желательно:

- небольшой focused verification for:
  - voice tail behavior
  - extraction noise reduction
  - summary safety/faithfulness

## Ограничения

- не делать новый полный Stage 5 redesign
- не уходить в expensive pass revival
- не ломать Stage 6 contracts
- не делать full rerun silently
- changes должны быть маленькими и стабилизационными

## Финальный отчет строго

1. Что изменено
2. Какие файлы изменены
3. Что стало лучше в Stage 5
4. Что проверено
5. Какие ограничения остались

