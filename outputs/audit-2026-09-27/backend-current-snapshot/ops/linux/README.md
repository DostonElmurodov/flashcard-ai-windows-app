# Owl AI API Linux Deployment

This folder contains deployment templates and notes for running the `mavrylo` backend on Linux.

The current CI/CD workflow is **Docker-first**:

- GitHub Actions builds and tests the .NET API.
- GitHub Actions builds a Docker image.
- The image is pushed to GHCR.
- Manual deploy SSHes into the server and runs `docker compose pull && docker compose up -d`.

The older `owl-api.service` systemd template is still kept for reference, but it is not what the
current GitHub Actions deploy step uses.

## Target Production Shape

```text
Internet
  -> HTTPS 443
  -> nginx on host
  -> http://127.0.0.1:5289
  -> Docker container mavrylo-api
  -> ASP.NET Core on container port 8080
  -> PostgreSQL on the host
```

Only nginx should be reachable from the public internet. The API container should be published only
to `127.0.0.1`.

## Files In This Folder

| File | Purpose |
| --- | --- |
| `README.md` | this deployment guide |
| `owl-api.env.example` | safe production env template, no real secrets |
| `nginx-owl-api.conf.example` | nginx reverse-proxy template |
| `owl-api.service` | legacy/optional systemd service template |
| `SERVER-SETUP.md` | older long setup guide; useful background, but Docker README is primary |

## Required Server Components

Install on Ubuntu:

- Docker Engine with Compose plugin
- PostgreSQL 14 or newer
- nginx
- certbot
- UFW or another firewall

The Docker image already contains the ASP.NET runtime, so the server does not need the .NET SDK for
the Docker deployment path.

## Directory Layout

Recommended host paths:

```text
/opt/apps/mavrylo-api/
  docker-compose.yml

/etc/owl-ai/
  api.env
  secrets/
    SubscriptionKey.p8

/var/log/mavrylo-api/
```

Create directories:

```bash
sudo mkdir -p /opt/apps/mavrylo-api /etc/owl-ai/secrets /var/log/mavrylo-api
sudo chmod 750 /etc/owl-ai /etc/owl-ai/secrets
```

## PostgreSQL

Create the database and user:

```bash
sudo -u postgres psql
```

```sql
CREATE USER owl_ai WITH PASSWORD 'CHANGE_ME_LONG_RANDOM_PASSWORD';
CREATE DATABASE owl_ai OWNER owl_ai;
GRANT ALL PRIVILEGES ON DATABASE owl_ai TO owl_ai;
\c owl_ai
GRANT ALL ON SCHEMA public TO owl_ai;
ALTER SCHEMA public OWNER TO owl_ai;
```

If the API runs in Docker and PostgreSQL runs on the host, the container must be able to reach the
host database. With Docker bridge networking, the host is commonly reachable as `172.17.0.1`.

PostgreSQL must listen on localhost and the docker bridge address. In `postgresql.conf`, use a
careful value such as:

```text
listen_addresses = '127.0.0.1,172.17.0.1'
```

In `pg_hba.conf`, allow the Docker bridge subnet:

```text
host    owl_ai    owl_ai    172.17.0.0/16    scram-sha-256
```

Reload PostgreSQL:

```bash
sudo systemctl reload postgresql
```

Keep port `5432` closed to the public internet.

## Server Environment

Copy the template:

```bash
sudo cp ops/linux/owl-api.env.example /etc/owl-ai/api.env
sudo nano /etc/owl-ai/api.env
```

For Docker, these values are important:

```text
ASPNETCORE_URLS=http://0.0.0.0:8080
ConnectionStrings__Default=Host=172.17.0.1;Port=5432;Database=owl_ai;Username=owl_ai;Password=CHANGE_ME
AI__Provider=OpenAI
```

Generate `Jwt__Key`:

```bash
openssl rand -base64 64
```

The value must be one line. If your terminal wraps it visually, do not copy a newline into the env
file.

Protect the env file:

```bash
sudo chown root:root /etc/owl-ai/api.env
sudo chmod 640 /etc/owl-ai/api.env
```

## Apple `.p8` Key

The App Store Server API private key must live only on the server.

Recommended path:

```text
/etc/owl-ai/secrets/SubscriptionKey.p8
```

Permissions:

```bash
sudo chown root:root /etc/owl-ai/secrets/SubscriptionKey.p8
sudo chmod 640 /etc/owl-ai/secrets/SubscriptionKey.p8
```

Never commit this file. Never copy its contents into source code, the iOS app, logs, screenshots,
or GitHub Actions secrets unless you intentionally choose an inline-key deployment strategy.

## Docker Compose

Create `/opt/apps/mavrylo-api/docker-compose.yml`:

```yaml
services:
  api:
    image: ghcr.io/dostonelmurodov/mavrylo:latest
    container_name: mavrylo-api
    restart: unless-stopped
    network_mode: bridge
    env_file:
      - /etc/owl-ai/api.env
    ports:
      - "127.0.0.1:5289:8080"
    volumes:
      - /etc/owl-ai/secrets:/etc/owl-ai/secrets:ro
      - /var/log/mavrylo-api:/app/logs
```

Log in to GHCR once on the server. The package is private unless you make it public:

```bash
echo YOUR_GITHUB_PAT_WITH_READ_PACKAGES | docker login ghcr.io -u YOUR_GITHUB_USERNAME --password-stdin
```

Start manually:

```bash
cd /opt/apps/mavrylo-api
docker compose pull
docker compose up -d
docker compose logs -f api
```

Health check:

```bash
curl -fsS http://127.0.0.1:5289/health
```

