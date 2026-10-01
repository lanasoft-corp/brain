"""Point d'entrée LanaSoft du serveur Mem0 (remplace ``main:app`` du serveur officiel).

Le serveur officiel construit sa configuration à partir de variables d'environnement, avec
OpenAI imposé comme fournisseur, et la modifie ensuite par ``POST /configure`` en la stockant
dans sa base. Ici, la configuration vient uniquement d'un fichier monté depuis le ConfigMap :
elle suit git, pas l'historique des appels à l'API.

Deux fonctions de ``server_state`` sont remplacées avant l'import de ``main`` (qui les importe
par nom) :
- ``initialize_state`` charge le fichier et ignore les surcharges stockées en base ;
- ``update_config`` refuse toute modification, donc ``POST /configure`` répond 403.

Le reste du serveur (routes, authentification, dashboard) est celui de la version officielle.
"""

import json
import os
import re
from copy import deepcopy
from typing import Any, Dict

import server_state
from fastapi import HTTPException
from mem0 import Memory

CONFIG_FILE = os.environ.get("MEM0_CONFIG_FILE", "/etc/mem0/config.json")

_UNRESOLVED = re.compile(r"\$\{?[A-Za-z_][A-Za-z0-9_]*\}?")


def load_config() -> Dict[str, Any]:
    """Lit le fichier de configuration et remplace les ${VARIABLE} par l'environnement.

    Les secrets (clés d'API, mot de passe Postgres) restent ainsi dans des Secrets Kubernetes :
    le fichier n'en contient que les noms. Une variable absente arrête le démarrage plutôt que
    de laisser passer une clé vide.
    """
    with open(CONFIG_FILE, encoding="utf-8") as f:
        text = os.path.expandvars(f.read())
    missing = sorted(set(_UNRESOLVED.findall(text)))
    if missing:
        raise RuntimeError(f"{CONFIG_FILE}: variables d'environnement absentes : {', '.join(missing)}")
    return json.loads(text)


def _initialize_state(_default_config: Dict[str, Any]) -> None:
    config = load_config()
    with server_state._state_lock:
        server_state._current_config = deepcopy(config)
        server_state._memory_instance = Memory.from_config(config)


def _update_config(_updates: Dict[str, Any]) -> Dict[str, Any]:
    raise HTTPException(
        status_code=403,
        detail="La configuration est gérée par le déploiement (ConfigMap) : POST /configure est désactivé.",
    )


server_state.initialize_state = _initialize_state
server_state.update_config = _update_config

from main import app  # noqa: E402  (doit suivre le remplacement ci-dessus)

__all__ = ["app", "load_config"]
