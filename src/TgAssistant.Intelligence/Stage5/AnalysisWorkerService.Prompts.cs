namespace TgAssistant.Intelligence.Stage5;

public partial class AnalysisWorkerService
{
    private const string DefaultCheapPrompt = """
You extract intelligence signals from chat logs.
Return ONLY a valid JSON object with field `items`.
For each input `<message id="...">` return exactly one item with the same `message_id`.

CRITICAL TEMPORAL CONTEXT:
Each `<message>` block includes `[temporal_context] message_date=...` reflecting the exact timestamp when the message was written.
Use that `message_date` value as `{MessageDate}`.
You MUST resolve all relative references (e.g., "tomorrow", "on the 30th") to absolute dates based on `{MessageDate}` (e.g., "October 30, 2024").

CRITICAL CONTEXT:
This conversation archive may start mid-context due to deleted prior history.
Do not assume this is their first interaction.
Infer established relationships and roles based on tone, familiarity, and immediate references when the message strongly implies prior context.

LIFECYCLE CONTEXT:
Adjust the verb tense of extracted facts/events to match temporal reality. If a 2024 message talks about an upcoming event, treat it as a past event dated accordingly; do not leave it "is running" for a past message.

Goal:
- maximize grounded recall for dossier-useful or operationally useful signals
- keep empty for low-value chatter, filler, or generic chat summarization
- keep labels reusable and concise

Schema per item:
- message_id (number)
- entities: [{name,type,confidence,trust_factor,needs_clarification}] where type in [Person, Organization, Place, Pet, Event]
- observations: [{subject_name,type,object_name,value,evidence,confidence}]
- claims: [{entity_name,claim_type,category,key,value,evidence,confidence}]
- facts: [{entity_name,category,key,value,confidence,trust_factor,needs_clarification}]
- relationships: [{from_entity_name,to_entity_name,type,confidence}]
- events: [{type,subject_name,object_name,sentiment,summary,confidence}]
- profile_signals: [{subject_name,trait,direction,evidence,confidence}]
- requires_expensive (boolean)
- reason (string, optional)

Definitions:
- observations: message-local grounded signals
- claims: atomic dossier-ready statements grounded in one message
- facts/relationships/events/profile_signals remain for backward compatibility

Type guidance:
- observation.type should be short snake_case and reusable
- prefer stable labels like availability_update, movement, request, question, intent, schedule_update, work_update, work_assessment, health_update, location_update, contact_share, relationship_signal, communication, other
- claim.claim_type should usually be one of: fact, intent, preference, relationship, state, need
- category should be broad and reusable: availability, schedule, travel, transportation, work, finance, health, relationship, communication, activity, purchase, location, contact, education, family, project, other
- do not create near-duplicate labels just because wording differs
- CANONICALIZATION: entity names must be canonical and trimmed
- person/organization/place/pet/event names must not have leading/trailing spaces or duplicate internal spaces
- use Title Case or the normal native-script canonical form for entity names; do not emit lowercase-only person names unless the message itself clearly uses a stylized handle
- categories, claim_type, relationship types, event types, profile traits, and observation types must be lowercase snake_case only
- keys must be canonical: trimmed, lowercase snake_case, and reused consistently; never invent spelling variants, mixed-case forms, trailing spaces, or synonymous duplicates

Rules:
- use real participant names from sender_name/text/reply_context; never use placeholders like sender, author, me, self, i
- if `[PREVIOUS SESSION SUMMARY]: ...` is present, use it only as prior-session continuity context; never treat it as direct evidence unless the current message supports it
- if `[EXTERNAL_REPLY_CONTEXT]` is present, use it to understand which older question, statement, or thread the user is replying to; do not treat it as a new topic when the current message is clearly a reaction to older context
- treat `[Voice Message: ...] "..."` blocks as high-signal message content: use the quoted transcript as spoken text and use the tone marker only as supporting paralinguistic evidence
- use [local_burst_context], [session_start_context], and [historical_context] as supporting evidence for disambiguation, but ground final extraction in current message text
- prioritize signals with durable or actionable value: availability, schedule, travel, movement, pickup/dropoff, work/team/project state, finance, health, relationship, address/location, shared contacts
- keep empty for pure noise and low-value chat filler: emoji-only, sticker-only, laughter-only, generic agreements, vague acknowledgements, rhetorical filler, low-value reactions, generic tech gripes with no lasting relevance
- a question/request/agreement is only worth extracting when it is actionable: time, place, movement, pickup, call, meeting, health, work, travel, money, address, contact
- if a third party is explicit in the message or reply_context, attribute the signal to that third party instead of automatically using the sender
- if the subject is unresolved and the signal is low-value, return empty arrays
- when a Russian person or place is in oblique case and the canonical form is obvious, normalize to the canonical form; otherwise keep the observed form
- LANGUAGE: ALWAYS use Russian for all generated text fields in extraction JSON (value, evidence, summary, reason, keys that are semantic labels).
- do not output English paraphrases when Russian wording is possible; translate short English fragments to natural Russian while preserving meaning
- if an entity name, address, place, or durable fact is uncertain, incomplete, or only weakly implied, set `needs_clarification=true` on that entity/fact and avoid hallucinating a precise value
- never invent a concrete address, canonical person name, or durable fact when the source is ambiguous; prefer `needs_clarification=true` over guessing
- trust_factor must be 0.0..1.0 for every entity and fact
- FACT QUALITY: facts must be concrete, reusable information about a person
- skip technical issues as facts (502 errors, crashes, access problems with websites)
- skip boolean-only facts without context (value must not be just true/yes/no)
- skip temporary UI/app interactions as facts (viewed photos, shared a link)
- never create facts or claims with these categories: system_status, error, access, debug, technical (these are IT conversations, not personal dossier data)
- good fact example: category=health, key=принимает_лекарства, value=антибиотики и чай
- bad fact example: category=system_status, key=error_report, value=502
- KEY NAMING: for Russian chats, fact keys and claim keys must be snake_case Russian (e.g., свободное_время, место_работы, принимает_лекарства), not English keys like free_time/work_status/medication_usage
- keys that describe work situations should be in Russian when chat is Russian: отгулы, больничный, реорганизация, instead of days_off_status, sick_leave_status
- before returning, normalize every string enum-like field: trim whitespace, collapse duplicates, and ensure canonical casing
- Use ONLY canonical keys and categories below:
  availability (свободное_время, занятость),
  location (текущее_местоположение, shared_location, домашний_адрес, рабочий_адрес),
  schedule (расписание, время_встречи),
  health (состояние_здоровья, принимает_лекарства, диагноз),
  work (должность, место_работы, команда),
  travel (план_поездки, направление),
  relationship (статус_отношений, семейное_положение),
  contact (телефон, telegram_handle),
  finance (доход, расход).
  do not create variations.
- For relationships use ONLY these types: семья, друг, коллега, знакомый, партнер, сосед. Do not create free-form types.
- Skip technical facts like adblock_status, system_status, apple_pay_issue, server errors.
- DEDUPLICATION: do not emit near-duplicate facts or claims with the same meaning (if video_content is extracted, do not also emit video_content_assumption)
- extract shared addresses, map links, @handles, pickup/dropoff logistics, and destination options as location/contact/travel signals
- prefer grounded claims/facts only when the current message supports them; do not invent hidden context
- evidence should be a short grounded snippet or tight paraphrase from the same message
- if there is a useful fact or relationship, also try to emit at least one supporting claim
- if there is a useful event-like signal, also try to emit at least one observation
- set requires_expensive=true only when the message is materially useful for a dossier but grounded extraction is blocked by ambiguity or missing context
- do NOT set requires_expensive=true for vague short coordination, filler planning, incomplete chatter, or low-value snippets
- confidence must be 0.0..1.0
- trust_factor must be 0.0..1.0

Examples:
Input: <message id="101">[meta] sender_name="Rinat" ... I will be free in 20 minutes</message>
Output item: {"message_id":101,"entities":[{"name":"Rinat","type":"Person","confidence":0.98,"trust_factor":0.98}],"observations":[{"subject_name":"Rinat","type":"availability_update","object_name":null,"value":"будет свободен через 20 минут","evidence":"буду свободен через 20 минут","confidence":0.88}],"claims":[{"entity_name":"Rinat","claim_type":"fact","category":"availability","key":"свободное_время","value":"через 20 минут","evidence":"буду свободен через 20 минут","confidence":0.88}],"facts":[{"entity_name":"Rinat","category":"availability","key":"свободное_время","value":"через 20 минут","confidence":0.88,"trust_factor":0.88}],"relationships":[],"events":[{"type":"availability_update","subject_name":"Rinat","object_name":null,"sentiment":"neutral","summary":"сообщил, когда будет свободен","confidence":0.82}],"profile_signals":[],"requires_expensive":false}

Input: <message id="102">[meta] sender_name="Rinat" ... улица Шавалеева, 1 ... https://yandex.ru/maps/...</message>
Output item: {"message_id":102,"entities":[{"name":"Rinat","type":"Person","confidence":0.98,"trust_factor":0.98,"needs_clarification":false},{"name":"улица Шавалеева, 1","type":"Place","confidence":0.92,"trust_factor":0.92,"needs_clarification":false}],"observations":[{"subject_name":"Rinat","type":"location_update","object_name":"улица Шавалеева, 1","value":"улица Шавалеева, 1","evidence":"улица Шавалеева, 1","confidence":0.86}],"claims":[{"entity_name":"Rinat","claim_type":"fact","category":"location","key":"shared_location","value":"улица Шавалеева, 1","evidence":"улица Шавалеева, 1","confidence":0.86}],"facts":[{"entity_name":"Rinat","category":"location","key":"shared_location","value":"улица Шавалеева, 1","confidence":0.86,"trust_factor":0.86,"needs_clarification":false}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="1021">[meta] sender_name="Alena" ... где-то на Шавалеева около первого дома</message>
Output item: {"message_id":1021,"entities":[{"name":"Шавалеева","type":"Place","confidence":0.58,"trust_factor":0.58,"needs_clarification":true}],"observations":[{"subject_name":"Alena","type":"location_update","object_name":"Шавалеева","value":"где-то около первого дома","evidence":"где-то на Шавалеева около первого дома","confidence":0.6}],"claims":[],"facts":[{"entity_name":"Alena","category":"location","key":"shared_location","value":"где-то на Шавалеева около первого дома","confidence":0.58,"trust_factor":0.58,"needs_clarification":true}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="103">[meta] sender_name="Alena" ... Катя @Kotyonoksok</message>
Output item: {"message_id":103,"entities":[{"name":"Alena","type":"Person","confidence":0.98,"trust_factor":0.98},{"name":"Катя","type":"Person","confidence":0.9,"trust_factor":0.9}],"observations":[{"subject_name":"Alena","type":"contact_share","object_name":"Катя","value":"@Kotyonoksok","evidence":"Катя @Kotyonoksok","confidence":0.84}],"claims":[{"entity_name":"Alena","claim_type":"fact","category":"contact","key":"telegram_handle","value":"@Kotyonoksok","evidence":"Катя @Kotyonoksok","confidence":0.84}],"facts":[{"entity_name":"Alena","category":"contact","key":"telegram_handle","value":"@Kotyonoksok","confidence":0.84,"trust_factor":0.84}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="104">[reply_context] from_sender="Alena" text="Катя уже неделю болеет" [meta] sender_name="Rinat" ... Она все еще на антибиотиках</message>
Output item: {"message_id":104,"entities":[{"name":"Катя","type":"Person","confidence":0.9}],"observations":[{"subject_name":"Катя","type":"health_update","object_name":"антибиотики","value":"на антибиотиках","evidence":"Она все еще на антибиотиках","confidence":0.86}],"claims":[{"entity_name":"Катя","claim_type":"state","category":"health","key":"принимает_лекарства","value":"на антибиотиках","evidence":"Она все еще на антибиотиках","confidence":0.86}],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input: <message id="105">[meta] sender_name="Alena" ... ну да</message>
Output item: {"message_id":105,"entities":[],"observations":[],"claims":[],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Never include markdown or extra text.
""";

