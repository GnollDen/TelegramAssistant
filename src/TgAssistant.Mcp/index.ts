import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { StreamableHTTPServerTransport } from "@modelcontextprotocol/sdk/server/streamableHttp.js";
import { createServer, type IncomingMessage, type ServerResponse } from "node:http";
import { Pool } from "pg";
import { z } from "zod";

const postgresHost = process.env.POSTGRES_HOST ?? "127.0.0.1";
const postgresUser = process.env.POSTGRES_USER ?? "tgassistant";
const postgresPassword = process.env.POSTGRES_PASSWORD ?? "";
const postgresDb = process.env.POSTGRES_DB ?? "tgassistant";
const postgresPort = Number.parseInt(process.env.POSTGRES_PORT ?? "5432", 10);
const mcpTransport = (process.env.MCP_TRANSPORT ?? "stdio").trim().toLowerCase();
const sseHost = (process.env.MCP_SSE_HOST ?? "127.0.0.1").trim();
const ssePortRaw = Number.parseInt(process.env.MCP_SSE_PORT ?? "3800", 10);
const ssePort = Number.isNaN(ssePortRaw) ? 3800 : ssePortRaw;
const sseAuthToken = (process.env.MCP_SSE_AUTH_TOKEN ?? "").trim();
const allowContainerBind =
  (process.env.MCP_SSE_ALLOW_CONTAINER_BIND ?? "false").trim().toLowerCase() === "true";
const syntheticSmokeChatIdMin = 9_000_000_000_000;
const allowSyntheticScopes =
  (process.env.MCP_ALLOW_SYNTHETIC_SCOPES ?? "false").trim().toLowerCase() === "true";

const pool = new Pool({
  host: postgresHost,
  user: postgresUser,
  password: postgresPassword,
  database: postgresDb,
  port: Number.isNaN(postgresPort) ? 5432 : postgresPort
});

const server = new McpServer({
  name: "tgassistant-mcp",
  version: "0.1.0"
});

type EntityRow = {
  id: string;
  name: string | null;
  type: string | null;
  actor_key: string | null;
  telegram_user_id: string | null;
  telegram_username: string | null;
};

async function findBestEntityByName(entityName: string): Promise<EntityRow | null> {
  const normalizedName = entityName.trim();
  if (!normalizedName) {
    return null;
  }

  const entityResult = await pool.query<EntityRow>(
    `
      select
        id,
        name,
        type,
        actor_key,
        telegram_user_id,
        telegram_username
      from entities
      where name ilike $1
      order by
        case when lower(name) = lower($2) then 0 else 1 end asc,
        position(lower($2) in lower(name)) asc,
        char_length(name) asc,
        id asc
      limit 1
    `,
    [`%${normalizedName}%`, normalizedName]
  );

  return entityResult.rows[0] ?? null;
}

function formatValue(value: unknown): string {
  if (value === null || value === undefined) {
    return "";
  }

  if (typeof value === "string") {
    return value;
  }

  return JSON.stringify(value);
}

function maybeGetString(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function extractObservationTarget(
  value: unknown,
  fallbackEntityName: string
): string {
  const directString = maybeGetString(value);
  if (directString !== null) {
    return directString;
  }

  if (value !== null && typeof value === "object" && !Array.isArray(value)) {
    const record = value as Record<string, unknown>;
    const keys = [
      "target",
      "object",
      "to",
      "related_to",
      "related_entity",
      "entity",
      "person",
      "name"
    ];

    for (const key of keys) {
      const candidate = maybeGetString(record[key]);
      if (candidate !== null) {
        return candidate;
      }
    }
  }

  return fallbackEntityName;
}

function toTextResult(text: string) {
  return {
    content: [
      {
        type: "text" as const,
        text
      }
    ]
  };
}

function ensureOperatorVisibleScope(chatId: number, caseId?: number): string | null {
  if (allowSyntheticScopes || chatId < syntheticSmokeChatIdMin) {
    return null;
  }

  const scopeText =
    caseId === undefined
      ? `chat ${chatId}`
      : `case ${caseId} / chat ${chatId}`;
  return `Scope ${scopeText} is synthetic/smoke and hidden from default operator MCP reads. Set MCP_ALLOW_SYNTHETIC_SCOPES=true for explicit engineering/debug access.`;
}

function formatUtc(value: Date | string | null | undefined): string {
  if (!value) {
    return "n/a";
  }

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "n/a";
  }

  return date.toISOString().replace("T", " ").slice(0, 16) + " UTC";
}

function formatShortDate(value: Date | string | null | undefined): string {
  if (!value) {
    return "n/a";
  }

  const date = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "n/a";
  }

  return date.toISOString().slice(0, 10);
}

function formatNumber(value: number | null | undefined): string {
  return typeof value === "number" ? value.toFixed(2) : "n/a";
}

function clipText(value: string | null | undefined, maxLength = 140): string {
  const normalized = value?.replace(/\s+/g, " ").trim() ?? "";
  if (!normalized) {
    return "n/a";
  }

  if (normalized.length <= maxLength) {
    return normalized;
  }

  return normalized.slice(0, maxLength - 3).trimEnd() + "...";
}

function parseJsonSafe(value: unknown): unknown {
  if (value === null || value === undefined) {
    return null;
  }

  if (typeof value !== "string") {
    return value;
  }

  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  try {
    return JSON.parse(trimmed);
  } catch {
    return trimmed;
  }
}

function parseStringList(value: unknown, maxItems = 6): string[] {
  const parsed = parseJsonSafe(value);
  if (Array.isArray(parsed)) {
    return parsed
      .map((item) => maybeGetString(item))
      .filter((item): item is string => item !== null)
      .slice(0, maxItems);
  }

  const direct = maybeGetString(parsed);
  return direct ? [direct] : [];
}

function parseRiskSummary(value: string | null): string {
  if (!value?.trim()) {
    return "n/a";
  }

  const parsed = parseJsonSafe(value);
  if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
    const record = parsed as Record<string, unknown>;
    const labels = Array.isArray(record.labels)
      ? record.labels
          .map((item) => maybeGetString(item))
          .filter((item): item is string => item !== null)
      : [];
    const flags = Array.isArray(record.ethical_flags)
      ? record.ethical_flags
          .map((item) => maybeGetString(item))
          .filter((item): item is string => item !== null)
      : [];

    const parts: string[] = [];
    if (labels.length > 0) {
      parts.push(`risk=${labels.slice(0, 3).join(",")}`);
    }

    if (flags.length > 0) {
      parts.push(`ethics=${flags.slice(0, 3).join(",")}`);
    }

    if (typeof record.score === "number") {
      parts.push(`score=${record.score.toFixed(2)}`);
    }

    if (parts.length > 0) {
      return parts.join(" | ");
    }
  }

  return clipText(value, 100);
}

