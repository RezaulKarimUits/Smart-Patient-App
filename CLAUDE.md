# InPatient — project notes for Claude sessions

Multi-tenant hospital in-patient (ward) management demo for Bangladesh. ASP.NET Core API + SQL Server (database-first) + Expo React Native app. AI (OpenAI) only explains/summarises/structures — coded rules decide alerts; AI output is never saved without user confirm.

## Layout
- `backend/Database/` — `001_schema.sql`, `002_seed.sql` (idempotent), `003_views.sql`, `run-db.ps1|sh`, `scaffold.ps1|sh`
- `backend/InPatient.Api/` — controllers (thin) → `Services/` (logic). `Data/Entities` + `Data/InPatientDbContext.cs` are **scaffolded: never edit**. Customise in `Data/Partials/*.cs` (partial classes, `OnModelCreatingPartial` tenant filters, SaveChanges HospitalId stamping).
- `backend/InPatient.Tests/` — xUnit (NEWS2, dose schedule, monitoring rules, voice validation, tenancy, wristband check)
- `frontend/` — Expo Router app (TypeScript strict). `src/theme`, `src/components`, `src/api`, `app/` routes grouped by role.

## Commands
- DB (re)create + seed: `pwsh backend/Database/run-db.ps1 -Reset` (drops & recreates InPatientDb; seed times are relative to "now", so reset before a demo)
- Re-scaffold after schema change: `pwsh backend/Database/scaffold.ps1`
- AI provider: any OpenAI-compatible API via `Ai:BaseUrl`. Dev uses Groq (user-secrets): `Ai:BaseUrl=https://api.groq.com/openai/v1`, `Ai:ChatModel=openai/gpt-oss-120b` (strict JSON schema OK), `Ai:VisionModel=qwen/qwen3.8-27b` (report photos), `Ai:TranscriptionModel=whisper-large-v3` (prompt hint capped at 600 chars). Empty BaseUrl = OpenAI defaults.
- Storage: Azure container `demo` on account `hospitalwardmgt`, via a container SAS URL in user-secrets (`dotnet user-secrets set "Storage:ContainerSasUrl" "<url>" --project backend/InPatient.Api`). Blobs go to `hospital-{id}/admissions/...`; downloads use HMAC-signed `/api/files/{id}/content` links (≤10 min), because a container SAS can't mint per-blob SAS and must never reach clients. Current SAS expires 2026-11-07. Without the secret, the API falls back to Azurite (`azurite --silent --location %TEMP%\azurite`) with one container per hospital and real SAS URLs.
- API: `dotnet run --project backend/InPatient.Api` → http://0.0.0.0:5080 (Swagger `/swagger`, board `/board/`)
- Tests: `dotnet test backend/InPatient.sln`
- App: `cd frontend && npx expo start` ; typecheck `npx tsc --noEmit`

## Conventions
- Status/enum columns are NVARCHAR + CHECK constraints; C# constants live in `Domain/Enums.cs` (string constants, not C# enums).
- All timestamps stored UTC (`datetime2`). Clinical schedule logic uses Asia/Dhaka (UTC+6, no DST) via `IClock`/`Dhaka` helper. `IClock` supports dev time-advance.
- Errors: throw `ApiException(status, msg)` → `{ "error": "..." }`. DTOs only, never return entities.
- Every tenant table has `HospitalId`; global query filter via `ICurrentUser`. Background jobs use `CurrentUser.RunAsSystem()`.
- No patient names in logs. AI calls log model, latency, tokens.
- Voice and AI text are English only (`Ai:TranscriptionLanguage=en`); no Bangla/Banglish rules, samples or UI text.
- Demo password for all seeded users: `Demo@123`.
