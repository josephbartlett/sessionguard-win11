# Roadmap

This roadmap is intentionally phased. The MVP stays narrow so each subsequent version can expand capability without weakening the safety and honesty of the product.

## Near-Term Execution Plan

### 0.3.x Stabilization Track

Purpose:
Reduce operational ambiguity in the new service-backed architecture before expanding feature scope.

Planned work:
- strengthen service startup, scan, and control-plane diagnostics
- validate published-install behavior outside the repo root
- expand tests around service operations and health reporting
- keep local fallback explicit so operators can tell whether the dashboard is using the service

Current status:
- service health snapshot persistence is now part of the stabilization work
- repo-external published-layout validation is now part of the stabilization work
- install-readiness validation and script coverage are now part of the stabilization work
- the 0.3.x stabilization exit criteria are satisfied on this branch; next planned feature work is v0.4.0

Exit criteria:
- service startup and failure states are visible through status tooling
- published service layout is documented and behaves consistently
- regression coverage exists for the key service lifecycle paths

### v0.4.0 Execution Detail

See [`docs/plans/v0.4.0-workspace-safety-plan.md`](/C:/Users/decoy/sessionguard-win11/docs/plans/v0.4.0-workspace-safety-plan.md) for the concrete implementation phases, dependencies, and acceptance gates.

Current status:
- Phase 1 workspace models and heuristics are now implemented on this branch
- Phase 2 advisory workspace snapshot persistence is now implemented on this branch
- Phase 3 dashboard rendering for workspace-risk summaries is now implemented on this branch
- Phase 4 validation and release-hardening work is now complete on this branch
- the remaining release step is tag-and-push approval plus final release notes handoff

### 0.4.x Validation Hardening

Purpose:
Reduce manual release review by making the same Windows validation path runnable both locally and in GitHub Actions.

Planned work:
- add a repo-owned Windows validation script that runs build, test, and UI smoke together
- add a GitHub Actions workflow on `windows-latest`
- publish UI smoke screenshots, summary data, and test results as CI artifacts
- keep UI validation instructions and repo rules aligned with the CI entry point

Current status:
- v0.4.1 added deterministic local UI smoke scenarios and screenshot capture
- v0.4.2 adds the GitHub Actions validation path, shared repo-owned validation script, and updated repo guidance
- the remaining step for v0.4.2 is the first pushed workflow run on GitHub Actions

Exit criteria:
- a Windows GitHub Actions workflow runs the repo-owned validation script
- the workflow uploads smoke screenshots and summary artifacts on every run
- local documentation and AGENTS guidance point to the same validation entry point

### v0.5.0 Execution Detail

Purpose:
Turn restart guidance into deterministic, user-configurable policy behavior without claiming that SessionGuard can control every Windows restart path.

Current status:
- the branch now includes a separate `config/policies.json` schema
- policy rules are now evaluated in the coordinator and surfaced in the dashboard
- temporary approval windows are now persisted locally and exposed through the control plane
- the WPF dashboard now exposes policy decision, matched rules, and approval grant or clear actions

Exit criteria:
- users can define multiple policy rules with deterministic ordering
- the dashboard can explain which rule changed the displayed policy state
- temporary restart approval windows are visible, persisted, and reversible

## v0.1.0 - MVP Desktop Monitor

### Purpose

Deliver a local desktop utility that makes restart risk visible, detects active protected tooling, and applies a minimal set of reversible native mitigations without disabling Windows Update.

### Key features

- desktop application
- restart state inspection
- protected process detection
- configurable process list
- logging and diagnostics
- basic mitigation guidance and admin-aware mitigation controls

### Architectural changes

- establish `App`, `Core`, and `Infrastructure` layers
- keep Windows-specific code behind interfaces
- use JSON config and file-based local logging
- store mitigation backup state locally for reversible registry changes

### Risks and limitations

- user-mode visibility is incomplete
- dashboard is not always-on protection
- no service or tray presence yet
- mitigation scope is intentionally small

### Acceptance criteria

- solution builds locally
- dashboard launches and displays current status plus process detection
- config changes take effect without recompiling
- mitigation writes fail clearly when not elevated and succeed when elevated
- logs capture scans and action outcomes

## v0.2.0 - Improved Restart Awareness

### Purpose

Increase confidence in restart state detection and provide better visibility into Windows Update orchestration without expanding into unsupported behavior.

### Key features

- deeper Windows restart signal detection
- pending reboot indicator aggregation
- better Windows Update orchestration visibility
- admin-optional mitigation controls
- stronger scan summaries and risk explanations

### Architectural changes