function formatArtifactStatus(row: {
  artifact_generated_at?: Date | string | null;
  artifact_refreshed_at?: Date | string | null;
  artifact_stale_at?: Date | string | null;
  artifact_is_stale?: boolean | null;
  artifact_stale_reason?: string | null;
}): string | null {
  if (!row.artifact_generated_at) {
    return null;
  }

  const parts = [`generated=${formatUtc(row.artifact_generated_at)}`];
  if (row.artifact_refreshed_at) {
    parts.push(`refreshed=${formatUtc(row.artifact_refreshed_at)}`);
  }

  if (row.artifact_stale_at) {
    parts.push(`stale_at=${formatUtc(row.artifact_stale_at)}`);
  }

  if (row.artifact_is_stale) {
    parts.push(`stale=${row.artifact_stale_reason ?? "yes"}`);
  }

  return parts.join(" | ");
}

function formatPeriodLabel(row: {
  period_label?: string | null;
  period_custom_label?: string | null;
}): string {
  return row.period_custom_label?.trim() || row.period_label?.trim() || "n/a";
}

function formatPeriodWindow(startAt: Date | string | null, endAt: Date | string | null): string {
  return `[${formatShortDate(startAt)}..${endAt ? formatShortDate(endAt) : "now"}]`;
}

function asGuidArray(values: string[]): string[] {
  return values.filter((value) =>
    /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)
  );
}

server.registerTool(
  "search_facts",
  {
    description: "Search facts in PostgreSQL by free text, entity name, and category.",
    inputSchema: {
      query: z
        .string()
        .min(1)
        .optional()
        .describe("Free-text search across entity name, fact key, and fact value."),
      entity_name: z
        .string()
        .min(1)
        .optional()
        .describe("Case-insensitive entity name filter."),
      category: z
        .string()
        .min(1)
        .optional()
        .describe("Case-insensitive fact category filter."),
      limit: z
        .number()
        .int()
        .positive()
        .max(500)
        .optional()
        .describe("Maximum rows to return. Defaults to 50.")
    }
  },
  async ({
    query,
    entity_name,
    category,
    limit
  }: {
    query?: string;
    entity_name?: string;
    category?: string;
    limit?: number;
  }) => {
    const whereClauses: string[] = [];
    const params: Array<string | number> = [];

    const queryFilter = query?.trim();
    if (queryFilter) {
      params.push(`%${queryFilter}%`);
      const placeholder = `$${params.length}`;
      whereClauses.push(
        `(e.name ilike ${placeholder} or f.key ilike ${placeholder} or f.value::text ilike ${placeholder})`
      );
    }

    const entityNameFilter = entity_name?.trim();
    if (entityNameFilter) {
      params.push(`%${entityNameFilter}%`);
      whereClauses.push(`e.name ilike $${params.length}`);
    }

    const categoryFilter = category?.trim();
    if (categoryFilter) {
      params.push(categoryFilter);
      whereClauses.push(`lower(f.category) = lower($${params.length})`);
    }

    const effectiveLimit = limit ?? 50;
    params.push(effectiveLimit);

    const sql = `
      select
        e.name as entity_name,
        f.category,
        f.key,
        f.value,
        f.confidence,
        f.decay_class
      from facts f
      join entities e on f.entity_id = e.id
      ${whereClauses.length > 0 ? `where ${whereClauses.join(" and ")}` : ""}
      order by e.name asc, f.category asc, f.key asc
      limit $${params.length}
    `;

    try {
      const result = await pool.query<{
        entity_name: string | null;
        category: string | null;
        key: string | null;
        value: unknown;
        confidence: number | null;
        decay_class: string | null;
      }>(sql, params);

      if (result.rows.length === 0) {
        return {
          content: [
            {
              type: "text",
              text: "No facts found for the provided filters."
            }
          ]
        };
      }

      const lines = result.rows.map((row, index) => {
        const formattedValue =
          row.value === null || row.value === undefined
            ? ""
            : typeof row.value === "string"
              ? row.value
              : JSON.stringify(row.value);

        const confidenceText = row.confidence ?? "n/a";
        const decayClassText = row.decay_class ?? "n/a";

        return `${index + 1}. ${row.entity_name ?? "unknown"} | ${row.category ?? "unknown"} | ${row.key ?? "unknown"} = ${formattedValue} (confidence: ${confidenceText}, decay_class: ${decayClassText})`;
      });

      return {
        content: [
          {
            type: "text",
            text: lines.join("\n")
          }
        ]
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);

      return {
        content: [
          {
            type: "text",
            text: `search_facts failed: ${message}`
          }
        ]
      };
    }
  }
);

server.registerTool(
  "get_entity_dossier",
  {
    description:
      "Get a comprehensive dossier for an entity, grouped by fact category.",
    inputSchema: {
      entity_name: z
        .string()
        .min(1)
        .describe("Entity name to search using case-insensitive partial match.")
    }
  },
  async ({ entity_name }: { entity_name: string }) => {
    try {
      const normalizedName = entity_name.trim();
      const entity = await findBestEntityByName(normalizedName);

      if (!entity) {
        return {
          content: [
            {
              type: "text",
              text: `No entity found for "${normalizedName}".`
            }
          ]
        };
      }

      const factsResult = await pool.query<{
        id: string;
        category: string | null;
        key: string | null;
        value: unknown;
        status: string | null;
        confidence: number | null;
        is_current: boolean | null;
        source_message_id: number | null;
      }>(
        `
          select
            id,
            category,
            key,
            value,
            status,
            confidence,
            is_current,
            source_message_id
          from facts
          where entity_id = $1
          order by
            lower(coalesce(category, '')),
            lower(coalesce(key, '')),
            id asc
        `,
        [entity.id]
      );

      const lines: string[] = [];
      const entityDisplayName = entity.name ?? "unknown";

      lines.push(`Entity Dossier: ${entityDisplayName}`);
      lines.push(`Entity ID: ${entity.id}`);
      lines.push(`Type: ${entity.type ?? "n/a"}`);
      lines.push(`Telegram Username: ${entity.telegram_username ?? "n/a"}`);
      lines.push(`Telegram User ID: ${entity.telegram_user_id ?? "n/a"}`);
      lines.push(`Actor Key: ${entity.actor_key ?? "n/a"}`);
      lines.push(`Matched Query: ${normalizedName}`);
      lines.push("");

      if (factsResult.rows.length === 0) {
        lines.push("No facts found for this entity.");

        return {
          content: [
            {
              type: "text",
              text: lines.join("\n")
            }
          ]
        };
      }

      const grouped = new Map<string, typeof factsResult.rows>();
      for (const row of factsResult.rows) {
        const category = row.category?.trim() || "uncategorized";
        const current = grouped.get(category) ?? [];
        current.push(row);
        grouped.set(category, current);
      }

      lines.push(`Total Facts: ${factsResult.rows.length}`);
      for (const [category, rows] of grouped.entries()) {
        lines.push("");
        lines.push(`Category: ${category}`);
        for (const row of rows) {
          const formatted = formatValue(row.value);
          const confidenceText = row.confidence ?? "n/a";
          const statusText = row.status ?? "n/a";
          const currentText =
            row.is_current === null ? "n/a" : row.is_current ? "yes" : "no";
          const sourceText = row.source_message_id ?? "n/a";

          lines.push(
            `- ${row.key ?? "unknown"}: ${formatted} (status: ${statusText}, confidence: ${confidenceText}, is_current: ${currentText}, source_message_id: ${sourceText})`
          );
        }
      }

      return {
        content: [
          {
            type: "text",
            text: lines.join("\n")
          }
        ]
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);

      return {
        content: [
          {
            type: "text",
            text: `get_entity_dossier failed: ${message}`
          }
        ]
      };
    }
  }
);

