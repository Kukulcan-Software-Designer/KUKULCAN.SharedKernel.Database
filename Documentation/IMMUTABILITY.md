# Persistence Immutability

## Purpose

`IImmutable` marks entities that are append-only after insertion.

## Enforcement

``` mermaid
flowchart TD
    E["Tracked IImmutable entity"] --> S{"State"}
    S -->|Added| OK["Allowed"]
    S -->|Unchanged| OK
    S -->|Modified| X["InvalidOperationException"]
    S -->|Deleted| X
```

## Why Persistence-Level Enforcement?

Immutability is particularly important at the persistence boundary
because EF Core can receive modifications from many different
application paths.

The interceptor provides a centralized enforcement mechanism.

## Allowed Operation

Insertion is allowed.

After insertion, updates and deletes are rejected.

## Consequence

The consuming application must treat immutable entities as append-only
records and create new records rather than modifying historical ones.
