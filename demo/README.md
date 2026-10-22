# Local demo

Runs brain on your machine with Docker: the MCP gateway, Mem0 (API and dashboard), PostgreSQL with
pgvector, and a Keycloak with a demo realm. Authentication is the same as in production: OAuth,
scopes `brain` and `brain:write`, audience checked by the gateway.

[Version française](README.fr.md)

| Service | Address |
|---|---|
| MCP gateway | http://localhost:8080/mcp |
| Keycloak (realm `demo`) | http://localhost:8081 |
| Mem0 dashboard | http://localhost:3000 |
| Mem0 API (called by the dashboard) | http://localhost:8888 |

You need an Anthropic API key (Claude Haiku, fact extraction) and a Google AI key (Gemini,
embeddings). Everything else runs locally, and the ports only listen on 127.0.0.1. If 8080 or
8081 are already taken, `MCP_PORT` and `KEYCLOAK_PORT` in `demo/.env` change them; adjust the
addresses below accordingly.

## Start

From the repository root:

```bash
# Official Mem0 server, at the version the image is built from. Without line-ending conversion:
# on Windows, an entrypoint.sh in CRLF keeps the dashboard from starting.
git clone -c core.autocrlf=false --depth 1 --branch v2.2.1 https://github.com/mem0ai/mem0.git upstream

cp demo/.env.example demo/.env
# fill in demo/.env: the two API keys, then random values (openssl rand -hex 24)

docker compose -f demo/compose.yaml up -d --build
```

The `keycloak-config` service creates the realm and then stops: `docker compose -f
demo/compose.yaml logs keycloak-config` must end without an error.

## Connect Claude Code

```bash
claude mcp add --transport http brain http://localhost:8080/mcp
```

In Claude Code, `/mcp` opens the connection: sign in with the user `demo` and the password
`DEMO_USER_PASSWORD` from `demo/.env`. Claude Code identifies itself with its Client ID Metadata
Document, with no prior registration in Keycloak. Then ask it to remember a fact, and to find it
again in a new session.

## Try it without an MCP client

The `demo-agent` client gets a token with client credentials, like an agent of your own:

```bash
source demo/.env
TOKEN=$(curl -s http://localhost:8081/realms/demo/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=demo-agent -d client_secret="$DEMO_AGENT_SECRET" \
  -d scope="brain brain:write" | jq -r .access_token)

curl -s http://localhost:8080/mcp -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"remember","arguments":{"fact":"The brain demo has been running locally since today.","project":"demo"}}}'
```

Without a token, the gateway answers 401 and says where to find the authorization server
(`/.well-known/oauth-protected-resource`).

## Fully local, with no API key

The `local` profile replaces Claude Haiku and Gemini with Ollama: a local LLM for fact extraction
(`session_summary`) and `bge-m3` for embeddings. No data leaves the machine. In `demo/.env`:

```bash
COMPOSE_PROFILES=local
MEM0_CONFIG=config.local.json
OLLAMA_LLM_MODEL=qwen2.5:7b   # pick one that fits the available memory
```

The `ollama-pull` service downloads both models on first start, which takes a few minutes. Ollama
runs on the CPU in this demo: extracting a session summary is much slower there than with the
API. `remember` does not use the LLM (the fact is stored as written), only the embedding.

The two modes do not have the same vector dimension (768 for Gemini, 1024 for `bge-m3`): each has
its own collection, and switching modes starts from an empty memory.

## Read the memory

The dashboard (http://localhost:3000) asks you to create an administrator account on first
visit. It then lists every memory, with its project, its kind and the agent that wrote it.

## Stop

```bash
docker compose -f demo/compose.yaml down      # keeps the data
docker compose -f demo/compose.yaml down -v   # also wipes the memory
```
