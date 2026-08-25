# Code Coverage

## Scope

The coverage target is the production assembly `KUKULCAN.SharedKernel.Database`.
Test assemblies are excluded from the report.

Coverage is evaluated from two complementary sources:

- **Unit tests** provide deterministic line and branch coverage of the production library.
- **Provider-specific integration tests** validate the same infrastructure against each supported relational DBMS.

The supported providers are:

- Microsoft SQL Server
- PostgreSQL
- MySQL

## Current Unit-Test Coverage

The current `Code Coverage` workflow on `main` completed successfully after merging `TEST/Coverage`. The report was generated from **132/132 passing unit tests** on commit `59f0e1b6460d436e51e48cc3c585e5ad3975f724`.

| Metric | Result |
|---|---:|
| Line coverage | **96.68% (292/302)** |
| Branch coverage | **92.70% (89/96)** |

This is the current authoritative unit-coverage measurement for the production assembly.

### Class-level coverage

| Production class | Line coverage | Branch coverage |
|---|---:|---:|
| `KukulcanDbContextBase` | **95.42%** | **91.30%** |
| `SlowQueryInterceptor` | **92.00%** | **100%** |
| `DomainEventDispatchInterceptor` | **100%** | **50%** |
| `KukulcanDatabaseStartupInitializer<TContext>` | **100%** | **50%** |
| All remaining production classes | **100%** | **100%** |

The ten uncovered production lines are concentrated in two areas:

- `KukulcanDbContextBase`: lines **142, 166, 225, 227, 242-245**. These are provider-resolution/configuration defensive paths.
- `SlowQueryInterceptor`: lines **43-44**.

The branch report also contains partially covered decision points in `DomainEventDispatchInterceptor` and `KukulcanDatabaseStartupInitializer<TContext>`. Their executable lines are covered, but not every branch is exercised by the deterministic unit suite.

The coverage result should therefore not be interpreted as "100% API behavior covered". Integration tests remain necessary to validate provider-backed behavior, especially for SQL Server, PostgreSQL and MySQL.

No tests should be added solely to manufacture a 100% line or branch percentage through artificial reflection or assembly-loading scenarios unless those paths correspond to meaningful supported behavior.

## Integration-Test Coverage

Integration coverage must be measured independently for each real DBMS. The provider-specific integration workflows execute against real database engines and should be considered the authoritative source for provider-backed execution coverage.

At present, the repository does **not** publish consolidated provider-specific line/branch percentages in `COVERAGE.md`. Successful integration-test execution demonstrates behavioral coverage but is not itself a numerical coverage measurement.

| DBMS | Integration coverage |
|---|---:|
| Microsoft SQL Server | **Not currently published** |
| PostgreSQL | **Not currently published** |
| MySQL | **Not currently published** |

## Integration-Test Scope

The provider suites cover the following responsibilities:

| Responsibility | SQL Server | PostgreSQL | MySQL |
|---|:---:|:---:|:---:|
| Real provider selection and persistence | Yes | Yes | Yes |
| Tenant filtering/isolation | Yes | Yes | Yes |
| Tenant model-cache isolation | Yes | Yes | Yes |
| Audit interceptor | Yes | Yes | Yes |
| Soft delete | Yes | Yes | Yes |
| Domain-event dispatch | Yes | Yes | Yes |
| Immutable entity protection | Yes | Yes | Yes |
| Slow-query diagnostics | Yes | Yes | Yes |
| Cancellation behavior | Yes | Yes | Yes |
| Unit of Work transactions | Yes | Yes | Yes |
| Synchronous persistence paths | Yes | Yes | Yes |
| Asynchronous persistence paths | Yes | Yes | Yes |
| Provider-specific configuration | Yes | Yes | Yes |
| Real retry behavior | Yes | Yes | Yes |
| Real transaction commit/rollback/end semantics | Yes | Yes | Yes |
| Real migration/seed pipeline | Yes | Yes | Yes |

Tests are duplicated between providers only where provider-specific execution can change the result. Provider-independent behavior remains primarily covered by the deterministic unit suite.

## Test Adequacy Decision

The current unit suite provides high production-code coverage, but it does not replace integration verification. The combination of deterministic unit tests and real-provider integration tests is the intended coverage model for this project.

The remaining unit uncovered lines are mostly defensive provider-resolution and interceptor paths. They should only be targeted when a test can represent a meaningful supported scenario rather than merely increasing a percentage.

The provider-specific integration suites are particularly important for provider behavior that cannot be inferred from the unit suite, including real transactions, cancellation, retry execution, migrations/seed, tenant model-cache isolation and provider-specific command interception.

## CI Coverage Artifacts

The unit `Code Coverage` workflow generates a Cobertura artifact named `kukulcan-sharedkernel-database-coverage`.

Provider-specific integration workflows may generate their own test and coverage artifacts; those results should be reported separately by provider rather than merged into the deterministic unit-coverage percentage.
