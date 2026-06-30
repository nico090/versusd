FROM python:3.12-slim
WORKDIR /app

# Docker CLI so the master server can spawn game-server containers on-demand
# (talks to the host daemon via the mounted /var/run/docker.sock).
# Note: install docker-cli, NOT docker.io — on Debian trixie the `docker` client
# binary lives in the separate docker-cli package, which docker.io only
# *recommends*, so --no-install-recommends would otherwise omit the client.
RUN apt-get update \
    && apt-get install -y --no-install-recommends docker-cli \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY app ./app
EXPOSE 8000
CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
