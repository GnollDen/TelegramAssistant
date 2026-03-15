# mcp/AGENTS.md — MCP Server specific instructions

## This directory

TypeScript MCP server that provides LLM access to TelegramAssistant data.
Connects directly to PostgreSQL (same database as the C# app).

## Build & verify

```bash
npm install
npm run build    # must pass with zero errors
npm run dev      # starts with tsx for local testing
```

## Conventions

- All source code in `src/`.
- One file per tool group: `read-dossier.ts`, `read-messages.ts`, `read-ops.ts`, `write-mgmt.ts`, `write-ops.ts`.
- Entry point: `src/index.ts` — creates McpServer, registers tools, starts SSE transport.
- Database: `src/db.ts` — thin wrapper around `pg.Pool`.
- All SQL queries use parameterized placeholders ($1, $2...), never template literals for values.
- Tool results are always `{ content: [{ type: "text", text: JSON.stringify(data, null, 2) }] }`.
- Entity lookups are case-insensitive, searching `entities.name`, `entities.aliases`, and `entity_aliases.alias_norm`.
- Write operations enqueue commands into existing tables — they do NOT execute actions directly.

## Dependencies

Only these packages are allowed:
- `@modelcontextprotocol/sdk` — MCP protocol
- `pg` — PostgreSQL driver
- `zod` — input schema validation

No ORMs, no Express, no other frameworks.
