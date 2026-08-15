# Security Policy

## Supported Versions

Only the latest maintained release of `KUKULCAN.SharedKernel.Database` is actively supported for security fixes.

## Reporting a Vulnerability

Do not report vulnerabilities through public issues or discussions. Send responsible-disclosure reports privately to **jpardo.kukulcan@gmail.com**. If GitHub Private Vulnerability Reporting is enabled, prefer that mechanism.

## Relevant Areas

Please report vulnerabilities affecting database configuration, sensitive-data logging, tenant-isolation filters, transaction handling or persistence behavior caused by this package.

## Security Principles

- Sensitive-data logging is disabled by default.
- Provider packages are supplied by consumers.
- Tenant isolation is applied through persistence-level global filters.
- Public APIs are intentionally small.
- Nullable reference types and warnings-as-errors are enabled.

## Response Process

1. Acknowledge the report.
2. Reproduce and assess the issue.
3. Prepare and validate a fix.
4. Release and document the fix when appropriate.
