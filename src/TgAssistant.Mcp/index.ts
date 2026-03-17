import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
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

let activeSseTransport: SSEServerTransport | null = null;
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

  throw new Error(`Unsupported MCP_TRANSPORT="${mcpTransport}". Use "stdio" or "sse".`);
}

async function startSseTransportAsync(): Promise<void> {
  ensureLocalhostBind(sseHost);
  if (!sseAuthToken) {
    throw new Error("MCP_SSE_AUTH_TOKEN is required when MCP_TRANSPORT=sse.");
  }

  sseHttpServer = createServer(async (req, res) => {
    try {
      const requestUrl = new URL(req.url ?? "/", `http://${sseHost}:${ssePort}`);
      if (!isAuthorized(req)) {
        res.statusCode = 401;
        res.setHeader("www-authenticate", "Bearer");
        res.end("Unauthorized");
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