- add more `IRestartSignalProvider` implementations
- introduce confidence weighting for conflicting signals
- separate raw signals from user-facing summaries more explicitly
- add richer diagnostic output for scan provenance

### Risks and limitations

- Windows internals can still vary by build and policy environment
- more signals increase interpretation complexity
- some sources may still require elevation or partial trust assumptions

### Acceptance criteria

- multiple independent providers contribute to the restart summary
- the UI can distinguish pending restart, ambiguous state, and limited visibility
- diagnostics show which providers contributed to each result
- non-admin mode still behaves safely and read-only

## v0.3.0 - Background Protection Architecture

### Purpose

Move from a foreground monitor to a continuously available protection architecture with a background service and a lighter user shell.

### Key features

- Windows Service component
- tray app plus service communication
- continuous restart monitoring
- protection rules
- named-pipe control plane for status and action requests
- local fallback when the service boundary is unavailable during development or recovery
- publish and service-management scripts for local installation and verification

### Architectural changes

- split the current desktop app into service-hosted monitoring and a user-mode tray client
- add IPC between the tray process and the service
- move privileged policy writes into the service boundary
- persist health state for tray rendering and diagnostics
- distinguish authoritative service-owned state from local fallback state in the UI
- version the IPC transport before the service contract spreads further

### Risks and limitations

- service installation and lifecycle increase operational complexity
- IPC and privilege boundaries require careful hardening
- background monitoring changes deployment and testing requirements
- hybrid fallback behavior must stay explicit so operators do not confuse local fallback with service-backed protection

### Acceptance criteria

- service can run independently of the interactive window
- tray app can read current health state and request safe actions
- restart monitoring continues after the main window is closed
- service and tray logs are separated and correlated
- the UI clearly reports whether it is connected to the service or running in local fallback mode
- local install, start, stop, and status operations are documented and scriptable

## v0.4.0 - Workspace Safety Layer

### Purpose

Improve the product’s understanding of what makes a session unsafe to restart and establish the foundation for recovery-aware behavior.

### Key features

- advisory workspace snapshot foundation
- process and session awareness improvements
- browser, terminal, editor, and local-runtime heuristic grouping
- richer per-group disruption explanations and confidence reporting

### Architectural changes

- add a workspace-state subsystem distinct from restart-state inspection
- define snapshot metadata models even if full recovery is not yet implemented
- introduce pluggable heuristics for terminals, browsers, editors, and local servers

### Risks and limitations

- workspace heuristics can become brittle quickly if they overreach
- browsers and terminals expose limited safe state data
- user trust depends on the product staying explicit about what it inferred versus what it knows

### Acceptance criteria

- the app can distinguish generic protected tools from higher-risk live workspaces
- snapshot metadata can be created without breaking the current MVP flows
- the UI explains why a workspace was considered risky
- automated tests cover the first heuristic rules and snapshot persistence

## v0.5.0 - Policy Engine

### Purpose

Allow users or administrators to define clearer restart behavior rules instead of relying only on fixed mitigation defaults.

### Key features

- configurable restart windows
- "never restart while X is running" rules
- approval workflows
- rule-driven protection behavior

### Architectural changes

- add a formal rules engine and policy schema
- store policy definitions separately from static app settings
- add evaluation traces so users can understand why a rule fired
- prepare a management story for team-shared configurations

### Risks and limitations

- rule conflicts can create confusing outcomes
- approval workflows need careful UX to avoid alert fatigue
- broader policy scope may imply stronger guarantees than the platform can deliver

### Acceptance criteria

- users can define multiple rules with deterministic evaluation
- the product can explain which rule changed the displayed state
- policy configuration is validated before activation

## v1.0.0 - Production Platform

### Purpose

Turn SessionGuard into a hardened Windows restart-mitigation platform suitable for sustained daily use and more formal deployment.

### Key features

- hardened service architecture
- advanced restart mitigation
- enterprise deployment considerations
- mature diagnostics and recovery pathways

### Architectural changes

- formal service hardening, health reporting, and upgrade behavior
- clearer versioned contracts between UI, service, and policy engine
- deployment packaging and configuration migration support
- optional enterprise-friendly controls without requiring cloud dependency

### Risks and limitations

- platform hardening raises maintenance cost
- enterprise deployment expectations may exceed what local-only restart mitigation can guarantee
- production support requires broader validation across Windows builds and policy environments

### Acceptance criteria

- service, tray app, and policy engine are versioned and stable
- deployment and upgrade paths are documented and repeatable
- diagnostics are sufficient for field support
- the product remains explicit that it mitigates restart disruption rather than guaranteeing universal restart suppression
