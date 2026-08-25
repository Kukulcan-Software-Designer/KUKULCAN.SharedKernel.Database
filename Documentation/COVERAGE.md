# Code Coverage

## Scope

The coverage target is the production assembly `KUKULCAN.SharedKernel.Database`.
Test assemblies are excluded from the report.

Coverage is evaluated from two complementary sources:

- **Unit tests** provide deterministic line and branch coverage of the production library.
- **Provider-specific integration tests** validate that the same infrastructure behaves correctly against each supported relational DBMS.

The supported providers are:

- Microsoft SQL Server
- PostgreSQL
- MySQL

## Current Unit-Test Baseline

The latest successful `Code Coverage` workflow on `main` was inspected before this coverage-completion branch was created. Its Cobertura artifact reports:

| Metric | Baseline |
|---|---:|
| Line coverage | **94.78% (218/230)** |
| Branch coverage | **97.29% (72/74)** |

The baseline gap is not caused by untested interceptor or Unit of Work behavior. It is concentrated in the provider-resolution path added for MySQL support.

### Identified unit-test gaps

`KukulcanDbContextBase` was the only production class below complete line coverage:

- `ConfigureMySql(...)` was not exercised by the unit-test project.
- The `DatabaseProvider.MySql` switch arm was therefore not covered.
- The defensive `Assembly.GetType(...) ?? throw ...` path in `LoadProviderExtensionType(...)` was not covered.

The branch `TEST/CoverageCompleted` adds the missing MySQL provider package to the unit-test project and adds focused tests for:

1. Successful MySQL provider configuration.
2. MySQL provider configuration failure wrapping.
3. Missing provider extension type handling.

No additional unit tests are required for the already fully covered interceptors, filters, tenant model cache or Unit of Work paths.

## Integration-Test Coverage Strategy

Integration tests are not expected to replace unit-test branch coverage. Their purpose is to prove provider-specific behavior against a real database engine.

The three provider suites cover the following responsibilities:

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

PostgreSQL contains additional provider-independent regression scenarios and remains the broadest relational reference suite. That does not imply that SQL Server or MySQL need copies of every PostgreSQL test: tests should be duplicated only when they validate behavior that can differ at the provider boundary.

### Integration gaps requiring no additional test cases

The current SQL Server, PostgreSQL and MySQL suites already exercise the important provider boundary behaviors. No additional integration scenario was identified as necessary solely for increasing confidence in the current production implementation.

In particular, adding duplicate tests for every PostgreSQL scenario to SQL Server and MySQL would increase maintenance without adding meaningful provider coverage where the tested behavior is provider-independent.

## Integration Coverage Reporting

The integration projects already contain `coverage.runsettings`, but the integration workflow previously executed them without collecting coverage. The coverage-completion branch changes the workflow so each DBMS job now runs with `XPlat Code Coverage` and uploads an independent Cobertura artifact:

- `kukulcan-sharedkernel-database-postgresql-coverage`
- `kukulcan-sharedkernel-database-sqlserver-coverage`
- `kukulcan-sharedkernel-database-mysql-coverage`

This allows the integration suites to be audited quantitatively per provider instead of relying only on successful test execution.

## Acceptance Criteria for This Branch

The branch is considered coverage-complete after CI confirms:

1. The unit-test project passes with the new MySQL provider tests.
2. Unit line coverage reaches **100%**.
3. Unit branch coverage reaches **100%**, unless the resulting report identifies a genuinely artificial defensive branch that should remain intentionally uncovered.
4. PostgreSQL integration tests pass.
5. Microsoft SQL Server integration tests pass.
6. MySQL integration tests pass.
7. Each integration job produces a Cobertura artifact that can be inspected at class and method level.

A successful test run alone does not prove complete coverage; the generated Cobertura reports must be inspected before the branch is frozen.
