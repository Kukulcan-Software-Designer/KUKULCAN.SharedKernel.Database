# Code Coverage

## Scope

The coverage target is the production assembly `KUKULCAN.SharedKernel.Database`.
Test assemblies are excluded from the report.

Coverage is evaluated primarily from the **unit-test coverage report** because
unit tests are responsible for deterministic code-path and branch coverage.
The integration suite has a different purpose: it verifies persistence behavior
against a real database provider and is not used to define the branch-coverage
threshold.

## Current Coverage Baseline

The current unit-test coverage report is:

| Metric | Result |
|---|---:|
| Line coverage | **100% (221/221)** |
| Branch coverage | **97.36% (74/76)** |

All executable production lines are covered. All classes and methods in the
production assembly have executable line coverage, including:

- `KukulcanDbContextBase`;
- `TenantModelCacheKeyFactory`;
- `UnitOfWork<TContext>`;
- `AuditSaveChangesInterceptor`;
- `DomainEventDispatchInterceptor`;
- `ImmutableEntityInterceptor`;
- `SlowQueryInterceptor`;
- `SoftDeleteInterceptor`;
- `ModelBuilderExtensions`;
- `ServiceCollectionExtensions`.

The remaining two uncovered branches are intentional and do not represent
unsupported behavior left untested.

## Why Branch Coverage Is 97.36%

The two uncovered branches belong to `KukulcanDbContextBase` and are the
failure sides of the runtime type-resolution expressions used by
`ConfigureSqlServer` and `ConfigurePostgresSql`:

```csharp
Type.GetType("...Microsoft.EntityFrameworkCore.SqlServer")
    ?? throw NotInstalled("Microsoft.EntityFrameworkCore.SqlServer");

Type.GetType("...Npgsql.EntityFrameworkCore.PostgreSQL")
    ?? throw NotInstalled("Npgsql.EntityFrameworkCore.PostgreSQL");
```

The consuming test project references both provider packages. Therefore, in
the supported test environment, the corresponding EF Core provider assemblies
are present and `Type.GetType(...)` resolves successfully. The `null`
branches cannot be reached naturally without first creating an environment in
which a required provider assembly is deliberately made unavailable.

Forcing those branches solely to obtain a numerical 100% branch-coverage value
would require techniques such as manipulating assembly loading, introducing a
production-only seam, or otherwise changing the runtime environment for the
purpose of the test. That would make the tests less deterministic and less
representative of the supported configuration.

The provider error contract is nevertheless covered. `ConfigureProvider`
rejects unsupported providers, `InvokeProviderUseMethod` covers the missing
compatible reflection method path, and `NotInstalled` is exercised directly,
including both forms of its exception construction.

The decision is therefore deliberate:

> **100% line coverage and 97.36% branch coverage are the accepted and reviewed
> coverage boundary for this module. The remaining two branches are defensive
> provider-resolution branches whose natural execution would require an
> unsupported test environment.**

The project does not add artificial tests merely to raise the coverage
percentage.

## Unit Tests vs. Integration Tests

The two test layers have different responsibilities.

### Unit tests

Unit tests cover deterministic logic without requiring PostgreSQL, including:

- constructor argument validation;
- provider-selection errors;
- provider reflection and configuration logic;
- `UnitOfWork<TContext>` contracts;
- interceptor branches and synchronous/asynchronous entry points;
- model-builder behavior that can be validated without a real database.

### Integration tests

Integration tests validate behavior that depends on a real relational provider,
including:

- PostgreSQL connectivity and persistence;
- tenant isolation against real database rows;
- model-cache isolation across tenants;
- audit and soft-delete persistence;
- domain-event dispatch after successful persistence;
- immutable-entity enforcement against PostgreSQL;
- slow-query diagnostics against real database commands;
- transaction, cancellation and rollback behavior.

The integration suite is intentionally not used as a coverage threshold. A
real database test can execute production code while still leaving defensive
reflection branches unobservable under the supported runtime configuration.

## Local execution

Run the complete NUnit unit-test suite with the explicit coverage configuration:

```bash
dotnet test \
  Tests/KUKULCAN.SharedKernel.Database.Tests/KUKULCAN.SharedKernel.Database.Tests.csproj \
  --configuration Release \
  --settings Tests/KUKULCAN.SharedKernel.Database.Tests/coverage.runsettings \
  --logger "console;verbosity=normal" \
  --collect:"XPlat Code Coverage"
```

The generated Cobertura report is written below `TestResults/` for the test run.

## Audit Rule

Coverage is considered complete only after the generated report has been
inspected at class and method level. A successful test run alone does not prove
complete coverage.

The audit must verify, at minimum:

- `KukulcanDbContextBase` persistence, provider and model-building branches;
- tenant model cache key behavior;
- service registration and missing-configuration guards;
- soft-delete, audit, immutable-entity and domain-event interceptors;
- slow-query interceptor synchronous and asynchronous paths;
- unit-of-work transaction success, failure and cancellation paths;
- public option/configuration behavior.

Interface-only contracts, global usings and build targets are not expected to
contribute executable production coverage.
