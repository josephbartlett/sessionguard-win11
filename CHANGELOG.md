# Changelog

All notable changes to this project should be recorded here in reverse chronological order.

## Unreleased

No unreleased changes yet.

## 0.5.0 - 2026-03-11

- Added a separate `config/policies.json` schema for restart windows, process or workspace restart blocks, and approval-required rules.
- Added a new policy evaluation layer in `SessionGuard.Core` with deterministic rule ordering and matched-rule explanations.
- Added persisted temporary restart approval state at `state/policy-approval.json` plus control-plane methods to grant or clear approval.
- Updated the WPF dashboard to show policy decision, approval state, matched rules, and approval actions.
- Added service utility commands to grant or clear restart approval through the running service.
- Expanded tests for policy evaluation, approval persistence, configuration parsing, and policy-driven protection mode behavior.

## 0.4.2 - 2026-03-11

- Added a repo-owned Windows validation script that runs build, test, and UI smoke through one entry point.
- Added a GitHub Actions workflow on `windows-latest` to run validation and upload smoke/test artifacts.
- Updated `AGENTS.md` and the README so UI-affecting changes use the same validation path locally and in CI.
- Updated the UI smoke script to support configuration selection and CI-friendly no-build execution.
- Ignored `artifacts/ci/` so local validation runs do not dirty the worktree.

## 0.4.1 - 2026-03-11

- Added deterministic WPF UI smoke scenarios plus a scenario-backed app startup path for automation and screenshot capture.
- Added `tests/SessionGuard.UiSmoke` and `scripts/ui/Run-UiSmoke.ps1` so the app can be launched, checked, and screenshotted without manual desktop setup.
- Added stable automation IDs to the WPF dashboard and screenshot artifacts under `artifacts/ui/smoke`.
- Tightened visible UI issues found by the new smoke pass, including clipped metric text and raw enum labels leaking into the workspace table.
- Added `0.4.1` release metadata, packaging defaults, and release notes for the UI automation patch release.

## 0.4.0 - 2026-03-11

- Added a separate workspace-risk model with grouped heuristics for terminals, editors, browsers, local dev-server style runtimes, and generic protected tools.
- Tightened standalone runtime heuristics so lone runtime processes surface as elevated risk instead of automatically high risk.
- Added advisory workspace metadata persistence at `state/workspace-snapshot.json` alongside the existing scan snapshot.
- Updated the WPF dashboard to show workspace safety groups, confidence, and per-group reasons.
- Expanded tests for workspace heuristics, snapshot persistence, and the updated scan result/control-plane contract.
- Bumped the local named-pipe protocol version to `1.1` to keep app and service payloads aligned for the richer scan result.
- Prepared `0.4.0` release metadata, packaging defaults, and release notes.

## 0.3.1 - 2026-03-11

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
