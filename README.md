# SafeVault — ASP.NET Core Secure Coding Project

SafeVault is an educational ASP.NET Core Web API project for the Microsoft Back-End Developer Specialization. It demonstrates secure input handling, ASP.NET Core Identity, JWT authentication, role-based access control (RBAC), Entity Framework Core, and automated security testing.

## 1. Requirements

- .NET 8 SDK
- Git
- GitHub account
- Visual Studio 2022 or VS Code (optional)

## 2. Configure the JWT Signing Key

The application requires a JWT signing key of at least 32 bytes. Never commit a real signing key to GitHub.

For local development, initialize User Secrets:

```powershell
dotnet user-secrets init --project SafeVault.Api/SafeVault.Api.csproj
```

Set a development signing key:

```powershell
dotnet user-secrets set "Jwt:Key" "replace-with-a-random-secret-at-least-32-bytes-long" --project SafeVault.Api/SafeVault.Api.csproj
```

Replace the example value with a randomly generated secret. User Secrets are intended for development, not production secret management.

## 3. Build and Run

Run the following commands separately from the repository root:

Restore dependencies:

```powershell
dotnet restore SafeVault.Api/SafeVault.Api.csproj
```

Build the API:

```powershell
dotnet build SafeVault.Api/SafeVault.Api.csproj
```

Start the API:

```powershell
dotnet run --project SafeVault.Api/SafeVault.Api.csproj
```

The application uses SQLite and initializes its database schema at startup.

## 4. API Endpoints

| Method | Endpoint | Purpose | Access |
|---|---|---|---|
| POST | `/api/auth/register` | Register a new account | Public |
| POST | `/api/auth/login` | Authenticate and obtain a JWT | Public |
| GET | `/api/notes` | Retrieve the authenticated user's notes | Authenticated |
| POST | `/api/notes` | Create a note | Authenticated |
| GET | `/api/admin/users` | List registered users | Admin role |

Example registration request:

```json
{
  "email": "user@example.com",
  "password": "StrongPassword123!"
}
```

Example note request:

```json
{
  "content": "My first SafeVault note"
}
```

For protected endpoints, send the access token in the HTTP authorization header:

```text
Authorization: Bearer <access-token>
```

Newly registered users are assigned the `User` role. Public registration must never allow users to grant themselves administrative privileges.

## 5. Run Security Tests

Restore the test project:

```powershell
dotnet restore SafeVault.Tests/SafeVault.Tests.csproj
```

Build the test project:

```powershell
dotnet build SafeVault.Tests/SafeVault.Tests.csproj
```

Execute the tests:

```powershell
dotnet test SafeVault.Tests/SafeVault.Tests.csproj
```

Record the actual test results, including passed, failed, and skipped counts. Investigate failures before submitting the project.

## 6. Security Controls

- **Password security:** ASP.NET Core Identity manages password hashing and verification.
- **Input validation:** Registration and note endpoints validate user input.
- **SQL injection prevention:** Entity Framework Core uses parameterized database operations.
- **JWT authentication:** Token validation checks the signature, issuer, audience, and expiration.
- **Role-based authorization:** The administrative endpoint requires the `Admin` role.
- **User data isolation:** Notes are scoped to the authenticated user's identifier.
- **XSS mitigation:** User-provided note content is returned as JSON data. Any browser client must render untrusted content as text rather than injecting it with `innerHTML`.
- **Secret management:** JWT signing keys are supplied through configuration or User Secrets instead of being committed to source control.

## 7. Security Verification Summary

| Risk | Mitigation | Verification |
|---|---|---|
| SQL injection | Entity Framework Core parameterized operations | Injection-payload tests |
| Weak passwords | Identity password policy and input validation | Registration validation tests |
| Broken authentication | Identity login and JWT bearer validation | Authentication and token validation tests |
| Broken authorization | Admin-only endpoint and restricted role assignment | Verify ordinary users receive HTTP 403 |
| Cross-site scripting | JSON data responses and safe client-side rendering | XSS payload contract tests; browser rendering requires separate verification |
| Cross-user data access | Owner-scoped note queries | Note ownership isolation tests |
| Secret exposure | Configuration and User Secrets | Configuration review |

Only mark a control as verified if the relevant test or review has actually been completed.

## 8. Microsoft Copilot Usage

The assignment involves using Microsoft Copilot to assist with secure coding, authentication and authorization, and security debugging.

The `COPILOT_USAGE.md` file records the prompts used, findings, changes made, commands executed, and actual test outcomes. The `SECURITY_SUMMARY.md` file documents identified risks and mitigations.

Copilot suggestions must be reviewed, and test results must reflect actual execution rather than expected output.

## 9. Limitations and Future Improvements

This is an educational starter project, not a production-ready security solution.

Further work may include:

- Expanded integration tests using isolated test databases.
- Rate limiting and monitoring of authentication attempts.
- Secure secret management and HTTPS configuration for deployment.
- Database migrations and operational logging.
- Token revocation and a carefully designed refresh-token workflow.
- Controlled administrative account provisioning.
- CSRF protection if cookie-based authentication is introduced.
- Browser-level testing of XSS prevention in any consuming front end.

Review the implementation and run the complete test suite before submitting the public GitHub repository for peer review.
