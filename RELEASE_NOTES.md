# Release Notes

## KUKULCAN.SharedKernel.Database 1.0.0

### Overview

This release establishes `KUKULCAN.SharedKernel.Database` as shared EF Core persistence infrastructure built on `KUKULCAN.SharedKernel`.

### Highlights

- `KukulcanDbContextBase` centralizes cross-cutting persistence configuration.
- SQL Server and PostgreSQL provider selection is supported without embedding provider packages.
- Audit, soft delete, tenant isolation, domain-event dispatch and immutable-entity enforcement are centralized.
- `IUnitOfWork` and `UnitOfWork<TContext>` provide explicit transaction management.
- `SlowQueryInterceptor` provides configurable slow-command diagnostics.
- Provider-backed integration validation is split into dedicated PostgreSQL and Microsoft SQL Server test projects, allowing each provider suite to run independently through Testcontainers and CI.

### Known Limitations

- Provider-specific packages are supplied by consumers.
- Tenant filtering recognizes a `Guid` property named `TenantId`.
- Migration and seed flags are configuration data; this package does not execute application startup migration workflows itself.
