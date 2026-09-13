param(
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityPath,
    [ValidateSet('PrepareContent', 'EditMode', 'PlayMode')][string]$Mode = 'PrepareContent',
    [string]$TestFilter = 'Armok.Tests',
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
} else {
    $arguments += @('-runTests', '-testPlatform', $Mode, '-testFilter', ('"' + $TestFilter + '"'),
        '-testResults', ('"' + (Join-Path $reportRoot 'results.xml') + '"'))
}

# Use a separate checkout or close its editor first. Unity enforces its project lock.
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Output "Unity $Mode validation started (PID $($process.Id))."
$process.WaitForExit()
$result = [ordered]@{ mode=$Mode; editorVersion=$version; exitCode=$process.ExitCode; completedUtc=[DateTime]::UtcNow.ToString('o') }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $reportRoot 'process-result.json') -Encoding UTF8
if ($process.ExitCode -ne 0) { throw "Unity validation failed with exit code $($process.ExitCode). Inspect $reportRoot." }
Write-Output "Unity $Mode validation completed successfully. Reports: $reportRoot"
