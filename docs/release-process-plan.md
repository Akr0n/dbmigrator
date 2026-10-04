# Single-trunk release process: implementation plan

Steps use checkbox (`- [ ]`) syntax for tracking. Design: `docs/release-process-design.md` (approved 2026-10-03). Step 1 of the
migration, releasing v1.0.77 with the current flow, is done separately and is not part of this plan.

**Goal:** replace the develop/staging/main flow with `main` plus short PRs, and one manual `release.yml` (`dry-run`, `candidate`,
`stable`) that builds, tests, runs the E2E matrix and publishes exactly what it tested.

**Architecture:** one reusable `_build.yml` (restore, build, test, publish) used by `ci.yml` (required PR check) and by `release.yml`
(`plan` -> `build` + `e2e` -> `publish`). The release is created as a draft, checked, then published. The version is stamped into the
exe with `-p:Version` alone (the SDK appends `+<full commit sha>` to the informational version by itself). Every task below is shippable and reversible on its own; the old DEV/PROD workflows
keep working until Task 7.

**Tech stack:** GitHub Actions (YAML, bash on ubuntu, pwsh on windows), `gh` CLI, .NET 10, Avalonia, xUnit.

## Global constraints

- Existing releases and tags `v1.0.70`, `v1.0.75`, `v1.0.76`, `v1.0.77` stay intact; a wrong release is followed by the next number, a tag is never reused.
- The release assets are exactly five, with these names: `DatabaseMigrator.exe`, `RELEASE_NOTES.txt`, `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.txt`.
- Actions are pinned to: `actions/checkout@v7`, `actions/setup-dotnet@v6`, `actions/upload-artifact@v7`, `actions/download-artifact@v8`
  (v8.0.1 is current and its README pairs it with upload-artifact v7), `actions/cache@v6`.
- `permissions: contents: read` everywhere; `contents: write` only in the `publish` job.
- `publish` runs only with the positive condition `github.event_name == 'workflow_dispatch' && inputs.channel != 'dry-run'` (on the
  schedule `inputs.channel` is empty, so the negative form alone would publish).
- The E2E matrix blocks every stable release, with no skip switch.
- A stable release comes only from `refs/heads/main` and only if `src/`, `*.sln`, `README.md`, `LICENSE` or `THIRD-PARTY-NOTICES.txt` changed since the last stable tag.
- Every change goes through a PR from a short-lived branch. Repository settings (ruleset, branch deletion) are changed only after the owner confirms.
- A workflow file cannot be run before it is on the default branch (`workflow_dispatch`), so the first run of each new workflow is a dry run on `main`.
- PR titles end up in the release notes: write them descriptively.

## File structure

| File | Responsibility |
|---|---|
| `.github/workflows/_build.yml` (new) | the only place with restore, build, test and publish; takes an optional `version` |
| `.github/workflows/release.yml` (new) | `plan`, `build`, `e2e`, `publish`; manual dispatch and a weekly dry run |
| `.github/workflows/e2e-matrix.yml` | gains `workflow_call`; loses its cron (the weekly dry run runs it) |
| `.github/workflows/ci.yml` | becomes a thin caller of `_build.yml`, no `paths:` filter |
| `.github/release.yml` (new) | release-notes configuration: excludes the `dependencies` label |
| `src/DatabaseMigrator/AppVersion.cs` (new) | formats the informational version for the window title and the log |
| `src/DatabaseMigrator/Views/MainWindow.axaml.cs` | title with version; one log line per start |
| `src/DatabaseMigrator/DatabaseMigrator.csproj` | drops the fixed `AssemblyVersion` / `FileVersion` |
| `tests/DatabaseMigrator.UiTests/AppVersionTests.cs` (new) | the formatting rules |
| `tests/DatabaseMigrator.UiTests/WindowSmokeTests.cs` | the title shows the version |
| `workflow-develop.yml`, `workflow-main.yml` | deleted in Task 7 |
| `DEPLOYMENT.md`, `README.md` | the new procedure (Task 8) |

---

### Task 1: New inert files (migration step 2)

Adds the new workflows without changing any behaviour: nothing triggers them except a manual dispatch (and the Monday dry run, which
publishes nothing). The old PROD does not publish for this merge: its second parent will not be in `develop`'s history.

**Files:**
- Create: `.github/workflows/_build.yml`, `.github/workflows/release.yml`, `.github/release.yml`
- Modify: `.github/workflows/e2e-matrix.yml`
- Docs carried along: `docs/release-process-design.md`, `docs/release-process-plan.md`

**Interfaces:**
- Produces: `_build.yml` input `version` (string, `X.Y.Z`, `X.Y.Z-rc.N` or empty); artifact `exe` (7 days) when a version is given; artifact
  `test-results-windows`. `release.yml` plan outputs `tag`, `version` (the tag without the `v`: `1.0.78`, or `1.0.78-rc.1` for a candidate), `previous`, `prerelease`. `e2e-matrix.yml` callable.

- [ ] **Step 1: Start from `main` on a short-lived branch and bring the design docs along**

The design and this plan were committed on the local `develop` after the last pushed commit (they never went through the old flow).
Take exactly those commits:

