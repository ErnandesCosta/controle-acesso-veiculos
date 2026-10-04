# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html)
from its first published release.

## [Unreleased]

### Added

- Semantic frontend design tokens and a documented WCAG 2.2 contrast matrix
  for the future light, dark, and system appearance foundation.

- An OCI homologation preflight runbook separates safe account verification,
  cost and capacity gates from Terraform planning and resource creation.
- A proposed OCI homologation topology records the single-node pilot boundary,
  public exposure, administrative access, cost preflight and evolution gates
  before any cloud resource is provisioned.
- A guided operational-pilot runbook separates real users, real environments,
  operational data and production, with staged gates, sanitized observation,
  incident severity and explicit pause criteria.
- A reviewable OCI backup baseline provisions a private retention-protected
  bucket, customer-managed encryption, separated IAM roles, absence monitoring,
  and an instance-principal publisher without committing tenancy identifiers.
- A development-only Swagger UI presents the native OpenAPI contract, marks
  protected operations with Bearer authentication, and remains unavailable in
  production.
- Repository governance now includes a public security-reporting policy,
  contribution guidance, and area-based code-owner routing.

### Changed

- The API authorizes the Transport Sector to perform supervised descriptive
  access corrections under the same audited server-side rules used by the
  other authorized profiles; matching frontend exposure remains pending.
- The backend test suite uses xUnit v3 and propagates cancellation through
  application, authentication, access, fleet, event, and platform scenarios.
- Closed Pull Request caches are removed automatically, while signed SBOM and
  provenance attestations remain in GitHub Artifact Attestations instead of
  appearing as digest-like GHCR package versions.

### Security

- ASP.NET Core Data Protection keys persist across API container replacement,
  use a shared application discriminator, and are encrypted with an external
  X.509 certificate mounted separately from its password.
- Backend runtime base packages and the frontend transitive `brace-expansion`
  dependency were refreshed to patched versions.

## [0.3.0] - 2026-09-29

### Added

- A production-oriented Compose file consumes matching versioned backend and
  frontend images from GHCR without rebuilding application images on the host.
- Exceptional access closure records the declared outcome without fabricating
  an exit time and keeps its reason, observation, actor, and audit trail.
- A verified PostgreSQL dump manifest records size and SHA-256, is checked before
  restore, and is exercised with a negative tampering test in CI.
- Authenticated password changes require the current credential and atomically
  replace its hash, revoke renewable sessions, invalidate previously issued
  access tokens, and write a credential-free audit record.
- Administrative account provisioning can issue an expiring temporary
  credential, and administrative resets rotate credential state while revoking
  existing sessions and requiring the account holder to choose a permanent password.

### Changed

- Primary backend and frontend GHCR packages publish only from reviewed semantic
  release tags, keeping ordinary `main` pushes validation-only.
- User-facing text and high-priority actions use consistent language, visual
  hierarchy, and accessible confirmation patterns across the main workflows.
- Destructive and sensitive frontend operations use reusable accessible
  confirmation dialogs with focus management and keyboard support.
- Unit 1 evidence, the integrated homologation guide, ZAP evidence, retention,
  recovery targets, and the protected OCI backup design were consolidated.

### Fixed

- Conditional form controls receive focus only after they become available,
  preserving keyboard operation without focus races.

### Security

- JWT validation now checks a server-side credential version, and password
  changes have a dedicated per-user rate limit and PostgreSQL concurrency lock.
- Shared-device sessions now expire after 15 minutes without valid human
  activity and retain an absolute server-controlled limit of 12 hours.
- Temporary credentials are generated with operating-system cryptographic
  randomness, consumed atomically on first login, stored only as password hashes,
  returned with no-store headers, and restricted from business operations until
  their mandatory replacement.
- Event authorization creation, changes, and cancellation are restricted to the
  Administrator profile while read access remains available to all MVP profiles.
- HTTP telemetry records route templates instead of user-controlled request
  targets, avoiding query-string and path-value disclosure.

This is a technical MVP release for demonstration and evaluation. It is not an
institutional production approval or deployment.

## [0.2.0] - 2026-09-20

### Added

- Integrated vehicle-access MVP for general entries, open records, exits,
  audited descriptive corrections, history, recurring vehicle-and-driver lookup,
  institutional fleet and drivers, vehicle usages, event authorizations, daily
  summaries, user accounts, and audit-trail consultation.
- Individual authentication with short-lived access tokens, server-controlled
  renewable sessions, protected cookies, CSRF validation, rotation, revocation,
  logout, account lockout, and progressive password-hash upgrades.
- Responsive React interface with profile-aware navigation, accessible feedback,
  keyboard flows, mobile layouts, and automated accessibility checks.
- Operational shortcuts for reusing canonical recurring vehicle and driver data
  and filtering open accesses by the categories present in the current result.
- Structured Issue forms for defects, improvements, and documentation.
- Pull Request checklist for traceability, validation, security, privacy, and operations.
- Project favicon based on the vehicle-access artwork.
- PostgreSQL persistence with versioned Entity Framework Core migrations,
  transactionally consistent auditing, constraints, and concurrency controls.
- Disposable PostgreSQL recovery and integrated Compose smoke-test workflows.
- OpenTelemetry instrumentation for configurable OTLP export.
- Multi-platform `linux/amd64` and `linux/arm64` container validation and publication.
- Architecture-specific SPDX SBOMs attached to each published multi-platform manifest.
- Passive OWASP ZAP baseline reports for the disposable integrated stack.
- Semantic container tags associated with reviewed Git tags and GitHub Releases.

### Changed

- Authenticated navigation now uses a contained active state and the accessible
  drawer through tablet widths, preserving the fixed sidebar for wide screens.
- Container scanning now rejects every critical vulnerability, including findings
  without an available fix, while continuing to reject fixable high-severity findings.
- Backend and frontend runtime containers use non-root users, read-only root
  filesystems, dropped capabilities, and defensive HTTP headers.

### Fixed

- Warning-level GitHub Code Quality Reliability findings in backend queries and tests.
- Intermittent recurring-search tests by isolating mock state and controlling only
  the debounce clock in the affected scenarios.
- OCI attestation jobs can persist artifact storage metadata with job-scoped permission.

### Security

- GitHub Actions use job-scoped minimum permissions and third-party actions pinned
  to reviewed commit SHAs.
- Published manifests include signed provenance and architecture-specific SBOM
  attestations bound to their immutable digest.

This is a technical MVP release for demonstration and evaluation. It is not an
institutional production approval or deployment.

## Release process

Before publishing a release:

1. review the changes accumulated under `Unreleased` against merged Pull Requests;
2. move those entries to a new `MAJOR.MINOR.PATCH` section with the release date;
3. keep only user-facing, security, operational, and compatibility-relevant changes;
4. create the Git tag and GitHub Release from the reviewed commit on `main`;
5. open a new empty `Unreleased` section for subsequent work.

Changes merged before this changelog was introduced remain traceable through the
Git history, Pull Requests, Issues, and project Wiki. They must not be
retroactively presented as a published release without an evidence-based review.

[Unreleased]: https://github.com/ifpebj-ti/controle-acesso-veiculos/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/ifpebj-ti/controle-acesso-veiculos/releases/tag/v0.3.0
[0.2.0]: https://github.com/ifpebj-ti/controle-acesso-veiculos/releases/tag/v0.2.0