server.registerTool(
  "get_relationships",
  {
    description:
      "Get relationship edges for an entity, preferring structured relationships and falling back to observations.",
    inputSchema: {
      entity_name: z
        .string()
        .min(1)
        .describe("Entity name to search using case-insensitive partial match.")
    }
  },
  async ({ entity_name }: { entity_name: string }) => {
    const normalizedName = entity_name.trim();

    try {
      const entity = await findBestEntityByName(normalizedName);
      if (!entity) {
        return {
          content: [
            {
              type: "text",
              text: `No entity found for "${normalizedName}".`
            }
          ]
        };
      }

      const entityDisplayName = entity.name ?? normalizedName;
      const lines: string[] = [
        `Relationships for: ${entityDisplayName}`,
        `Entity ID: ${entity.id}`,
        ""
      ];

      let relationshipQueryError: string | null = null;
      let relationshipRows: Array<{
        from_name: string | null;
        to_name: string | null;
        type: string | null;
        status: string | null;
        confidence: number | null;
        context_text: string | null;
      }> = [];

      try {
        const structuredResult = await pool.query<{
          from_name: string | null;
          to_name: string | null;
          type: string | null;
          status: string | null;
          confidence: number | null;
          context_text: string | null;
        }>(
          `
            select
              e_from.name as from_name,
              e_to.name as to_name,
              r.type,
              r.status,
              r.confidence,
              r.context_text
            from relationships r
            left join entities e_from on e_from.id = r.from_entity_id
            left join entities e_to on e_to.id = r.to_entity_id
            where r.from_entity_id = $1 or r.to_entity_id = $1
            order by
              lower(coalesce(r.type, '')),
              lower(coalesce(e_from.name, '')),
              lower(coalesce(e_to.name, ''))
          `,
          [entity.id]
        );
        relationshipRows = structuredResult.rows;
      } catch (error) {
        relationshipQueryError =
          error instanceof Error ? error.message : String(error);
      }

      if (relationshipRows.length > 0) {
        lines.push("Source: relationships");
        for (const row of relationshipRows) {
          const fromName = row.from_name ?? "unknown";
          const toName = row.to_name ?? "unknown";
          const typeText = row.type ?? "related_to";
          const statusText = row.status ?? "n/a";
          const confidenceText = row.confidence ?? "n/a";
          const contextText = row.context_text ?? "n/a";

          lines.push(
            `- [${fromName}] --(${typeText})--> [${toName}] (status: ${statusText}, confidence: ${confidenceText}, context: ${contextText})`
          );
        }

        return {
          content: [
            {
              type: "text",
              text: lines.join("\n")
            }
          ]
        };
      }

      let observationRows: Array<{
        subject_name: string | null;
        observation_type: string | null;
        value: unknown;
        evidence: string | null;
        confidence: number | null;
      }> = [];

      try {
        const observationsResult = await pool.query<{
          subject_name: string | null;
          observation_type: string | null;
          value: unknown;
          evidence: string | null;
          confidence: number | null;
        }>(
          `
            select
              subject_name,
              observation_type,
              value,
              evidence,
              confidence
            from intelligence_observations
            where observation_type ilike '%relationship%'
              and (
                entity_id = $1
                or subject_name ilike $2
                or value::text ilike $2
              )
            order by lower(coalesce(observation_type, '')), message_id desc
          `,
          [entity.id, `%${entityDisplayName}%`]
        );
        observationRows = observationsResult.rows;
      } catch (error) {
        const observationError =
          error instanceof Error ? error.message : String(error);
        const detail = relationshipQueryError
          ? `relationships query error: ${relationshipQueryError}; observations query error: ${observationError}`
          : `observations query error: ${observationError}`;

        return {
          content: [
            {
              type: "text",
              text: `get_relationships failed: ${detail}`
            }
          ]
        };
      }

      if (observationRows.length === 0) {
        if (relationshipQueryError) {
          lines.push(
            "No relationships found. Structured relationships query was unavailable, and no relationship observations matched."
          );
          lines.push(`Structured query error: ${relationshipQueryError}`);
        } else {
          lines.push(
            "No relationships found in relationships or intelligence_observations."
          );
        }

        return {
          content: [
            {
              type: "text",
              text: lines.join("\n")
            }
          ]
        };
      }

      lines.push("Source: intelligence_observations (fallback)");
      if (relationshipQueryError) {
        lines.push(`Structured query error: ${relationshipQueryError}`);
      }

      for (const row of observationRows) {
        const subject = row.subject_name?.trim() || entityDisplayName;
        const target = extractObservationTarget(row.value, entityDisplayName);
        const typeText = row.observation_type ?? "relationship_observation";
        const confidenceText = row.confidence ?? "n/a";
        const evidenceText = row.evidence ?? "n/a";

        lines.push(
          `- [${subject}] --(${typeText})--> [${target}] (confidence: ${confidenceText}, evidence: ${evidenceText})`
        );
      }

      return {
        content: [
          {
            type: "text",
            text: lines.join("\n")
          }
        ]
      };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return {
        content: [
          {
            type: "text",
            text: `get_relationships failed: ${message}`
          }
        ]
      };
    }
  }
);

