# brain

Une mémoire partagée par tous tes assistants IA : Claude.ai, Claude Code, Cowork, VS Code et tes
propres agents lisent et écrivent les mêmes faits, exposés par un serveur MCP authentifié par
OAuth. Ce que l'un apprend, les autres le savent, sur n'importe quel poste.

| Dossier | Contenu |
|---|---|
| [`src/LanaSoft.Brain.Mcp/`](src/LanaSoft.Brain.Mcp/) | passerelle MCP (.NET 10, SDK MCP officiel) : jetons OAuth vérifiés, identité tirée du jeton, filtre anti-secret, outils `recall`, `list_recent`, `remember`, `forget`, `rename_project`, `session_summary` |
| [`tests/LanaSoft.Brain.Mcp.Tests/`](tests/LanaSoft.Brain.Mcp.Tests/) | tests de la passerelle : authentification, scopes, identité, filtre anti-secret, aller-retour |
| [`mem0/`](mem0/) | image `mem0-api` : serveur Mem0 officiel + point d'entrée LanaSoft (configuration par fichier, `POST /configure` désactivé), script de réindexation |
| [`charts/mem0/`](charts/mem0/) | chart Helm de l'API et du dashboard Mem0 ; la configuration Mem0 est dans [`files/config.json`](charts/mem0/files/config.json) |
| [`charts/mcp/`](charts/mcp/) | chart Helm de la passerelle (2 réplicas, Ingress public) |
| [`demo/`](demo/) | démo locale avec Docker Compose : passerelle, Mem0, pgvector et Keycloak |
| [`scripts/quality/`](scripts/quality/) | test de qualité de la recherche (20 faits fictifs, 10 questions) |

## Essayer

La [démo locale](demo/README.md) lance tout sur ton poste avec `docker compose`, avec une clé
d'API Anthropic et une clé Google AI, ou sans aucune clé avec le profil `local` (Ollama).

## Modèles : API ou local

Mem0 a besoin d'un modèle d'embeddings pour chaque fait, et d'un LLM pour extraire les faits des
résumés de session. Deux configurations sont fournies dans [`charts/mem0/files/`](charts/mem0/files/) :

| Fichier | LLM | Embeddings | Quand |
|---|---|---|---|
| `config.json` | Claude Haiku (API Anthropic) | Gemini, 768 dimensions (API Google) | peu de volume, pas de GPU : quelques appels par jour coûtent moins que du matériel |
| `config.local.json` | modèle Ollama au choix (`OLLAMA_LLM_MODEL`) | `bge-m3` via Ollama, 1024 dimensions | aucune donnée ne doit sortir, ou un GPU est déjà disponible |

Le chart choisit le fichier par `configFile` et reçoit `OLLAMA_BASE_URL` et `OLLAMA_LLM_MODEL` par
`api.extraEnv`. Les dimensions diffèrent : chaque configuration a sa collection de vecteurs, et
passer de l'une à l'autre repart d'une collection vide (`lanasoft_reindex.py` ne gère qu'un
changement de modèle à dimension égale).

## Configurer la passerelle

Réglages de la section `Brain` (variables `Brain__*`). Aucun n'a de valeur par défaut qui pointe
vers un vrai déploiement : sans les trois premiers, la passerelle refuse de démarrer.

| Réglage | Rôle |
|---|---|
| `Resource` | URL publique de la passerelle, ex. `https://brain.example.com/mcp` ; c'est aussi l'audience exigée dans les jetons |
| `Authority` | émetteur des jetons, ex. `https://auth.example.com/realms/example` |
| `AgentOwner` | utilisateur propriétaire des mémoires écrites par les agents (client credentials) |
| `MetadataAddress` | document de découverte OpenID, si la passerelle joint le fournisseur d'identité par une autre adresse que l'émetteur |
| `RequireHttpsMetadata` | `true` par défaut ; `false` seulement en local |
| `AgentAliases` | noms courts des clients identifiés par une URL (Claude Code : `claude-code`) |
| `Mem0Url`, `Mem0ApiKey` | API Mem0 et sa clé |

Le fournisseur d'identité doit proposer les scopes `brain` (lecture) et `brain:write` (écriture),
chacun avec un mapper d'audience vers `Resource`. Le realm de la démo,
[`demo/keycloak/demo.yaml`](demo/keycloak/demo.yaml), sert de modèle, y compris pour les clients
identifiés par un Client ID Metadata Document.

## Déployer sur Kubernetes

Les charts n'ont que des valeurs d'exemple : le déploiement fournit ses propres values (hôtes,
émetteur, propriétaire, dépôt d'images, annotations d'Ingress, StorageClass). Les secrets sont
créés hors chart, ou par External Secrets Operator si `secrets.store` est renseigné. Les charts
supposent Traefik comme Ingress controller.

## Construire en local

```bash
dotnet test LanaSoft.Brain.slnx

git clone --depth 1 --branch v2.2.1 https://github.com/mem0ai/mem0.git upstream
docker build -f mem0/Dockerfile -t brain-mem0-api:local .
docker build -t brain-mem0-dashboard:local upstream/server/dashboard
```

## Branches et versions

GitFlow, versions calculées par GitVersion :

| Branche | Version publiée |
|---|---|
| `feature/*` | rien (PR vers `develop`) |
| `develop` | `0.x.y-alpha.N` |
| `release/x.y.z` | `x.y.z-beta.N` |
| `main` + tag `vx.y.z` posé à la main | `x.y.z` |

Chaque version publiée est taguée dans git (`v0.1.0-alpha.3`, …). Images et charts portent la
même version.

## Monter de version Mem0

1. `mem0Ref` dans la CI et `mem0ai==` dans `mem0/requirements.lanasoft.txt`.
2. Vérifier que `server_state.initialize_state` et `update_config` existent toujours et sont
   importés par nom dans `main.py` (le point d'entrée les remplace) : la CI le contrôle.
3. Relancer le test de qualité après déploiement.

## Réindexer

Après un changement de modèle d'embedding (même dimension) :

```bash
kubectl exec deploy/mem0-api -- python lanasoft_reindex.py --dry-run
kubectl exec deploy/mem0-api -- python lanasoft_reindex.py
```

## Licence

[Apache-2.0](LICENSE).
