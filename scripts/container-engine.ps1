<#
.SYNOPSIS
    Resolves the OCI container engine used by the E2E tooling (Podman by default,
    Docker as a transition-period fallback) and exposes it to the caller.

.DESCRIPTION
    Dot-source this file to get the Resolve-ContainerEngine function. The engine is
    selected via the DBMIGRATOR_CONTAINER_ENGINE environment variable:

        - unset            -> "podman" (the default going forward)
        - "podman"/"docker" -> looked up on PATH
        - an absolute path  -> used verbatim (must be an existing executable)

    Podman on Windows is frequently not on PATH right after install, so when "podman"
    is requested but missing from PATH we fall back to the standard install location.

    'podman' and 'docker' share the same CLI surface for the sub-commands used by the
    E2E orchestration (run / exec / cp / inspect / logs / rm / network / volume), so a
    single resolved engine path drives both.
#>

function Resolve-ContainerEngine {
    [CmdletBinding()]
    [OutputType([string])]
    param()

    $requested = $env:DBMIGRATOR_CONTAINER_ENGINE
    if ([string]::IsNullOrWhiteSpace($requested)) {
        $requested = 'podman'
    }

    # 1. Absolute/relative path to an existing executable.
    if (Test-Path -LiteralPath $requested -PathType Leaf) {
        return (Resolve-Path -LiteralPath $requested).Path
    }

    # 2. Bare name on PATH.
    $cmd = Get-Command $requested -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($cmd) {
        return $cmd.Source
    }

    # 3. Podman fallback: standard Windows install locations (PATH not yet propagated).
    if ($requested -ieq 'podman') {
        $candidates = @(
            (Join-Path $env:ProgramFiles 'RedHat\Podman\podman.exe'),
            (Join-Path $env:LOCALAPPDATA 'Programs\RedHat\Podman\podman.exe')
        )
        foreach ($candidate in $candidates) {
            if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                return $candidate
            }
        }
    }

    throw "Container engine '$requested' non trovato. Installa Podman (o Docker) " +
          "oppure imposta DBMIGRATOR_CONTAINER_ENGINE al percorso dell'eseguibile."
}
