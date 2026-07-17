[CmdletBinding()]
param(
    [string]$UnityEditorPath = $env:UNITY_EDITOR_PATH,
    [string]$ArtifactRoot = $env:WIA_BOOTSTRAP_ARTIFACTS
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $projectRoot "artifacts/production-bootstrap"
}

$ArtifactRoot = [System.IO.Path]::GetFullPath($ArtifactRoot)
New-Item -ItemType Directory -Force -Path $ArtifactRoot | Out-Null

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $projectVersionFile = Join-Path $projectRoot "ProjectSettings/ProjectVersion.txt"
    $versionLine = Get-Content -LiteralPath $projectVersionFile |
        Where-Object { $_ -like "m_EditorVersion:*" } |
        Select-Object -First 1

    if ($versionLine -notmatch "m_EditorVersion:\s*(.+)$") {
        throw "Could not read the Unity version from '$projectVersionFile'."
    }

    $unityVersion = $Matches[1].Trim()
    $UnityEditorPath = Join-Path `
        "C:/Program Files/Unity/Hub/Editor" `
        "$unityVersion/Editor/Unity.exe"
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity Editor was not found at '$UnityEditorPath'. Set UNITY_EDITOR_PATH."
}

$editorLog = Join-Path $ArtifactRoot "editor.log"
$env:WIA_BOOTSTRAP_ARTIFACTS = $ArtifactRoot

$unityArguments = @(
    "-batchmode"
    "-nographics"
    "-quit"
    "-projectPath"
    $projectRoot
    "-executeMethod"
    "ProductionBootstrapCi.Run"
    "-logFile"
    $editorLog
)

& $UnityEditorPath @unityArguments
$unityExitCode = $LASTEXITCODE

if ($unityExitCode -ne 0) {
    Write-Error "Production bootstrap failed with Unity exit code $unityExitCode. See '$editorLog'."
    exit $unityExitCode
}

Write-Host "Production bootstrap passed. Artifacts: $ArtifactRoot"