server.registerTool(
  "get_current_state",
  {
    description:
      "Get the latest current-state snapshot for a Stage 6 case/chat in a compact operator-readable format.",
    inputSchema: {
      case_id: z.number().int().positive().describe("Stage 6 case ID."),
      chat_id: z.number().int().positive().describe("Telegram chat ID.")
    }
  },
  async ({ case_id, chat_id }: { case_id: number; chat_id: number }) => {
    const scopeError = ensureOperatorVisibleScope(chat_id, case_id);
    if (scopeError) {
      return toTextResult(scopeError);
    }

    type CurrentStateRow = {
      artifact_generated_at: Date | null;
      artifact_refreshed_at: Date | null;
      artifact_stale_at: Date | null;
      artifact_is_stale: boolean | null;
      artifact_stale_reason: string | null;
      id: string;
      as_of: Date;
      dynamic_label: string | null;
      relationship_status: string | null;
      alternative_status: string | null;
      initiative_score: number | null;
      responsiveness_score: number | null;
      openness_score: number | null;
      warmth_score: number | null;
      reciprocity_score: number | null;
      ambiguity_score: number | null;
      avoidance_risk_score: number | null;
      escalation_readiness_score: number | null;
      external_pressure_score: number | null;
      confidence: number | null;
      key_signal_refs_json: unknown;
      risk_refs_json: unknown;
      source_session_id: string | null;
      source_message_id: number | null;
      created_at: Date;
      period_id: string | null;
      period_label: string | null;
      period_custom_label: string | null;
      period_start_at: Date | null;
      period_end_at: Date | null;
      period_summary: string | null;
    };

    try {
      const artifactResult = await pool.query<CurrentStateRow>(
        `
          select
            a.generated_at as artifact_generated_at,
            a.refreshed_at as artifact_refreshed_at,
            a.stale_at as artifact_stale_at,
            a.is_stale as artifact_is_stale,
            a.stale_reason as artifact_stale_reason,
            s.id,
            s.as_of,
            s.dynamic_label,
            s.relationship_status,
            s.alternative_status,
            s.initiative_score,
            s.responsiveness_score,
            s.openness_score,
            s.warmth_score,
            s.reciprocity_score,
            s.ambiguity_score,
            s.avoidance_risk_score,
            s.escalation_readiness_score,
            s.external_pressure_score,
            s.confidence,
            s.key_signal_refs_json,
            s.risk_refs_json,
            s.source_session_id,
            s.source_message_id,
            s.created_at,
            p.id::text as period_id,
            p.label as period_label,
            p.custom_label as period_custom_label,
            p.start_at as period_start_at,
            p.end_at as period_end_at,
            p.summary as period_summary
          from stage6_artifacts a
          join domain_state_snapshots s on s.id::text = a.payload_object_id
          left join domain_periods p on p.id = s.period_id
          where a.case_id = $1
            and a.chat_id = $2
            and a.artifact_type = 'current_state'
            and a.scope_key = $3
            and a.is_current = true
          order by a.generated_at desc
          limit 1
        `,
        [case_id, chat_id, `chat:${chat_id}`]
      );

      const fallbackResult =
        artifactResult.rows[0] !== undefined
          ? artifactResult
          : await pool.query<CurrentStateRow>(
              `
                select
                  null::timestamptz as artifact_generated_at,
                  null::timestamptz as artifact_refreshed_at,
                  null::timestamptz as artifact_stale_at,
                  null::boolean as artifact_is_stale,
                  null::text as artifact_stale_reason,
                  s.id,
                  s.as_of,
                  s.dynamic_label,
                  s.relationship_status,
                  s.alternative_status,
                  s.initiative_score,
                  s.responsiveness_score,
                  s.openness_score,
                  s.warmth_score,
                  s.reciprocity_score,
                  s.ambiguity_score,
                  s.avoidance_risk_score,
                  s.escalation_readiness_score,
                  s.external_pressure_score,
                  s.confidence,
                  s.key_signal_refs_json,
                  s.risk_refs_json,
                  s.source_session_id,
                  s.source_message_id,
                  s.created_at,
                  p.id::text as period_id,
                  p.label as period_label,
                  p.custom_label as period_custom_label,
                  p.start_at as period_start_at,
                  p.end_at as period_end_at,
                  p.summary as period_summary
                from domain_state_snapshots s
                left join domain_periods p on p.id = s.period_id
                where s.case_id = $1
                  and (s.chat_id = $2 or s.chat_id is null)
                order by
                  case when s.chat_id = $2 then 0 else 1 end asc,
                  s.as_of desc,
                  s.created_at desc
                limit 1
              `,
              [case_id, chat_id]
            );

      const row = fallbackResult.rows[0];
      if (!row) {
        return toTextResult(
          `Current state: insufficient data for case ${case_id} / chat ${chat_id}.`
        );
      }

      const signals = parseStringList(row.key_signal_refs_json, 5);
      const risks = parseStringList(row.risk_refs_json, 5);
      const lines = [
        `Current state | as_of=${formatUtc(row.as_of)} | dynamic=${row.dynamic_label ?? "n/a"} | status=${row.relationship_status ?? "n/a"} | conf=${formatNumber(row.confidence)}`,
        `Scores | init=${formatNumber(row.initiative_score)} resp=${formatNumber(row.responsiveness_score)} open=${formatNumber(row.openness_score)} warmth=${formatNumber(row.warmth_score)} reciprocity=${formatNumber(row.reciprocity_score)}`,
        `Risk frame | ambiguity=${formatNumber(row.ambiguity_score)} avoidance=${formatNumber(row.avoidance_risk_score)} readiness=${formatNumber(row.escalation_readiness_score)} external=${formatNumber(row.external_pressure_score)} alt=${row.alternative_status ?? "n/a"}`
      ];

      if (row.period_id) {
        lines.push(
          `Period | ${formatPeriodWindow(row.period_start_at, row.period_end_at)} ${formatPeriodLabel(row)} | ${clipText(row.period_summary, 110)}`
        );
      }

      lines.push(
        `Signals | ${signals.length > 0 ? signals.join(", ") : "insufficient data"}`
      );

      if (risks.length > 0) {
        lines.push(`Risk refs | ${risks.join(", ")}`);
      }

      const artifactStatus = formatArtifactStatus(row);
      if (artifactStatus) {
        lines.push(`Artifact | ${artifactStatus}`);
      }

      return toTextResult(lines.join("\n"));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return toTextResult(`get_current_state failed: ${message}`);
    }
  }
);

