# FlashCard API (.NET 9)

Minimal REST API: JWT auth (register/login, Google id_token, Apple identity token in dev), CRUD for words/categories/user settings, sync pull/push, OpenAI proxy.

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- Optional: `OpenAI:ApiKey` in `appsettings.json` or environment for AI routes

## Run

```bash
cd apps/api-dotnet
dotnet restore
dotnet run --launch-profile http
```

API base: `http://localhost:5288`

- `POST /api/auth/register` `{ "email", "password" }`
- `POST /api/auth/login`
- `GET /api/auth/me` (Bearer)
- SQLite file `flashcard.db` in the project directory (or set `ConnectionStrings:Default` to PostgreSQL)

## Production

- Set a strong `Jwt:Key` (32+ random bytes as string).
- Set `Apple:SkipSignatureValidation` to `false` and implement JWKS validation.
- Set `Google:ClientIds` to allowed OAuth client IDs (comma-separated).
- Replace `EnsureCreated` in `Program.cs` with EF migrations for PostgreSQL.

See [contracts/openapi/openapi.yaml](../contracts/openapi/openapi.yaml) for the contract.
