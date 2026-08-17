# KUKULCAN.SharedKernel.Database.Client

## Overview

`KUKULCAN.SharedKernel.Database.Client` is an interactive console application that demonstrates how to consume and exercise the public infrastructure provided by `KUKULCAN.SharedKernel.Database`.

The client is intentionally implemented as a **consumer application**, rather than as another part of the database infrastructure. Its purpose is to provide a concrete, executable example of how an application can configure the database module, compose its dependencies, create a concrete `DbContext`, and observe the behavior of the persistence cross-cutting concerns.

The client targets **.NET 10** and uses **Entity Framework Core 10**.

It is not a replacement for the automated NUnit test project. The automated tests verify behavior deterministically; this client provides an interactive environment in which the behavior can be observed through a real EF Core context and a configured database provider.

---

## Purpose

The project has four main purposes:

1. Demonstrate the intended public consumption model of `KUKULCAN.SharedKernel.Database`.
2. Provide executable examples for the infrastructure features exposed by the library.
3. Make persistence behavior observable through an interactive console UI.
4. Provide a small reference application for developers integrating the library into a host or bounded context.

The client deliberately keeps its own domain model small. The entities under `Client/` exist only to demonstrate infrastructure behavior.

---

## Architectural Position

The client sits above the database infrastructure and consumes it.

```text
┌─────────────────────────────────────────────┐
│    KUKULCAN.SharedKernel.Database.Client    │
│                                             │
│  Interactive consumer / demonstration host  │
└──────────────────────┬──────────────────────┘
                       │ consumes
                       ▼
┌─────────────────────────────────────────────┐
│       KUKULCAN.SharedKernel.Database        │
│                                             │
│ EF Core infrastructure                      │
│ DbContext base / interceptors / UoW /       │
│ configuration / persistence extensions      │
└──────────────────────┬──────────────────────┘
                       │ consumes
                       ▼
┌─────────────────────────────────────────────┐
│            KUKULCAN.SharedKernel            │
│                                             │
│ Domain contracts and cross-cutting domain   │
│ abstractions                                │
└─────────────────────────────────────────────┘
```

The dependency direction is therefore:

```text
Client
  │
  ▼
SharedKernel.Database
  │
  ▼
SharedKernel
```

The client does **not** modify the architecture of the database library and does not introduce infrastructure abstractions back into the SharedKernel.

---

## Why a Separate Client Exists

The database library is infrastructure. Infrastructure behavior is often easier to understand when it is consumed by a real application instead of being inspected only through unit tests.

The client therefore complements the test project:

| Project | Purpose |
|---|---|
| `KUKULCAN.SharedKernel.Database` | Production persistence infrastructure |
| `KUKULCAN.SharedKernel.Database.Tests` | Automated behavioral verification |
| `KUKULCAN.SharedKernel.Database.Client` | Interactive consumption and demonstration |

The distinction is intentional.

Tests answer:

> Does the implementation behave correctly?

The client answers:

> How is the implementation consumed, configured and observed by an application?

---

## Features Demonstrated

The interactive client exposes the following areas.

### 1. Database Configuration

The client demonstrates `KukulcanDatabaseOptions` and displays the effective configuration, including:

- selected provider;
- command timeout;
- sensitive-data logging;
- detailed errors;
- retry configuration;
- connection-pool configuration;
- migration-related options.

The selected provider is supplied through the console UI.

---

### 2. Unit of Work

The client demonstrates:

- `UnitOfWork<TContext>`;
- asynchronous `SaveChangesAsync`;
- explicit transaction handling;
- commit;
- rollback;
- persistence through a concrete `DbContext`.

The demonstration uses `ClientProduct` entities so the persistence operation can be observed directly.

The client does not introduce repositories because `KUKULCAN.SharedKernel.Database` deliberately does not define a generic repository abstraction.

---

### 3. Audit Interceptor

`ClientProduct` derives from:

```csharp
AuditableEntity<ClientEntityId>
```

The client therefore provides a concrete consumer of the SharedKernel auditing contract.

The `AuditSaveChangesInterceptor` is responsible for persistence-time audit timestamps.

The demonstration allows the developer to observe:

- entity creation;
- population of `CreatedOn`;
- modification;
- population/update of `ModifiedOn`.

The audit timestamps are controlled by the persistence infrastructure rather than by application code manipulating the internal setters directly.

---

### 4. Soft Delete

`ClientProduct` implements:

```csharp
ISoftDelete
```

The client demonstrates the conversion of a normal EF Core delete operation into a logical deletion.

The persistence infrastructure handles:

