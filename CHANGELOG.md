# Changelog

All notable changes to this project should be recorded here in reverse chronological order.

## 1.2.0 - 2026-03-14

- Refined the everyday tray workflow so the tray menu now centers status, next step, one common action, and a smaller support area instead of mirroring the dashboard.
- Added `Open elevated controls` as the explicit path when the service is connected but the current app session cannot perform service-owned write actions.
- Tightened tray, dashboard, notification, and recommendation wording so service connectivity and write access are described consistently.
- Added a first-class `--technical-view` startup path so the elevated SessionGuard window can open directly into the detailed control surface without creating a second tray shell.
- Hardened the service control plane with bounded IPC payload sizes, per-request timeouts, trusted-server verification, and install-user pipe ACLs for published installs.
- Scoped installed `logs/` and `state/` access to the installing user, administrators, and `SYSTEM`, and stamped that access boundary into the install manifest for published service installs.
- Expanded script coverage for install-manifest authorization stamping and added protocol tests for oversized IPC payload rejection.
- Expanded tests for operator alerts, app startup options, and tray/action-state mapping, and refreshed the tray workflow plan and operator docs for the new runtime model.

## 1.1.4 - 2026-03-13

- Fixed the combined installer so reinstall and upgrade flows stop the running installed tray app before replacing files under `Program Files\SessionGuard`.
- Reused the same installed-app shutdown logic for combined uninstall to keep install and removal behavior aligned.
- Improved post-install app-launch warnings so blocked first launches explicitly call out possible SmartScreen or Windows protection prompts while keeping the install successful.
- Fixed the service health snapshot so upgrade installs refresh the reported product version and runtime metadata instead of preserving stale values from the previous install.
- Updated the install docs and generated setup README to explain reinstall behavior, `-DoNotLaunchApp`, and the expected unsigned first-launch prompt.
- Added regression coverage for stopping a running installed `SessionGuard.App.exe` during installer operations.

## 1.1.3 - 2026-03-13

- Fixed the combined installer so a blocked post-install tray launch no longer makes the whole install look failed.
- Changed the immediate post-install app launch path to prefer the interactive shell context instead of depending on the elevated installer process.
- Added install-path diagnostics for post-install app launch attempts and warnings.
- Updated install docs and the generated setup README to explain that a blocked immediate launch does not invalidate the install.

## 1.1.2 - 2026-03-13

- Simplified the top-level README so it works better as a public repo front door instead of mixing end-user, operator, contributor, and release-engineering detail.
- Added `docs/development.md` to hold source-build, validation, packaging, and tag-driven release instructions.
- Tightened `docs/getting-started.md` so it stays focused on install and common operator paths.

## 1.1.1 - 2026-03-13

- Fixed single-instance app scoping so installed, source-run, and elevated SessionGuard app sessions no longer collide with each other unexpectedly.
- Clarified the elevated-action operator path and combined-install opt-out switches across the repo and generated bundle README.
- Renamed the preferred public release asset to the setup zip and aligned release upload ordering and docs around that end-user path.
- Updated service-script tests so bundled manifest fixtures use the current product version instead of stale `1.0.4` values.

## 1.1.0 - 2026-03-13

- Reworked the desktop shell into a more deliberate tray-first flow with simpler header actions, clearer tray summaries, and a contextual primary tray action.
- Added single-instance app coordination so repeated launches reuse the existing tray app and can reopen the current dashboard instead of starting duplicates.
- Tightened installed startup behavior and manual validation guidance around sign-in launch, hide-to-tray behavior, and tray-first daily use.
- Refined the main-window header layout so the title, summary text, and utility controls share a cleaner left-aligned rhythm.
- Expanded tests, release validation, and UI smoke coverage for the updated startup options, operator alert copy, and tray-first overview behavior.

## 1.0.4 - 2026-03-13

- Tightened the service boundary so guard-mode changes now require the same elevated service-backed path as mitigation and approval writes.
- Removed machine-specific executable and directory paths from published app, service, and bundle manifests.
- Fixed combined install auto-launch so unrelated `SessionGuard.App` processes no longer suppress the installed tray app launch.
- Replaced the bundled root README with an install-focused package guide instead of the full source-repo README.
## 1.0.3 - 2026-03-13

- Hardened the service pipe so service-backed mitigation and approval changes now require an authorized administrative caller instead of trusting raw pipe connectivity alone.
- Added safe recovery for corrupt `state/policy-approval.json` by quarantining invalid files and keeping scans or service startup alive.
- Tightened public release asset publishing so app, service, and combined bundle packages no longer preserve live logs or state from a reused publish directory.
- Fixed the sign-in startup path so `--start-minimized` launches quietly to the tray instead of flashing the dashboard window first.
- Fixed the shipped service-status script so it follows the active install root when SessionGuard is installed and no longer falls back to stale repo-local publish metadata when it is not.
- Fixed combined uninstall so it stops the running tray app before removing the installed files.
- Updated runtime and getting-started docs to explain the service-versus-app model, current-user startup registration caveat, and the difference between service connectivity and write access.

