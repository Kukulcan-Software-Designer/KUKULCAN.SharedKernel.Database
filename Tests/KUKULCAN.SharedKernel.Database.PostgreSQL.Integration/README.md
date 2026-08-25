# KUKULCAN.SharedKernel.Database.PostgreSQL.Integration

Integration tests for `KUKULCAN.SharedKernel.Database` against a real PostgreSQL instance.

## Test infrastructure

The suite uses Testcontainers to provision PostgreSQL for the test run. The container lifecycle is owned by the test fixture, so the project does not require an externally provisioned PostgreSQL service.

## Running locally

```bash
dotnet test Tests/KUKULCAN.SharedKernel.Database.PostgreSQL.Integration/KUKULCAN.SharedKernel.Database.PostgreSQL.Integration.csproj --configuration Release
```

Coverage settings can be supplied with `coverage.runsettings` when a coverage report is required.