server.registerTool(
  "get_strategy",
  {
    description:
      "Get the latest Stage 6 strategy and its main options for a case/chat in a compact operator-readable format.",
    inputSchema: {
      case_id: z.number().int().positive().describe("Stage 6 case ID."),
      chat_id: z.number().int().positive().describe("Telegram chat ID.")
    }
  },
  async ({ case_id, chat_id }: { case_id: number; chat_id: number }) => {
    const scopeError = ensureOperatorVisibleScope(chat_id, case_id);
    if (scopeError) {
      return toTextResult(scopeError);
    }

    type StrategyRecordRow = {
      artifact_generated_at: Date | null;
      artifact_refreshed_at: Date | null;
      artifact_stale_at: Date | null;
      artifact_is_stale: boolean | null;
      artifact_stale_reason: string | null;
      id: string;
      period_id: string | null;
      state_snapshot_id: string | null;
      strategy_confidence: number | null;
      recommended_goal: string | null;
      why_not_others: string | null;
      micro_step: string | null;
      horizon_json: string | null;
      source_session_id: string | null;
      source_message_id: number | null;
      created_at: Date;
      period_label: string | null;
      period_custom_label: string | null;
      period_start_at: Date | null;
      period_end_at: Date | null;
    };

    type StrategyOptionRow = {
      action_type: string | null;
      summary: string | null;
      purpose: string | null;
      risk: string | null;
      when_to_use: string | null;
      success_signs: string | null;
      failure_signs: string | null;
      is_primary: boolean;
    };

    try {
      const artifactResult = await pool.query<StrategyRecordRow>(
        `
          select
            a.generated_at as artifact_generated_at,
            a.refreshed_at as artifact_refreshed_at,
            a.stale_at as artifact_stale_at,
            a.is_stale as artifact_is_stale,
            a.stale_reason as artifact_stale_reason,
            r.id,
            r.period_id::text as period_id,
            r.state_snapshot_id::text as state_snapshot_id,
            r.strategy_confidence,
            r.recommended_goal,
            r.why_not_others,
            r.micro_step,
            r.horizon_json,
            r.source_session_id::text as source_session_id,
            r.source_message_id,
            r.created_at,
            p.label as period_label,
            p.custom_label as period_custom_label,
            p.start_at as period_start_at,
            p.end_at as period_end_at
          from stage6_artifacts a
          join domain_strategy_records r on r.id::text = a.payload_object_id
          left join domain_periods p on p.id = r.period_id
          where a.case_id = $1
            and a.chat_id = $2
            and a.artifact_type = 'strategy'
            and a.scope_key = $3
            and a.is_current = true
          order by a.generated_at desc
          limit 1
        `,
        [case_id, chat_id, `chat:${chat_id}`]
      );

      const recordResult =
        artifactResult.rows[0] !== undefined
          ? artifactResult
          : await pool.query<StrategyRecordRow>(
              `
                select
                  null::timestamptz as artifact_generated_at,
                  null::timestamptz as artifact_refreshed_at,
                  null::timestamptz as artifact_stale_at,
                  null::boolean as artifact_is_stale,
                  null::text as artifact_stale_reason,
                  r.id,
                  r.period_id::text as period_id,
                  r.state_snapshot_id::text as state_snapshot_id,
                  r.strategy_confidence,
                  r.recommended_goal,
                  r.why_not_others,
                  r.micro_step,
                  r.horizon_json,
                  r.source_session_id::text as source_session_id,
                  r.source_message_id,
                  r.created_at,
                  p.label as period_label,
                  p.custom_label as period_custom_label,
                  p.start_at as period_start_at,
                  p.end_at as period_end_at
                from domain_strategy_records r
                left join domain_periods p on p.id = r.period_id
                where r.case_id = $1
                  and (r.chat_id = $2 or r.chat_id is null)
                order by
                  case when r.chat_id = $2 then 0 else 1 end asc,
                  r.created_at desc
                limit 1
              `,
              [case_id, chat_id]
            );

      const record = recordResult.rows[0];
      if (!record) {
        return toTextResult(
          `Strategy: insufficient data for case ${case_id} / chat ${chat_id}.`
        );
      }

      const optionsResult = await pool.query<StrategyOptionRow>(
        `
          select
            action_type,
            summary,
            purpose,
            risk,
            when_to_use,
            success_signs,
            failure_signs,
            is_primary
          from domain_strategy_options
          where strategy_record_id = $1::uuid
          order by is_primary desc, action_type asc, summary asc
          limit 4
        `,
        [record.id]
      );

      const primary = optionsResult.rows.find((row) => row.is_primary) ?? optionsResult.rows[0];
      const horizon = parseStringList(record.horizon_json, 4);
      const lines = [
        `Strategy | created=${formatUtc(record.created_at)} | confidence=${formatNumber(record.strategy_confidence)} | goal=${clipText(record.recommended_goal, 90)}`,
        `Micro-step | ${clipText(record.micro_step, 180)}`
      ];

      if (primary) {
        lines.push(
          `Primary option | ${primary.action_type ?? "n/a"} | ${clipText(primary.summary, 120)} | ${parseRiskSummary(primary.risk)}`
        );
      }

      if (optionsResult.rows.length > 0) {
        lines.push(
          `Options | ${optionsResult.rows
            .map((row, index) => {
              const primaryTag = row.is_primary ? "*" : "";
              return `${index + 1}${primaryTag}. ${row.action_type ?? "n/a"} - ${clipText(row.summary, 70)}`;
            })
            .join(" ; ")}`
        );
      } else {
        lines.push("Options | insufficient data");
      }

      if (record.period_id) {
        lines.push(
          `Period | ${formatPeriodWindow(record.period_start_at, record.period_end_at)} ${formatPeriodLabel(record)}`
        );
      }

      if (horizon.length > 0) {
        lines.push(`Horizon | ${horizon.join(" -> ")}`);
      }

      if (record.why_not_others?.trim()) {
        lines.push(`Why not others | ${clipText(record.why_not_others, 180)}`);
      }

      const artifactStatus = formatArtifactStatus(record);
      if (artifactStatus) {
        lines.push(`Artifact | ${artifactStatus}`);
      }

      return toTextResult(lines.join("\n"));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return toTextResult(`get_strategy failed: ${message}`);
    }
  }
);

server.registerTool(
  "get_profiles",
  {
    description:
      "List the latest available Stage 6 profiles for a case/chat, summarized for operator review.",
    inputSchema: {
      case_id: z.number().int().positive().describe("Stage 6 case ID."),
      chat_id: z.number().int().positive().describe("Telegram chat ID."),
      subject_type: z
        .string()
        .min(1)
        .optional()
        .describe("Optional subject type filter such as self, other, or pair."),
      subject_id: z
        .string()
        .min(1)
        .optional()
        .describe("Optional exact subject ID filter."),
      limit: z
        .number()
        .int()
        .positive()
        .max(12)
        .optional()
        .describe("Maximum profiles to list. Defaults to 6.")
    }
  },
  async ({
    case_id,
    chat_id,
    subject_type,
    subject_id,
    limit
  }: {
    case_id: number;
    chat_id: number;
    subject_type?: string;
    subject_id?: string;
    limit?: number;
  }) => {
    const scopeError = ensureOperatorVisibleScope(chat_id, case_id);
    if (scopeError) {
      return toTextResult(scopeError);
    }

    type ProfileRow = {
      id: string;
      subject_type: string;
      subject_id: string;
      period_id: string | null;
      summary: string | null;
      confidence: number | null;
      stability: number | null;
      created_at: Date;
      period_label: string | null;
      period_custom_label: string | null;
      period_start_at: Date | null;
      period_end_at: Date | null;
    };

    type TraitRow = {
      profile_snapshot_id: string;
      trait_key: string | null;
      value_label: string | null;
      confidence: number | null;
      stability: number | null;
    };

    try {
      const clauses = ["s.case_id = $1", "(s.chat_id = $2 or s.chat_id is null)"];
      const params: Array<number | string> = [case_id, chat_id];

      const subjectTypeFilter = subject_type?.trim();
      if (subjectTypeFilter) {
        params.push(subjectTypeFilter);
        clauses.push(`s.subject_type = $${params.length}`);
      }

      const subjectIdFilter = subject_id?.trim();
      if (subjectIdFilter) {
        params.push(subjectIdFilter);
        clauses.push(`s.subject_id = $${params.length}`);
      }

      const effectiveLimit = limit ?? 6;
      params.push(effectiveLimit);

      const profilesResult = await pool.query<ProfileRow>(
        `
          select *
          from (
            select distinct on (s.subject_type, s.subject_id)
              s.id,
              s.subject_type,
              s.subject_id,
              s.period_id::text as period_id,
              s.summary,
              s.confidence,
              s.stability,
              s.created_at,
              p.label as period_label,
              p.custom_label as period_custom_label,
              p.start_at as period_start_at,
              p.end_at as period_end_at
            from domain_profile_snapshots s
            left join domain_periods p on p.id = s.period_id
            where ${clauses.join(" and ")}
            order by
              s.subject_type asc,
              s.subject_id asc,
              case when s.chat_id = $2 then 0 else 1 end asc,
              s.created_at desc
          ) latest
          order by latest.created_at desc, latest.subject_type asc, latest.subject_id asc
          limit $${params.length}
        `,
        params
      );

      if (profilesResult.rows.length === 0) {
        return toTextResult(
          `Profiles: not found for case ${case_id} / chat ${chat_id}.`
        );
      }

      const snapshotIds = asGuidArray(profilesResult.rows.map((row) => row.id));
      const traitsBySnapshot = new Map<string, TraitRow[]>();
      if (snapshotIds.length > 0) {
        const traitsResult = await pool.query<TraitRow>(
          `
            select
              profile_snapshot_id::text as profile_snapshot_id,
              trait_key,
              value_label,
              confidence,
              stability
            from domain_profile_traits
            where profile_snapshot_id = any($1::uuid[])
            order by profile_snapshot_id, confidence desc nulls last, stability desc nulls last, trait_key asc
          `,
          [snapshotIds]
        );

        for (const row of traitsResult.rows) {
          const group = traitsBySnapshot.get(row.profile_snapshot_id) ?? [];
          if (group.length < 3) {
            group.push(row);
          }
          traitsBySnapshot.set(row.profile_snapshot_id, group);
        }
      }

      const lines = [
        `Profiles | count=${profilesResult.rows.length} | case=${case_id} | chat=${chat_id}`
      ];

      for (const row of profilesResult.rows) {
        const traits = traitsBySnapshot.get(row.id) ?? [];
        const traitText =
          traits.length > 0
            ? traits
                .map(
                  (trait) =>
                    `${trait.trait_key ?? "trait"}=${trait.value_label ?? "n/a"}(${formatNumber(trait.confidence)})`
                )
                .join(", ")
            : "insufficient data";

        const periodText = row.period_id
          ? `${formatPeriodWindow(row.period_start_at, row.period_end_at)} ${formatPeriodLabel(row)}`
          : "global";

        lines.push(
          `- ${row.subject_type}/${row.subject_id} | conf=${formatNumber(row.confidence)} stab=${formatNumber(row.stability)} | ${periodText} | ${clipText(row.summary, 90)} | signals: ${traitText}`
        );
      }

      return toTextResult(lines.join("\n"));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return toTextResult(`get_profiles failed: ${message}`);
    }
  }
);

