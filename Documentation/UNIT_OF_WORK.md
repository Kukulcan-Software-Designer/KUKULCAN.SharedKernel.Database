# Unit of Work

## Purpose

`IUnitOfWork` defines the persistence operations required by the
database infrastructure.

`UnitOfWork<TContext>` provides the implementation.

## Operations

``` text
SaveChangesAsync
BeginTransactionAsync
CommitTransactionAsync
RollbackTransactionAsync
EndTransactionAsync
Dispose
DisposeAsync
```

## Transaction Lifecycle

``` mermaid
stateDiagram-v2
    [*] --> NoTransaction
    NoTransaction --> Active: BeginTransactionAsync
    Active --> NoTransaction: CommitTransactionAsync
    Active --> NoTransaction: RollbackTransactionAsync
    Active --> NoTransaction: EndTransactionAsync
```

## Commit

Commit performs:

1.  `SaveChangesAsync`;
2.  transaction commit;
3.  transaction disposal.

If saving or committing fails, the transaction is still disposed and
cleared.

## Rollback

Rollback performs:

1.  transaction rollback;
2.  transaction disposal;
3.  transaction reference clearing.

## Why a Unit of Work?

The abstraction gives application code a stable persistence contract
without requiring it to know the concrete `DbContext` implementation.

It also provides an explicit place for transaction lifecycle management.

## Limitations

The current implementation is intentionally small. It does not provide:

-   repositories;
-   distributed transactions;
-   nested transaction abstractions;
-   savepoints;
-   outbox coordination.

Those concerns belong to higher-level infrastructure when actually
required.
