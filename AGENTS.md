# SessionGuard Repo Guide

## Scope and product guardrails

- Keep SessionGuard focused on reducing Windows restart disruption without disabling Windows Update.
- Do not claim guaranteed prevention of every restart path.
- Prefer small, auditable changes over speculative platform tricks.
- Treat registry and policy edits as reversible operations and preserve prior values whenever SessionGuard manages them.

## Branching and git hygiene

- Default branch is `main`.
- Stay on `main` unless a human explicitly asks for another branch.
- Keep commits non-interactive and script-friendly.
- Do not rewrite published history unless a human explicitly asks for it.
- Before tagging a release, ensure `git status` is clean and local build/test verification has been run.

## Commit style

- Prefer short imperative commit subjects.
- Keep the first line under roughly 72 characters when practical.
- Use commit subjects that describe the observable repo change, for example:
  - `Add restart signal aggregation`
  - `Document mitigation limitations`
  - `Refine WPF dashboard status cards`
- Separate unrelated work into separate commits when practical.
- If a change affects behavior, update the relevant docs in the same commit.

## Semantic versioning

- Use `MAJOR.MINOR.PATCH`.
- `PATCH` is for backward-compatible fixes, documentation clarifications, tests, and low-risk internal cleanup.
- `MINOR` is for backward-compatible feature additions or meaningful MVP expansions.
- `MAJOR` is for intentional breaking changes in configuration, contracts, packaging, or operator expectations.
- Pre-1.0 versions should still follow semantic intent:
  - `0.x.0` for feature milestones
  - `0.0.x` for narrow fixes if that phase is ever needed

## Tagging and release rules

- Tag releases as `vMAJOR.MINOR.PATCH`.
- Use annotated tags for releases.
- Release notes should include:
  - user-visible capabilities
  - key architectural changes
  - limitations or known constraints
  - build/run verification status
- Do not tag a release if the repo does not build locally.

## Project structure expectations

- Put pure logic and contracts in `src/SessionGuard.Core`.
- Put Windows/system-facing implementations in `src/SessionGuard.Infrastructure`.
- Put presentation and WPF-only code in `src/SessionGuard.App`.
- Keep defaults and operator-editable behavior in `config/*.json` rather than hard-coding them.
- Keep derived folders such as `bin/`, `obj/`, `logs/`, `state/`, and packaging artifacts out of source control unless a human explicitly asks otherwise.

## Testing and verification

- Run `dotnet build SessionGuard.sln` after meaningful code changes.
- Run `dotnet test SessionGuard.sln` when core logic, configuration loading, or status evaluation changes.
- For UI or startup changes, verify the desktop app launches and shows a top-level window.
- If a feature requires elevation, verify both elevated and non-elevated behavior and document the difference.

## Documentation rules

- Update `README.md` when setup, behavior, or review steps change.
- Update `docs/limitations.md` when platform limits become clearer.
- Update `docs/roadmap.md` when release sequencing or version intent changes.
- Keep documentation honest about confidence, permissions, and Windows platform boundaries.

## Logging and diagnostics

- Prefer terse structured logs.
- Log scan start/finish, signal-provider failures, mitigation attempts, and permission issues.
- Do not add noisy debug logging by default.

## UI and UX expectations

- Preserve a professional, minimal, Windows-native style.
- Prefer clarity and state visibility over visual flourish.
- Avoid speculative controls or workflow surfaces that do not map to a real MVP use case.