```bash
git fetch origin
git log --oneline origin/develop..develop        # only the docs commits: docs/release-process-design.md and this plan
git switch -c ci/release-workflow origin/main
git cherry-pick origin/develop..develop
```

- [ ] **Step 2: Create `.github/workflows/_build.yml`**

```yaml
name: Build and test

on:
  workflow_call:
    inputs:
      version:
        description: 'X.Y.Z or X.Y.Z-rc.N stamped into the exe; empty means build and test only'
        type: string
        default: ''

permissions:
  contents: read

jobs:
  build:
    runs-on: windows-latest
    timeout-minutes: 30
    env:
      VERSION: ${{ inputs.version }}
    steps:
      - name: Checkout
        uses: actions/checkout@v7

      - name: Set up .NET 10.0
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'

      - name: Restore
        run: dotnet restore DatabaseMigrator.sln

      - name: Build (Release)
        shell: pwsh
        run: |
          $buildArgs = @('DatabaseMigrator.sln', '--configuration', 'Release', '--no-restore')
          if ($env:VERSION) {
            $buildArgs += "-p:Version=$env:VERSION"
          }
          dotnet build @buildArgs
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

      - name: Test
        run: dotnet test DatabaseMigrator.sln --configuration Release --no-build --logger "trx;LogFileName=test-results.trx"

      - name: Publish (win-x64)
        if: inputs.version != ''
        shell: pwsh
        run: |
          dotnet publish src/DatabaseMigrator/DatabaseMigrator.csproj --configuration Release --runtime win-x64 --self-contained --no-build --output ./publish "-p:Version=$env:VERSION"
          if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v7
        with:
          name: test-results-windows
          path: '**/test-results.trx'
          overwrite: true

      - name: Upload the exe
        if: inputs.version != ''
        uses: actions/upload-artifact@v7
        with:
          name: exe
          path: publish/DatabaseMigrator.exe
          retention-days: 7
          if-no-files-found: error
          overwrite: true
```

- [ ] **Step 3: Create `.github/workflows/release.yml`**

