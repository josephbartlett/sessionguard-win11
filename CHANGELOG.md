# Changelog

All notable changes to this project should be recorded here in reverse chronological order.

## Unreleased

- Added a persisted `state/service-health.json` snapshot for service startup, scan, and pipe diagnostics.
- Updated the service status tooling to surface health snapshot data alongside control-plane reachability.
- Added a published-layout validation script and runtime path tests for repo-external service validation.
- Added install-readiness validation and script tests for the non-admin and layout-check paths.
- Made local fallback wording in the desktop UI more explicit so operators can distinguish in-process scans from service-backed monitoring.
- Added a concrete post-`v0.3.0` execution plan and a `v0.4.0` workspace-safety implementation plan in `docs/plans/`.

## 0.3.0 - 2026-03-11

- Added a versioned named-pipe control plane shared by the desktop app and service-hostable runtime.
- Added a tray-aware WPF shell that prefers the service control plane and falls back locally when needed.
- Added service-owned scan state orchestration and shared snapshot persistence behind the new control plane.
- Added separate app and service log files for clearer operational diagnostics.
- Added service publish, install, start, stop, status, and uninstall scripts for local Windows Service validation.
- Added release metadata, release notes, and packaging automation for the 0.3.0 milestone.
- Hardened long-running process detection so transient or protected process inspection failures do not break scans.
- Expanded tests and refreshed docs to match the current service-plus-client architecture.

## 0.2.0 - 2026-03-11

- Added multiple restart-signal providers for Windows Update Agent, Windows Update UX settings, scheduled task visibility, and deeper registry coverage.
- Added aggregated signal analysis to distinguish definitive pending reboot state from ambiguous orchestration activity and limited visibility.
- Added a machine-readable `state/current-scan.json` snapshot as service/tray groundwork.
- Added `SessionGuard.Service`, a service-hostable background worker that reuses the shared scan coordinator and snapshot output.
- Updated the dashboard and docs to show provider-level signal coverage and improved restart awareness.

## 0.1.1 - 2026-03-11

- Expanded [`AGENTS.md`](/C:/Users/decoy/sessionguard-win11/AGENTS.md) with explicit push approval rules.
- Added a standard release-title and markdown-description template for future approved pushes.
- Clarified that agents may create local commits but must not push without explicit approval.

## 0.1.0 - 2026-03-11

- Added the initial WPF MVP for SessionGuard.
- Added protected-process detection, restart-state inspection, configurable JSON settings, and local logging.
- Added reversible native mitigation controls for restart-related Windows Update policies.
- Added unit tests, architecture notes, limitations, and roadmap documentation.