server.registerTool(
  "get_periods",
  {
    description:
      "List recent Stage 6 periods for a case/chat in a compact operator-readable format.",
    inputSchema: {
      case_id: z.number().int().positive().describe("Stage 6 case ID."),
      chat_id: z.number().int().positive().describe("Telegram chat ID."),
      limit: z
        .number()
        .int()
        .positive()
        .max(20)
        .optional()
        .describe("Maximum periods to list. Defaults to 6.")
    }
  },
  async ({
    case_id,
    chat_id,
    limit
  }: {
    case_id: number;
    chat_id: number;
    limit?: number;
  }) => {
    const scopeError = ensureOperatorVisibleScope(chat_id, case_id);
    if (scopeError) {
      return toTextResult(scopeError);
    }

    type PeriodRow = {
      id: string;
      label: string | null;
      custom_label: string | null;
      start_at: Date;
      end_at: Date | null;
      is_open: boolean;
      summary: string | null;
      open_questions_count: number | null;
      interpretation_confidence: number | null;
      review_priority: number | null;
      status_snapshot: string | null;
      dynamic_snapshot: string | null;
      key_signals_json: unknown;
      what_helped: string | null;
      what_hurt: string | null;
      created_at: Date;
    };

    try {
      const effectiveLimit = limit ?? 6;
      const result = await pool.query<PeriodRow>(
        `
          select
            id::text as id,
            label,
            custom_label,
            start_at,
            end_at,
            is_open,
            summary,
            open_questions_count,
            interpretation_confidence,
            review_priority,
            status_snapshot,
            dynamic_snapshot,
            key_signals_json,
            what_helped,
            what_hurt,
            created_at
          from domain_periods
          where case_id = $1
            and (chat_id = $2 or chat_id is null)
          order by
            case when chat_id = $2 then 0 else 1 end asc,
            start_at desc,
            created_at desc
          limit $3
        `,
        [case_id, chat_id, effectiveLimit]
      );

      if (result.rows.length === 0) {
        return toTextResult(
          `Periods: not found for case ${case_id} / chat ${chat_id}.`
        );
      }

      const lines = [`Periods | count=${result.rows.length} | case=${case_id} | chat=${chat_id}`];
      for (const row of result.rows) {
        const signals = parseStringList(row.key_signals_json, 3);
        const label = row.custom_label?.trim() || row.label?.trim() || "unnamed";
        const stateText = [row.status_snapshot, row.dynamic_snapshot]
          .map((value) => maybeGetString(value))
          .filter((value): value is string => value !== null)
          .join(" / ");

        lines.push(
          `- ${formatPeriodWindow(row.start_at, row.end_at)} ${label}${row.is_open ? " [open]" : ""} | open_q=${row.open_questions_count ?? 0} conf=${formatNumber(row.interpretation_confidence)} priority=${row.review_priority ?? 0} | ${stateText || "state=n/a"} | ${clipText(row.summary, 90)}${signals.length > 0 ? ` | key=${signals.join(", ")}` : ""}`
        );
      }

      return toTextResult(lines.join("\n"));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return toTextResult(`get_periods failed: ${message}`);
    }
  }
);

