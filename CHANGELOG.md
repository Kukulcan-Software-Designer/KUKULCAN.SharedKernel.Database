# Changelog

All notable changes to `KUKULCAN.SharedKernel.Database` are documented here. The project follows Semantic Versioning.

## [Unreleased]

No unreleased changes are currently documented.

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