- `IsDeleted`;
- `DeletedOn`;
- conversion of delete operations;
- global filtering of deleted records.

The client also provides a `Restore()` operation so the observable state can be demonstrated again.

---

### 5. Immutable Entities

The client includes a demonstration for `ImmutableEntityInterceptor`.

The purpose is to show that entities marked through the SharedKernel immutability contract cannot be modified or deleted once persisted, according to the infrastructure rules.

The client intentionally does not bypass the interceptor.

---

### 6. Domain Events

`ClientOrder` derives from the SharedKernel aggregate/entity infrastructure and exposes a `Place()` operation that registers:

```csharp
OrderPlacedEvent
```

The event is dispatched through:

```csharp
IDomainEventDispatcher
```

The client provides:

```csharp
ConsoleDomainEventDispatcher
```

which writes the dispatched event and its public properties to the console.

This makes the persistence-to-domain-event pipeline observable.

```text
ClientOrder.Place()
        │
        ▼
Domain event registered
        │
        ▼
SaveChangesAsync()
        │
        ▼
DomainEventDispatchInterceptor
        │
        ▼
IDomainEventDispatcher
        │
        ▼
ConsoleDomainEventDispatcher
        │
        ▼
Console output
```

---

### 7. Slow Query Diagnostics

The client registers:

```csharp
SlowQueryInterceptor
```

and configures the threshold through the application configuration.

The purpose is to demonstrate how slow database commands can be observed without embedding diagnostic logic into application entities.

The client uses the normal EF Core interception mechanism provided by the database infrastructure.

---

### 8. Soft-Delete Global Filter

The client exposes the global soft-delete filter separately from the interceptor demonstration.

This distinction is important:

```text
Delete operation
     │
     ▼
SoftDeleteInterceptor
     │
     ▼
IsDeleted = true
DeletedOn = timestamp
     │
     ▼
Global query filter
     │
     ▼
Deleted rows excluded from normal queries
```

The interceptor changes persistence state; the global filter changes normal query visibility.

---

### 9. Tenant Filter

The client provides a concrete:

```csharp
ITenantContext
```

implementation through:

```csharp
ConsoleTenantContext
```

and demonstrates tenant-aware persistence filtering.

The current tenant is represented by a `Guid`.

The client therefore demonstrates the intended separation:

```text
Application / Host
        │
        ▼
ITenantContext
        │
        ▼
SharedKernel.Database
        │
        ▼
EF Core global tenant filter
```

Tenant awareness remains a persistence concern and is not added to SharedKernel merely because EF Core needs it.

---

## Client Entities

The client deliberately uses a small set of demonstration entities.

### `ClientEntityId`

A concrete `GuidEntityId` implementation used by the client entities.

It exists only because the SharedKernel identifier abstraction is intentionally abstract and applications are expected to provide their concrete identifier types.

---

### `ClientProduct`

Demonstrates:

- `AuditableEntity<TId>`;
- `ISoftDelete`;
- identifier conversion;
- EF Core persistence;
- update behavior;
- soft deletion;
- restoration.

Main properties include:

```text
Name
Price
Category
IsDeleted
DeletedOn
```

---

### `ClientOrder`

Demonstrates:

- SharedKernel entity/aggregate behavior;
- auditing;
- domain-event registration;
- persistence of an entity whose domain-event collection is ignored by EF Core.

Calling:

```csharp
Place()
```

registers an `OrderPlacedEvent`.

---

### `OrderPlacedEvent`

A concrete SharedKernel domain event used to demonstrate dispatch after successful persistence.

The event contains:

- `OrderId`;
- `OrderNumber`;
- `TotalAmount`;
- `OccurredOn`.

---

### `DemoAuditLog`

A simple persistence entity used by the client to make audit-related demonstrations observable.

---

### `DemoTenantDocument`

A simple tenant-aware persistence entity used to demonstrate tenant filtering.

---

## Dependency Injection

The client builds a normal `Microsoft.Extensions.DependencyInjection` container.

The important registrations include:

```csharp
services.AddSingleton<ConsoleCurrentUser>();
services.AddSingleton<ConsoleTenantContext>();
services.AddSingleton<ITenantContext>(
    sp => sp.GetRequiredService<ConsoleTenantContext>());

services.AddSingleton<ConsoleDateTimeProvider>();
services.AddSingleton<IClock>(
    sp => sp.GetRequiredService<ConsoleDateTimeProvider>());

services.AddSingleton<ConsoleDomainEventDispatcher>();
services.AddSingleton<IDomainEventDispatcher>(
    sp => sp.GetRequiredService<ConsoleDomainEventDispatcher>());

services.AddSingleton<SlowQueryInterceptor>();

services.AddDbContext<ClientDbContext>();

services.AddScoped<UnitOfWork<ClientDbContext>>();
```

