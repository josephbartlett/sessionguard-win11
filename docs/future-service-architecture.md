# Future Service Architecture

## Goal

Evolve SessionGuard from a foreground desktop monitor into a split architecture with a background Windows Service and a lighter user shell.

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

1. move signal providers and mitigation writers behind service-hostable abstractions
2. introduce a small local IPC contract for health state and action requests
3. downgrade the current WPF window into a tray-aware client
4. add service installation, health checks, and upgrade-safe persisted state
