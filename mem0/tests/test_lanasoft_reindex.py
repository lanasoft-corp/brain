from types import SimpleNamespace

import pytest

import lanasoft_reindex

DIMS = 3


class FakeStore:
    def __init__(self, rows, nested=True):
        self.rows = rows
        self.nested = nested
        self.updates = []

    def list(self, top_k):
        return [self.rows] if self.nested else self.rows

    def update(self, vector_id, vector, payload):
        self.updates.append((vector_id, vector, payload))


class FakeEmbedder:
    def __init__(self, dims=DIMS):
        self.dims = dims
        self.texts = []

    def embed(self, text, action):
        assert action == "update"
        self.texts.append(text)
        return [0.5] * self.dims


def row(id, text):
    return SimpleNamespace(id=id, payload={"data": text, "user_id": "cyril"} if text is not None else None)


@pytest.fixture
def run(monkeypatch):
    """Lance main() avec un magasin et un embedder factices ; renvoie (code, magasin, embedder)."""

    def go(rows, *argv, dims=DIMS, nested=True):
        store, embedder = FakeStore(rows, nested), FakeEmbedder(dims)
        config = {
            "vector_store": {"config": {"embedding_model_dims": DIMS}},
            "embedder": {"config": {"model": "test-embedding"}},
        }
        monkeypatch.setattr(lanasoft_reindex, "load_config", lambda: config)
        monkeypatch.setattr(
            lanasoft_reindex.Memory, "from_config",
            classmethod(lambda cls, c: SimpleNamespace(vector_store=store, embedding_model=embedder)),
        )
        monkeypatch.setattr("sys.argv", ["lanasoft_reindex.py", *argv])
        return lanasoft_reindex.main(), store, embedder

    return go


def test_reindexes_every_memory_and_keeps_its_payload(run, capsys):
    rows = [row("m1", "Cyril aime le café"), row("m2", "Le NAS est un Minisforum N5")]

    code, store, embedder = run(rows)

    assert code == 0
    assert embedder.texts == ["Cyril aime le café", "Le NAS est un Minisforum N5"]
    assert store.updates == [(r.id, [0.5] * DIMS, r.payload) for r in rows]
    out = capsys.readouterr().out
    assert "2 mémoires, modèle test-embedding" in out
    assert "2/2" in out


def test_accepts_a_flat_list_from_the_store(run):
    code, store, _ = run([row("m1", "fait")], nested=False)

    assert code == 0
    assert [u[0] for u in store.updates] == ["m1"]


def test_skips_memories_without_text(run, capsys):
    code, store, embedder = run([row("m1", None), row("m2", ""), row("m3", "fait")])

    assert code == 0
    assert embedder.texts == ["fait"]
    assert [u[0] for u in store.updates] == ["m3"]
    assert "m1 : pas de texte, ignorée" in capsys.readouterr().out


def test_dry_run_changes_nothing(run):
    code, store, embedder = run([row("m1", "fait")], "--dry-run")

    assert code == 0
    assert store.updates == []
    assert embedder.texts == []


def test_stops_when_the_dimension_does_not_match(run, capsys):
    code, store, _ = run([row("m1", "fait"), row("m2", "autre")], dims=DIMS + 1)

    assert code == 1
    assert store.updates == []
    assert f"Dimension {DIMS + 1} au lieu de {DIMS}" in capsys.readouterr().err


def test_refuses_to_run_when_the_batch_may_be_truncated(run, monkeypatch, capsys):
    monkeypatch.setattr(lanasoft_reindex, "BATCH", 2)

    code, store, _ = run([row("m1", "a"), row("m2", "b")])

    assert code == 1
    assert store.updates == []
    assert "augmenter BATCH" in capsys.readouterr().err
