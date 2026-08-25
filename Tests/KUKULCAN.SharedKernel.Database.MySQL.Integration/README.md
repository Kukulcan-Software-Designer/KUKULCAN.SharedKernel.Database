# KUKULCAN.SharedKernel.Database.MySQL.Integration

Integration tests for `KUKULCAN.SharedKernel.Database` against a real MySQL 8.4 server running in Testcontainers.

## Scope

The suite validates the same persistence integration areas covered by the SQL Server and PostgreSQL suites, adapted to the MySQL provider:

- provider selection and real database persistence
- dependency-injection registration and database options
- tenant isolation and model-cache keys
- soft delete and auditing
- immutable entities
- domain-event dispatch
- synchronous and asynchronous save paths
- slow-query interception
- cancellation
- unit-of-work transactions, rollback, commit and disposal
- model configuration discovery

## Requirements

- .NET 10 SDK
- Docker/compatible container runtime

The database is created and destroyed automatically by Testcontainers. No local MySQL installation or application configuration file is required.

## Provider

The tests use the official `MySql.EntityFrameworkCore` Connector/NET provider version `10.0.9` against MySQL 8.4.