```yaml
name: Release

on:
  workflow_dispatch:
    inputs:
      channel:
        description: 'dry-run: build and test only. candidate: prerelease vX.Y.Z-rc.N from any ref. stable: release from main.'
        type: choice
        options:
          - dry-run
          - candidate
          - stable
        default: dry-run
      version:
        description: 'vX.Y.Z or X.Y.Z; empty means the next patch after the last stable release'
        type: string
        default: ''
  schedule:
    - cron: '0 4 * * 1'   # every Monday: a dry run on main, which also runs the E2E matrix

permissions:
  contents: read

concurrency:
  group: ${{ github.event_name == 'workflow_dispatch' && inputs.channel != 'dry-run' && 'release' || format('release-{0}', github.run_id) }}
  cancel-in-progress: false

jobs:
  plan:
    runs-on: ubuntu-latest
    timeout-minutes: 10
    outputs:
      tag: ${{ steps.plan.outputs.tag }}
      version: ${{ steps.plan.outputs.version }}
      previous: ${{ steps.plan.outputs.previous }}
      prerelease: ${{ steps.plan.outputs.prerelease }}
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Decide what this run does, and refuse anything unsafe
        id: plan
        shell: bash
        env:
          CHANNEL: ${{ github.event_name == 'schedule' && 'dry-run' || inputs.channel }}
          REQUESTED: ${{ inputs.version }}
          REF: ${{ github.ref }}
        run: |
          set -euo pipefail
          fail() { echo "::error::$1"; exit 1; }
          case "$CHANNEL" in dry-run|candidate|stable) ;; *) fail "unknown channel '$CHANNEL'" ;; esac

          previous=$(git tag --list 'v[0-9]*.[0-9]*.[0-9]*' | { grep -v -- '-' || true; } | sort -V | tail -n 1)
          [ -n "$previous" ] || fail "no stable tag found: cannot work out the next version"

          if [ -n "$REQUESTED" ]; then
            version="${REQUESTED#v}"
          else
            last="${previous#v}"
            version="${last%.*}.$(( ${last##*.} + 1 ))"
          fi
          [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || fail "version '$version' is not X.Y.Z"
          newest=$(printf '%s\n%s\n' "${previous#v}" "$version" | sort -V | tail -n 1)
          { [ "$newest" = "$version" ] && [ "${previous#v}" != "$version" ]; } || fail "version $version does not increase over $previous"

          tag="v$version"
          if [ "$CHANNEL" = candidate ]; then
            # highest number in use + 1, not the count: with rc.2 left over after rc.1 was deleted, a count would pick rc.2 again
            highest=$(git tag --list "$tag-rc.*" | sed 's/.*-rc\.//' | { grep -E '^[0-9]+$' || true; } | sort -n | tail -n 1)
            tag="$tag-rc.$(( 10#${highest:-0} + 1 ))"
          fi
          if git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then fail "tag $tag already exists: a tag is never reused"; fi

          for f in README.md LICENSE THIRD-PARTY-NOTICES.txt; do
            [ -f "$f" ] || fail "$f is missing: it is shipped with every release"
          done

          if [ "$CHANNEL" = stable ]; then
            [ "$REF" = refs/heads/main ] || fail "a stable release comes only from main (this run is on $REF)"
            rc=0; git diff --quiet "$previous" HEAD -- src '*.sln' README.md LICENSE THIRD-PARTY-NOTICES.txt || rc=$?
            [ "$rc" -le 1 ] || fail "git diff failed (exit $rc)"
            [ "$rc" -eq 1 ] || fail "nothing shippable changed since $previous (src, *.sln, README.md, LICENSE, THIRD-PARTY-NOTICES.txt)"
          fi

          prerelease=false; [ "$CHANNEL" = candidate ] && prerelease=true
          {
            echo "tag=$tag"
            echo "version=${tag#v}"
            echo "previous=$previous"
            echo "prerelease=$prerelease"
          } >> "$GITHUB_OUTPUT"
          {
            echo "## $CHANNEL: $tag"
            echo "Previous stable release: $previous. Commits since then:"
            echo
            git log --oneline "$previous..HEAD" | sed 's/^/- /'
          } >> "$GITHUB_STEP_SUMMARY"

  build:
    needs: plan
    uses: ./.github/workflows/_build.yml
    with:
      version: ${{ needs.plan.outputs.version }}

  e2e:
    needs: plan
    uses: ./.github/workflows/e2e-matrix.yml

  publish:
    needs: [plan, build, e2e]
    if: github.event_name == 'workflow_dispatch' && inputs.channel != 'dry-run'
    runs-on: ubuntu-latest
    timeout-minutes: 20
    permissions:
      contents: write
    env:
      GH_TOKEN: ${{ github.token }}
      GH_REPO: ${{ github.repository }}
      TAG: ${{ needs.plan.outputs.tag }}
      PREVIOUS: ${{ needs.plan.outputs.previous }}
      PRERELEASE: ${{ needs.plan.outputs.prerelease }}
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          persist-credentials: false

      - name: Refuse to continue over an existing release or a leftover draft
        shell: bash
        run: |
          if out=$(gh release view "$TAG" 2>&1); then
            echo "::error::A release (or a leftover draft) for $TAG already exists. Delete a draft with: gh release delete $TAG --yes"
            exit 1
          elif ! grep -qi 'release not found' <<<"$out"; then
            echo "::error::Cannot tell whether a release for $TAG exists: $out"
            exit 1
          fi

      - name: Download the exe that was built and tested above
        uses: actions/download-artifact@v8
        with:
          name: exe

      - name: Write the release notes
        shell: bash
        run: |
          set -euo pipefail
          gh api "repos/$GITHUB_REPOSITORY/releases/generate-notes" \
            -f tag_name="$TAG" -f target_commitish="$GITHUB_SHA" -f previous_tag_name="$PREVIOUS" --jq .body > RELEASE_NOTES.txt
          {
            echo
            echo "## Build"
            echo "- $TAG, commit $GITHUB_SHA"
            echo "- .NET 10, Windows x64, self-contained"
          } >> RELEASE_NOTES.txt
          cat RELEASE_NOTES.txt >> "$GITHUB_STEP_SUMMARY"

      - name: Create the draft, check it, publish it
        shell: bash
        run: |
          set -euo pipefail
          flags=(--draft --target "$GITHUB_SHA" --title "Release $TAG" --notes-file RELEASE_NOTES.txt)
          if [ "$PRERELEASE" = true ]; then flags+=(--prerelease); fi
          gh release create "$TAG" "${flags[@]}" DatabaseMigrator.exe RELEASE_NOTES.txt README.md LICENSE THIRD-PARTY-NOTICES.txt
          count=$(gh release view "$TAG" --json assets --jq '.assets | length')
          if [ "$count" != 5 ]; then
            echo "::error::$count assets instead of 5: the draft is left for inspection (gh release delete $TAG --yes)"
            exit 1
          fi
          gh release edit "$TAG" --draft=false
```

- [ ] **Step 4: Create `.github/release.yml`**

```yaml
changelog:
  exclude:
    labels:
      - dependencies
  categories:
    - title: Changes
      labels:
        - '*'
```

- [ ] **Step 5: Edit `.github/workflows/e2e-matrix.yml`** (as it is on `main`): replace the trigger block

```yaml
on:
  workflow_dispatch:
  schedule:
    - cron: "0 3 * * 1"
```

with

```yaml
on:
  workflow_dispatch:
  workflow_call:
```

The weekly run now comes from `release.yml` (Monday 04:00 UTC dry run, which includes the E2E job).

- [ ] **Step 6: Validate all four YAML files before pushing** (a workflow file that does not parse breaks the weekly run and every dispatch)