## 1.0.2 - 2026-03-12

- Added a combined install path that installs the service, registers the tray app to start at user sign-in, and launches the app minimized to the tray.
- Added a shared app-plus-service bundle publish path, root-level bundle install and uninstall entry points, and a bundled release zip so end users do not need to juggle separate app and service packages.
- Added explicit runtime-model documentation explaining the difference between the service, the app, the tray icon, and startup behavior.

## 1.0.1 - 2026-03-12

- Tightened the documentation set for a post-1.0 release, including a shorter README, a new getting-started guide, and a dedicated manual validation checklist.
- Removed stale active-use "MVP" framing from current product docs and tightened repo documentation rules in `AGENTS.md`.
- Simplified the roadmap into a product-facing shipped-history plus likely-next-steps view.
- Updated one remaining mitigation description string so the running product no longer refers to itself as an MVP instance.

## 1.0.0 - 2026-03-12

- Shipped the full SessionGuard platform baseline: restart-signal inspection, workspace-risk heuristics, policy engine, tray-aware desktop shell, and service-backed monitoring.
- Simplified the default dashboard into an overview-first experience with clearer plain-language status and an explicit `Simple view` / `Technical view` split.
- Hardened the service lifecycle with publish preservation, runtime validation, install manifests, config schema migration, and a one-command deployment update flow.
- Tightened the service boundary so mitigation writes and approval changes remain service-owned while local fallback stays read-only for those actions.
- Added deterministic Windows validation with UI smoke automation, local and CI entry points, and packaged screenshot artifacts.
- Added desktop-app publishing, versioned binary release assets, and a tag-driven GitHub Actions workflow for desktop app, service, and source packages.
- Added operator-facing diagnostics for policy validation, approval timing, service health, published-layout validation, and config-backup visibility.
- Corrected Windows `sc.exe` service-creation argument formatting so elevated install and update flows work reliably on a real machine.
- Added `Update-SessionGuardServiceDeployment.ps1` as the supported upgrade path for installed service deployments.

## 0.5.3 - 2026-03-11

- Added operator-facing approval-timing text and tray-status summaries so the current approval state is visible without reopening the full dashboard.
- Added local tray balloon notifications for service fallback, service reconnection, approval activation, approval expiry, approval expiry warnings, and policy transition states.
- Added an approval-expiry warning lead-time setting in `config/appsettings.json` and enabled desktop notifications by default.
- Fixed approval transition messaging so manually cleared approval windows are not mislabeled as expired.
- Added empty-state messaging for policy, workspace, protected-process, and mitigation grids so low-activity views look intentional instead of blank.
- Expanded tests for operator alert transitions, approval expiry handling, and policy-configuration notification edges.

## 0.5.2 - 2026-03-11

- Tightened the control-plane boundary so mitigation writes and restart-approval changes are service-owned and no longer execute in local fallback.
- Added explicit local-fallback messaging and disabled service-owned write actions in the WPF dashboard when the background service is unavailable.
- Added a dedicated control-plane unavailable exception so the hybrid client only falls back on transport or availability failures, not on application-level service errors.
- Bumped the local named-pipe protocol to `1.2` to reflect the updated action-result contract.
- Added service startup approval-state recovery and persisted approval-recovery details into `state/service-health.json`.
- Expanded tests for service-only actions, remote-application-failure handling, approval recovery, and the updated IPC payloads.

## 0.5.1 - 2026-03-11

- Added policy configuration validation and diagnostics so malformed or conflicting `config/policies.json` content no longer fails the entire scan path.
- Added safe fallback behavior for invalid policy JSON by disabling policy evaluation, surfacing operator-facing diagnostics, and keeping restart inspection and workspace monitoring available.
- Added clearer policy evaluation trace output plus explicit precedence messaging when blocking rules and approval rules overlap.
- Fixed approval-window selection so the highest-precedence matching approval rule determines the recommended approval duration instead of whichever matching rule happened to evaluate last.
- Updated the WPF dashboard to show policy configuration health, diagnostic entries, and evaluation trace details.
- Expanded tests for malformed policy JSON, duplicate or conflicting policy rules, approval-window precedence, persisted expired approval cleanup, and policy-validation payload serialization.

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

- Expanded [`AGENTS.md`](AGENTS.md) with explicit push approval rules.
- Added a standard release-title and markdown-description template for future approved pushes.
- Clarified that agents may create local commits but must not push without explicit approval.

## 0.1.0 - 2026-03-11

- Added the initial WPF MVP for SessionGuard.
- Added protected-process detection, restart-state inspection, configurable JSON settings, and local logging.
- Added reversible native mitigation controls for restart-related Windows Update policies.
- Added unit tests, architecture notes, limitations, and roadmap documentation.
