# Image mem0-api : serveur Mem0 officiel (dossier server/ du dépôt mem0ai/mem0, au tag MEM0_REF)
# + point d'entrée LanaSoft (configuration par fichier, cf. lanasoft_main.py).
#
# Contexte de construction : racine de ce dépôt, avec le dépôt officiel cloné dans upstream/
# (fait par la CI) :
#   git clone --depth 1 --branch v2.2.1 https://github.com/mem0ai/mem0.git upstream
#   docker build -f mem0/Dockerfile -t mem0-api .
FROM python:3.12-slim@sha256:2f17fc044b579bab302c2e8054d3a686e2cb9a83de48e70534b94cd8ebbe06a9

ENV PYTHONUNBUFFERED=1 \
    PYTHONDONTWRITEBYTECODE=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1 \
    MEM0_TELEMETRY=false \
    MEM0_CONFIG_FILE=/etc/mem0/config.json \
    MEM0_DIR=/data/.mem0 \
    HOME=/data

WORKDIR /app

COPY upstream/server/requirements.txt ./requirements.txt
COPY mem0/requirements.lanasoft.txt ./requirements.lanasoft.txt
RUN pip install -r requirements.txt -r requirements.lanasoft.txt

COPY upstream/server/ ./
COPY mem0/lanasoft_main.py mem0/lanasoft_reindex.py ./

# Utilisateur non root ; /data (volume) porte l'historique SQLite et l'état local de la
# bibliothèque mem0 (MEM0_DIR), qui sinon écrirait dans un répertoire personnel inexistant.
RUN useradd --uid 10001 --no-create-home --shell /usr/sbin/nologin mem0 \
    && mkdir /data && chown 10001 /data
USER 10001

EXPOSE 8000

# Migrations de la base applicative (comptes, clés d'API), puis le serveur. Pas de --reload.
CMD ["sh", "-c", "alembic upgrade head && exec uvicorn lanasoft_main:app --host 0.0.0.0 --port 8000 --proxy-headers --forwarded-allow-ips '*'"]
