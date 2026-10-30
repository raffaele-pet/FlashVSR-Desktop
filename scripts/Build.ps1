param([string]$Dotnet = 'dotnet', [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$payloadRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'engine-payload'))
if (-not $payloadRoot.StartsWith($artifactRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid staging path' }
if (Test-Path -LiteralPath $payloadRoot) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
$sourceFolders = @('flashvsr_plus','inference','model_io','models','operators/flashvsr_attn','bridge','prompt_tensor')
foreach ($folder in $sourceFolders) {
    $base = Join-Path $projectRoot $folder
    foreach ($file in (Get-ChildItem -LiteralPath $base -Recurse -File)) {
        if ($file.FullName -match '[\\/](__pycache__|[^\\/]+\.egg-info)[\\/]' -or $file.Extension -eq '.pyc') { continue }
        $relative = $file.FullName.Substring($projectRoot.Length + 1)
        $destination = Join-Path $payloadRoot $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }
}
foreach ($name in @('pyproject.toml','requirements.txt','requirements-desktop.txt','LICENSE','README_ENGINE.md','THIRD_PARTY_NOTICES.md')) {
    if (Test-Path -LiteralPath (Join-Path $projectRoot $name)) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $payloadRoot }
}
$zip = Join-Path $projectRoot 'desktop/Assets/engine.zip'
Copy-Item -LiteralPath (Join-Path $projectRoot 'README_ENGINE.md') -Destination (Join-Path $payloadRoot 'README.md')
Compress-Archive -Path (Join-Path $payloadRoot '*') -DestinationPath $zip -Force
& $Dotnet publish (Join-Path $projectRoot 'desktop/FlashVsr.Desktop.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $artifactRoot 'app')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$setup = Join-Path $artifactRoot 'FlashVSR-Desktop-Setup-1.0.0.exe'
Copy-Item -LiteralPath (Join-Path $artifactRoot 'app/FlashVSRDesktop.exe') -Destination $setup -Force
Get-FileHash -LiteralPath $setup -Algorithm SHA256 | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($setup))" } | Set-Content -LiteralPath ($setup + '.sha256') -Encoding ascii
Write-Output "Setup: $setup"
