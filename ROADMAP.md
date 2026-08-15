# Roadmap

## Purpose

This roadmap describes possible evolution of `KUKULCAN.SharedKernel.Database`. It is intentionally conservative: the package should remain focused persistence infrastructure.

## Current Status

The implementation provides the base DbContext, configuration, provider selection, unit of work, transaction management, persistence interceptors, tenant filtering, model-builder conventions and DI registration.

## Near-Term Priorities

- Preserve a small and explicit public API.
- Keep XML documentation complete.
- Maintain behavior-focused tests for EF Core persistence behavior.
- Validate SQL Server and PostgreSQL integration in consuming environments.
- Keep provider-specific packages outside the core package.

## Future Considerations

Potential improvements may include additional provider adapters, configurable slow-query diagnostics and additional persistence conventions when repeated cross-module requirements justify them. These are candidates, not commitments.

## Out of Scope

Generic repositories, application services, CQRS handlers, validation frameworks and bounded-context business models do not belong in this package.

## Architectural Rule

New functionality should only be promoted here when it is genuinely shared by multiple modules and clearly belongs to persistence infrastructure.
