# Release process: single-trunk design

Status: approved by the owner on 2026-10-03, not implemented yet. This file is the design; `DEPLOYMENT.md` becomes the
procedure once step 9 of the migration is done.

## Decisions

| Question | Decision |
|---|---|
| Direction | Single trunk: `main` is the only long-lived branch; the `develop`/`staging`/PR-to-`main` flow goes away |
| How a stable release starts | Manual `workflow_dispatch` of `release.yml` with `channel=stable`; no extra approval step |
| Does the E2E matrix block a stable release | Yes, always, with no skip switch (a flaky run is re-dispatched) |
| How test builds reach the work PC | `channel=candidate`: a public prerelease `vX.Y.Z-rc.N` on demand, from any branch or PR |

## Why (measured on 2026-10-03, not opinions)

- `main` has about 40 commits (Dependabot bumps) that `develop` never receives, so every release is a three-way merge.
- 14 of 51 PROD runs are green and publish nothing: the gate reads the shape of the merge.
- PROD rebuilds the exe: the shipped file differs byte for byte from the one DEV tested (37 pairs out of 37), and the PROD test step is
  `continue-on-error`.
- The E2E matrix (three real databases) is not part of any gate; `ci.yml` is path-filtered to `src/**` and `*.sln`, and `main` has no
  required checks (ruleset `10526240` only forbids deletion and force-push, and requests a Copilot review).
- Every push to `develop` publishes a ~185 MB prerelease: 77 of them, 12-14 GB.
- `AssemblyVersion` is fixed at 1.0.0, so a `debug.log` from the work PC cannot say which build produced it.
- The release notes list every PR since #72 (fixed separately in the current flow; see "Release notes" below).

## The process

**Per change** (replaces pushing to `develop`, which had no checks):

```
git switch -c fix/x && git push -u origin HEAD
gh pr create --fill && gh pr merge --auto --merge --delete-branch
```

The PR merges itself once the required check passes (about 4 minutes).

**Per release** (replaces: fast-forward staging, open the PR, merge it, wait for PROD, check the assets):

```
gh workflow run release.yml -f channel=stable
```

| Channel | What it does | Publishes |
|---|---|---|
| `dry-run` (default, and every Monday from a schedule) | plan, build, test, E2E; the exe is kept as a 7-day artifact; the commits since the previous stable release are printed (the notes API needs a write token, which a dry run does not have) | nothing |
| `candidate` | as above, then a prerelease `vX.Y.Z-rc.N` from any ref | prerelease, deleted by hand after the stable one |
| `stable` | as above, only from `refs/heads/main` | release `vX.Y.Z` |

### Workflows

- **`_build.yml`** (reusable): restore, build, test (blocking), and, when a version is given, `publish --no-build` with
  `-p:Version`. The one place with this logic; it replaces the copies in DEV and PROD.
- **`ci.yml`**: runs on every PR and on pushes to `main`, calls `_build.yml`, no `paths:` filter (the required check must always
  report), `permissions: contents: read`.
- **`e2e-matrix.yml`**: gains `workflow_call` next to `workflow_dispatch`, with a job timeout; its cron moves into `release.yml`.
- **`release.yml`**: jobs `plan` (ubuntu) -> `build` (windows) in parallel with `e2e` (ubuntu) -> `publish` (ubuntu).
  - `plan` (read-only token) computes the next patch version from the last stable tag (`sort -V`) and refuses: a tag that already
    exists, a version that does not increase, a missing shipped file (README, LICENSE, THIRD-PARTY-NOTICES.txt), and for
    `stable` a ref other than `refs/heads/main` or no change since the last stable tag in `src/`, `*.sln`, `README.md`, `LICENSE` or
    `THIRD-PARTY-NOTICES.txt`. Every guard prints why before `exit 1`.
  - `publish` runs only with `if: github.event_name == 'workflow_dispatch' && inputs.channel != 'dry-run'` (the positive form: on the
    schedule `inputs.channel` is empty and the negative form alone would publish). It creates the release as a **draft** with the five
    assets, checks that there are exactly five, and only then publishes. The tag therefore appears at publication; a failed run leaves
    no tag without a release. `contents: write` exists only in this job, so the refusal of a leftover draft (a draft is invisible to a
    read-only token) is its first step.
  - Concurrency: real releases share the group `release`: a running release is never cancelled, and a newer queued run replaces an
    older one that is still queued; dry runs and the schedule use a group per run.
