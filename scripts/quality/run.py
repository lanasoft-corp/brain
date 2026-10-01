"""Test de qualité de la recherche : 20 faits, 10 questions reformulées, le bon fait doit sortir
dans le top 3.

Les faits sont écrits tels quels (infer=False, pas d'appel au LLM) sous un user_id dédié, puis
supprimés à la fin, même en cas d'échec. Seuls les embeddings et la recherche sont testés.

    kubectl -n brain port-forward svc/mem0-api 18000:8000
    MEM0_URL=http://localhost:18000 MEM0_API_KEY=m0sk_... python scripts/quality/run.py
"""

import json
import os
import statistics
import sys
import time
import urllib.request
from pathlib import Path

URL = os.environ.get("MEM0_URL", "http://localhost:18000").rstrip("/")
KEY = os.environ["MEM0_API_KEY"]
USER = "quality-test"
TOP = 3


def call(method: str, path: str, body=None):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(
        URL + path, data=data, method=method,
        headers={"X-API-Key": KEY, "Content-Type": "application/json"},
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        return json.loads(resp.read() or "null")


def main() -> int:
    spec = json.loads((Path(__file__).parent / "facts.json").read_text(encoding="utf-8"))
    facts, questions = spec["facts"], spec["questions"]
    call("DELETE", f"/memories?user_id={USER}")
    try:
        add_ms, search_ms = [], []
        for fact in facts:
            start = time.perf_counter()
            call("POST", "/memories", {
                "messages": [{"role": "user", "content": fact}],
                "user_id": USER, "infer": False,
            })
            add_ms.append((time.perf_counter() - start) * 1000)
        passed = first = 0
        for item in questions:
            start = time.perf_counter()
            res = call("POST", "/search", {"query": item["q"], "filters": {"user_id": USER}, "top_k": TOP})
            search_ms.append((time.perf_counter() - start) * 1000)
            hits = [r["memory"] for r in (res.get("results") or res)][:TOP]
            expected = facts[item["expect"]]
            rank = hits.index(expected) + 1 if expected in hits else None
            passed += rank is not None
            first += rank == 1
            status = f"rang {rank}" if rank else "ABSENT"
            print(f"[{status:>7}] {item['q']}")
            if not rank:
                for h in hits:
                    print(f"            - {h[:100]}")
        print(f"\n{passed}/{len(questions)} questions avec le bon fait dans le top {TOP}, {first} au rang 1.")
        print(f"Temps médian : écriture {statistics.median(add_ms):.0f} ms, recherche {statistics.median(search_ms):.0f} ms.")
        return 0 if passed == len(questions) else 1
    finally:
        call("DELETE", f"/memories?user_id={USER}")


if __name__ == "__main__":
    sys.exit(main())
