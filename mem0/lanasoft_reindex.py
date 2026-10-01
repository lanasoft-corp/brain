"""Réindexation : recalcule le vecteur de chaque mémoire avec l'embedder configuré.

À lancer après un changement de modèle d'embedding (même dimension) :
    kubectl -n brain exec deploy/mem0-api -- python lanasoft_reindex.py [--dry-run]

Le texte, les identifiants et les métadonnées de chaque mémoire sont conservés ; seul le vecteur
change. Aucun appel au LLM. Pour changer de dimension, il faut une nouvelle collection : ce script
refuse de continuer si la dimension produite ne correspond pas à celle de la configuration.
"""

import argparse
import sys

from lanasoft_main import load_config
from mem0 import Memory

BATCH = 10_000


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--dry-run", action="store_true", help="compte les mémoires sans rien modifier")
    args = parser.parse_args()

    config = load_config()
    expected_dims = config["vector_store"]["config"]["embedding_model_dims"]
    memory = Memory.from_config(config)
    store, embedder = memory.vector_store, memory.embedding_model

    results = store.list(top_k=BATCH)
    rows = results[0] if results and isinstance(results[0], list) else results or []
    print(f"{len(rows)} mémoires, modèle {config['embedder']['config'].get('model')}", flush=True)
    if len(rows) >= BATCH:
        print(f"Plus de {BATCH} mémoires : augmenter BATCH avant de relancer.", file=sys.stderr)
        return 1
    if args.dry_run:
        return 0

    for i, row in enumerate(rows, 1):
        text = (row.payload or {}).get("data")
        if not text:
            print(f"  {row.id} : pas de texte, ignorée", flush=True)
            continue
        vector = embedder.embed(text, "update")
        if len(vector) != expected_dims:
            print(f"Dimension {len(vector)} au lieu de {expected_dims} : arrêt.", file=sys.stderr)
            return 1
        store.update(vector_id=row.id, vector=vector, payload=row.payload)
        if i % 50 == 0 or i == len(rows):
            print(f"  {i}/{len(rows)}", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