server.registerTool(
  "get_profile_signals",
  {
    description:
      "Get the latest traits/signals for one Stage 6 profile subject in a compact operator-readable format.",
    inputSchema: {
      case_id: z.number().int().positive().describe("Stage 6 case ID."),
      chat_id: z.number().int().positive().describe("Telegram chat ID."),
      subject_type: z.string().min(1).describe("Subject type such as self, other, or pair."),
      subject_id: z.string().min(1).describe("Exact subject ID."),
      limit: z
        .number()
        .int()
        .positive()
        .max(20)
        .optional()
        .describe("Maximum signals to return. Defaults to 10.")
    }
  },
  async ({
    case_id,
    chat_id,
    subject_type,
    subject_id,
    limit
  }: {
    case_id: number;
    chat_id: number;
    subject_type: string;
    subject_id: string;
    limit?: number;
  }) => {
    const scopeError = ensureOperatorVisibleScope(chat_id, case_id);
    if (scopeError) {
      return toTextResult(scopeError);
    }

    type SnapshotRow = {
      id: string;
      summary: string | null;
      confidence: number | null;
      stability: number | null;
      period_id: string | null;
      created_at: Date;
      period_label: string | null;
      period_custom_label: string | null;
      period_start_at: Date | null;
      period_end_at: Date | null;
    };

    type SignalRow = {
      trait_key: string | null;
      value_label: string | null;
      confidence: number | null;
      stability: number | null;
      is_sensitive: boolean | null;
      evidence_refs_json: unknown;
    };

    try {
      const normalizedSubjectType = subject_type.trim();
      const normalizedSubjectId = subject_id.trim();
      const snapshotResult = await pool.query<SnapshotRow>(
        `
          select
            s.id::text as id,
            s.summary,
            s.confidence,
            s.stability,
            s.period_id::text as period_id,
            s.created_at,
            p.label as period_label,
            p.custom_label as period_custom_label,
            p.start_at as period_start_at,
            p.end_at as period_end_at
          from domain_profile_snapshots s
          left join domain_periods p on p.id = s.period_id
          where s.case_id = $1
            and (s.chat_id = $2 or s.chat_id is null)
            and s.subject_type = $3
            and s.subject_id = $4
          order by
            case when s.chat_id = $2 then 0 else 1 end asc,
            s.created_at desc
          limit 1
        `,
        [case_id, chat_id, normalizedSubjectType, normalizedSubjectId]
      );

      const snapshot = snapshotResult.rows[0];
      if (!snapshot) {
        return toTextResult(
          `Profile signals: not found for ${normalizedSubjectType}/${normalizedSubjectId} in case ${case_id} / chat ${chat_id}.`
        );
      }

      const effectiveLimit = limit ?? 10;
      const signalsResult = await pool.query<SignalRow>(
        `
          select
            trait_key,
            value_label,
            confidence,
            stability,
            is_sensitive,
            evidence_refs_json
          from domain_profile_traits
          where profile_snapshot_id = $1::uuid
          order by confidence desc nulls last, stability desc nulls last, trait_key asc
          limit $2
        `,
        [snapshot.id, effectiveLimit]
      );

      if (signalsResult.rows.length === 0) {
        return toTextResult(
          `Profile signals: insufficient data for ${normalizedSubjectType}/${normalizedSubjectId}; snapshot exists but traits are missing.`
        );
      }

      const lines = [
        `Profile signals | ${normalizedSubjectType}/${normalizedSubjectId} | snapshot=${formatUtc(snapshot.created_at)} | conf=${formatNumber(snapshot.confidence)} stab=${formatNumber(snapshot.stability)}`,
        `Summary | ${clipText(snapshot.summary, 160)}`
      ];

      if (snapshot.period_id) {
        lines.push(
          `Period | ${formatPeriodWindow(snapshot.period_start_at, snapshot.period_end_at)} ${formatPeriodLabel(snapshot)}`
        );
      }

      for (const row of signalsResult.rows) {
        const evidenceCount = Array.isArray(parseJsonSafe(row.evidence_refs_json))
          ? (parseJsonSafe(row.evidence_refs_json) as unknown[]).length
          : 0;
        lines.push(
          `- ${row.trait_key ?? "trait"} = ${row.value_label ?? "n/a"} | conf=${formatNumber(row.confidence)} stab=${formatNumber(row.stability)}${row.is_sensitive ? " sensitive" : ""}${evidenceCount > 0 ? ` ev=${evidenceCount}` : ""}`
        );
      }

      return toTextResult(lines.join("\n"));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return toTextResult(`get_profile_signals failed: ${message}`);
    }
  }
);

server.registerTool(
  "get_session_summaries",
  {
    description:
      "Get recent chat session summaries in a compact operator-readable format.",
    inputSchema: {
      chat_id: z.number().int().positive().describe("Telegram chat ID."),
      limit: z
        .number()
        .int()
        .positive()
        .max(20)
        .optional()
        .describe("Maximum sessions to list. Defaults to 8.")
    }
  },
  async ({ chat_id, limit }: { chat_id: number; limit?: number }) => {
    const scopeError = ensureOperatorVisibleScope(chat_id);
    if (scopeError) {
      return toTextResult(scopeError);
    }

    type SessionRow = {
      id: string;
      session_index: number;
      start_date: Date;
      end_date: Date;
      last_message_at: Date;
      summary: string | null;
      is_finalized: boolean;
      is_analyzed: boolean;
      updated_at: Date;
    };

    try {
      const effectiveLimit = limit ?? 8;
      const result = await pool.query<SessionRow>(
        `
          select
            id::text as id,
            session_index,
            start_date,
            end_date,
            last_message_at,
            summary,
            is_finalized,
            is_analyzed,
            updated_at
          from chat_sessions
          where chat_id = $1
          order by end_date desc, session_index desc
          limit $2
        `,
        [chat_id, effectiveLimit]
      );

      if (result.rows.length === 0) {
        return toTextResult(`Session summaries: not found for chat ${chat_id}.`);
      }

      const lines = [`Session summaries | count=${result.rows.length} | chat=${chat_id}`];
      for (const row of result.rows) {
        lines.push(
          `- #${row.session_index} ${formatPeriodWindow(row.start_date, row.end_date)} | analyzed=${row.is_analyzed ? "yes" : "no"} finalized=${row.is_finalized ? "yes" : "no"} | ${clipText(row.summary, 120)}`
        );
      }

      return toTextResult(lines.join("\n"));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return toTextResult(`get_session_summaries failed: ${message}`);
    }
  }
);

let activeSseTransport: SSEServerTransport | null = null;
let streamableHttpTransport: StreamableHTTPServerTransport | null = null;
let sseHttpServer: ReturnType<typeof createServer> | null = null;

async function main(): Promise<void> {
  await pool.query("select 1");

  if (mcpTransport === "stdio") {
    const transport = new StdioServerTransport();
    await server.connect(transport);
    return;
  }

  if (mcpTransport === "sse") {
    await startSseTransportAsync();
    return;
  }

  if (mcpTransport === "streamable-http" || mcpTransport === "streamable_http") {
    await startStreamableHttpTransportAsync();
    return;
  }

  throw new Error(
    `Unsupported MCP_TRANSPORT="${mcpTransport}". Use "stdio", "sse", or "streamable-http".`
  );
}

