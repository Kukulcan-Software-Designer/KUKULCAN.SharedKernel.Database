# KUKULCAN.SharedKernel.Database.SQLServer.Integration

Integration tests for `KUKULCAN.SharedKernel.Database` against Microsoft SQL Server.

## Test infrastructure

The suite uses Testcontainers to provision SQL Server for the test run. The container lifecycle is owned by the test fixture, so the project does not require an externally provisioned SQL Server service.

## Running locally

```bash
dotnet test Tests/KUKULCAN.SharedKernel.Database.SQLServer.Integration/KUKULCAN.SharedKernel.Database.SQLServer.Integration.csproj --configuration Release
```

Coverage settings can be supplied with `coverage.runsettings` when a coverage report is required.
