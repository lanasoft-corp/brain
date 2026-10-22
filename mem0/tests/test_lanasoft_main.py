import json

import pytest
import server_state
from conftest import FakeHTTPException

import lanasoft_main


def test_load_config_replaces_environment_variables(config_file, monkeypatch):
    monkeypatch.setenv("BRAIN_TEST_KEY", "secret-value")
    config_file(json.dumps({"llm": {"api_key": "${BRAIN_TEST_KEY}"}, "plain": "$BRAIN_TEST_KEY"}))

    assert lanasoft_main.load_config() == {"llm": {"api_key": "secret-value"}, "plain": "secret-value"}


def test_load_config_refuses_missing_variables(config_file, monkeypatch):
    monkeypatch.delenv("BRAIN_TEST_ABSENT", raising=False)
    monkeypatch.delenv("BRAIN_TEST_OTHER", raising=False)
    config_file(json.dumps({"a": "${BRAIN_TEST_OTHER}", "b": "$BRAIN_TEST_ABSENT"}))

    with pytest.raises(RuntimeError, match=r"absentes : \$BRAIN_TEST_ABSENT, \$\{BRAIN_TEST_OTHER\}"):
        lanasoft_main.load_config()


def test_server_state_functions_are_replaced_on_import():
    assert server_state.initialize_state is lanasoft_main._initialize_state
    assert server_state.update_config is lanasoft_main._update_config


def test_initialize_state_loads_the_file_and_ignores_the_stored_config(config_file, monkeypatch):
    config = {"vector_store": {"provider": "pgvector"}}
    config_file(json.dumps(config))
    built = []
    monkeypatch.setattr(lanasoft_main.Memory, "from_config", classmethod(lambda cls, c: built.append(c) or "memory"))

    server_state.initialize_state({"stored": "in database"})

    assert server_state._current_config == config
    assert server_state._memory_instance == "memory"
    assert built == [config]


def test_initialize_state_keeps_its_own_copy_of_the_config(config_file, monkeypatch):
    config_file(json.dumps({"nested": {"value": 1}}))
    monkeypatch.setattr(lanasoft_main.Memory, "from_config", classmethod(lambda cls, c: c))

    server_state.initialize_state({})
    server_state._memory_instance["nested"]["value"] = 2

    assert server_state._current_config == {"nested": {"value": 1}}


def test_update_config_is_refused():
    with pytest.raises(FakeHTTPException) as error:
        server_state.update_config({"llm": {}})

    assert error.value.status_code == 403
    assert "ConfigMap" in error.value.detail
