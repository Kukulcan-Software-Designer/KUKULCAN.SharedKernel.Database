# Changelog

All notable changes to `KUKULCAN.SharedKernel.Database` are documented here. The project follows Semantic Versioning.

## [Unreleased]

### Testing

- Split provider-backed integration validation into two independent test projects:
  - `KUKULCAN.SharedKernel.Database.PostgreSQL.Integration` for PostgreSQL.
  - `KUKULCAN.SharedKernel.Database.SQLServer.Integration` for Microsoft SQL Server.
- Updated the solution, CI workflows and testing documentation to execute and describe both provider-specific integration suites independently.

## [1.0.0]

### Added

- `KukulcanDbContextBase`.
- `DatabaseProvider` and `KukulcanDatabaseOptions`.
- `IImmutable`, `ITenantContext` and `IUnitOfWork`.
- Soft-delete and tenant model-builder conventions.
- `AddKukulcanDbContext<TContext>`.
- Audit, soft-delete, domain-event, immutable-entity and slow-query interceptors.
- `UnitOfWork<TContext>` with explicit transaction lifecycle support.

### Architecture

- Migrated database infrastructure to consume `KUKULCAN.SharedKernel` contracts.
- Kept persistence-only contracts inside the database module.
- Kept provider packages as consumer responsibilities.
