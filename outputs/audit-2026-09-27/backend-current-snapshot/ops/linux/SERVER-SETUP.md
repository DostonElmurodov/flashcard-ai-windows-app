# Owl AI — Полная настройка сервера (Ubuntu + PostgreSQL + nginx + HTTPS + GitHub Actions)

Подробная пошаговая инструкция для деплоя backend (этот репозиторий `mavrylo`) на чистый Ubuntu-сервер.
Выполняй фазы **по порядку**. Команды копируй как есть, заменяя только значения в `ВЕРХНЕМ_РЕГИСТРЕ`.

> Объяснения — на русском, команды — на английском. Всё, что в `ВОТ_ТАКОМ_ВИДЕ`, нужно заменить своими значениями.

## Содержание

0. [Что приготовить заранее](#0-что-приготовить-заранее-выпиши-на-бумажку)
1. [Базовые пакеты](#1-базовые-пакеты)
2. [PostgreSQL](#2-postgresql--база-пользователь-и-права)
3. [Пользователь приложения и папки](#3-пользователь-приложения-и-папки)
4. [Секреты: env-файл и `.p8`](#4-секреты-env-файл-и-p8)
5. [systemd-сервис](#5-systemd-сервис)
6. [DNS](#6-dns--направь-домен-на-сервер)
7. [nginx + HTTPS](#7-nginx--https-lets-encrypt-бесплатно)
8. [Деплой через GitHub Actions](#8-деплой-через-github-actions)
9. [Первый деплой и проверка](#9-первый-деплой-и-проверка)
10. [Настройка на стороне Apple](#10-настройка-на-стороне-apple-app-store-connect)
11. [Подключение iOS](#11-подключи-ios-приложение-к-серверу)
12. [Бэкапы базы](#12-бэкапы-базы-данных)
13. [Откат релиза (rollback)](#13-откат-релиза-rollback)
14. [Усиление безопасности (опционально)](#14-усиление-безопасности-опционально)
15. [Частые проблемы](#15-частые-проблемы)
16. [Полезные команды](#16-полезные-команды)
17. [Чек-лист](#чек-лист)

---

## 0. Что приготовить заранее (выпиши на бумажку)

| Что | Где взять | Пример |
|-----|-----------|--------|
| **Публичный IP сервера** | панель хостинга или `curl -4 ifconfig.me` | `203.0.113.10` |
| **Домен** | у тебя `api.mavrylo.com` | — |
| **Регистратор домена** | где покупал `mavrylo.com` | Namecheap / Cloudflare / рег.ру |
| **Пароль для Postgres** | придумай сам, длинный | `openssl rand -hex 24` |
| **JWT-ключ** | сгенерируешь в Фазе 4 | `openssl rand -base64 64` |
| **Apple Issuer ID** | App Store Connect → Users and Access → Integrations → App Store Connect API | server-only |
| **Apple Key ID** | оттуда же (в имени файла `.p8`) | server-only |
| **Apple Team ID** | Apple Developer → Membership | server-only |
| **Файл `.p8`** | App Store Connect (уже скачан) | НИКОГДА не коммить |
| **OpenAI/Gemini API key** | OpenAI Platform / Google AI Studio | server-only |
| **Email для Let's Encrypt** | твой рабочий email | для уведомлений о сертификате |

> ⚠️ **Безопасность:** `.p8`, IssuerId, KeyId, API-ключи — **только на сервере**. Никогда не клади их в git, в iOS, в скриншоты, в логи.
>
> ⚠️ **Пароль Postgres:** используй только буквы и цифры (`openssl rand -hex 24`). Избегай символов `; = ' " пробел` — они ломают строку подключения вида `key=value;key=value`.

---

## 1. Базовые пакеты

Подключись к серверу:
```bash
ssh ВАШ_ПОЛЬЗОВАТЕЛЬ@IP_СЕРВЕРА
```

Обнови систему:
```bash
sudo apt update && sudo apt upgrade -y
```

### 1.1. .NET 10 ASP.NET Core Runtime (репозиторий Microsoft)
```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/ms.deb
sudo dpkg -i /tmp/ms.deb
sudo apt update
sudo apt install -y aspnetcore-runtime-10.0
```

Проверь:
```bash
dotnet --list-runtimes      # должно быть Microsoft.AspNetCore.App 10.x И Microsoft.NETCore.App 10.x
```

> **Если пакета `aspnetcore-runtime-10.0` нет** (репозиторий Microsoft ещё не обновился для твоей версии Ubuntu) — поставь через официальный скрипт:
> ```bash
> curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
> sudo bash /tmp/dotnet-install.sh --channel 10.0 --runtime aspnetcore --install-dir /usr/share/dotnet
> sudo ln -sfn /usr/share/dotnet/dotnet /usr/bin/dotnet
> ```
> Нам нужен именно **ASP.NET Core Runtime** (не SDK) — приложение публикуется framework-dependent и запускается как `dotnet Mavrylo.dll`.

### 1.2. Остальные пакеты
```bash
sudo apt install -y postgresql nginx rsync certbot python3-certbot-nginx ufw
```

### 1.3. Синхронизация времени (важно для JWT и Apple-подписей)
JWT и проверка подписей Apple чувствительны ко времени (допуск всего 2 минуты). Убедись, что время точное:
```bash
timedatectl                  # System clock synchronized: yes ; NTP service: active
sudo timedatectl set-ntp true   # включить NTP, если выключен
```

### 1.4. Фаервол (SSH, HTTP, HTTPS)
```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw --force enable
sudo ufw status
```

---

## 2. PostgreSQL — база, пользователь и **права**

Создай БД и пользователя (замени `СИЛЬНЫЙ_ПАРОЛЬ`; запиши его — нужен в Фазе 4):
```bash
sudo -u postgres psql <<'SQL'
CREATE USER owl_ai WITH PASSWORD 'СИЛЬНЫЙ_ПАРОЛЬ';
CREATE DATABASE owl_ai OWNER owl_ai;
GRANT ALL PRIVILEGES ON DATABASE owl_ai TO owl_ai;
SQL
```

### 2.1. Права на схему `public` (ОБЯЗАТЕЛЬНО для PostgreSQL 15+)
На PostgreSQL 15 и новее `GRANT ON DATABASE` **не даёт** право создавать таблицы — иначе миграции упадут с `permission denied for schema public`. Выдай права на схему **внутри** базы:
```bash
sudo -u postgres psql -d owl_ai <<'SQL'
GRANT ALL ON SCHEMA public TO owl_ai;
ALTER SCHEMA public OWNER TO owl_ai;
SQL
```

### 2.2. Проверь подключение по паролю (TCP, как ходит приложение)
```bash
psql "host=127.0.0.1 port=5432 dbname=owl_ai user=owl_ai password=СИЛЬНЫЙ_ПАРОЛЬ" -c "select version();"
```
Должна вывестись версия PostgreSQL. Если `password authentication failed` — проверь пароль; если `no pg_hba.conf entry` — для localhost обычно уже разрешено, иначе добавь в `/etc/postgresql/*/main/pg_hba.conf` строку `host owl_ai owl_ai 127.0.0.1/32 scram-sha-256` и `sudo systemctl reload postgresql`.

### 2.3. (Безопасность) Postgres слушает только localhost
По умолчанию так и есть. Проверь, что порт 5432 не торчит наружу:
```bash
sudo ss -ltnp | grep 5432    # ожидаем 127.0.0.1:5432, НЕ 0.0.0.0:5432
```

> Таблицы создавать вручную **не нужно** — приложение само применяет EF-миграции при первом запуске.

---

## 3. Пользователь приложения и папки

```bash
# системный пользователь сервиса (без возможности логина)
sudo useradd --system --home /var/www/owl-api --shell /usr/sbin/nologin owlapi

# папки приложения и секретов
sudo mkdir -p /var/www/owl-api/releases /etc/owl-ai/secrets

# твой deploy-пользователь должен быть в группе owlapi, чтобы заливать релизы
sudo usermod -aG owlapi $USER      # ВАЖНО: после этого выйди и зайди по SSH заново

# владелец дерева приложения — твой пользователь, группа owlapi (сервис читает по группе)
sudo chown -R $USER:owlapi /var/www/owl-api
sudo find /var/www/owl-api -type d -exec chmod 2750 {} \;   # 2 = setgid: новые файлы наследуют группу owlapi

# секреты: владелец root, группа owlapi, доступ только им
sudo chown -R root:owlapi /etc/owl-ai
sudo chmod 750 /etc/owl-ai /etc/owl-ai/secrets
```

> После `usermod -aG owlapi` **обязательно** переподключись по SSH (`exit`, потом снова `ssh ...`), иначе членство в группе не применится и деплой упадёт на правах.

---

## 4. Секреты: env-файл и `.p8`

### 4.1. Сгенерируй JWT-ключ
```bash
openssl rand -base64 64
```
Скопируй результат — вставишь в `Jwt__Key` ниже.

### 4.2. Создай файл окружения
```bash
sudo nano /etc/owl-ai/api.env
```

Вставь и **замени все `CHANGE_ME` / пароль / ключ**:
```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5289
# За nginx-прокси: доверять X-Forwarded-* (правильный IP для rate-limit и https-схема)
ASPNETCORE_FORWARDEDHEADERS_ENABLED=true

ConnectionStrings__Default=Host=127.0.0.1;Port=5432;Database=owl_ai;Username=owl_ai;Password=СИЛЬНЫЙ_ПАРОЛЬ_ИЗ_ФАЗЫ_2

Jwt__Key=ВСТАВЬ_СЮДА_РЕЗУЛЬТАТ_openssl_rand
Jwt__Issuer=https://api.mavrylo.com
Jwt__Audience=flashcard-ai
Jwt__ExpiresMinutes=10080
Jwt__DeviceExpiresMinutes=60

LegacyAuth__Enabled=false

AiProtection__Enabled=true
AiProtection__RequireAssertion=true
AiProtection__FreeDailyQuota=40

Apple__ClientId=com.mavrylo.owlai
Apple__TeamId=CHANGE_ME_TEAM_ID
Apple__SkipSignatureValidation=false

Apple__AppStoreServer__IssuerId=CHANGE_ME_ISSUER_ID
Apple__AppStoreServer__KeyId=CHANGE_ME_KEY_ID
Apple__AppStoreServer__BundleId=com.mavrylo.owlai
Apple__AppStoreServer__PrivateKeyPath=/etc/owl-ai/secrets/SubscriptionKey.p8
Apple__AppStoreServer__Environment=Sandbox

AI__Provider=OpenAI
Gemini__ApiKey=
Gemini__Model=gemini-2.5-flash
OpenAI__ApiKey=CHANGE_ME
OpenAI__Model=gpt-6-luna

Google__ClientIds=
```
Сохрани (`Ctrl+O`, `Enter`, `Ctrl+X`).

> `Apple__AppStoreServer__Environment=Sandbox` — пока тестируешь покупки в Sandbox. Когда приложение выйдет в проде — поменяешь на `Production`.

### 4.3. Загрузи `.p8` на сервер
Файл, скачанный из App Store Connect, называется `SubscriptionKey_<ВАШ_KEY_ID>.p8`.

**С Mac** (новый терминал, НЕ на сервере):
```bash
scp /ПУТЬ/НА/MAC/SubscriptionKey_ВАШ_KEY_ID.p8 \
    ВАШ_ПОЛЬЗОВАТЕЛЬ@IP_СЕРВЕРА:/tmp/SubscriptionKey.p8
```

Обратно **на сервере** — перенеси в защищённую папку:
```bash
sudo mv /tmp/SubscriptionKey.p8 /etc/owl-ai/secrets/SubscriptionKey.p8
sudo chown root:owlapi /etc/owl-ai/secrets/SubscriptionKey.p8
sudo chmod 640 /etc/owl-ai/secrets/SubscriptionKey.p8
```

> Путь `/etc/owl-ai/secrets/SubscriptionKey.p8` должен совпадать с `Apple__AppStoreServer__PrivateKeyPath` в env.

### 4.4. Закрой права на env-файл
```bash
sudo chown root:owlapi /etc/owl-ai/api.env
sudo chmod 640 /etc/owl-ai/api.env
```

---

## 5. systemd-сервис

> Готовый шаблон лежит в `ops/linux/owl-api.service`. Содержимое ниже идентично.

```bash
sudo nano /etc/systemd/system/owl-api.service
```
Вставь:
```ini
[Unit]
Description=Owl AI FlashCard API
After=network.target postgresql.service

[Service]
Type=simple
User=owlapi
Group=owlapi
WorkingDirectory=/var/www/owl-api/current
ExecStart=/usr/bin/dotnet /var/www/owl-api/current/Mavrylo.dll
EnvironmentFile=/etc/owl-ai/api.env
Restart=always
RestartSec=5
KillSignal=SIGINT
SyslogIdentifier=owl-api
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ReadWritePaths=/var/www/owl-api /etc/owl-ai

[Install]
WantedBy=multi-user.target
```
Включи (запустится после первого деплоя кода):
```bash
sudo systemctl daemon-reload
sudo systemctl enable owl-api
```

> Сейчас `/var/www/owl-api/current` ещё пустой — сервис не стартует, это нормально. Код появится в Фазе 9.

---

## 6. DNS — направь домен на сервер

В панели регистратора `mavrylo.com` создай A-запись:

| Тип | Имя (Host) | Значение | TTL |
|-----|-----------|----------|-----|
| `A` | `api` | `IP_СЕРВЕРА` | Auto / 3600 |

Проверь **с Mac** (через несколько минут):
```bash
dig +short api.mavrylo.com
```
Должен вывести IP сервера. Пока не выводит — HTTPS в Фазе 7 не получится, жди обновления DNS.

> Если домен за Cloudflare: на время выпуска сертификата поставь запись в режим **DNS only** (серое облачко), иначе certbot может не пройти проверку.

---

## 7. nginx + HTTPS (Let's Encrypt, бесплатно)

### 7.1. Временный HTTP-конфиг (нужен certbot'у для проверки домена)
```bash
sudo nano /etc/nginx/sites-available/owl-api
```
Вставь:
```nginx
server {
    listen 80;
    server_name api.mavrylo.com;

    location / {
        proxy_pass http://127.0.0.1:5289;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
```
Включи сайт:
```bash
sudo ln -sfn /etc/nginx/sites-available/owl-api /etc/nginx/sites-enabled/owl-api
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
```

### 7.2. Выпусти сертификат
```bash
sudo certbot --nginx -d api.mavrylo.com --redirect --agree-tos -m ТВОЙ_EMAIL@example.com --no-eff-email
```
Certbot сам перепишет конфиг под HTTPS (443), добавит редирект с HTTP и поставит systemd-таймер авто-обновления.

### 7.3. Подними лимит размера запроса (скан фото — до 20 МБ)
```bash
sudo nano /etc/nginx/sites-available/owl-api
```
В блок `server { listen 443 ssl; ... }` добавь строку (после `server_name`):
```nginx
    client_max_body_size 25m;
```
Применить:
```bash
sudo nginx -t && sudo systemctl reload nginx
```

Проверь авто-обновление:
```bash
sudo certbot renew --dry-run
```

---

## 8. Деплой через GitHub Actions

CI/CD готов: `.github/workflows/api-ci-cd.yml`. По кнопке он собирает API, заливает через SSH+rsync в `releases/<sha>`, переключает симлинк `current` и перезапускает сервис.

### 8.1. SSH-ключ для деплоя
**На сервере** создай ключ специально для деплоя:
```bash
ssh-keygen -t ed25519 -f ~/.ssh/owl_deploy -N "" -C "github-actions-deploy"
cat ~/.ssh/owl_deploy.pub >> ~/.ssh/authorized_keys
chmod 600 ~/.ssh/authorized_keys
cat ~/.ssh/owl_deploy        # ← скопируй ВЕСЬ приватный ключ (с строками BEGIN/END) — пойдёт в секрет GitHub
```

> Deploy-пользователь — твой обычный sudo-пользователь (тот, что в группе `owlapi` из Фазы 3). Сервис работает под `owlapi`, а файлы заливает твой пользователь.

### 8.2. Разреши деплой-пользователю перезапуск сервиса без пароля
```bash
echo "$USER ALL=(ALL) NOPASSWD: /usr/bin/systemctl restart owl-api, /usr/bin/systemctl status owl-api" | sudo tee /etc/sudoers.d/owl-api-deploy
sudo chmod 440 /etc/sudoers.d/owl-api-deploy
sudo visudo -c                 # проверка синтаксиса sudoers
```

### 8.3. Секреты в GitHub
Репозиторий `mavrylo` → **Settings → Secrets and variables → Actions → New repository secret**:

| Имя секрета | Значение |
|-------------|----------|
| `DEPLOY_SSH_HOST` | `api.mavrylo.com` (или IP сервера) |
| `DEPLOY_SSH_PORT` | `22` |
| `DEPLOY_SSH_USER` | твой sudo-пользователь (тот же, что в Фазе 3) |
| `DEPLOY_SSH_PRIVATE_KEY` | весь приватный ключ из 8.1 (`~/.ssh/owl_deploy`) |
| `DEPLOY_PATH` | `/var/www/owl-api` |
| `DEPLOY_SERVICE_NAME` | `owl-api` |

> Деплой-job использует окружение `production`. Зайди в **Settings → Environments**, создай окружение `production` (можно без доп. настроек) — иначе job не запустится.

### 8.4. Порядок первого запуска
1. Убедись, что **Фазы 2–5 выполнены** (БД, права, env, `.p8`, systemd) — миграции применяются при первом старте, поэтому БД и env должны быть готовы ДО деплоя.
2. Отправь актуальный код в ветку `main` (это делаешь ты).
3. GitHub → **Actions → API CI/CD → Run workflow → Run** (`workflow_dispatch`).

---

## 9. Первый деплой и проверка

После запуска workflow — на сервере:
```bash
sudo systemctl status owl-api --no-pager        # active (running)
sudo journalctl -u owl-api -n 80 --no-pager     # логи старта: миграции применились без ошибок?
```

Health-check локально на сервере:
```bash
curl -i http://127.0.0.1:5289/health            # {"ok":true}
```

Через домен (с Mac или сервера):
```bash
curl -i https://api.mavrylo.com/health
curl -i https://api.mavrylo.com/owlai/legal/privacy
```
Ожидаем `HTTP/2 200`.

Проверь, что защита включена (без токена должно быть 401, НЕ 200):
```bash
curl -i -X POST https://api.mavrylo.com/owlai/ai/word-detail
```

Проверь таблицы:
```bash
psql "host=127.0.0.1 port=5432 dbname=owl_ai user=owl_ai password=СИЛЬНЫЙ_ПАРОЛЬ" -c "\dt"
```
Должны быть `devices`, `subscriptions`, `device_words`, `challenges`, `ai_usage`, `translation_cache`, `__EFMigrationsHistory` и др.

---

## 10. Настройка на стороне Apple (App Store Connect)

Чтобы подписки/возвраты доходили до бэкенда, Apple должна слать **Server Notifications v2** на наш публичный URL.

1. **App Store Connect → твоё приложение → App Information → App Store Server Notifications.**
2. Укажи URL:
   - **Sandbox URL:** `https://api.mavrylo.com/owlai/app-store-notifications/notifications`
   - **Production URL:** тот же (заполнишь при релизе в прод).
3. Эндпоинт публичный (доверие к нему — через проверку подписи Apple JWS, не по токену).

Также убедись, что **App Store Connect API key** (тот самый `.p8`), его **Issuer ID** и **Key ID** соответствуют значениям в `/etc/owl-ai/api.env` (Фаза 4.2). Эти данные используются бэкендом для запросов к App Store Server API.

> Реальная проверка App Attest и Sandbox-покупок возможна только на **физическом устройстве** против этого публичного HTTPS-бэкенда.

---

## 11. Подключи iOS-приложение к серверу

После того как `https://api.mavrylo.com/health` отвечает 200:
- В iOS-репо `flashcard-ai-ios` поменяй `APIBaseURL` (сейчас `http://127.0.0.1:5289` в `Resources/Info.plist`) на `https://api.mavrylo.com`.
- Это нужно для теста с реального устройства и для релиза. (Скажи мне — поменяю и проверю сборку.)

---

## 12. Бэкапы базы данных

Простой ежедневный дамп через cron.

```bash
sudo mkdir -p /var/backups/owl-ai
sudo chown postgres:postgres /var/backups/owl-ai

# ежедневный бэкап в 03:30, хранить 14 дней
echo '30 3 * * * postgres pg_dump owl_ai | gzip > /var/backups/owl-ai/owl_ai_$(date +\%F).sql.gz && find /var/backups/owl-ai -name "*.sql.gz" -mtime +14 -delete' | sudo tee /etc/cron.d/owl-ai-backup
sudo chmod 644 /etc/cron.d/owl-ai-backup
```

Проверить бэкап вручную:
```bash
sudo -u postgres pg_dump owl_ai | gzip > /tmp/owl_ai_test.sql.gz && ls -lh /tmp/owl_ai_test.sql.gz
```

Восстановление (пример):
```bash
gunzip -c /var/backups/owl-ai/owl_ai_ГГГГ-ММ-ДД.sql.gz | sudo -u postgres psql owl_ai
```

> Скачивай дампы и на отдельное хранилище — диск сервера может умереть вместе с бэкапами.

---

## 13. Откат релиза (rollback)

GitHub Actions хранит релизы в `/var/www/owl-api/releases/<git-sha>` и переключает симлинк `current`. Откатиться = указать `current` на прошлый релиз и перезапустить.

```bash
ls -1t /var/www/owl-api/releases        # список релизов, новые сверху
# выбери предыдущий <sha>:
sudo ln -sfn /var/www/owl-api/releases/ПРЕДЫДУЩИЙ_SHA /var/www/owl-api/current
sudo systemctl restart owl-api
sudo systemctl status owl-api --no-pager
```

> ⚠️ Откат кода НЕ откатывает миграции БД. Если новый релиз изменил схему, при откате может понадобиться ручная правка. Делай миграции аддитивными.

---

## 14. Усиление безопасности (опционально, но рекомендуется)

```bash
# fail2ban против перебора SSH
sudo apt install -y fail2ban
sudo systemctl enable --now fail2ban

# (если используешь ключи) отключи вход по паролю
sudo sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo systemctl reload ssh
```

Для маленького VPS (1 ГБ RAM) добавь swap, чтобы сервис не падал под нагрузкой:
```bash
sudo fallocate -l 2G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
```

---

## 15. Частые проблемы

| Симптом | Причина / решение |
|---------|-------------------|
| `owl-api` не стартует, `Jwt:Key is missing` | не заполнил `Jwt__Key` в `/etc/owl-ai/api.env` |
| Падает `AiProtection:Enabled must be true` | в env должно быть `AiProtection__Enabled=true` |
| Падает `Apple:SkipSignatureValidation must be false` | поставь `Apple__SkipSignatureValidation=false` |
| `permission denied for schema public` при старте | не выдал права на схему (Фаза 2.1) |
| `password authentication failed` (`28P01`) | неверный пароль в `ConnectionStrings__Default` |
| Старт падает на спецсимволах в строке подключения | в пароле Postgres есть `; = ' "` или пробел — пересоздай пароль из букв/цифр |
| `certbot` не выпускает сертификат | DNS не указывает на сервер (`dig +short api.mavrylo.com`), порт 80 закрыт, или Cloudflare-проксирование включено |
| Все клиенты выглядят как `127.0.0.1`, rate-limit бьёт всех вместе | не задал `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (Фаза 4.2) |
| 401 на валидном токене / Apple JWS невалиден | время сервера сбито — проверь `timedatectl` (Фаза 1.3) |
| GitHub Actions: `Permission denied` при rsync | deploy-пользователь не в группе `owlapi` / нет прав на `/var/www/owl-api` (Фаза 3 + переподключиться по SSH) |
| GitHub Actions: `sudo: a password is required` | не настроил `/etc/sudoers.d/owl-api-deploy` (Фаза 8.2) |
| GitHub Actions: job `deploy` не запускается | не создал окружение `production` (Фаза 8.3) |
| `502 Bad Gateway` | сервис `owl-api` не запущен (`systemctl status owl-api`) |
| Скан фото → `413 Request Entity Too Large` | нет `client_max_body_size 25m;` в nginx (Фаза 7.3) |
| Подписки/возвраты не обновляют статус | не настроил Server Notifications URL в App Store Connect (Фаза 10) |

---

## 16. Полезные команды

```bash
# логи в реальном времени
sudo journalctl -u owl-api -f

# последние ошибки
sudo journalctl -u owl-api -p err -n 50 --no-pager

# перезапуск / статус
sudo systemctl restart owl-api
sudo systemctl status owl-api --no-pager

# текущий релиз
ls -la /var/www/owl-api/current

# какие env-файлы подхвачены (без вывода значений)
sudo systemctl show owl-api -p EnvironmentFiles

# nginx
sudo nginx -t && sudo systemctl reload nginx

# Postgres: размер БД и список таблиц
sudo -u postgres psql -d owl_ai -c "\dt+"
```

---

## Чек-лист

- [ ] 1. Базовые пакеты (.NET runtime, postgres, nginx, certbot, ufw) + NTP
- [ ] 2. PostgreSQL: БД `owl_ai` + пользователь + **права на схему public**
- [ ] 3. Пользователь `owlapi` + папки + права (переподключиться по SSH)
- [ ] 4. `/etc/owl-ai/api.env` заполнен (+ `ASPNETCORE_FORWARDEDHEADERS_ENABLED`) + `.p8` загружен + права закрыты
- [ ] 5. systemd-сервис `owl-api` создан и enabled
- [ ] 6. DNS: `api.mavrylo.com` → IP сервера (`dig` подтверждает)
- [ ] 7. nginx + HTTPS (certbot) + `client_max_body_size 25m`
- [ ] 8. SSH-ключ деплоя + sudoers + окружение `production` + 6 секретов в GitHub
- [ ] 9. Push в main → Run workflow → `https://api.mavrylo.com/health` = 200, защищённый эндпоинт = 401
- [ ] 10. App Store Connect: Server Notifications URL + сверка Issuer/Key ID
- [ ] 11. (позже) поменять `APIBaseURL` в iOS на `https://api.mavrylo.com`
- [ ] 12. Бэкапы БД (cron)
- [ ] 13. (знать) процедуру отката релиза
- [ ] 14. (опц.) fail2ban / отключить пароль SSH / swap

Когда пройдёшь — напиши, на каком шаге остановился или какие ошибки в логах, разберём.