```bash
PY="/c/Users/feder/AppData/Local/Programs/Python/Python311/python.exe"   # python3 is the Store shim; this one has PyYAML
"$PY" - <<'PY'
import yaml
for f in ('_build', 'release', 'e2e-matrix'):
    d = yaml.safe_load(open(f'.github/workflows/{f}.yml', encoding='utf-8'))
    print(f, 'ok, jobs:', list(d['jobs']))
d = yaml.safe_load(open('.github/release.yml', encoding='utf-8')); print('release.yml ok')
r = yaml.safe_load(open('.github/workflows/release.yml', encoding='utf-8'))
assert r['jobs']['publish']['if'] == "github.event_name == 'workflow_dispatch' && inputs.channel != 'dry-run'"
assert r['jobs']['publish']['permissions'] == {'contents': 'write'}
assert r['jobs']['publish']['needs'] == ['plan', 'build', 'e2e']
assert r['permissions'] == {'contents': 'read'}
e = yaml.safe_load(open('.github/workflows/e2e-matrix.yml', encoding='utf-8'))
trig = e.get('on', e.get(True))
assert 'workflow_call' in trig and 'schedule' not in trig
print('structure ok')
PY
```
Expected: `structure ok`. (YAML 1.1 reads the key `on` as boolean `True`, which is why the script falls back to `e.get(True)`.)

- [ ] **Step 7: Commit the files, open the PR, let it merge**

```bash
git add .github/workflows/_build.yml .github/workflows/release.yml .github/release.yml .github/workflows/e2e-matrix.yml
git commit -m "ci(release): workflow di rilascio manuale con canali dry-run, candidate e stable (ancora inattivo)"
git push -u origin ci/release-workflow
gh pr create --base main --title "Nuovo workflow di rilascio manuale (dry-run, candidate, stable): file nuovi, nessun cambiamento di comportamento" --body "Primo passo del nuovo processo di rilascio (docs/release-process-design.md). Aggiunge _build.yml, release.yml e .github/release.yml e rende e2e-matrix.yml richiamabile. Il vecchio DEV/PROD resta attivo. Il primo dry-run e' possibile solo dopo il merge."
gh pr merge --merge --delete-branch
```

Expected: the merge is a merge commit; the old PROD workflow runs for it and ends green **without** publishing ("The merged commit ... is not part of develop"). Verify with `gh run list --workflow "PROD - Test & Release .NET Application" --limit 1` and `gh release list --limit 2` (no new release).

Rollback: `git revert -m 1 <merge sha>` through a PR.

---

### Task 2: First dry run on `main` (migration step 3)

No repository change; this proves `_build.yml`, `release.yml`, the version stamp and the E2E call before anything depends on them.

- [ ] **Step 1: Dispatch and watch**

```bash
gh workflow run release.yml            # channel defaults to dry-run
sleep 5; id=$(gh run list --workflow release.yml --limit 1 --json databaseId --jq '.[0].databaseId')
gh run watch "$id" --exit-status
```

