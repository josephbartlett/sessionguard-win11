# Limitations

## Platform limitations

- Windows can restart for reasons outside the small set of user-mode-visible signals SessionGuard inspects.
- Policy-backed mitigations reduce risk but do not override every restart path on every device configuration.
- SessionGuard now includes a hardened local service install and upgrade path, but it still does not ship a kernel component, enterprise management channel, or centralized policy authority.

## Visibility limitations

- Registry indicators are heuristic inputs, not a complete source of truth.
- Windows Update Agent COM, UX settings, and scheduled-task clues improve coverage, but they still do not expose every restart decision path.
- Smart scheduler predictions and scheduled-task visibility are advisory orchestration clues, not proof that Windows will definitely restart at a specific time.
- Protected workspace detection is still process-name based. It does not know whether a terminal has unsaved work, whether a browser tab matters to the user, or whether a runtime process is truly serving a critical workload.
- Terminal and shell heuristics intentionally treat open shells as risky even when the shell may be idle. That biases toward visibility over false reassurance.
- Browser heuristics only operate on configured protected browsers and still cannot tell whether session restore, pinned tabs, or form-state recovery is enabled.
- Editor and IDE heuristics do not inspect project state, dirty buffers, debugger sessions, or build progress.
- Local dev-server style runtime detection is intentionally conservative and may produce false positives for short-lived scripts or background tools that are not truly restart-sensitive.
- Local dev-server style runtime detection can also miss runtimes outside the current allowlist, so absence of a runtime risk item is not proof that no long-running work exists.

## Mitigation limitations

- Applying native mitigation settings requires administrative rights.
- SessionGuard only manages a small set of Windows Update restart-related policy values.
- Reset behavior restores previously observed values when SessionGuard backed them up, but it does not infer organization-managed intent beyond those saved values.

## Policy engine limitations

- Policy rules are evaluated by SessionGuard, not enforced by Windows itself. They are meant to guide safe restart behavior and approval handling inside the product, not to guarantee OS-level compliance.
- The current rule set is intentionally local and single-machine. There is no shared approval authority, multi-user coordination, or signed policy distribution model yet.
- Temporary approval windows are stored locally in `state/policy-approval.json`. They survive app and service restarts, but they are not audited beyond local logs and local state.
- Malformed `config/policies.json` now disables policy evaluation instead of failing the whole dashboard refresh. That is safer operationally, but it also means bad policy edits can silently remove rule enforcement until the operator reviews the diagnostic section.
- Mitigation writes and approval changes are now service-owned. If the service is unavailable, SessionGuard intentionally drops to read-only monitoring rather than attempting those writes from the desktop fallback path.
- Even when the service is connected, the desktop app still needs an elevated SessionGuard window before it can request service-owned mitigation or approval changes. SessionGuard now enforces that boundary per command instead of trusting pipe connectivity alone.
- Process-block rules still depend on user-mode process visibility. If a critical process is hidden by permissions, short-lived, or outside the configured rule inputs, the rule can miss it.
- Restart-window rules currently use the local machine clock and local time zone without a separate organizational calendar or maintenance-window service.
- The installed service control plane plus the installed `logs/` and `state/` directories are now scoped to the installing user, administrators, and `SYSTEM`. That improves local privacy on shared machines, but it also means reinstalling from a different account shifts who can use the installed control plane directly.

## UX limitations

- The desktop UI now behaves as a tray-first shell, but deeper diagnosis still depends on opening the full dashboard window.
- Desktop notifications are local tray balloon tips only. They are not Windows toast notifications, do not integrate with a richer action center workflow, and are easiest to notice when the dashboard window is hidden or minimized.
- The local named-pipe control plane is now versioned, but compatibility is still only guaranteed within the local SessionGuard release line.
- Service installation, update, and start or stop scripts are included, but the workflow is still aimed at local operator deployment rather than enterprise fleet rollout.
- Published runtime config is now preserved across republish operations and has a bounded schema migration path, but future breaking config redesigns will still need explicit migration steps before they can be claimed upgrade-safe.
- The combined install path currently registers tray startup for the current user only. If multiple users sign into the same machine, each user would need their own app auto-start registration.
- If SessionGuard is installed using alternate credentials, both the tray startup registration and the installed service control-plane access will be scoped to the installing account rather than whichever user later signs in normally.
- The tray shell and the dashboard are still the same WPF process. SessionGuard does not yet ship a separate ultra-light tray-only executable.

## Recovery limitations

- Only advisory workspace metadata snapshotting
- No process relaunch orchestration
- No browser or editor recovery integration

## Practical reading of the product

Use SessionGuard as a local restart-awareness and mitigation helper. Do not treat it as a guarantee, a replacement for backups, or a substitute for saving work before maintenance windows.