Expected:

```json
{"ok":true}
```

## nginx

Use `nginx-owl-api.conf.example` as the base config.

Important parts:

- public HTTPS terminates at nginx
- nginx proxies to `http://127.0.0.1:5289`
- `client_max_body_size 25m` is required for image upload AI endpoint
- `X-Forwarded-*` headers are forwarded

Install:

```bash
sudo cp ops/linux/nginx-owl-api.conf.example /etc/nginx/sites-available/owl-api
sudo ln -sfn /etc/nginx/sites-available/owl-api /etc/nginx/sites-enabled/owl-api
sudo nginx -t
sudo systemctl reload nginx
```

After DNS points `api.mavrylo.com` to the server, issue a certificate:

```bash
sudo certbot --nginx -d api.mavrylo.com
sudo certbot renew --dry-run
```

## Firewall

Allow only SSH, HTTP, and HTTPS publicly:

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw --force enable
sudo ufw status
```

Do not expose PostgreSQL publicly.

## GitHub Actions Deployment

Workflow file:

```text
.github/workflows/api-ci-cd.yml
```

Required repository secrets:

```text
DEPLOY_SSH_HOST
DEPLOY_SSH_PORT
DEPLOY_SSH_USER
DEPLOY_SSH_PRIVATE_KEY
DEPLOY_PATH
```

Recommended value:

```text
DEPLOY_PATH=/opt/apps/mavrylo-api
```

The deploy job runs only for `workflow_dispatch`. A normal push to `main` builds, tests, and pushes
the image, but does not SSH into the server.

The deploy user must be able to:

- SSH to the server
- run `docker compose pull`
- run `docker compose up -d`
- access the Docker daemon, usually by being in the `docker` group

## App Store Connect

Configure App Store Server Notifications v2:

```text
https://api.mavrylo.com/owlai/app-store-notifications/notifications
```

Use Sandbox while testing. Switch `Apple__AppStoreServer__Environment=Production` only when the app
is ready for production transactions.

The `.p8` key, issuer id, and key id belong only on the backend server.

## Verification Checklist

Before first deploy:

- DNS for `api.mavrylo.com` points to the server
- nginx has a valid certificate
- PostgreSQL database and user exist
- PostgreSQL allows the Docker bridge subnet if API runs in Docker
- `/etc/owl-ai/api.env` has real values
- `Jwt__Key` is long and one line
- `AiProtection__Enabled=true`
- `Apple__SkipSignatureValidation=false`
- `/etc/owl-ai/secrets/SubscriptionKey.p8` exists and is readable by the container
- GHCR login works on the server
- `docker compose pull` works in `DEPLOY_PATH`

After deploy:

```bash
docker compose ps
docker compose logs --tail=100 api
curl -fsS http://127.0.0.1:5289/health
curl -fsS https://api.mavrylo.com/health
```

Expected protected AI behavior without credentials:

```bash
curl -i -X POST https://api.mavrylo.com/owlai/ai/word-detail
```

Expected status: `401 Unauthorized` when `AiProtection__Enabled=true`.

## Operations Commands

```bash
cd /opt/apps/mavrylo-api

# status
docker compose ps

# logs
docker compose logs -f api
docker compose logs --tail=200 api

# restart
docker compose restart api

# update to latest image
docker compose pull
docker compose up -d

# inspect env file path without printing secrets
docker inspect mavrylo-api --format '{{json .Config.Env}}'
```

## Backups

Create a daily PostgreSQL dump:

```bash
sudo mkdir -p /var/backups/owl-ai
sudo chown postgres:postgres /var/backups/owl-ai
```

Example cron entry:

```bash
echo '30 3 * * * postgres pg_dump owl_ai | gzip > /var/backups/owl-ai/owl_ai_$(date +\%F).sql.gz && find /var/backups/owl-ai -name "*.sql.gz" -mtime +14 -delete' | sudo tee /etc/cron.d/owl-ai-backup
sudo chmod 644 /etc/cron.d/owl-ai-backup
```

Test a dump:

```bash
sudo -u postgres pg_dump owl_ai | gzip > /tmp/owl_ai_test.sql.gz
ls -lh /tmp/owl_ai_test.sql.gz
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Container exits immediately | check `docker compose logs api` |
| `Jwt:Key is missing` | set `Jwt__Key` in `/etc/owl-ai/api.env` |
| `AiProtection:Enabled must be true` | set `AiProtection__Enabled=true` |
| `Apple:SkipSignatureValidation must be false` | set `Apple__SkipSignatureValidation=false` |
| API cannot connect to DB | check `ConnectionStrings__Default`, docker bridge, `pg_hba.conf`, and firewall |
| nginx returns 502 | container is down or not listening on `127.0.0.1:5289` |
| image pull fails | run `docker login ghcr.io` with a token that has `read:packages` |
| image upload returns 413 | set `client_max_body_size 25m` in nginx |
| all clients look like localhost | ensure forwarded headers are set by nginx |
| Apple JWS fails | verify server time, Apple root certs, environment, and production signature settings |

## Systemd Variant

`owl-api.service` is a host-runtime service file for running `dotnet Mavrylo.dll` directly
without Docker. If you choose that path:

- install ASP.NET Core Runtime 10 on the host
- publish the app to `/var/www/owl-api/current`
- set `ASPNETCORE_URLS=http://127.0.0.1:5289`
- use `ConnectionStrings__Default=Host=127.0.0.1;...`
- configure the service with `EnvironmentFile=/etc/owl-ai/api.env`

The current GitHub Actions deploy job does not use this systemd path.
