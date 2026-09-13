param([string]$ProjectPath = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath $ProjectPath).Path
$failures = New-Object 'System.Collections.Generic.List[string]'

Push-Location -LiteralPath $projectRoot
try {
    & git lfs version
    if ($LASTEXITCODE -ne 0) { throw 'Install Git LFS before restoring this checkout.' }
    $paths = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate project files with Git.' }

    $fileCount = 0
    foreach ($relativePath in $paths) {
        $filePath = Join-Path $projectRoot $relativePath
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
            $failures.Add("Missing tracked file: $relativePath")
            continue
        }
        $fileCount++
        $stream = [IO.File]::OpenRead($filePath)
        try {
            $prefix = New-Object byte[] 128
            $read = $stream.Read($prefix, 0, $prefix.Length)
            if ([Text.Encoding]::ASCII.GetString($prefix, 0, $read).StartsWith('version https://git-lfs.github.com/spec/v1')) {
                $failures.Add("Unresolved LFS pointer: $relativePath")
            }
        }
        finally { $stream.Dispose() }

        if ($relativePath.StartsWith('Assets/') -and -not $relativePath.EndsWith('.meta') -and
            -not (Test-Path -LiteralPath ($filePath + '.meta') -PathType Leaf)) {
            $failures.Add("Missing asset metadata: $relativePath.meta")
        }
    }

    foreach ($required in @('Packages/manifest.json', 'Packages/packages-lock.json',
        'ProjectSettings/ProjectVersion.txt', 'Assets/RemoteClientDF-Net/DFHack/RemoteClient.cs',
        'Assets/RemoteClientDF-Net/LICENSE', 'ProtoGen/protogen.exe', 'ProtoGen/protobuf-net.dll')) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { $failures.Add("Missing dependency: $required") }
    }
    if (Test-Path -LiteralPath .gitmodules) { $failures.Add('Unexpected submodule configuration; the DF client is vendored. See Docs/ThirdParty.md.') }

    $playerSettings = Get-Content ProjectSettings/ProjectSettings.asset -Raw
    if ($playerSettings -notmatch '(?m)^\s*activeInputHandler:\s*1\s*$') { $failures.Add('Active Input Handling must be Input System Package (New).') }
    $graphics = Get-Content ProjectSettings/GraphicsSettings.asset -Raw
    if ($graphics -notmatch 'm_CustomRenderPipeline:\s*\{fileID: 0\}') { $failures.Add('Graphics settings must use the Built-in Render Pipeline.') }
    $quality = Get-Content ProjectSettings/QualitySettings.asset -Raw
    foreach ($pipeline in [regex]::Matches($quality, '(?m)^\s*customRenderPipeline:.*$')) {
        if ($pipeline.Value -notmatch '\{fileID: 0\}') { $failures.Add('A Quality preset overrides the Built-in Render Pipeline.') }
    }

    if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
    Write-Output "PASS: $fileCount project files checked; no LFS placeholders or missing asset metadata; required dependencies and input/rendering settings present."
    Write-Output 'Compilation, generated-resource preparation, Play Mode, and player builds are separate checks described in Docs/Development.md.'
}
finally { Pop-Location }
