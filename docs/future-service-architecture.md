# Future Service Architecture

## Goal

Evolve SessionGuard from a foreground desktop monitor into a split architecture with a background Windows Service and a lighter user shell.

## Current status

The repo now includes `SessionGuard.Service`, a background worker/service-host foundation that:

- reuses the shared scan coordinator
- reuses the same provider stack
- writes the shared `state/current-scan.json` snapshot
- can be run locally from the CLI as a console host

This is not the full service split yet. It is the first concrete background component.

## Proposed components

### Windows Service

- owns continuous monitoring
- performs privileged reads and writes
- evaluates protection rules
- exposes current health and mitigation state through a local IPC boundary
- persists service diagnostics and state snapshots

### Tray app

- renders current restart risk without requiring the full dashboard window
- requests actions from the service
- shows prompts, warnings, and future approval workflows
- opens the richer desktop dashboard on demand

### Shared core

- keeps the current status models, policy contracts, and evaluation logic
- remains reusable by both the service and the tray app

## IPC direction

- local named pipes are a reasonable Windows-first starting point
- IPC contracts should be versioned early
- only the service should write privileged mitigation settings
- the tray app should not need administrative rights for normal use

## Security posture

- keep the service privilege set as narrow as possible
- validate every IPC request
- log requested actions and their outcomes
- avoid implementing brittle restart-blocking tricks that look like malware or tamper with unsupported internals

## Future feature fit

This split architecture supports:

- continuous restart monitoring even when the dashboard window is closed
- restart approval windows
- workspace snapshot metadata
- richer browser and terminal heuristics
- policy-driven behavior across multiple user sessions

## Migration path from the MVP

Current foundation already in repo:

- `state/current-scan.json` captures the latest aggregated scan result in a machine-readable format
- core status models already separate raw indicators from user-facing evaluation
- `SessionGuard.Service` already hosts the scan loop outside the WPF app

Migration path:

1. move signal providers and mitigation writers behind service-hostable abstractions
2. introduce a small local IPC contract for health state and action requests
3. downgrade the current WPF window into a tray-aware client
4. add service installation, health checks, and upgrade-safe persisted state
