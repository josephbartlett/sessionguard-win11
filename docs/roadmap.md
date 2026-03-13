# Roadmap

SessionGuard is now on a `1.x` release line. The roadmap below focuses on two things:

- what already shipped and is part of the product baseline
- where the product can provide the next real user value without overstating certainty

## Current Baseline

`v1.0.0` established the current product baseline:

- service-backed restart awareness
- workspace-risk heuristics
- local policy rules and approval windows
- overview-first desktop UI with `Simple view` and `Technical view`
- service install, validation, and update tooling
- local and GitHub Actions validation, including UI smoke automation
- tag-driven binary release assets for app, service, and source packages

## Completed Milestones

### v1.0.1 through v1.0.4 — Post-Launch Hardening

Purpose:
Tighten the first public release line so the product is easier to install, easier to understand, and safer to operate.

Delivered:

- documentation cleanup and release-note alignment
- combined app-plus-service bundle install flow
- service-side authorization hardening for write actions
- runtime-neutral package manifests and cleaner bundle docs
- validation and packaging fixes needed for public releases

### v0.1.0 — Desktop Monitor Foundation

Purpose:
Create the first Windows desktop utility that surfaces restart risk and applies a small set of reversible mitigations without disabling Windows Update.

Delivered:

- WPF desktop dashboard
- protected process detection
- bounded restart-state inspection
- configurable JSON settings
- local logging and unit tests

### v0.2.0 — Improved Restart Awareness

Purpose:
Increase confidence in restart-state detection and show more of the Windows Update orchestration picture.

Delivered:

- deeper registry and Windows Update signal coverage
- better aggregation of pending restart versus ambiguous activity
- richer diagnostic provenance in the dashboard

### v0.3.0 / v0.3.1 — Background Protection Architecture

Purpose:
Move from a foreground-only monitor to a service-backed architecture with real operational tooling.

Delivered:

- `SessionGuard.Service`
- versioned named-pipe control plane
- service publish, install, update, and uninstall scripts
- service health reporting
- published-layout validation and install-readiness checks

### v0.4.0 / v0.4.1 / v0.4.2 — Workspace Safety and Validation Hardening

Purpose:
Make SessionGuard better at recognizing risky live work and reduce manual release validation.

Delivered:

- workspace-risk heuristics and advisory snapshot metadata
- overview-first UI cleanup
- `Simple view` and `Technical view`
- deterministic UI smoke automation
- GitHub Actions Windows validation

### v0.5.0 through v0.5.3 — Policy Engine and Operator UX

Purpose:
Turn restart guidance into rule-driven behavior and make the result understandable in day-to-day use.

Delivered:

- `config/policies.json`
- deterministic policy evaluation
- approval windows and approval persistence
- policy diagnostics and conflict handling
- service-owned write boundary for mitigation and approval actions
- tray summaries and operator-facing notifications

### v1.0.0 — Production Platform Baseline

Purpose:
Ship a coherent, supportable local product with a real service lifecycle, repeatable validation, and versioned distribution.

Delivered:

- hardened service install and update flow
- config schema versioning and bounded upgrade path
- versioned release automation for binary assets
- release-grade documentation and validation coverage

## Likely Next Improvements

The items below are not promises. They are the most likely next areas of value based on the current product shape.

### v1.1.0 — Tray-First Experience

Purpose:
Make SessionGuard feel like a calm background utility that is understandable from the tray first and the dashboard second.

Planned work:

- tighter tray workflow and clearer tray status
- fewer duplicated controls between notifications, tray, and dashboard
- better action prompts when approval, mitigation, or service attention is needed
- startup and sign-in behavior that feels deliberate instead of incidental
- automation and review coverage for tray-first flows

Plan:

- [`docs/plans/v1.1.0-tray-first-experience-plan.md`](plans/v1.1.0-tray-first-experience-plan.md)

Risks:

- too much tray abstraction can hide important diagnostic context
- notification noise can hurt trust if thresholds are not tightened at the same time
- startup polish can easily create regressions if install, sign-in, and manual launch paths diverge

### 1.x — Packaging and Distribution

Purpose:
Make SessionGuard easier to install and update for direct-download users.

Likely work:

- stronger install and upgrade messaging
- optional signing and trust improvements

Risks:

- packaging effort can sprawl quickly
- installer convenience should not weaken auditability or service safety

### 1.x — Policy and Workspace Refinement

Purpose:
Improve the quality of advice without pretending SessionGuard has deeper app state than it really does.

Likely work:

- richer workspace hints
- more useful policy explanations
- better handling of ambiguous restart pressure

Risks:

- heuristic complexity can create false confidence
- more rule surface can make policy behavior harder to understand

### 1.x — Optional Store-Friendly Edition

Purpose:
Explore whether a reduced-distribution variant could fit Microsoft Store constraints while the full product remains a direct-download tool.

Likely work:

- define a monitor-only or reduced-privilege packaging model
- separate store-compatible functionality from the full service-backed build

Risks:

- store constraints do not map cleanly to the current service-plus-elevation model
- splitting editions can create support and expectation complexity

## Product Guardrail

No matter which path comes next, SessionGuard should stay explicit about one thing:

It mitigates restart disruption where Windows allows it. It does not promise universal restart suppression, and it should not drift into unsupported behavior just to sound stronger.
