# KUKULCAN.SharedKernel.Database — SharedKernel Migration

## Scope

This document records the migration of the former database infrastructure toward `KUKULCAN.SharedKernel`.

## Current Boundary

The database module consumes SharedKernel contracts including `IAuditable`, `ISoftDelete`, `IHasDomainEvents`, `IDomainEvent`, `IDomainEventDispatcher` and `IClock`.

Persistence-only contracts remain local: `IImmutable`, `ITenantContext` and `IUnitOfWork`.

## Behavior

- Audit timestamps are populated through `IAuditable` and `IClock`.
- Deletes of `ISoftDelete` entities become logical deletes and are filtered globally.
- Tenant isolation is applied to entities exposing a `Guid TenantId` property.
- Pending SharedKernel domain events are dispatched after successful persistence.
- `IImmutable` entities cannot be updated or deleted.

## Provider Strategy

The core package references EF Core but not provider-specific packages. Consumers provide SQL Server, PostgreSQL or another provider. Built-in provider selection currently recognizes SQL Server and PostgreSQL.

## Result

SharedKernel remains focused on reusable domain and cross-cutting contracts while this package owns EF Core, transactions, provider configuration and persistence conventions.
