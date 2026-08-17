# Testing Strategy

## Testing Philosophy

Tests for `KUKULCAN.SharedKernel.Database` should verify actual
persistence behavior rather than create artificial coverage.

The most valuable tests exercise:

-   EF Core model metadata;
-   query filters;
-   SaveChanges interception;
-   transaction behavior;
-   dependency injection registration;
-   domain event dispatch;
-   audit timestamp updates;
-   immutable entity enforcement;
-   slow-query logging.

## Recommended Test Layers

``` mermaid
flowchart TD
    U["Unit tests"] --> I["Infrastructure integration tests"]
    I --> EF["EF Core model / ChangeTracker"]
    EF --> DB["Provider-specific tests when required"]
```

## Unit-Level Tests

Pure configuration and guard clauses can use lightweight tests.

Examples:

-   null argument validation;
-   option defaults;
-   enum values;
-   service registration;
-   extension method return identity.

## EF Core Tests

Behavior depending on ChangeTracker or model metadata should use a real
EF Core model.

Examples:

-   soft-delete filters;
-   tenant filters;
-   immutable interception;
-   auditing;
-   domain events.

## SQLite

SQLite in-memory is useful for transaction behavior because EF Core's
in-memory provider does not model relational transactions faithfully.

## Provider Tests

SQL Server and PostgreSQL-specific behavior should be tested in
provider-aware test environments when the provider packages are actually
consumed.

The shared library should not pretend that provider-specific behavior is
completely equivalent across engines.

## Obsolete API Policy

Tests should use the current EF Core metadata APIs.

For example:

``` csharp
GetDeclaredQueryFilters()
```

should be preferred over obsolete:

``` csharp
GetQueryFilter()
```

This keeps the test suite aligned with EF Core 10.
