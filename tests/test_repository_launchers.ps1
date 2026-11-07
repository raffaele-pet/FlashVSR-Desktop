$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $projectRoot 'scripts\RepositoryLauncher.ps1') -NoUi
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts'))
$testRoot = [IO.Path]::GetFullPath((Join-Path $artifactRoot ('launcher-tests-' + [guid]::NewGuid().ToString('N'))))
if (-not $testRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid test directory' }
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$originalLocation = Get-Location
$originalPath = $env:PATH
$originalCapture = $env:FLASHVSR_ARGUMENT_CAPTURE
$checks = 0
function Assert-True { param([bool]$Condition, [string]$Message); if (-not $Condition) { throw $Message }; $script:checks++ }
function Assert-Throws { param([scriptblock]$Operation, [string]$Message); $threw = $false; try { & $Operation | Out-Null } catch { $threw = $true }; Assert-True $threw $Message }
function New-FakeInstall {
    param([string]$Path)
    foreach ($name in @('FlashVSRDesktop.exe','.venv\Scripts\python.exe','engine\bridge\worker.py')) {
        $file = Join-Path $Path $name
        New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
        [IO.File]::WriteAllText($file, 'test fixture - never executed')
    }
    [IO.File]::WriteAllText((Join-Path $Path 'flashvsr-install.json'), '{"Version":"1.0.1"}')
}
try {
    Initialize-BootstrapUi
    Assert-True ($null -ne ('FlashVsrRepository.DownloadWindow' -as [type])) 'Download UI did not compile in Windows PowerShell.'
    $default = Join-Path $testRoot 'default install'
    New-FakeInstall $default
    Assert-True ((Find-FlashVsrApplication -DefaultRoot $default -ShortcutPaths @()) -eq (Join-Path $default 'FlashVSRDesktop.exe')) 'Default install was not found.'
    Assert-True ($null -eq (Find-FlashVsrApplication -DefaultRoot (Join-Path $testRoot 'missing') -ShortcutPaths @())) 'Missing install was incorrectly accepted.'
    Remove-Item -LiteralPath (Join-Path $default '.venv\Scripts\python.exe')
    Assert-True (-not (Test-FlashVsrRoot $default)) 'Incomplete install was incorrectly accepted.'
    New-FakeInstall $default
    [IO.File]::WriteAllText((Join-Path $default 'flashvsr-install.json'), 'broken json')
    Assert-True (-not (Test-FlashVsrRoot $default)) 'Broken manifest was incorrectly accepted.'
    New-FakeInstall $default

    $custom = Join-Path $testRoot ('Custom ' + [char]0xe0 + ' & spazio !100%')
    New-FakeInstall $custom
    $lnk = Join-Path $testRoot 'Custom app.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($lnk)
    $shortcut.TargetPath = Join-Path $custom 'FlashVSRDesktop.exe'
    $shortcut.Arguments = '--setup'
    $shortcut.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
    Assert-True ((Find-FlashVsrApplication -DefaultRoot $default -ShortcutPaths @($lnk)) -eq (Join-Path $custom 'FlashVSRDesktop.exe')) 'Custom install was not preferred over the default install.'

    $installer = Join-Path $testRoot 'FlashVSR-Desktop-Setup-1.0.1.exe'
    [IO.File]::WriteAllText($installer, 'checked installer test payload')
    $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
    $checksum = $installer + '.sha256'
    [IO.File]::WriteAllText($checksum, ($hash + '  ' + [IO.Path]::GetFileName($installer)))
    Assert-InstallerChecksum $installer $checksum
    $checks++
    [IO.File]::AppendAllText($installer, 'tampered')
    Assert-Throws { Assert-InstallerChecksum $installer $checksum } 'Tampered installer was accepted.'
    [IO.File]::WriteAllText($checksum, ($hash + '  other.exe'))
    Assert-Throws { Assert-InstallerChecksum $installer $checksum } 'Wrong checksum filename was accepted.'

    $release = [pscustomobject]@{ tag_name = 'v1.0.1'; assets = @(
        [pscustomobject]@{ name='FlashVSR-Desktop-Setup-1.0.1.exe'; browser_download_url='https://github.com/raffaele-pet/FlashVSR-Desktop/releases/download/v1.0.1/FlashVSR-Desktop-Setup-1.0.1.exe' },
        [pscustomobject]@{ name='FlashVSR-Desktop-Setup-1.0.1.exe.sha256'; browser_download_url='https://github.com/raffaele-pet/FlashVSR-Desktop/releases/download/v1.0.1/FlashVSR-Desktop-Setup-1.0.1.exe.sha256' }
    ) }
    Assert-True ((Select-InstallerAssets $release).Version -eq '1.0.1') 'Valid release was rejected.'
    $release.assets[0].browser_download_url = 'https://example.invalid/untrusted.exe'
    Assert-Throws { Select-InstallerAssets $release } 'Unexpected download host was accepted.'
    $release.assets[0].browser_download_url = 'https://github.com/raffaele-pet/FlashVSR-Desktop/releases/download/v1.0.1/FlashVSR-Desktop-Setup-1.0.1.exe'
    $release.tag_name = 'v1.0.2'
    Assert-Throws { Select-InstallerAssets $release } 'Release/version mismatch was accepted.'
    $release.tag_name = 'v1.0.1'
    $release.assets = @($release.assets[0])
    Assert-Throws { Select-InstallerAssets $release } 'Missing checksum was accepted.'

    # Run the actual BAT files from another working directory. A tiny native
    # argument recorder replaces powershell.exe only in this test process's
    # PATH, so the test neither installs software nor opens the real app.
    $repoCopy = Join-Path $testRoot ('Downloaded repository ' + [char]0xe0 + ' & !100%')
    New-Item -ItemType Directory -Path (Join-Path $repoCopy 'scripts') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'Install.bat'),(Join-Path $projectRoot 'Launch.bat') -Destination $repoCopy
    [IO.File]::WriteAllText((Join-Path $repoCopy 'scripts\RepositoryLauncher.ps1'), '# argument fixture')
    $fakeBin = Join-Path $testRoot 'fake-bin'; New-Item -ItemType Directory -Path $fakeBin | Out-Null
    Add-Type -OutputAssembly (Join-Path $fakeBin 'powershell.exe') -OutputType WindowsApplication -TypeDefinition @'
using System;
using System.IO;
using System.Diagnostics;
class ArgumentRecorder {
    static void Main(string[] args) {
        string output = Environment.GetEnvironmentVariable("FLASHVSR_ARGUMENT_CAPTURE");
        string[] lines = new string[args.Length+1];
        lines[0]=Process.GetCurrentProcess().Id.ToString();
        Array.Copy(args,0,lines,1,args.Length);
        File.WriteAllLines(output+".pending",lines);
        File.Move(output+".pending",output);
    }
}
'@
    $env:PATH = $fakeBin + ';' + $originalPath
    Set-Location $testRoot
    foreach ($entry in @(@('Install.bat','Install'), @('Launch.bat','Launch'))) {
        $capture = Join-Path $testRoot ($entry[1] + '.args')
        $env:FLASHVSR_ARGUMENT_CAPTURE = $capture
        $command = New-Object Diagnostics.ProcessStartInfo
        $command.FileName = $env:ComSpec
        $command.Arguments = '/d /s /c ""' + (Join-Path $repoCopy $entry[0]) + '""'
        $command.WorkingDirectory = $testRoot
        $command.UseShellExecute = $false
        $command.CreateNoWindow = $true
        $batch = [Diagnostics.Process]::Start($command)
        if (-not $batch.WaitForExit(10000)) { throw 'BAT dispatch timed out' }
        $batch.Dispose()
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        while (-not (Test-Path -LiteralPath $capture) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 30 }
        Assert-True (Test-Path -LiteralPath $capture) ($entry[0] + ' did not dispatch PowerShell.')
        $arguments = [IO.File]::ReadAllLines($capture)
        $fileIndex = [Array]::IndexOf($arguments, '-File')
        $actionIndex = [Array]::IndexOf($arguments, '-Action')
        Assert-True ($fileIndex -gt 0 -and $arguments[$fileIndex+1] -eq (Join-Path $repoCopy 'scripts\RepositoryLauncher.ps1')) ($entry[0] + ' lost its quoted, repository-relative script path.')
        Assert-True ($actionIndex -gt 0 -and $arguments[$actionIndex+1] -eq $entry[1]) ($entry[0] + ' dispatched the wrong action.')
        $recorder = Get-Process -Id ([int]$arguments[0]) -ErrorAction SilentlyContinue
        if ($null -ne $recorder) { [void]$recorder.WaitForExit(2000) }
    }
    Write-Output ('PASS: ' + $checks + ' repository launcher checks.')
} finally {
    Set-Location $originalLocation
    $env:PATH = $originalPath
    $env:FLASHVSR_ARGUMENT_CAPTURE = $originalCapture
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (-not $resolved.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing test cleanup outside artifacts' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
