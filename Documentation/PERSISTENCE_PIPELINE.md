# Persistence Pipeline

## Overview

The persistence pipeline is centered on EF Core `SaveChanges`
interception.

``` mermaid
sequenceDiagram
    participant App as Application
    participant Db as DbContext
    participant Audit as Audit Interceptor
    participant Soft as Soft Delete Interceptor
    participant Imm as Immutable Interceptor
    participant EF as EF Core
    participant Events as Domain Event Interceptor
    participant Dispatcher as Domain Event Dispatcher

    App->>Db: SaveChanges()
    Db->>Audit: SavingChanges
    Audit->>Soft: continue
    Soft->>Imm: continue
    Imm->>EF: continue
    EF-->>Db: persistence succeeds
    Db->>Events: SavedChanges
    Events->>Dispatcher: DispatchAsync(event)
```

## Audit Stage

Added entities receive `CreatedOn`.

Modified entities receive `ModifiedOn`.

The interceptor uses `IClock`, which avoids coupling audit timestamps to
system time and improves testability.

## Soft Delete Stage

A physical delete is converted into a modification:

``` text
Deleted
  ↓
Modified
  ↓
IsDeleted = true
DeletedOn = current UTC time
```

## Immutable Stage

Entities implementing `IImmutable` cannot be:

-   modified;
-   deleted.

Insertion remains allowed.

## Persistence Stage

After the interceptor stages complete, EF Core persists the tracked
changes.

## Domain Event Stage

Domain events are dispatched only from the `SavedChanges` callbacks.

This means the event dispatch stage occurs after the database save
operation has reported success.

## Important Consideration

The event interceptor clears pending events before dispatching them.

This avoids duplicate dispatch if the same aggregate remains tracked,
but it also means dispatch failure semantics must be considered by the
consuming application.

The library deliberately does not implement an outbox mechanism.
