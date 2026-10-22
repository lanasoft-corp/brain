# Démo locale

Lance brain sur ton poste avec Docker : la passerelle MCP, Mem0 (API et dashboard), PostgreSQL
avec pgvector, et un Keycloak avec un realm de démo. L'authentification est la même qu'en
production : OAuth, scopes `brain` et `brain:write`, audience vérifiée par la passerelle.

[English version](README.md)

| Service | Adresse |
|---|---|
| Passerelle MCP | http://localhost:8080/mcp |
| Keycloak (realm `demo`) | http://localhost:8081 |
| Dashboard Mem0 | http://localhost:3000 |
| API Mem0 (appelée par le dashboard) | http://localhost:8888 |

Il faut une clé d'API Anthropic (Claude Haiku, extraction des faits) et une clé Google AI
(Gemini, embeddings). Tout le reste tourne en local, et les ports n'écoutent que sur 127.0.0.1.
Si 8080 ou 8081 sont déjà pris, `MCP_PORT` et `KEYCLOAK_PORT` dans `demo/.env` les changent ;
adapte alors les adresses ci-dessous.

## Démarrer

Depuis la racine du dépôt :

```bash
# Serveur Mem0 officiel, à la version dont l'image est construite. Sans conversion des fins de
# ligne : sous Windows, un entrypoint.sh en CRLF empêche le dashboard de démarrer.
git clone -c core.autocrlf=false --depth 1 --branch v2.2.1 https://github.com/mem0ai/mem0.git upstream

cp demo/.env.example demo/.env
# remplir demo/.env : les deux clés d'API, puis des valeurs aléatoires (openssl rand -hex 24)

docker compose -f demo/compose.yaml up -d --build
```

Le service `keycloak-config` crée le realm puis s'arrête : `docker compose -f demo/compose.yaml
logs keycloak-config` doit finir sans erreur.

## Brancher Claude Code

```bash
claude mcp add --transport http brain http://localhost:8080/mcp
```

Dans Claude Code, `/mcp` ouvre la connexion : identifie-toi avec l'utilisateur `demo` et le mot de
passe `DEMO_USER_PASSWORD` de `demo/.env`. Claude Code s'identifie par son Client ID Metadata
Document, sans enregistrement préalable dans Keycloak. Demande-lui ensuite de retenir un fait,
puis de le retrouver dans une nouvelle session.

## Essayer sans client MCP

Le client `demo-agent` obtient un jeton par client credentials, comme un agent maison :

```bash
source demo/.env
TOKEN=$(curl -s http://localhost:8081/realms/demo/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=demo-agent -d client_secret="$DEMO_AGENT_SECRET" \
  -d scope="brain brain:write" | jq -r .access_token)

curl -s http://localhost:8080/mcp -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"remember","arguments":{"fact":"La démo de brain tourne en local depuis aujourd hui.","project":"demo"}}}'
```

Sans jeton, la passerelle répond 401 et indique où trouver le serveur d'autorisation
(`/.well-known/oauth-protected-resource`).

## Tout en local, sans clé d'API

Le profil `local` remplace Claude Haiku et Gemini par Ollama : un LLM local pour l'extraction des
faits (`session_summary`) et `bge-m3` pour les embeddings. Aucune donnée ne quitte le poste. Dans
`demo/.env` :

```bash
COMPOSE_PROFILES=local
MEM0_CONFIG=config.local.json
OLLAMA_LLM_MODEL=qwen2.5:7b   # à choisir selon la mémoire disponible
```

Le service `ollama-pull` télécharge les deux modèles au premier démarrage, ce qui prend quelques
minutes. Ollama tourne sur le CPU dans cette démo : l'extraction d'un résumé de session y est
nettement plus lente qu'avec l'API. `remember` n'utilise pas le LLM (le fait est stocké tel quel),
seulement l'embedding.

Les deux modes n'ont pas la même dimension de vecteurs (768 pour Gemini, 1024 pour `bge-m3`) :
chacun a sa collection, et changer de mode repart d'une mémoire vide.

## Relire la mémoire

Le dashboard (http://localhost:3000) demande de créer un compte administrateur au premier
passage. Il liste ensuite toutes les mémoires, avec leur projet, leur nature et l'agent qui les a
écrites.

## Arrêter

```bash
docker compose -f demo/compose.yaml down      # garde les données
docker compose -f demo/compose.yaml down -v   # efface aussi la mémoire
```
