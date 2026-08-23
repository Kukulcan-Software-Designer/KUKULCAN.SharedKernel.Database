# Code Coverage

## Scope

The coverage target is the production assembly `KUKULCAN.SharedKernel.Database`.
Test assemblies are excluded from the report.

## Local execution

Run the complete NUnit suite with the explicit coverage configuration:

```bash
dotnet test \
  Tests/KUKULCAN.SharedKernel.Database.Tests/KUKULCAN.SharedKernel.Database.Tests.csproj \
  --configuration Release \
  --settings Tests/KUKULCAN.SharedKernel.Database.Tests/coverage.runsettings \
  --logger "console;verbosity=normal" \
  --collect:"XPlat Code Coverage"
```

The generated Cobertura report is written below `TestResults/` for the test run.

## Audit rule

Coverage is considered complete only after the generated report has been inspected at class and method level. A successful test run alone does not prove complete coverage.

The audit must verify, at minimum:

- `KukulcanDbContextBase` persistence and model-building branches;
- tenant model cache key behavior;
- provider configuration branches;
- service registration and missing-configuration guards;
- soft-delete, audit, immutable-entity and domain-event interceptors;
- slow-query interceptor synchronous and asynchronous paths;
- unit-of-work transaction success, failure and cancellation paths;
- public option/configuration behavior.

Interface-only contracts, global usings and build targets are not expected to contribute executable production coverage.