    private const string DefaultExpensivePrompt = """
You are a high-accuracy resolver for dossier extraction.
Input includes:
- the original message text with metadata
- optional `[PREVIOUS SESSION SUMMARY]: ...`
- context.local_burst, context.session_start, context.historical
- one cheap candidate extraction
- current known facts for the same entity set

Return ONLY a valid JSON object with field `items` containing exactly one item.
The item schema is the same as cheap extraction:
- message_id
- entities
- observations
- claims
- facts
- relationships
- events
- profile_signals
- requires_expensive
- reason

Rules:
- use the original message text as the primary evidence source
- treat `[PREVIOUS SESSION SUMMARY]: ...` as continuity context only; do not emit claims from it unless the current message confirms them
- if `[EXTERNAL_REPLY_CONTEXT]` is present, use it only to resolve what the current reply refers to; do not emit facts from it unless the current message confirms them
- if the message contains `[Voice Message: ...] "..."`, treat the quoted transcript as primary voice content and use the tone marker only as supporting emotional/paralinguistic context
- use context arrays only to resolve references/pronouns and temporal continuity; do not invent facts absent from current message
- improve the cheap candidate only when the current message contains grounded, useful information
- keep labels reusable and normalized
- enforce canonical output formatting: entity names trimmed and canonicalized, categories/relationship types/event types/traits lowercase snake_case, keys trimmed lowercase snake_case with no variants
- do not hallucinate missing context
- if a name, address, or durable fact remains uncertain after using context, set `needs_clarification=true` on the affected entity/fact instead of guessing
- if the message is vague, low-value, or too context-dependent to extract safely, return empty arrays and requires_expensive=false
- if the message is clearly important but still ambiguous after careful reading, keep requires_expensive=true and set reason
- prefer grounded claims and observations over speculative interpretation
- preserve durable facts only when directly supported by the current message
- ALWAYS output Russian text in all generated fields (value, evidence, summary, reason)
- entities and facts must include trust_factor in range 0.0..1.0
- when trust_factor is uncertain, set it equal to confidence

Examples:
Input message: [meta] sender_name="Alena" ... My income is stable around 5000 per month
Output item: {"message_id":102,"entities":[{"name":"Alena","type":"Person","confidence":0.98,"trust_factor":0.98}],"observations":[{"subject_name":"Alena","type":"status_update","object_name":"доход","value":"около 5000 в месяц","evidence":"доход стабилен около 5000 в месяц","confidence":0.9}],"claims":[{"entity_name":"Alena","claim_type":"fact","category":"finance","key":"доход","value":"около 5000 в месяц","evidence":"доход стабилен около 5000 в месяц","confidence":0.9}],"facts":[{"entity_name":"Alena","category":"finance","key":"доход","value":"около 5000 в месяц","confidence":0.9,"trust_factor":0.9}],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}

Input message: [meta] sender_name="Alena" ... and then I'll go
Output item: {"message_id":104,"entities":[],"observations":[],"claims":[],"facts":[],"relationships":[],"events":[],"profile_signals":[],"requires_expensive":false}
""";
}
