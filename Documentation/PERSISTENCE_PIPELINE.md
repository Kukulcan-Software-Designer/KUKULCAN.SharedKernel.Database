# Persistence Pipeline

## Overview

The persistence pipeline is centered on EF Core `SaveChanges` interception.

``` mermaid
sequenceDiagram
    participant App as Application
    participant Db as DbContext
    participant Slow as Slow Query Interceptor
    participant Soft as Soft Delete Interceptor
    participant Audit as Audit Interceptor
    participant Imm as Immutable Interceptor
    participant EF as EF Core
    participant Events as Domain Event Interceptor
    participant Dispatcher as Domain Event Dispatcher

    App->>Db: SaveChanges()
    Db->>Slow: command diagnostics
    Slow->>Soft: continue
    Soft->>Audit: continue
    Audit->>Imm: continue
    Imm->>EF: continue
    EF-->>Db: persistence succeeds
    Db->>Events: SavedChanges
    Events->>Dispatcher: DispatchAsync(event)
```

## Interceptor Ordering

`KukulcanDbContextBase.OnConfiguring` registers the persistence interceptors in this order:

1. `SlowQueryInterceptor`, when supplied;
2. `SoftDeleteInterceptor`;
3. `AuditSaveChangesInterceptor`;
4. `DomainEventDispatchInterceptor`;
5. `ImmutableEntityInterceptor`.

The ordering is intentional. In particular, soft delete must run before audit so that a physical delete converted to `Modified` state can receive the appropriate `ModifiedOn` timestamp.

## Audit Stage

Added entities receive `CreatedOn`.

Modified entities receive `ModifiedOn`.

The interceptor uses `IClock`, which avoids coupling audit timestamps to system time and improves testability.

## Soft Delete Stage

A physical delete is converted into a modification:

```text
Deleted
  ↓
Modified
  ↓
IsDeleted = true
DeletedOn = current UTC time
```

This stage runs before audit so the logical delete is treated as a modification by the audit interceptor.

## Immutable Stage

Entities implementing `IImmutable` cannot be:

- modified;
- deleted.

Insertion remains allowed.

## Persistence Stage

After the SaveChanges interceptors have prepared the tracked state, EF Core persists the changes to the configured provider.

## Domain Event Stage

Domain events are dispatched from the `SavedChanges` callbacks after the database save operation has reported success.

The synchronous `SavedChanges` path invokes the same asynchronous dispatch logic and blocks only at that interceptor boundary. The asynchronous `SavedChangesAsync` path awaits the dispatcher directly.

## Event Collection and Clearing

Events are collected from tracked `IHasDomainEvents` aggregates.

The events are then:

1. copied into a separate list;
2. cleared from their aggregates;
3. dispatched sequentially.

Clearing occurs before dispatch so that an aggregate remaining tracked after the save does not retain already-collected events.

## No Outbox

The current project does not implement an outbox mechanism.

Therefore, dispatch reliability across process boundaries remains a responsibility of the consuming application architecture.
