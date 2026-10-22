"""Modules factices pour importer lanasoft_main et lanasoft_reindex hors de l'image.

Dans l'image, server_state et main viennent du serveur Mem0 officiel (upstream/server/), mem0 et
fastapi de ses dépendances. Les importer pour de vrai démarrerait le serveur, qui exige sa
configuration et sa base : on installe à leur place des modules minimaux dans sys.modules.
"""

import sys
import threading
import types
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))


class FakeHTTPException(Exception):
    def __init__(self, status_code, detail=None):
        super().__init__(detail)
        self.status_code = status_code
        self.detail = detail


class FakeMemory:
    """Remplacé par chaque test qui en a besoin (monkeypatch de Memory.from_config)."""

    @classmethod
    def from_config(cls, config):
        return cls()


def _module(name, **attrs):
    module = types.ModuleType(name)
    module.__dict__.update(attrs)
    sys.modules[name] = module
    return module


def _original_initialize_state(_default_config):
    raise AssertionError("initialize_state d'origine appelé : lanasoft_main ne l'a pas remplacé")


def _original_update_config(_updates):
    raise AssertionError("update_config d'origine appelé : lanasoft_main ne l'a pas remplacé")


SERVER_STATE = _module(
    "server_state",
    _state_lock=threading.Lock(),
    _current_config=None,
    _memory_instance=None,
    initialize_state=_original_initialize_state,
    update_config=_original_update_config,
)
_module("fastapi", HTTPException=FakeHTTPException)
_module("mem0", Memory=FakeMemory)
_module("main", app=object())


@pytest.fixture
def config_file(tmp_path, monkeypatch):
    """Écrit un fichier de configuration et y fait pointer lanasoft_main."""
    import lanasoft_main

    def write(text):
        path = tmp_path / "config.json"
        path.write_text(text, encoding="utf-8")
        monkeypatch.setattr(lanasoft_main, "CONFIG_FILE", str(path))
        return path

    return write