This is intentionally close to how a real application host would compose the database infrastructure.

---

## Database Provider Selection

The client supports the provider options exposed by the current database infrastructure.

The console application allows the user to select:

```text
PostgreSQL
SQL Server
```

The selected provider determines which connection string is taken from `appsettings.json`.

The database infrastructure currently exposes:

```csharp
DatabaseProvider.PostgresSql
DatabaseProvider.SqlServer
```

Provider-specific EF Core packages remain in the consuming project.

This keeps the core database library provider-independent while allowing an application to choose its concrete database technology.

---

## Configuration

The client reads:

```text
appsettings.json
```

and environment variables.

The relevant configuration sections include:

```text
Demo
Providers
Kukulcan:Database
```

A simplified configuration structure is:

```json
{
  "Demo": {
    "SlowQueryThresholdMs": 500
  },
  "Providers": {
    "PostgreSql": {
      "ConnectionString": "..."
    },
    "SqlServer": {
      "ConnectionString": "..."
    }
  },
  "Kukulcan": {
    "Database": {
      "Provider": "PostgreSql",
      "ConnectionString": "...",
      "CommandTimeoutSeconds": 30,
      "Retry": {
        "Enabled": true
      }
    }
  }
}
```

Connection strings should be replaced with environment-specific values before using the client against a real database.

---

## Database Initialization

The current client uses:

```csharp
db.Database.EnsureCreatedAsync(...)
```

to prepare the demonstration database.

This is appropriate for a demonstration application because the goal is to make the infrastructure immediately executable.

It should not be interpreted as a recommendation to replace migration-based database lifecycle management in production applications.

The client is a demonstration host, not a production database deployment mechanism.

---

## EF Core Context

`ClientDbContext` derives from:

```csharp
KukulcanDbContextBase
```

and demonstrates the expected extension model.

It exposes:

```csharp
DbSet<ClientProduct> Products
DbSet<DemoAuditLog> AuditLogs
DbSet<ClientOrder> Orders
DbSet<DemoTenantDocument> TenantDocuments
```

The context also configures the client entities and registers the slow-query interceptor.

The database schema used by the demo is:

```text
demo
```

---

## Console UI

The application uses **Spectre.Console** to provide an interactive menu.

The main menu exposes:

```text
1. Current configuration
2. Unit of Work
3. Audit interceptor
4. Soft-delete interceptor
5. Immutable entity interceptor
6. Domain-event interceptor
7. Slow-query interceptor
8. Soft-delete global filter
9. Tenant global filter
0. Exit
```

The UI exists only in the client project.

It is deliberately not part of `KUKULCAN.SharedKernel.Database`.

---

## Demonstration Stubs

Some infrastructure contracts require application-specific implementations.

The client provides simple console-oriented implementations for:

### `ConsoleCurrentUser`

Represents the current application user.

It can switch between:

- authenticated;
- unauthenticated.

---

### `ConsoleTenantContext`

Provides the current tenant identifier required by the database tenant-filter infrastructure.

---

### `ConsoleDateTimeProvider`

Provides deterministic/current time information required by persistence services.

---

### `ConsoleDomainEventDispatcher`

Implements:

```csharp
IDomainEventDispatcher
```

and writes dispatched events to the console.

This is deliberately simple. A production application would normally connect the dispatcher to its application messaging or domain-event handling infrastructure.

---

## Relationship With Automated Tests

The client does not duplicate the NUnit test suite.

The projects have different responsibilities:

```text
                         ┌───────────────────────┐
                         │ SharedKernel.Database │
                         └───────────┬───────────┘
                                     │
                  ┌──────────────────┴──────────────────┐
                  │                                     │
                  ▼                                     ▼
        ┌────────────────────┐               ┌────────────────────┐
        │ Database.Tests     │               │ Database.Client    │
        │                    │               │                    │
        │ Automated          │               │ Interactive        │
        │ deterministic      │               │ real consumption   │
        │ verification       │               │ demonstration       │
        └────────────────────┘               └────────────────────┘
```

Tests should remain focused on behavior and regression protection.

The client should remain focused on discoverability, integration examples and manual exploration.

---

## Project Structure

The relevant client structure is:

