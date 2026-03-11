# Limitations

## Platform limitations

- Windows can restart for reasons outside the small set of user-mode-visible signals this MVP inspects.
- Policy-backed mitigations reduce risk but do not override every restart path on every device configuration.
- SessionGuard does not ship a kernel component, service hardening layer, or enterprise management channel in v0.1.0.

## Visibility limitations

- Registry indicators are heuristic inputs, not a complete source of truth.
- Windows Update Agent COM, UX settings, and scheduled-task clues improve coverage, but they still do not expose every restart decision path.
- Smart scheduler predictions and scheduled-task visibility are advisory orchestration clues, not proof that Windows will definitely restart at a specific time.
- Protected workspace detection is process-name based. It does not know whether a terminal has unsaved work or whether a browser tab matters to the user.

## Mitigation limitations

- Applying native mitigation settings requires administrative rights.
- SessionGuard only manages a small set of Windows Update restart-related policy values.
- Reset behavior restores previously observed values when SessionGuard backed them up, but it does not infer organization-managed intent beyond those saved values.

## UX limitations

- The desktop UI is still a full window, not a tray utility.
- Desktop notifications are not implemented in the MVP; warning behavior is currently focused on dashboard state and optional window raising.
- A service-hostable worker project now exists, but service installation, auto-start behavior, and tray coordination are not yet part of the shipped workflow.

## Recovery limitations

- No workspace snapshotting
- No process relaunch orchestration
- No browser or editor recovery integration
- No approval workflow for restarts

## Practical reading of the MVP

Use SessionGuard as a local restart-awareness and mitigation helper. Do not treat it as a guarantee, a replacement for backups, or a substitute for saving work before maintenance windows.
