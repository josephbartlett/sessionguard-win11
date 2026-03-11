# Product Brief

## Product

SessionGuard

## Problem

Windows 11 users who keep active development or research workspaces open can lose momentum when Windows Update restart behavior collides with terminals, browsers, editors, local servers, or long-running tasks. The product goal is to reduce that disruption without disabling Windows Update.

## Target user

- developers
- technical operators
- power users
- anyone who regularly keeps a live workspace open for hours

## MVP objective

Provide a local desktop utility that makes restart risk visible, detects whether a protected workspace is active, and applies a narrow set of reversible native mitigations where Windows supports them.

## MVP scope

- restart state dashboard
- protected process detection
- configurable process list
- local logging
- safe mitigation actions for native policy-backed restart behavior
- honest handling of admin-required paths

## Non-goals

- disabling Windows Update
- bypassing unsupported restart mechanisms
- promising absolute restart prevention
- deep app-specific workspace introspection
- enterprise policy distribution
- snapshot or recovery orchestration

## Design principles

- honest about Windows platform limits
- safe and reversible changes
- user-mode first with a service-backed path as the product matures
- minimal UI, high signal
- auditable behavior through logs and docs

## Success criteria for the MVP

- a developer can build and run the app locally
- the app surfaces useful restart and workspace state
- protected-process detection works with a configurable list
- mitigation actions succeed when elevated and fail clearly when not elevated
- docs explain purpose, limits, and future direction without overstating capability