- Actions are pinned to the versions already on `main` (checkout v7, setup-dotnet v6, upload-artifact v7); the `download-artifact`
  version that pairs with upload v7 is confirmed by the first dry run. `upload-artifact` uses `overwrite: true`,
  `if-no-files-found: error`, `retention-days: 7`.

### Version in the exe

Delete `AssemblyVersion` and `FileVersion` from the csproj (lines 19-20) and pass `-p:Version=X.Y.Z` to both build and publish, so
the informational version is `X.Y.Z+<sha>` (the SDK adds the commit by itself; passing `InformationalVersion` too would duplicate it).
A candidate carries its number in the exe as well: its version is `X.Y.Z-rc.N`, the tag without the `v`. The window title shows it with the sha cut to seven characters, and the log writes it at
every start (not only in the first line: log rotation moves the file).

### Release notes

`gh api repos/{repo}/releases/generate-notes` with an explicit `previous_tag_name` (the last stable tag). `.github/release.yml` excludes
the label `dependencies` (not the bot's login: that handle is not verified). The notes are the titles of the merged PRs, so PR titles
must be descriptive.

### Repository settings (done by the owner, or with explicit confirmation)

- Ruleset `10526240` on `main`: keep `deletion`, `non_fast_forward`, `copilot_code_review`; add `pull_request` (0 approvals) and
  `required_status_checks` with the check name read from a real PR in step 4 (expected `test / build`, to be confirmed),
  `strict_required_status_checks_policy: false`; keep the bypass list empty.
- `delete_branch_on_merge = true` (set at step 6). `allow_auto_merge` is already true (checked on 2026-10-03; the Dependabot
  auto-merge workflow relies on it), as is merge-commit-only (squash and rebase are off).

## Safety properties

| Property today | After |
|---|---|
| `main` cannot be force-pushed or deleted | Kept (the ruleset stays; rules are only added). The new `pull_request` rule also closes the direct pushes in history (`1b566d7`, `4f7642f`). |
| PRs only as merge commits | Replaced by "PR-only", enforced by the ruleset; the merge method no longer matters to a release |
| PROD publishes only a merge from `develop` | Replaced: nothing fires on push; a release needs a manual dispatch, `main`, and a version that increases. A Dependabot merge cannot release |
| A stable release exists only if DEV tagged it | Replaced: `publish` needs `build` and `e2e` in the same run |
| A tag or release is never overwritten | Kept and stronger: `plan` refuses an existing tag or draft; `gh release create` cannot update |
| Only tested code ships | Now true: build, test and publish run in one job on one commit with `--no-build` (today PROD ships a rebuild of a tree DEV never tested) |
| The merged tree is tested | The required PR check is real (today no CI runs on the merge); the release run tests the exact HEAD of `main` with blocking tests |
| E2E before a release | Enforced by `needs: e2e`, no bypass |
| The shipped assets are all there | Draft, count equal to five, then publish (today `fail_on_unmatched_files` is false, which fails open) |
| The exe knows its version | New |

## What gets worse

- Every change waits for the check (about 4 minutes); there is no instant push to `develop` any more.
- No hotfix by direct push: the ruleset has no bypass. In an emergency open a PR, or disable the ruleset for a few seconds with the API.
- A flaky UI test can now stop a PR; re-run the job.
- E2E blocks every stable release; 7 of 44 E2E runs failed so far, 3 of them from Podman drift on the runner. Re-dispatch (about 5 minutes).
- E2E runs at release time and on the weekly dry run, not per PR: a Linux-only bug shows up when releasing, not at the PR (still before
  publishing, unlike today).
- A scheduled dry run does not exercise `publish`; after a Dependabot bump of an artifact action, run a `candidate`.
- No downloadable prerelease per push; use `candidate`. The `-rc` prereleases are public and need no login, like the `-dev` ones.
- A commit that changes only tests, docs or workflows cannot be released. This is on purpose; widen the path list if it is ever needed.
- Forgetting `channel=stable` gives a harmless dry run.
- The builds are not reproducible (two builds of one commit differ) and there is no cryptographic provenance; the same as today.

## Migration

Each step is independently shippable, tested without a real release, and reversible. Nothing here is implemented until the plan is
approved.

1. **Release with the current flow** the window-fit fix and the changelog fix (descriptive PR title). Rollback: revert the commit.
2. **New inert files** in one PR from a short-lived branch: `_build.yml`, `release.yml` (default `dry-run`), `.github/release.yml`, and
   `workflow_call` in `e2e-matrix.yml` with a timeout. The old PROD does not publish for this merge (its second parent is not in
   `develop`'s history, the same shape as run PROD #43). Validation: read the diff and validate the YAML. The first dry run is possible only
   after the merge (`workflow_dispatch` needs the file on the default branch). Rollback: revert the PR.
3. **Dry run on `main`**: check the artifact, the printed notes, the computed version, and that `publish` does not start. Iterate with
   `gh workflow run release.yml --ref <branch>` (to be confirmed on first use; otherwise fix through a PR). Nothing is published.
4. **PR: `ci.yml` becomes reusable without the `paths:` filter**, plus the version stamping (csproj, window title, log line) and a UI test.
   Read the real check name with `gh pr checks` and write it down for step 6. Rollback: revert.
5. **Rehearsal of publishing**: `gh workflow run release.yml -f channel=candidate --ref <branch>` creates `vX.Y.Z-rc.1`. Check five assets,
   the prerelease flag, the exe's product version and that no draft is left; prove that `publish` does not run from the schedule; then
   `gh release delete vX.Y.Z-rc.1 --cleanup-tag --yes`.
6. **Ruleset**: add `pull_request` and the required check by the name found in step 4; set `delete_branch_on_merge` (`allow_auto_merge`
   is already on). Verify with `gh api repos/Akr0n/dbmigrator/rules/branches/main` and a test PR (it must wait for the check
   and a direct merge must be refused). Evaluate mode is probably not available on this plan, so this is validated by applying it;
   rollback in seconds: set the ruleset to `enforcement: disabled`.
7. **First stable with the new flow**: a PR with a change in `src/`, then `channel=stable`. Check five assets, the exe digest, the version in
   the window, notes with the real PRs. A wrong release is followed by the next number, never by reusing a tag.
8. **Remove the old workflows** (`workflow-develop.yml`, `workflow-main.yml`, the old `ci.yml` and E2E cron) after one clean stable and one
   green weekly dry run. `develop` and `staging` are ancestors of `main`: stop moving them and delete them after 30 days (ask first; they
   can be recreated from a SHA).
9. **Documentation**: the procedure goes in `DEPLOYMENT.md` and `README.md`, and the release instructions that still describe the old
   flow are removed.
10. **Optional, owner's call, irreversible**: delete the 77 `-dev` prereleases (about 12-14 GB, 41 with no downloads) without
    `--cleanup-tag` so the tags stay.

## Left out on purpose

- Artifact attestations: they need extra permissions and nobody asked for provenance.
- A ruleset on tags: tags are unprotected today too, so there is no regression; add it if it is ever needed.
- A GitHub Environment with a required reviewer: for a single developer it is two clicks and no extra safety.
- Automatic release on every merge: a dependency bump would release by itself.

## To confirm on first use

- The exact name of the required check of a reusable workflow (`test / build` expected).
- Whether `gh workflow run release.yml --ref <branch>` runs the branch's version of the file.
- The `download-artifact` version that pairs with `upload-artifact` v7.
- Whether the restore with `-r win-x64` and `--no-build` publish work together; the dry run shows it before any release.
- That the Dependabot auto-merge workflow still works once the required check exists (its PRs wait for the check, then merge).
