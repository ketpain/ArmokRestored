param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityPath,
    [ValidateSet('PrepareContent', 'EditMode', 'PlayMode', 'FullReloadLive')][string]$Mode = 'PrepareContent',
    [string]$TestFilter = 'Armok.Tests',
    [string]$TestAssembly,
    [string]$ReportDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$versionText = Get-Content -LiteralPath (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Raw
$version = [regex]::Match($versionText, '(?m)^m_EditorVersion: (\S+)').Groups[1].Value
if (-not $UnityPath) { $UnityPath = Join-Path ${env:ProgramFiles} "Unity/Hub/Editor/$version/Editor/Unity.exe" }
if (-not (Test-Path -LiteralPath $UnityPath -PathType Leaf)) { throw 'Pass -UnityPath with the path to the pinned Unity editor.' }
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $projectRoot "Logs/Validation-$Mode" }
$null = New-Item -ItemType Directory -Path $ReportDirectory -Force
$reportRoot = (Resolve-Path -LiteralPath $ReportDirectory).Path
$arguments = @('-batchmode', '-projectPath', ('"' + $projectRoot + '"'), '-logFile', ('"' + (Join-Path $reportRoot 'Editor.log') + '"'))
if ($Mode -eq 'PrepareContent') {
    $arguments += @('-quit', '-executeMethod', 'Armok.Editor.ProjectValidation.PrepareContent')
} elseif ($Mode -eq 'FullReloadLive') {
    $arguments += @('-executeMethod', 'Armok.Editor.SessionReloadValidation.Run')
} else {
    if (-not $TestAssembly) {
        if ($Mode -eq 'EditMode') { $TestAssembly = 'Armok.Content.Tests' }
        else { $TestAssembly = 'Armok.Input.Tests' }
    }
    $arguments += @('-runTests', '-testPlatform', $Mode, '-assemblyNames', ('"' + $TestAssembly + '"'), '-testFilter', ('"' + $TestFilter + '"'),
        '-testResults', ('"' + (Join-Path $reportRoot 'results.xml') + '"'))
}

# Use a separate checkout or close its editor first. Unity enforces its project lock.
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity $Mode validation started (PID $($process.Id))."
$process.WaitForExit()
$result = [ordered]@{ mode=$Mode; editorVersion=$version; exitCode=$process.ExitCode; completedUtc=[DateTime]::UtcNow.ToString('o') }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportRoot 'process-result.json') -Encoding UTF8
if ($Mode -eq 'FullReloadLive') {
    $fullReloadReport = Join-Path $projectRoot 'Logs/full-reload-validation.json'
    if (Test-Path -LiteralPath $fullReloadReport) {
        Copy-Item -LiteralPath $fullReloadReport -Destination (Join-Path $reportRoot 'full-reload-validation.json')
    }
}
if ($process.ExitCode -ne 0) { throw "Unity validation failed with exit code $($process.ExitCode). Inspect $reportRoot." }
Write-Output "Unity $Mode validation completed successfully. Reports: $reportRoot"
