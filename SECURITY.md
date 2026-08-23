# Security Policy

## Supported version

StepPilot is currently developed on the `master` branch. Security fixes should be applied to the current release line before production deployment.

## Reporting a vulnerability

Please do not publish exploitable security issues in public issues. Report them privately to the repository owner with a clear description, affected component, reproduction steps and the potential impact.

## Security baseline

StepPilot uses ASP.NET Core Identity, role-based authorization, tenant scoping via `CompanyId`, HTTPS redirection, antiforgery protection and basic security headers. Production deployments must additionally provide secure secret management, TLS termination, database encryption/backups, least-privilege database credentials, centralized logging/monitoring, patch management and tested restore procedures.

## Data protection

Employee, scheduling and time-tracking data are personal data. Production operators are responsible for defining lawful purposes, access policies, retention/deletion rules, data-subject processes, processor agreements and technical/organizational measures appropriate to their deployment.
