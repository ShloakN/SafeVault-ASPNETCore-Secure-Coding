# SafeVault Security Summary

## Vulnerabilities addressed
- SQL injection from unsafe query construction.
- Weak or malformed registration input.
- Authentication bypass and unprotected API endpoints.
- Privilege escalation through self-selected admin roles.
- Cross-site scripting risks when user-provided text is rendered as HTML.
- JWT token acceptance without complete issuer, audience, signature, or expiry validation.

## Fixes in this project
- Uses ASP.NET Core Identity for password hashing and user management.
- Applies password requirements and validates email/password input.
- Uses JWT bearer authentication with signature, issuer, audience, and lifetime validation.
- Protects private endpoints with authentication and restricts user listing to the Admin role.
- Assigns all self-registered accounts the least-privileged User role.
- Uses Entity Framework Core for parameterized database operations.
- Restricts notes queries to the authenticated user's ID.
- Returns note content as JSON data; any front end must render untrusted content as text.

## Automated test coverage
The xUnit integration suite uses a named, in-memory SQLite database isolated to its test application instance. It covers invalid and missing authentication input, malformed JSON, oversized and missing note content, SQL-injection-shaped login input, missing/invalid/expired JWTs, issuer/audience/signature validation, role-based admin access, registration role escalation, JSON/XSS response behavior, note ownership isolation, and Identity password hashing.

The XSS test verifies that user content is returned as JSON data unchanged. It does not execute a browser: browser clients must display untrusted note content as text and must not pass it to `innerHTML` or another HTML execution sink.

## Verification status
Using .NET SDK 8.0.426 via `C:\Program Files\dotnet\dotnet.exe`, restore, build, and test succeeded when explicitly targeting `SafeVault.Tests\SafeVault.Tests.csproj` (which references and builds the API): restore completed for both projects; build completed with 0 warnings and 0 errors; tests reported 24 passed, 0 failed, and 0 skipped. The bare `dotnet` command is not on this PowerShell process's `PATH`. Additionally, this SDK/MSBuild does not implicitly select or recognize the repository's `.slnx` as a solution, so use the explicit test project path or run with an SDK version that supports `.slnx` implicit solution handling.
