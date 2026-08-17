# Soft Delete

## Purpose

The database layer supports logical deletion through the SharedKernel
`ISoftDelete` contract.

## Write Behavior

``` mermaid
flowchart TD
    D["Entity marked Deleted"] --> I["SoftDeleteInterceptor"]
    I --> M["State = Modified"]
    M --> F["IsDeleted = true"]
    F --> T["DeletedOn = clock.UtcNow"]
```

The database row remains physically present.

## Read Behavior

`ModelBuilderExtensions.ApplySoftDeleteFilter()` configures a global
query filter for entities implementing `ISoftDelete`.

Conceptually:

``` sql
WHERE IsDeleted = false
```

## Why Global Filters?

A global filter prevents individual queries from having to remember the
soft-delete predicate.

This reduces the risk of accidentally returning logically deleted
records.

## Trade-off

Global query filters are implicit behavior. Developers must understand
that a normal EF Core query is filtered unless the application
deliberately bypasses the filter.

The infrastructure therefore favors safety and consistency over maximum
query explicitness.
