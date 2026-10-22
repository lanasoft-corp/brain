# brain

A memory shared by all your AI assistants: Claude.ai, Claude Code, Cowork, VS Code and your own
agents read and write the same facts, served by an MCP server authenticated with OAuth. What one
learns, the others know, on any computer.

[Version française](README.fr.md)

| Folder | Contents |
|---|---|
| [`src/LanaSoft.Brain.Mcp/`](src/LanaSoft.Brain.Mcp/) | MCP gateway (.NET 10, official MCP SDK): OAuth tokens checked, identity taken from the token, secret filter, tools `recall`, `list_recent`, `remember`, `forget`, `rename_project`, `session_summary` |
| [`tests/LanaSoft.Brain.Mcp.Tests/`](tests/LanaSoft.Brain.Mcp.Tests/) | gateway tests: authentication, scopes, identity, secret filter, round trip |
| [`mem0/`](mem0/) | `mem0-api` image: official Mem0 server + LanaSoft entry point (configuration from a file, `POST /configure` disabled), reindex script, and their tests in [`mem0/tests/`](mem0/tests/) (official server modules replaced by fakes) |
| [`charts/mem0/`](charts/mem0/) | Helm chart for the Mem0 API and dashboard; the Mem0 configuration is in [`files/config.json`](charts/mem0/files/config.json) |
| [`charts/mcp/`](charts/mcp/) | Helm chart for the gateway (2 replicas, optional Ingress) |
| [`demo/`](demo/) | local demo with Docker Compose: gateway, Mem0, pgvector and Keycloak |
| [`scripts/quality/`](scripts/quality/) | search quality test (20 made-up facts, 10 questions) |

The tool descriptions, log messages and code comments are in French.

## Try it

The [local demo](demo/README.md) runs everything on your machine with `docker compose`, with an
Anthropic API key and a Google AI key, or with no key at all using the `local` profile (Ollama).

## Models: API or local

Mem0 needs an embedding model for every fact, and an LLM to extract facts from session summaries.
Two configurations ship in [`charts/mem0/files/`](charts/mem0/files/):

| File | LLM | Embeddings | When |
|---|---|---|---|
| `config.json` | Claude Haiku (Anthropic API) | Gemini, 768 dimensions (Google API) | low volume, no GPU: a few calls a day cost less than hardware |
| `config.local.json` | any Ollama model (`OLLAMA_LLM_MODEL`) | `bge-m3` through Ollama, 1024 dimensions | no data may leave, or a GPU is already available |

The chart picks the file with `configFile` and receives `OLLAMA_BASE_URL` and `OLLAMA_LLM_MODEL`
through `api.extraEnv`. The dimensions differ: each configuration has its own vector collection,
and switching from one to the other starts from an empty collection (`lanasoft_reindex.py` only
handles a model change at the same dimension).

## Configure the gateway

Settings of the `Brain` section (`Brain__*` variables). None has a default value that points to a
real deployment: without the first three, the gateway refuses to start.

| Setting | Role |
|---|---|
| `Resource` | public URL of the gateway, e.g. `https://brain.example.com/mcp`; it is also the audience required in tokens |
| `Authority` | token issuer, e.g. `https://auth.example.com/realms/example` |
| `AgentOwner` | user who owns the memories written by agents (client credentials) |
| `MetadataAddress` | OpenID discovery document, if the gateway reaches the identity provider at a different address than the issuer |
| `RequireHttpsMetadata` | `true` by default; `false` only locally |
| `AgentAliases` | short names for clients identified by a URL (Claude Code: `claude-code`) |
| `Mem0Url`, `Mem0ApiKey` | Mem0 API and its key |

The identity provider must offer the scopes `brain` (read) and `brain:write` (write), each with an
audience mapper to `Resource`. The demo realm, [`demo/keycloak/demo.yaml`](demo/keycloak/demo.yaml),
is a template, including for clients identified by a Client ID Metadata Document.

## Deploy on Kubernetes

The charts only hold example values: the deployment supplies its own values (hosts, issuer,
owner, image repository, Ingress annotations, StorageClass). Secrets are created outside the
chart, or by External Secrets Operator if `secrets.store` is set.

The charts do not depend on any Ingress controller. Ingresses are disabled by default
(`ingress.enabled`); once enabled, their class and annotations are up to you, and `extraObjects`
renders the cluster-specific objects, such as the controller's middlewares: private network only
for the dashboard, `/mem0` prefix stripped in front of the API, rate limit in front of the gateway.
The NetworkPolicies of the mem0 chart only let in the controller selected by
`networkPolicy.ingressController`.

## Build locally

```bash
dotnet test LanaSoft.Brain.slnx
python -m pip install pytest && python -m pytest mem0/tests

git clone --depth 1 --branch v2.2.1 https://github.com/mem0ai/mem0.git upstream
docker build -f mem0/Dockerfile -t brain-mem0-api:local .
docker build -t brain-mem0-dashboard:local upstream/server/dashboard
```

## Branches and versions

GitFlow, versions computed by GitVersion:

| Branch | Published version |
|---|---|
| `feature/*` | nothing (PR to `develop`) |
| `develop` | `0.x.y-alpha.N` |
| `release/x.y.z` | `x.y.z-beta.N` |
| `main` + tag `vx.y.z` set by hand | `x.y.z` |

Every published version is tagged in git (`v0.1.0-alpha.3`, …). Images and charts carry the same
version.

## Upgrade Mem0

1. `mem0Ref` in the CI and `mem0ai==` in `mem0/requirements.lanasoft.txt`.
2. Check that `server_state.initialize_state` and `update_config` still exist and are imported by
   name in `main.py` (the entry point replaces them): the CI checks it.
3. Run the quality test again after deployment.

## Reindex

After a change of embedding model (same dimension):

```bash
kubectl exec deploy/mem0-api -- python lanasoft_reindex.py --dry-run
kubectl exec deploy/mem0-api -- python lanasoft_reindex.py
```

## License

[Apache-2.0](LICENSE).
