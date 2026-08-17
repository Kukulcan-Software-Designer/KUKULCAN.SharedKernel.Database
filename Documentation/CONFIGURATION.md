# Database Configuration

## Configuration Section

The configuration root is:

``` text
Kukulcan:Database
```

## Options

`KukulcanDatabaseOptions` contains:

``` text
Provider
ConnectionString
CommandTimeoutSeconds
EnableSensitiveDataLogging
EnableDetailedErrors
Retry
Pool
Migration
```

## Provider

The current `DatabaseProvider` enum contains:

``` text
SqlServer
PostgresSql
```

The provider package itself is not referenced by the shared database
package.

## Retry

Retry configuration contains:

``` text
Enabled
MaxRetryCount
MaxRetryDelaySeconds
```

The base context uses these values when configuring supported providers.

## Pool

The options model exposes:

``` text
Enabled
MinSize
MaxSize
```

These values describe pool configuration, although concrete provider
behavior remains provider-specific.

## Migration

Migration options contain:

``` text
AutoMigrateOnStartup
SeedDataOnStartup
```

The options model describes these concerns, while the current base
database infrastructure does not itself execute startup migrations or
seeding.

## Diagnostics

Two diagnostic flags are available:

``` text
EnableSensitiveDataLogging
EnableDetailedErrors
```

Sensitive data logging must be disabled in production.

## Dependency Injection

`AddKukulcanDbContext<TContext>()`:

1.  reads `Kukulcan:Database`;
2.  binds `KukulcanDatabaseOptions`;
3.  validates the connection string;
4.  registers the DbContext;
5.  registers `IUnitOfWork`;
6.  registers `SlowQueryInterceptor`.
