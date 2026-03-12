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
- Agents may create local commits when it helps keep work scoped and auditable.
- Agents must never push branches, tags, or commits unless a human explicitly approves that push in the current conversation.
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
- `MINOR` is for backward-compatible feature additions or meaningful product expansions.
- `MAJOR` is for intentional breaking changes in configuration, contracts, packaging, or operator expectations.
- Pre-1.0 versions should still follow semantic intent:
  - `0.x.0` for feature milestones
  - `0.0.x` for narrow fixes if that phase is ever needed

## Tagging and release rules

- Tag releases as `vMAJOR.MINOR.PATCH`.
- Use annotated tags for releases.
- If a human explicitly approves a push, return release notes in this exact response shape:
  - `Release title: <title>`
  - `Description:`
  - fenced `md` block containing the release body
- Release notes should include:
  - user-visible capabilities
  - key architectural changes
  - limitations or known constraints
  - build/run verification status
- Do not tag a release if the repo does not build locally.
- Keep release titles concise and versioned, for example `SessionGuard 0.1.1 - Repo Rules Update`.

## Release template

When a push has been explicitly approved, use this template in the final response:

Release title: `SessionGuard X.Y.Z - <short release name>`

Description:

```md
## SessionGuard X.Y.Z

Short summary of the release.

### Highlights

- item
- item

### Included in this release

- item
- item

### Architecture

- item
- item

### Limitations

- item
- item

### Verification

- `dotnet build SessionGuard.sln`
- `dotnet test SessionGuard.sln`
- other relevant verification
```

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
- For UI-affecting, startup, or release-readiness changes, run `powershell -ExecutionPolicy Bypass -File scripts/ci/Invoke-WindowsValidation.ps1` when practical; at minimum, run `powershell -ExecutionPolicy Bypass -File scripts/ui/Run-UiSmoke.ps1`.
- Review the generated screenshots under `artifacts/ui/smoke/` or `artifacts/ci/windows-validation/ui-smoke/` after UI smoke changes instead of assuming the dashboard still looks correct.
- If a feature requires elevation, verify both elevated and non-elevated behavior and document the difference.
- Keep GitHub Actions workflow logic thin. Prefer putting validation steps in repo-owned scripts and calling those scripts from `.github/workflows/`.

## Documentation rules

- Update `README.md` when setup, behavior, or review steps change.
- Keep `README.md` focused on first-run understanding, quick-start commands, and links to deeper docs rather than turning it into a dump of every operator detail.
- Update `docs/limitations.md` when platform limits become clearer.
- Update `docs/roadmap.md` when release sequencing or version intent changes.
- Prefer relative Markdown links inside repo docs so they work on GitHub as well as locally.
- Avoid calling the current product an "MVP" except when referring to historical pre-1.0 versions or release notes.
- If a doc or release note describes current status, make sure it reflects what is actually shipped rather than stale planning language.
- Keep documentation honest about confidence, permissions, and Windows platform boundaries.

## Logging and diagnostics

- Prefer terse structured logs.
- Log scan start/finish, signal-provider failures, mitigation attempts, and permission issues.
- Do not add noisy debug logging by default.

## UI and UX expectations

- Preserve a professional, minimal, Windows-native style.
- Prefer clarity and state visibility over visual flourish.
- Avoid speculative controls or workflow surfaces that do not map to a real product use case.
