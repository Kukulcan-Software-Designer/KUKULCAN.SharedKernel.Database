# Code Coverage

## Scope

The coverage target is the production assembly `KUKULCAN.SharedKernel.Database`.
Test assemblies are excluded from the report.

Coverage is evaluated primarily from the **unit-test coverage report** because unit tests are responsible for deterministic code-path and branch coverage.

The integration layer is split by provider: **PostgreSQL is the reference database management system (DBMS) for relational integration validation**, while a dedicated SQL Server integration project validates the Microsoft SQL Server provider path. Neither integration project defines the branch-coverage threshold.

## Current Coverage Baseline

The current coverage baseline is the result of the validated test strategy
using provider-specific integration suites for database-backed validation:

| Metric           |            Result  |
|------------------|-------------------:|
| Line coverage    | **100% (221/221)** |
| Branch coverage  | **97.36% (74/76)** |
| Reference DBMS   | **PostgreSQL**     |
| Additional DBMS  | **Microsoft SQL Server** |

All executable production lines are covered. All classes and methods in the
production assembly have executable line coverage, including:

All executable production lines are covered. The remaining two uncovered branches are intentional defensive provider-resolution branches.

## Why Branch Coverage Is 97.36%

The two uncovered branches belong to `KukulcanDbContextBase` and are the failure sides of the runtime type-resolution expressions used by `ConfigureSqlServer` and `ConfigurePostgresSql` when a required provider assembly cannot be resolved.

The supported unit-test environment references both provider packages, so those assemblies are present and the supported paths resolve successfully. Forcing assembly absence solely to reach 100% branch coverage would require an artificial runtime condition and would make the tests less representative of the supported configuration.

The provider error contract is still covered through unsupported-provider validation, reflection error handling and direct coverage of the `NotInstalled` error construction.

Therefore:

> **100% line coverage and 97.36% branch coverage are the accepted and reviewed coverage boundary for this module. PostgreSQL is the reference DBMS for database-backed validation and Microsoft SQL Server has its own dedicated integration suite.**

The project does not add artificial tests merely to raise the coverage percentage.

## Test Responsibilities

### Unit tests

`KUKULCAN.SharedKernel.Database.Tests` covers deterministic logic such as constructor argument validation, provider-selection errors, provider reflection/configuration logic, `UnitOfWork<TContext>` contracts and interceptor branches.

### PostgreSQL integration tests

`KUKULCAN.SharedKernel.Database.PostgreSQL.Integration` validates PostgreSQL connectivity, persistence, tenant isolation, model-cache isolation, auditing, soft delete, domain events, immutability, slow-query diagnostics and transaction/cancellation behavior.

### SQL Server integration tests

`KUKULCAN.SharedKernel.Database.SQLServer.Integration` validates Microsoft SQL Server provider configuration and real persistence, tenant isolation, model-cache behavior, interception, synchronous/asynchronous persistence, slow-query diagnostics, cancellation and `UnitOfWork<TContext>` transaction behavior.

Both integration projects use Testcontainers and own their database container lifecycle.

## Local Execution

Unit-test coverage:

```bash
dotnet test \
  Tests/KUKULCAN.SharedKernel.Database.Tests/KUKULCAN.SharedKernel.Database.Tests.csproj \
  --configuration Release \
  --settings Tests/KUKULCAN.SharedKernel.Database.Tests/coverage.runsettings \
  --logger "console;verbosity=normal" \
  --collect:"XPlat Code Coverage"
```

PostgreSQL integration tests:

```bash
dotnet test \
  Tests/KUKULCAN.SharedKernel.Database.PostgreSQL.Integration/KUKULCAN.SharedKernel.Database.PostgreSQL.Integration.csproj \
  --configuration Release
```

SQL Server integration tests:

```bash
dotnet test \
  Tests/KUKULCAN.SharedKernel.Database.SQLServer.Integration/KUKULCAN.SharedKernel.Database.SQLServer.Integration.csproj \
  --configuration Release
```

## Audit Rule

Coverage is considered complete only after the generated report has been inspected at class and method level. A successful test run alone does not prove complete coverage.
