# Contributing to KUKULCAN.SharedKernel.Database

## Philosophy

Contributions should preserve this project's role as shared EF Core persistence infrastructure. Changes should solve a real cross-module problem rather than move application-specific behavior into the shared package.

## Before Contributing

- Read `README.md`.
- Check existing issues and discussions.
- Verify that the proposed behavior is not already provided by `KUKULCAN.SharedKernel`.
- Confirm that the change belongs to persistence infrastructure.

## Public API

Keep the public surface minimal. New public abstractions require architectural justification. Do not add repositories, business rules or bounded-context entities.

## Coding Standards

- Target .NET 10.
- Keep nullable reference types enabled.
- Treat warnings as errors.
- Follow existing naming and formatting conventions.
- Add XML documentation to public APIs.
- Prefer SharedKernel contracts over duplicate abstractions.

## Persistence Changes

Changes to interceptors, global filters, transaction handling or `KukulcanDbContextBase` must document behavioral and compatibility impact.

## Provider Dependencies

Do not add provider packages to the core library unless the architecture is intentionally changed.

## Testing

Tests should verify real EF Core behavior, including change tracking, interceptors, filters, transactions and configuration. Do not add artificial tests solely to increase coverage.

## Pull Requests

Explain the problem, why it belongs here, public API impact, persistence impact and tests performed. Breaking changes require architectural review and release-note documentation.
