# SafeVault — ASP.NET Core Secure Coding Project

This project is a learning-oriented ASP.NET Core Web API for a Microsoft back-end security course. It demonstrates ASP.NET Core Identity, JWT authentication, role-based access control (RBAC), input validation, Entity Framework Core parameterized data access, and security tests.

## Requirements
- .NET 8 SDK
- Git
- A GitHub account
- Optional: Visual Studio 2022 or VS Code with C# Dev Kit

## Configure JWT signing key
The app intentionally fails to start if a JWT key is not configured. Never commit a real secret.

PowerShell (for the current terminal session):
```powershell
$env:Jwt__Key = "replace-with-a-random-secret-at-least-32-bytes-long"
```

Recommended local development approach:
```bash
dotnet user-secrets init --project SafeVault.Api
dotnet user-secrets set "Jwt:Key" "replace-with-a-long-random-secret-at-least-32-bytes" --project SafeVault.Api
```

## Build and run
```bash
dotnet restore
dotnet build
dotnet run --project SafeVault.Api
```

The API uses SQLite and creates its schema on first run. Endpoints:
- `POST /api/auth/register` — JSON `{ "email": "user@example.com", "password": "StrongPassword123!" }`
- `POST /api/auth/login` — returns a JWT access token
- `GET /api/notes` — requires `Authorization: Bearer <token>`
- `POST /api/notes` — requires a token; JSON `{ "content": "my note" }`
- `GET /api/admin/users` — requires the `Admin` role

New users are always assigned the `User` role. Do not add a public endpoint for granting Admin. For a real deployment, provision administrators through a controlled process.

## Run tests
```bash
dotnet test
```

Review all test results locally. The included tests are a starting point and may need adjustment depending on the SDK/runtime and test-host behavior.

## Security controls
- ASP.NET Core Identity manages password hashing and user storage.
- Password policy and request validation reject weak/invalid input.
- JWT validation checks issuer, audience, signing key, and expiration.
- Admin API is protected with `[Authorize(Roles = "Admin")]` endpoint metadata.
- Entity Framework Core LINQ queries use parameterized database operations.
- Notes are scoped to the authenticated user's ID.
- User-controlled note text is returned as JSON, not executed by the server. Any browser client must render it as text, not via `innerHTML`.

## Copilot assignment evidence
Use Microsoft/GitHub Copilot to review the project and tests, then document the actual prompts, suggestions, accepted changes, and test outcomes in `COPILOT_USAGE.md`. Suggested prompts:
1. "Review my ASP.NET Core Identity registration and login endpoints for security issues. Suggest fixes and explain them."
2. "Review JWT validation, token lifetime, issuer/audience validation, and role claims in this API."
3. "Generate xUnit tests for SQL injection payloads, weak passwords, authentication requirements, and RBAC."
4. "Review XSS handling and explain the difference between returning JSON and safely rendering text in a browser."

Do not claim Copilot was used or tests passed until you have actually done so.

## Security review summary
| Risk | Mitigation | Verification |
|---|---|---|
| SQL injection | EF Core parameterized data access | Injection-shaped login test |
| Weak passwords | Identity password policy and validation | Weak-password registration test |
| Broken authentication | JWT bearer validation and Identity password checks | Protected endpoint tests |
| Broken authorization | Admin endpoint requires Admin role; public registration assigns User | Extend tests to verify a normal token receives 403 |
| XSS | API returns text as JSON data; client must render as text | Contract test and manual client review |
| Secret leakage | JWT key loaded from configuration/user-secrets | App refuses to start without a key |

## Important limitations
This is an educational starter, not production-ready. Before deployment, add robust integration tests with isolated test databases, CSRF protection if cookie auth is introduced, rate limiting, refresh-token strategy/revocation, HTTPS and secret management, logging/monitoring, database migrations, and a controlled admin provisioning process. Verify all code and tests before submission.
