# Database Configuration

## Configuration Section

The configuration root is:

```text
Kukulcan:Database
```

## Options

`KukulcanDatabaseOptions` contains:

```text
Provider
ConnectionString
CommandTimeoutSeconds
EnableSensitiveDataLogging
EnableDetailedErrors
Retry
Pool
Migration
```

## Providers

The current `DatabaseProvider` enum supports:

```text
SqlServer
PostgresSql
MySql
```

The production `KUKULCAN.SharedKernel.Database` package remains provider-neutral and does not reference a concrete database provider package. The consuming infrastructure or host supplies the required provider package:

```text
Microsoft.EntityFrameworkCore.SqlServer
Npgsql.EntityFrameworkCore.PostgreSQL
MySql.EntityFrameworkCore
```

Provider configuration is resolved dynamically by `KukulcanDbContextBase`.

## Retry

Retry configuration contains:

```text
Enabled
MaxRetryCount
MaxRetryDelaySeconds
```

When enabled, these values are used by the provider configuration to enable the provider's EF Core execution strategy.

## Pool

Pool configuration contains:

```text
Enabled
MinSize
MaxSize
```

The shared database infrastructure applies these settings to the provider connection string for SQL Server, PostgreSQL and MySQL. The exact connection-string keywords are provider-specific, while the public configuration model remains provider-neutral.

## Migration and Seed

Migration options contain:

```text
AutoMigrateOnStartup
SeedDataOnStartup
```

When enabled through dependency injection:

- `AutoMigrateOnStartup` applies pending EF Core migrations during application startup.
- `SeedDataOnStartup` invokes an optional `IKukulcanDatabaseSeeder<TContext>` registered by the consuming application.

The SharedKernel Database package does not provide application-specific seed data; the consumer supplies the seeder implementation.

## Diagnostics

Two diagnostic flags are available:

```text
EnableSensitiveDataLogging
EnableDetailedErrors
```

Sensitive data logging must be disabled in production.

## Dependency Injection

`AddKukulcanDbContext<TContext>()`:

1. reads `Kukulcan:Database`;
2. binds `KukulcanDatabaseOptions`;
3. validates the connection string;
4. registers the derived DbContext;
5. registers `IUnitOfWork` as scoped;
6. registers `SlowQueryInterceptor` as a singleton;
7. keeps provider configuration provider-neutral and allows an explicitly configured EF Core provider to take precedence.

The registration also supports the optional startup migration and seed infrastructure configured through `Migration`.
