# Runtime Model

This is the plain-language explanation of how SessionGuard actually runs on a machine.

## Two processes, two jobs

### `SessionGuard.Service.exe`

This is the background engine.

It:

- runs restart-awareness scans in the background
- owns service-backed actions such as mitigation writes and approval changes
- can run as an installed Windows Service
- auto-starts with Windows when installed

It does **not**:

- show a tray icon
- show a dashboard window
- interact with the desktop directly

### `SessionGuard.App.exe`

This is the user-facing shell.

It:

- shows the dashboard window
- creates the tray icon
- displays notifications
- talks to the service over the local control plane when the service is available
- can run in a normal user session for monitoring only

It does **not**:

- install the service automatically just by being launched
- replace the service for service-owned write actions
- request service-owned guard-mode, mitigation, or approval changes unless the app itself is running as administrator

## Why both exist

The service and the app are separate on purpose.

- The **service** is the long-running background authority.
- The **app** is the visible operator experience.

That split matters because SessionGuard needs both:

- a process that can stay running even when the window is closed
- a process that can live in the tray and interact with the signed-in user

## Modes

### App-only mode

You launch `SessionGuard.App.exe` without the service running.

What you get:

- dashboard
- tray icon
- local fallback monitoring

What you do not get:

- service-backed mitigation writes
- service-backed approval changes

The UI should report `Control plane: Local fallback`.

### App plus local service host

You run `SessionGuard.Service.exe console` and also run `SessionGuard.App.exe`.

What you get:

- the dashboard and tray icon from the app
- background scanning from the service
- service-backed write actions only when the app is running as administrator

The UI should report `Control plane: Service`.

### Installed mode

You install SessionGuard with the combined installer script.

What happens:

- the service is installed as a Windows Service with delayed auto-start
- the app is registered to start at user sign-in with `--start-minimized`
- the app launches to the tray for the installing user

This is the intended always-on operator setup.

## Startup behavior

### Does the service start with Windows?

Yes, when installed. The service install script registers it with `delayed-auto` startup.

### Does the app start with Windows?

Not with Windows itself. It starts at **user sign-in** when the combined install path registers the app under the current user's Run key.

That means:

- the service is machine-level
- the tray app startup is user-level

If you install SessionGuard using alternate credentials or a different signed-in account, the tray startup registration lands in that installing user's profile. The intended flow is to install it from the same account that should see the tray icon at sign-in.

## Tray icon ownership

The tray icon belongs to the **app**.

The service never owns the tray icon. That is the correct Windows model.

## Distribution shape

The recommended end-user distribution is a **single combined bundle** that contains:

- `SessionGuard.App.exe`
- `SessionGuard.Service.exe`
- config defaults
- root-level `Install-SessionGuard.ps1` and `Uninstall-SessionGuard.ps1`
- supporting install and service scripts under `scripts\`

The separate app and service zip files still exist for advanced operator and debugging scenarios, but they are no longer the preferred end-user path.