```text
SourceClient/
└── KUKULCAN.SharedKernel.Database.SourceClient/
    ├── Client/
    │   ├── ClientDbContext.cs
    │   ├── ClientEntities.cs
    │   ├── ClientOrder.cs
    │   ├── ClientProduct.cs
    │   ├── ConsoleCurrentUser.cs
    │   ├── ConsoleDateTimeProvider.cs
    │   ├── ConsoleDomainEventDispatcher.cs
    │   ├── ConsoleTenantContext.cs
    │   ├── DemoAuditLog.cs
    │   ├── DemoTenantDocument.cs
    │   ├── OrderPlacedEvent.cs
    │   └── Stubs.cs
    ├── UI/
    │   └── ConsoleMenu.cs
    ├── Program.cs
    ├── appsettings.json
    └── KUKULCAN.SharedKernel.Database.Client.csproj
```

The client project name is:

```text
KUKULCAN.SharedKernel.Database.Client
```

The source directory currently uses the historical directory name:

```text
SourceClient
```

The directory name does not change the assembly or root namespace.

---

## Requirements

- .NET 10 SDK
- Entity Framework Core 10
- `KUKULCAN.SharedKernel`
- `KUKULCAN.SharedKernel.Database`
- PostgreSQL and/or SQL Server when the corresponding demonstration is executed
- A valid provider-specific connection string

The client project currently references:

```text
Microsoft.EntityFrameworkCore.SqlServer
Npgsql.EntityFrameworkCore.PostgreSQL
Microsoft.EntityFrameworkCore.Tools
Microsoft.Extensions.Configuration
Microsoft.Extensions.Configuration.Json
Microsoft.Extensions.Configuration.Binder
Microsoft.Extensions.Configuration.EnvironmentVariables
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Logging
Microsoft.Extensions.Logging.Console
Spectre.Console
```

---

## Running the Client

From the client project directory:

```bash
dotnet restore
dotnet build
dotnet run
```

Or from the solution root:

```bash
dotnet run --project SourceClient/KUKULCAN.SharedKernel.Database.SourceClient/KUKULCAN.SharedKernel.Database.Client.csproj
```

The application will ask for the database provider and then display the interactive menu.

---

## Environment Configuration

Do not commit real credentials to source control.

For local development, use:

- user secrets;
- environment variables;
- development-specific configuration files;
- container secrets;
- an external configuration provider.

The connection strings contained in the demonstration configuration are examples only.

---

## Architectural Principles Demonstrated

The client intentionally reinforces the following architectural decisions from `KUKULCAN.SharedKernel.Database`.

### Infrastructure remains infrastructure

The client consumes persistence infrastructure; it does not move persistence behavior into the domain model.

### SharedKernel remains stable

The client uses SharedKernel contracts such as:

```text
IAuditable
ISoftDelete
IImmutable
IClock
IDomainEvent
IDomainEventDispatcher
```

without modifying those contracts to accommodate EF Core.

### Persistence behavior is centralized

Audit handling, soft deletion, tenant filtering, immutability enforcement and domain-event dispatch remain infrastructure concerns.

### Provider dependencies belong to consumers

The database infrastructure remains provider-independent at its core. The client supplies the actual provider packages required to run the demonstration.

### No generic repository abstraction

The client accesses EF Core through the configured `DbContext` and `UnitOfWork<TContext>`.

This preserves the existing architectural decision not to introduce a generic repository layer merely for abstraction's sake.

### Demonstration code must remain disposable

The entities and console stubs are examples. They are not intended to become part of the shared infrastructure.

---

## Limitations

This project is intentionally a demonstration client.

It is not:

- a production application;
- a migration management tool;
- a benchmark suite;
- a replacement for the NUnit tests;
- a generic application template;
- a repository abstraction;
- a domain layer.

Its code should therefore remain small and explicit.

---

## Quality Boundary

The quality expectations are different from those of the library itself.

`KUKULCAN.SharedKernel.Database` is production infrastructure and therefore requires automated behavioral verification.

`KUKULCAN.SharedKernel.Database.Client` is an executable example and integration/demo host. Its primary quality criteria are:

- it compiles against the current public API;
- it demonstrates the intended integration model;
- its examples reflect the actual infrastructure behavior;
- its configuration is understandable;
- it does not introduce architectural dependencies back into the library.

---

## License

See the repository `LICENSE` file for the applicable GPL terms.

---

## Related Projects

### `KUKULCAN.SharedKernel`

Provides the shared domain and cross-cutting contracts consumed by the database infrastructure.

### `KUKULCAN.SharedKernel.Database`

Provides the persistence and Entity Framework Core infrastructure demonstrated by this client.

### `KUKULCAN.SharedKernel.Database.Tests`

Provides automated NUnit tests for the database infrastructure.

The client and tests intentionally complement one another rather than duplicate one another.