async function startSseTransportAsync(): Promise<void> {
  ensureLocalhostBind(sseHost);
  if (!sseAuthToken) {
    throw new Error("MCP_SSE_AUTH_TOKEN is required when MCP_TRANSPORT=sse.");
  }

  sseHttpServer = createServer(async (req, res) => {
    try {
      const requestUrl = new URL(req.url ?? "/", `http://${sseHost}:${ssePort}`);
      console.log(
        "MCP streamable-http request",
        JSON.stringify({
          method: req.method ?? "",
          path: requestUrl.pathname,
          accept: req.headers.accept ?? "",
          contentType: req.headers["content-type"] ?? "",
          hasAuth: Boolean(req.headers.authorization)
        })
      );
      if (requestUrl.pathname === "/mcp" && req.method === "OPTIONS") {
        res.statusCode = 204;
        res.setHeader("access-control-allow-origin", "*");
        res.setHeader("access-control-allow-methods", "GET,POST,DELETE,OPTIONS");
        res.setHeader(
          "access-control-allow-headers",
          "authorization,content-type,accept,mcp-session-id,mcp-protocol-version"
        );
        res.setHeader("access-control-max-age", "86400");
        res.end();
        return;
      }

      if (!isAuthorized(req)) {
        res.statusCode = 401;
        res.setHeader("www-authenticate", "Bearer");
        if (requestUrl.pathname === "/mcp") {
          res.setHeader("content-type", "application/json; charset=utf-8");
          res.end(
            JSON.stringify({
              jsonrpc: "2.0",
              error: {
                code: -32001,
                message: "Unauthorized"
              },
              id: null
            })
          );
        } else {
          res.end("Unauthorized");
        }
        return;
      }

      if (req.method === "GET" && requestUrl.pathname === "/sse") {
        if (activeSseTransport !== null) {
          res.statusCode = 409;
          res.end("An active SSE MCP session already exists.");
          return;
        }

        const transport = new SSEServerTransport("/messages", res);
        activeSseTransport = transport;
        res.on("close", () => {
          if (activeSseTransport?.sessionId === transport.sessionId) {
            activeSseTransport = null;
          }
        });
        await server.connect(transport);
        return;
      }

      if (req.method === "POST" && requestUrl.pathname === "/messages") {
        const sessionId = requestUrl.searchParams.get("sessionId");
        if (!activeSseTransport || !sessionId || sessionId !== activeSseTransport.sessionId) {
          res.statusCode = 404;
          res.end("Session not found");
          return;
        }

        const body = await readJsonBodyAsync(req);
        await activeSseTransport.handlePostMessage(
          req as IncomingMessage & { auth?: undefined },
          res as ServerResponse,
          body
        );
        return;
      }

      if (req.method === "GET" && requestUrl.pathname === "/health") {
        res.statusCode = 200;
        res.end("ok");
        return;
      }

      res.statusCode = 404;
      res.end("Not found");
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      res.statusCode = 500;
      res.end(`MCP SSE server error: ${message}`);
    }
  });

  await new Promise<void>((resolve, reject) => {
    const onError = (error: Error): void => reject(error);
    sseHttpServer!.once("error", onError);
    sseHttpServer!.listen(ssePort, sseHost, () => {
      sseHttpServer!.off("error", onError);
      resolve();
    });
  });

  console.log(
    `MCP SSE server started on http://${sseHost}:${ssePort} (endpoints: GET /sse, POST /messages?sessionId=..., GET /health)`
  );
}

async function startStreamableHttpTransportAsync(): Promise<void> {
  ensureLocalhostBind(sseHost);
  if (!sseAuthToken) {
    throw new Error(
      "MCP_SSE_AUTH_TOKEN is required when MCP_TRANSPORT=streamable-http."
    );
  }

  streamableHttpTransport = new StreamableHTTPServerTransport({
    // Stateless mode allows many independent client sessions without server-side session map management.
    sessionIdGenerator: undefined,
    enableJsonResponse: true
  });
  streamableHttpTransport.onerror = (error: Error) => {
    console.error("StreamableHTTP transport error", error);
  };
  streamableHttpTransport.onclose = () => {
    console.log("StreamableHTTP transport closed");
  };
  await server.connect(streamableHttpTransport);

  sseHttpServer = createServer(async (req, res) => {
    try {
      const requestUrl = new URL(req.url ?? "/", `http://${sseHost}:${ssePort}`);
      if (requestUrl.pathname === "/mcp" && req.method === "OPTIONS") {
        res.statusCode = 204;
        res.setHeader("access-control-allow-origin", "*");
        res.setHeader("access-control-allow-methods", "GET,POST,DELETE,OPTIONS");
        res.setHeader(
          "access-control-allow-headers",
          "authorization,content-type,accept,mcp-session-id,mcp-protocol-version"
        );
        res.setHeader("access-control-max-age", "86400");
        res.end();
        return;
      }

      if (!isAuthorized(req)) {
        res.statusCode = 401;
        res.setHeader("www-authenticate", "Bearer");
        res.end("Unauthorized");
        return;
      }

      if (requestUrl.pathname === "/health" && req.method === "GET") {
        res.statusCode = 200;
        res.end("ok");
        return;
      }

      if (requestUrl.pathname === "/mcp" && streamableHttpTransport !== null) {
        await streamableHttpTransport.handleRequest(
          req as IncomingMessage & { auth?: undefined },
          res as ServerResponse
        );
        return;
      }

      res.statusCode = 404;
      res.end("Not found");
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.error("Streamable HTTP request failed", error);
      res.statusCode = 500;
      if ((req.url ?? "").includes("/mcp")) {
        res.setHeader("content-type", "application/json; charset=utf-8");
        res.end(
          JSON.stringify({
            jsonrpc: "2.0",
            error: {
              code: -32603,
              message: `Internal error: ${message}`
            },
            id: null
          })
        );
      } else {
        res.end(`MCP streamable-http server error: ${message}`);
      }
    }
  });

  await new Promise<void>((resolve, reject) => {
    const onError = (error: Error): void => reject(error);
    sseHttpServer!.once("error", onError);
    sseHttpServer!.listen(ssePort, sseHost, () => {
      sseHttpServer!.off("error", onError);
      resolve();
    });
  });

  console.log(
    `MCP streamable-http server started on http://${sseHost}:${ssePort} (endpoints: POST/GET/DELETE /mcp, GET /health)`
  );
}

function ensureLocalhostBind(host: string): void {
  const normalized = host.toLowerCase();
  const allowed = new Set(["127.0.0.1", "::1", "localhost"]);
  if (allowContainerBind) {
    allowed.add("0.0.0.0");
  }

  if (!allowed.has(normalized)) {
    const allowedHosts = Array.from(allowed.values()).join(", ");
    throw new Error(
      `MCP_SSE_HOST must bind to localhost for safety. Allowed: ${allowedHosts}. Got: ${host}`
    );
  }
}

function isAuthorized(req: IncomingMessage): boolean {
  const header = req.headers.authorization;
  if (!header) {
    return false;
  }

  const match = /^Bearer\s+(.+)$/i.exec(header.trim());
  if (!match) {
    return false;
  }

  return match[1] === sseAuthToken;
}

async function readJsonBodyAsync(req: IncomingMessage): Promise<unknown> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) {
    chunks.push(Buffer.isBuffer(chunk) ? chunk : Buffer.from(chunk));
  }

  const rawBody = Buffer.concat(chunks).toString("utf8").trim();
  if (!rawBody) {
    return undefined;
  }

  return JSON.parse(rawBody);
}

async function shutdownAsync(): Promise<void> {
  if (sseHttpServer) {
    await new Promise<void>((resolve) => {
      sseHttpServer?.close(() => resolve());
    });
    sseHttpServer = null;
  }

  activeSseTransport = null;
  if (streamableHttpTransport) {
    await streamableHttpTransport.close().catch(() => undefined);
    streamableHttpTransport = null;
  }
  await pool.end().catch(() => undefined);
}

main().catch(async (error) => {
  console.error("Failed to start MCP server", error);
  await shutdownAsync();
  process.exitCode = 1;
});

process.on("SIGINT", async () => {
  await shutdownAsync();
  process.exit(0);
});

process.on("SIGTERM", async () => {
  await shutdownAsync();
  process.exit(0);
});