Expected: `plan`, `build`, `e2e` succeed, `publish` is skipped. If `plan` fails, the `::error::` line says why; fix the file in a PR (a workflow dispatched with `--ref <branch>` needs the file on the default branch to be registered; whether it then runs the branch's copy is confirmed here: `gh workflow run release.yml --ref <branch>` and look at `headSha` of the run).

- [ ] **Step 2: Check what the run produced**

```bash
gh run download "$id" -n exe -D out
pwsh -NoProfile -Command "(Get-Item out/DatabaseMigrator.exe).VersionInfo | Format-List ProductVersion, FileVersion"
gh run view "$id" --json jobs --jq '.jobs[] | "\(.name): \(.conclusion)"'
```

Expected: `ProductVersion` is `1.0.NN+<the run's full commit sha>` where `1.0.NN` is the next patch after the last stable tag (no `-rc` part: only a candidate carries one); the jobs list shows `publish: skipped`. (`FileVersion` is still `1.0.0` until Task 4 deletes the fixed value; that is expected here.)

- [ ] **Step 3: Check the three guards by dispatching them on purpose**

```bash
gh workflow run release.yml -f channel=stable -f version=v1.0.76   # must fail in plan: the version does not increase
gh workflow run release.yml -f channel=candidate -f version=v1.0.75 # must fail in plan: the version does not increase
```

Expected: both runs fail in `plan` with `does not increase over v1.0.77` and nothing is published. Record the real failure text in the PR that fixes anything found.

- [ ] **Step 4: If a build error appears, fix it before going on.** The two things the design flagged: `dotnet publish --runtime win-x64 --no-build`
after a solution build that took the runtime from the csproj, and the `download-artifact` pairing. Fix in a PR, repeat Steps 1-2.

---

### Task 3: `ci.yml` becomes the required check (migration step 4, first half)

**Files:**
- Modify: `.github/workflows/ci.yml` (replace the whole file)

**Interfaces:**
- Consumes: `_build.yml` with no `version` (build and test only).
- Produces: a check on every PR and on pushes to `main`, always reported (no `paths:` filter). Its name is read in Step 3 and used in Task 5.

- [ ] **Step 1: Replace `.github/workflows/ci.yml`**

```yaml
name: CI - .NET Build & Test

on:
  push:
    branches:
      - main
  pull_request:
    branches:
      - main

permissions:
  contents: read

concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: true

jobs:
  test:
    uses: ./.github/workflows/_build.yml
```

- [ ] **Step 2: Validate, then open the PR from a branch** (a PR that touches only a workflow file used to get no CI because of the old filter; the new one must run on this very PR)

```bash
git switch -c ci/ci-required-check origin/main
# edit ci.yml as above
PY="/c/Users/feder/AppData/Local/Programs/Python/Python311/python.exe"   # python3 is the Store shim; this one has PyYAML
"$PY" -c "import yaml; d=yaml.safe_load(open('.github/workflows/ci.yml',encoding='utf-8')); assert d['jobs']['test']['uses']=='./.github/workflows/_build.yml'; print('ok')"
git add .github/workflows/ci.yml
git commit -m "ci: il controllo CI gira su ogni PR e sui push a main, senza filtro sui percorsi, e usa _build.yml"
git push -u origin ci/ci-required-check
gh pr create --base main --title "CI su ogni PR: build e test sempre riportati, senza filtro sui percorsi" --body "ci.yml chiama _build.yml (stessa logica del rilascio) e non ha piu' il filtro paths: il controllo esiste anche per le PR che toccano solo test o workflow, cosi' potra' diventare obbligatorio."
```

- [ ] **Step 3: Read the real check name** (do not assume `test / build`)

```bash
gh pr checks --json name,state --jq '.[] | "\(.state)  \(.name)"'
```

Expected: a line for the CI job, most likely `test / build`. Write the exact string down: Task 5 puts it in the ruleset. Merge once it is green: `gh pr merge --merge --delete-branch`.

Rollback: revert the PR; the old `ci.yml` comes back.

---

### Task 4: The exe knows its version (migration step 4, second half)

**Files:**
- Create: `src/DatabaseMigrator/AppVersion.cs`, `tests/DatabaseMigrator.UiTests/AppVersionTests.cs`
- Modify: `src/DatabaseMigrator/Views/MainWindow.axaml.cs` (title, one log line), `src/DatabaseMigrator/DatabaseMigrator.csproj` (lines 19-20), `tests/DatabaseMigrator.UiTests/WindowSmokeTests.cs`

**Interfaces:**
- Produces: `DatabaseMigrator.AppVersion.Format(string? informationalVersion) : string` and `AppVersion.Current : string`
  (`"1.0.77+5bb2f8d"`: the informational version with the commit cut to seven characters).

- [ ] **Step 1: Write the failing tests** `tests/DatabaseMigrator.UiTests/AppVersionTests.cs`

```csharp
namespace DatabaseMigrator.UiTests;

/// <summary>
/// The release workflow stamps the exe with X.Y.Z+<full commit sha>; the window title and the log show the sha cut to seven
/// characters, so a debug.log sent from another PC says which build produced it.
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void TheCommitIsCutToSevenCharacters()
    {
        Assert.Equal("1.0.77+5bb2f8d", AppVersion.Format("1.0.77+5bb2f8d1234567890abcdef1234567890abcdef"));
    }

    [Theory]
    [InlineData("1.0.77", "1.0.77")]              // no commit in it: left alone
    [InlineData("1.0.77+abc", "1.0.77+abc")]      // already short
    [InlineData("1.0.77+1234567", "1.0.77+1234567")] // exactly seven
    [InlineData("1.0.78-rc.1+deadbeefcafe", "1.0.78-rc.1+deadbee")]
    public void OtherShapes(string input, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingStamped_IsShownAsUnknown(string? input)
    {
        Assert.Equal("unknown", AppVersion.Format(input));
    }

    [Fact]
    public void TheRunningAssemblyHasAVersion()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
    }
}
```

- [ ] **Step 2: Run to see it fail for the right reason** (the type does not exist)

Run: `dotnet test tests/DatabaseMigrator.UiTests/DatabaseMigrator.UiTests.csproj -c Release --filter FullyQualifiedName~AppVersionTests`
Expected: build error `The name 'AppVersion' does not exist`.

- [ ] **Step 3: Implement** `src/DatabaseMigrator/AppVersion.cs`

```csharp
using System.Reflection;

namespace DatabaseMigrator;

/// <summary>The version of this build, as the release workflow stamps it (-p:Version=X.Y.Z; the SDK appends +sha).</summary>
public static class AppVersion
{
    /// <summary>"1.0.77+5bb2f8d": the informational version with the commit cut to seven characters.</summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
            return "unknown";
        int plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return informationalVersion;
        string commit = informationalVersion[(plus + 1)..];
        return commit.Length > 7 ? $"{informationalVersion[..plus]}+{commit[..7]}" : informationalVersion;
    }

    public static string Current { get; } =
        Format(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
}
```

- [ ] **Step 4: Run to see it pass**

Run: the command of Step 2. Expected: all `AppVersionTests` pass.

- [ ] **Step 5: Write the failing test for the title** (append inside `WindowSmokeTests`, before the closing brace)

```csharp
    // The title carries the version of the build: a window or a screenshot sent from another PC says which build it is.
    [AvaloniaFact]
    public async Task TheTitleShowsTheVersionOfTheBuild()
    {
        var (window, viewModel) = await Ui.OpenWindowAsync();
        try
        {
            await Ui.WaitUntilAsync(() => window.Title is { } t && t.Contains(AppVersion.Current), "the title to show the version");
        }
        finally
        {
            Ui.Reset(window, viewModel);
        }
    }
```

Run: `dotnet test tests/DatabaseMigrator.UiTests/DatabaseMigrator.UiTests.csproj -c Release --filter FullyQualifiedName~TheTitleShowsTheVersionOfTheBuild`
Expected: FAIL (timeout waiting for the title: it is `Database Migrator`).

- [ ] **Step 6: Make the title carry the version.** In `MainWindow.axaml.cs`, add a property next to `Log`:

```csharp
    private static string BaseTitle => $"Database Migrator {AppVersion.Current}";
```

and change the two assignments in `InitializeViewModel` (lines ~165 and ~172):

```csharp
                        Title = $"{BaseTitle} — {src.DatabaseType}@{src.Server}  →  {tgt.DatabaseType}@{tgt.Server}";
```
```csharp
                        Title = BaseTitle;
```

- [ ] **Step 7: Log one line at every start.** In `InitializeViewModel`, right after `_vm = new MainWindowViewModel();`:

```csharp
            Log($"Database Migrator {AppVersion.Current} started");
```

(Every start, not once: log rotation moves the file, so the line is not always at the top. This line is verified by running the exe in Task 6; the shared test window cannot assert it reliably because other tests clear the log.)

- [ ] **Step 8: Delete the fixed values** in `src/DatabaseMigrator/DatabaseMigrator.csproj`: remove the two lines

```xml
    <AssemblyVersion>1.0.0</AssemblyVersion>
    <FileVersion>1.0.0</FileVersion>
```

so `-p:Version` reaches the file version too.

- [ ] **Step 9: Run everything**

Run: `dotnet build DatabaseMigrator.sln -c Release` then `dotnet test DatabaseMigrator.sln -c Release --no-build`
Expected: 0 warnings; every test passes, including the new ones.

- [ ] **Step 10: Look at it for real** (this PC runs at 125%, work area 1920x1020)

```powershell
dotnet publish src/DatabaseMigrator/DatabaseMigrator.csproj -c Release -r win-x64 --self-contained -o out-check "-p:Version=9.9.9"
(Get-Item out-check\DatabaseMigrator.exe).VersionInfo | Format-List ProductVersion, FileVersion
git rev-parse HEAD
```

Expected: `ProductVersion` is `9.9.9+<the full sha printed by git rev-parse HEAD>` (the SDK adds the commit by itself). Start `out-check\DatabaseMigrator.exe`: the title bar reads `Database Migrator 9.9.9+` followed by the first seven characters of that sha, and the last lines of `%LOCALAPPDATA%\DatabaseMigrator\debug.log` contain `Database Migrator 9.9.9+<those seven characters> started`. Delete `out-check` afterwards.

- [ ] **Step 11: Commit and PR**

```bash
git switch -c feat/version-in-exe origin/main
git add src/DatabaseMigrator/AppVersion.cs src/DatabaseMigrator/Views/MainWindow.axaml.cs src/DatabaseMigrator/DatabaseMigrator.csproj tests/DatabaseMigrator.UiTests/AppVersionTests.cs tests/DatabaseMigrator.UiTests/WindowSmokeTests.cs
git commit -m "feat(ui): la versione della build e' nel titolo della finestra e nel log a ogni avvio"
git push -u origin feat/version-in-exe
gh pr create --base main --title "La versione della build compare nel titolo della finestra e nel log a ogni avvio" --body "Il workflow di rilascio marchia l'exe con X.Y.Z+commit; un debug.log o uno screenshot dal PC di lavoro dice quale build li ha prodotti. Tolti AssemblyVersion e FileVersion fissi a 1.0.0 dal csproj."
```

Merge when the required check is green (`gh pr merge --merge --delete-branch`). Rollback: revert the PR.

---

### Task 5: Rehearse publishing with a candidate (migration step 5)

No repository change. It exercises the draft -> five assets -> publish path with a prerelease, then removes it.

- [ ] **Step 1: Dispatch a candidate from `main`**

```bash
gh workflow run release.yml -f channel=candidate
sleep 5; id=$(gh run list --workflow release.yml --limit 1 --json databaseId --jq '.[0].databaseId')
gh run watch "$id" --exit-status
```

- [ ] **Step 2: Check the result**

```bash
tag=$(gh release list --limit 1 --json tagName --jq '.[0].tagName'); echo "$tag"
gh release view "$tag" --json isDraft,isPrerelease,assets,tagName --jq '"draft=\(.isDraft) prerelease=\(.isPrerelease) assets=\([.assets[].name]|sort|join(","))"'
gh release download "$tag" -p DatabaseMigrator.exe -D out-rc
pwsh -NoProfile -Command "(Get-Item out-rc/DatabaseMigrator.exe).VersionInfo | Format-List ProductVersion, FileVersion"
```

Expected: `draft=false prerelease=true`, the five names `DatabaseMigrator.exe,LICENSE,README.md,RELEASE_NOTES.txt,THIRD-PARTY-NOTICES.txt`, the tag looks like `v1.0.NN-rc.1`, and `ProductVersion` is `1.0.NN-rc.1+<the run's full commit sha>` (the tag without the `v`, plus the commit).

- [ ] **Step 3: Prove the schedule cannot publish.** Read the last scheduled or dry-run run's jobs (`gh run view <id> --json jobs`) and confirm `publish` is `skipped`; the `if` condition in `release.yml` is the positive form (the Task 1 validation asserts it).

- [ ] **Step 4: Remove the rehearsal**

```bash
gh release delete "$tag" --cleanup-tag --yes
git fetch origin --prune --tags; git tag --list "v*-rc.*"    # nothing left
```

If any check failed: do not go on; fix `release.yml` through a PR and repeat from Step 1. A failed run may leave a draft: `gh release delete <tag> --yes`.

---

### Task 6: Ruleset and branch settings (migration step 6)

Changes repository settings: **show the owner the exact change and wait for a yes** before Step 2.

**Files:** none (repository settings via `gh api`).

- [ ] **Step 1: Save the current ruleset and prepare the new one** (the PUT replaces the whole ruleset, so the existing rules are kept)

```powershell
$check = "test / build"   # the exact name read in Task 3 Step 3
gh api repos/Akr0n/dbmigrator/rulesets/10526240 | Set-Content ruleset-before.json
$r = Get-Content ruleset-before.json -Raw | ConvertFrom-Json
$new = @(
  @{ type = 'pull_request'; parameters = @{ required_approving_review_count = 0; dismiss_stale_reviews_on_push = $false; require_code_owner_review = $false; require_last_push_approval = $false; required_review_thread_resolution = $false } },
  @{ type = 'required_status_checks'; parameters = @{ strict_required_status_checks_policy = $false; required_status_checks = @(@{ context = $check }) } }
)
$payload = @{ name = $r.name; target = $r.target; enforcement = 'active'; conditions = $r.conditions; bypass_actors = @(); rules = @($r.rules | ForEach-Object { @{ type = $_.type; parameters = $_.parameters } }) + $new }
$payload | ConvertTo-Json -Depth 10 | Set-Content ruleset-after.json
Compare-Object (($r.rules | ForEach-Object type)) (($payload.rules | ForEach-Object { $_.type }))
```

Expected: the comparison shows only `pull_request` and `required_status_checks` added; `deletion`, `non_fast_forward`, `copilot_code_review` stay. (A rule with `parameters = null`, such as `deletion`, must be sent without a `parameters` key if the API rejects the null: edit `ruleset-after.json` by hand in that case.)

- [ ] **Step 2: Apply it (after the owner's yes)**

```powershell
gh api -X PUT repos/Akr0n/dbmigrator/rulesets/10526240 --input ruleset-after.json
gh api repos/Akr0n/dbmigrator/rules/branches/main --jq '[.[] | .type] | join(",")'
gh api -X PATCH repos/Akr0n/dbmigrator -f delete_branch_on_merge=true --jq '{delete_branch_on_merge, allow_auto_merge}'
```

Expected: the rules list contains `pull_request` and `required_status_checks`; `delete_branch_on_merge` is true and `allow_auto_merge` still true.

- [ ] **Step 3: Prove it with a throwaway PR**

```bash
git switch -c test/ruleset origin/main; git commit --allow-empty -m "test: prova del ruleset"; git push -u origin test/ruleset
gh pr create --base main --title "Prova del ruleset (da chiudere)" --body "Non unire."
gh pr merge --merge        # must wait or be refused until the check passes; --auto is the normal way
git push origin test/ruleset:main   # must be refused: main only takes PRs
gh pr close --delete-branch
```

Expected: the direct push to `main` is rejected by the ruleset; the PR shows the required check pending, then green.

- [ ] **Step 4: Watch the first Dependabot PR after the change.** Its workflow (`workflow-dependabot-automerge.yml`) runs
`gh pr merge --auto --merge` for minor and patch updates of actions. With the required check it must now wait for the check and then
merge by itself; confirm with `gh pr view <number> --json state,mergeStateStatus,autoMergeRequest`. If it stays stuck, the check name in
the ruleset does not match the one the PR reports: compare with `gh pr checks <number>` and correct the ruleset.

Rollback in seconds: `gh api -X PUT repos/Akr0n/dbmigrator/rulesets/10526240 --input ruleset-before.json` (put back `enforcement: active` content from the saved file), or set `"enforcement": "disabled"` in it.

---

### Task 7: First stable release with the new flow, then remove the old workflows (migration steps 7 and 8)

- [ ] **Step 1: Release**

The change of Task 4 touches `src/`, so a stable release is allowed.

```bash
gh workflow run release.yml -f channel=stable
sleep 5; id=$(gh run list --workflow release.yml --limit 1 --json databaseId --jq '.[0].databaseId'); gh run watch "$id" --exit-status
```

- [ ] **Step 2: Check it**

```bash
tag=$(gh release list --limit 1 --json tagName --jq '.[0].tagName')
gh release view "$tag" --json isDraft,isPrerelease,assets --jq '"draft=\(.isDraft) prerelease=\(.isPrerelease) assets=\([.assets[].name]|sort|join(","))"'
gh release view "$tag" --json body --jq .body
gh release download "$tag" -p DatabaseMigrator.exe -D out-stable; (Get-Item out-stable\DatabaseMigrator.exe).VersionInfo.ProductVersion
gh release list --limit 4
```

Expected: five assets, not a draft, not a prerelease, notes listing only the PRs since the previous stable release, `ProductVersion` is `<tag without v>+<sha>`, `v1.0.70`, `v1.0.75`, `v1.0.76`, `v1.0.77` untouched. Start the downloaded exe: the title shows the version. A wrong release is followed by the next number, never by deleting and reusing a tag.

- [ ] **Step 3: One clean week.** After one green Monday dry run, go on.

- [ ] **Step 4: Remove the old workflows** (through a PR; this also stops the DEV prerelease on every push)

```bash
git switch -c ci/remove-old-release-flow origin/main
git rm .github/workflows/workflow-develop.yml .github/workflows/workflow-main.yml
git commit -m "ci: tolti i workflow DEV e PROD, il rilascio e' release.yml"
git push -u origin ci/remove-old-release-flow
gh pr create --base main --title "Tolti i vecchi workflow DEV e PROD: il rilascio passa solo da release.yml" --body "Dopo uno stable pulito e un dry-run settimanale verde."
```

`develop` and `staging` are ancestors of `main`: stop moving them and delete them after 30 days **only after asking the owner**
(`git push origin --delete develop staging`); they can be recreated from a SHA.

Rollback: revert the PR (the old workflows return; they publish only on a merge from `develop`, which no longer happens).

---

### Task 8: Documentation (migration step 9)

**Files:**
- Modify: `DEPLOYMENT.md`, `README.md` (the contributing/release parts), `ARCHITECTURE.md` only if it mentions the release flow.

- [ ] **Step 1: Find what still describes the old flow**

```bash
grep -n -iE "develop|staging|PROD|-dev|merge commit|prerelease" DEPLOYMENT.md README.md QUICKSTART.md ARCHITECTURE.md
```

- [ ] **Step 2: Rewrite those parts** with the real procedure:

```markdown
## Releasing

Every change goes through a pull request from a short-lived branch:

    git switch -c fix/x && git push -u origin HEAD
    gh pr create --fill && gh pr merge --auto --merge --delete-branch

The PR merges itself when the required check passes. A release is one manual workflow:

    gh workflow run release.yml -f channel=stable

Channels: `dry-run` (default; builds, tests and runs the E2E matrix, publishes nothing), `candidate` (a public prerelease
`vX.Y.Z-rc.N` from any branch, to try a build on another PC; delete it afterwards with `gh release delete vX.Y.Z-rc.N --cleanup-tag --yes`),
`stable` (from `main` only, and only if something shippable changed since the last release). The version is the next patch unless
`-f version=vX.Y.Z` is given. A wrong release is followed by the next number; a tag is never reused. The window title and the log show
the version and commit of the build.
```

- [ ] **Step 3: Check the links and commands in the text against the real files, then commit and PR**

```bash
git switch -c docs/release-procedure origin/main
git add DEPLOYMENT.md README.md
git commit -m "docs: la procedura di rilascio descrive il flusso a tronco unico"
git push -u origin docs/release-procedure
gh pr create --base main --title "Documentazione: la procedura di rilascio descrive il nuovo flusso a tronco unico" --body "Sostituisce le istruzioni sul flusso develop/staging/main."
```

(A docs-only PR cannot be released by design; it just lands.)

---

### Task 9 (optional, the owner decides, irreversible): remove the `-dev` prereleases

- [ ] **Step 1: Count what would go**

```bash
gh release list --limit 300 --json tagName,isPrerelease --jq '[.[] | select(.isPrerelease and (.tagName|endswith("-dev")))] | length'
```

- [ ] **Step 2: Only with the owner's explicit yes, delete the releases but keep the tags**

```bash
gh release list --limit 300 --json tagName,isPrerelease --jq '.[] | select(.isPrerelease and (.tagName|endswith("-dev"))) | .tagName' | while read t; do gh release delete "$t" --yes; done
```

(No `--cleanup-tag`: the tags stay. About 12-14 GB of assets are freed.)

---

## Self-review against the design

- Decisions (single trunk, manual dispatch, E2E always blocking, candidate channel): Tasks 1 and 5; `e2e` is a hard `needs` of `publish`.
- Workflows `_build.yml`, `ci.yml`, `e2e-matrix.yml`, `release.yml`, `.github/release.yml`: Tasks 1 and 3.
- Plan guards (existing tag, non-increasing version, missing files, stable only from `main`, nothing shippable changed): Task 1 Step 3 and checked in Task 2 Step 3; the leftover-draft refusal is the first `publish` step (a read-only token cannot see drafts).
- Draft -> five assets -> publish, notes with explicit previous tag: Task 1 Step 3, exercised in Task 5.
- Version in the exe, the title and the log at every start: Task 4.
- Ruleset and `delete_branch_on_merge` (`allow_auto_merge` already true): Task 6.
- Old workflows removed, branches left for 30 days, documentation, optional prerelease cleanup: Tasks 7, 8 and 9.
- Safety table: PR-only (Task 6), no publish on push (Task 1), tested bytes are the shipped bytes (`publish` downloads the artifact `build` made after `test`), E2E gate, assets counted, existing tag refused.
- Names used consistently: `_build.yml` input `version`; `release.yml` outputs `tag`, `version`, `previous`, `prerelease`; artifact `exe`; `AppVersion.Format` / `AppVersion.Current`.
