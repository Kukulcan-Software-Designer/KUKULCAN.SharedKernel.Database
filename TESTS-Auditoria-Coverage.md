# KUKULCAN.SharedKernel.Database — Test Coverage Audit

## Scope

This audit covers only `Source/KUKULCAN.SharedKernel.Database` and its test project. `KUKULCAN.SharedKernel` is not modified by this branch.

## Baseline

The existing suite contained 48 tests and was already passing before this audit.

## Coverage matrix

| Production area | Existing coverage | Audit additions |
|---|---:|---:|
| `Abstractions` | Contract coverage | No gap requiring additional behavior tests |
| `Configuration/DatabaseProvider` | Complete enum contract | No additional behavior required |
| `Configuration/KukulcanDatabaseOptions` | Defaults + mutability | No additional behavior required |
| `Extensions/ModelBuilderExtensions` | Soft-delete + tenant filters | Nulls, wrong tenant type, owned entities, entities without `TenantId` |
| `Extensions/ServiceCollectionExtensions` | Validation + registration | Options binding, lifetimes, actual slow-query interceptor attachment |
| `Interceptors/AuditSaveChangesInterceptor` | Added + async modified | Sync modified, unchanged, deleted |
| `Interceptors/DomainEventDispatchInterceptor` | Sync + async dispatch | Existing contract considered sufficient for current implementation |
| `Interceptors/ImmutableEntityInterceptor` | Add + modified + sync delete | Sync modified, async delete, no-violation path |
| `Interceptors/SlowQueryInterceptor` | Reader paths | Async reader, sync/async non-query, threshold boundary |
| `KukulcanDbContextBase` | Constructors + filters | Preconfigured options, sensitive/detailed errors, unsupported provider, combined filters |
| `TenantModelCacheKeyFactory` | No direct tests | Tenant-specific model isolation and model distinction |
| `UnitOfWork` | Main transaction lifecycle | Sync dispose, cancellation cleanup, idempotent dispose |
| Provider configuration | Not covered | SQL Server/PostgreSQL provider, command timeout, retry strategy |

## Audit findings fixed in this branch

### 1. Tenant model cache isolation

`TenantModelCacheKeyFactory` now has explicit integration coverage proving that two contexts of the same type with different tenants receive independent models and only see their own tenant rows.

### 2. Relational provider reflection

The previous reflection code looked for an incompatible `Action<object>` parameter. SQL Server and PostgreSQL actually expose provider-specific option-builder delegate types. The audit added real provider-package tests and corrected the reflection bridge to create the required strongly typed delegate dynamically.

### 3. Slow-query interceptor registration

`SlowQueryInterceptor` was registered in DI but was not attached to the `DbContext` options. The audit added an integration assertion for the actual EF Core interceptor collection and registered the singleton through `ConfigureDbContext`, preserving provider selection in `KukulcanDbContextBase`.

## Current expected suite size

The baseline 48 tests have been expanded by approximately 29 scenarios, for an expected total of **77 tests** once the branch is built and executed locally.

## Validation requirement

The branch is not considered frozen until the complete test project is executed locally in Release configuration and reports zero failures.

Recommended command:

```bash
dotnet test Tests/KUKULCAN.SharedKernel.Database.Tests/KUKULCAN.SharedKernel.Database.Tests.csproj \
  --configuration Release \
  --logger "console;verbosity=normal" \
  --collect:"XPlat Code Coverage"
```

The coverage artifact should then be inspected to identify any remaining uncovered production branches before the final `FROZEN` decision.
