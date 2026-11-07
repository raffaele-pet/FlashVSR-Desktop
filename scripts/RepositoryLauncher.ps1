param(
    [ValidateSet('Install', 'Launch')][string]$Action = 'Launch',
    [switch]$NoLaunch,
    [switch]$NoUi
)
$ErrorActionPreference = 'Stop'
$script:RepositoryRoot = Split-Path -Parent $PSScriptRoot
$script:ReleasePage = 'https://github.com/raffaele-pet/FlashVSR-Desktop/releases/latest'

function Test-FlashVsrRoot {
    param([string]$Root)
    if ([string]::IsNullOrWhiteSpace($Root)) { return $false }
    foreach ($relative in @('FlashVSRDesktop.exe', 'flashvsr-install.json', '.venv\Scripts\python.exe', 'engine\bridge\worker.py')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $relative) -PathType Leaf)) { return $false }
    }
    try { $manifest = Get-Content -LiteralPath (Join-Path $Root 'flashvsr-install.json') -Raw | ConvertFrom-Json }
    catch { return $false }
    return -not [string]::IsNullOrWhiteSpace($manifest.Version)
}

function Find-FlashVsrApplication {
    param(
        [string]$DefaultRoot = (Join-Path $env:LOCALAPPDATA 'Programs\FlashVSR Desktop'),
        [string[]]$ShortcutPaths = @(
            (Join-Path ([Environment]::GetFolderPath('Programs')) 'FlashVSR Desktop.lnk'),
            (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'FlashVSR Desktop.lnk')
        )
    )
    # The installer always creates a Start-menu shortcut, including when
    # a custom installation folder was chosen. Never execute its arguments.
    foreach ($shortcutPath in $ShortcutPaths) {
        if (-not (Test-Path -LiteralPath $shortcutPath -PathType Leaf)) { continue }
        $shell = $null; $shortcut = $null
        try {
            $shell = New-Object -ComObject WScript.Shell
            $shortcut = $shell.CreateShortcut($shortcutPath)
            $target = [string]$shortcut.TargetPath
            if ([IO.Path]::GetFileName($target) -ieq 'FlashVSRDesktop.exe') {
                $root = Split-Path -Parent $target
                if (Test-FlashVsrRoot $root) { return (Join-Path $root 'FlashVSRDesktop.exe') }
            }
        } catch { continue }
        finally {
            if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
            if ($null -ne $shell) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
        }
    }
    if (Test-FlashVsrRoot $DefaultRoot) { return (Join-Path $DefaultRoot 'FlashVSRDesktop.exe') }
    return $null
}

function Assert-InstallerChecksum {
    param([string]$Installer, [string]$ChecksumFile)
    $line = (Get-Content -LiteralPath $ChecksumFile -Raw).Trim()
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { throw 'Invalid release SHA-256 file.' }
    $expected = $matches[1]; $name = $matches[2].Trim()
    if ($name -cne [IO.Path]::GetFileName($Installer)) { throw 'The checksum belongs to a different file.' }
    $actual = (Get-FileHash -LiteralPath $Installer -Algorithm SHA256).Hash
    if ($actual -ine $expected) { throw 'SHA-256 verification failed. The installer will not run.' }
}

function Select-InstallerAssets {
    param($Release)
    $installers = @($Release.assets | Where-Object { $_.name -cmatch '^FlashVSR-Desktop-Setup-([0-9]+\.[0-9]+\.[0-9]+)\.exe$' })
    if ($installers.Count -ne 1) { throw 'Windows installer not found in the GitHub release.' }
    $installer = $installers[0]
    $version = [regex]::Match($installer.name, '([0-9]+\.[0-9]+\.[0-9]+)\.exe$').Groups[1].Value
    if ($Release.tag_name -cne ('v' + $version)) { throw 'Installer version does not match the release.' }
    $checksums = @($Release.assets | Where-Object { $_.name -ceq ($installer.name + '.sha256') })
    if ($checksums.Count -ne 1) { throw 'Release checksum not found.' }
    foreach ($asset in @($installer, $checksums[0])) {
        $uri = [uri]$asset.browser_download_url
        $expectedPath = '/raffaele-pet/FlashVSR-Desktop/releases/download/' + $Release.tag_name + '/' + $asset.name
        if ($uri.Scheme -cne 'https' -or $uri.Host -ine 'github.com' -or $uri.AbsolutePath -cne $expectedPath) {
            throw 'Unexpected download address. Use the official Releases page.'
        }
    }
    return [pscustomobject]@{ Version = $version; Installer = $installer; Checksum = $checksums[0] }
}

function Initialize-BootstrapUi {
    if ('FlashVsrRepository.DownloadWindow' -as [type]) { return }
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    # Native C# event handlers keep downloads responsive without depending
    # on PowerShell runspaces on background threads. No additional runtime.
    Add-Type -ReferencedAssemblies System.Windows.Forms,System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;
namespace FlashVsrRepository {
    public static class DownloadWindow {
        public static void Download(string url, string path, string iconPath) {
            Exception failure = null;
            bool cancelled = false;
            using (var form = new Form())
            using (var client = new WebClient()) {
                form.Text = "FlashVSR Desktop - Setup";
                form.ClientSize = new Size(510, 205);
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false; form.MinimizeBox = false;
                form.StartPosition = FormStartPosition.CenterScreen;
                form.BackColor = Color.FromArgb(17,18,21);
                form.ForeColor = Color.FromArgb(241,241,244);
                form.Font = new Font("Segoe UI", 10);
                if (File.Exists(iconPath)) form.Icon = new Icon(iconPath);
                var title = new Label { Text = "Download FlashVSR Desktop", AutoSize = true,
                    Location = new Point(24,23), Font = new Font("Segoe UI", 13, FontStyle.Bold) };
                var status = new Label { Text = "Downloading the installer from GitHub...",
                    Location = new Point(24,64), Size = new Size(460,38),
                    ForeColor = Color.FromArgb(152,153,165) };
                var progress = new ProgressBar { Location = new Point(24,110), Size = new Size(460,8),
                    Style = ProgressBarStyle.Marquee };
                var cancel = new Button { Text = "Cancel", Location = new Point(380,145),
                    Size = new Size(104,34), FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(36,37,44), ForeColor = form.ForeColor };
                form.Controls.AddRange(new Control[] { title, status, progress, cancel });
                client.Headers[HttpRequestHeader.UserAgent] = "FlashVSR-Desktop-Repository-Launcher";
                client.DownloadProgressChanged += (s,e) => {
                    if (e.TotalBytesToReceive > 0) {
                        progress.Style = ProgressBarStyle.Continuous;
                        progress.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage));
                        status.Text = string.Format("Installer download: {0:F1} / {1:F1} MB ({2}%)",
                            e.BytesReceived/1048576d, e.TotalBytesToReceive/1048576d, e.ProgressPercentage);
                    }
                };
                client.DownloadFileCompleted += (s,e) => { failure=e.Error; cancelled=e.Cancelled; form.Close(); };
                cancel.Click += (s,e) => { cancelled=true; cancel.Enabled=false; client.CancelAsync(); };
                form.FormClosing += (s,e) => { if (client.IsBusy) { e.Cancel=true; cancelled=true; client.CancelAsync(); } };
                form.Shown += (s,e) => {
                    try { client.DownloadFileAsync(new Uri(url), path); }
                    catch (Exception error) { failure=error; form.Close(); }
                };
                form.ShowDialog();
            }
            if (cancelled) throw new OperationCanceledException("Download cancelled.");
            if (failure != null) throw new IOException("Installer download failed.", failure);
        }
    }
}
'@
}

function Get-FlashVsrInstaller {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $release = Invoke-RestMethod 'https://api.github.com/repos/raffaele-pet/FlashVSR-Desktop/releases/latest' -Headers @{
        'User-Agent' = 'FlashVSR-Desktop-Repository-Launcher'; Accept = 'application/vnd.github+json'
    } -TimeoutSec 30
    $assets = Select-InstallerAssets $release
    $folder = Join-Path $env:LOCALAPPDATA ('FlashVSR Desktop\downloads\releases\v' + $assets.Version)
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $installer = Join-Path $folder $assets.Installer.name
    $checksum = Join-Path $folder $assets.Checksum.name
    Invoke-WebRequest $assets.Checksum.browser_download_url -OutFile $checksum -UseBasicParsing -TimeoutSec 30
    if (Test-Path -LiteralPath $installer -PathType Leaf) {
        try { Assert-InstallerChecksum $installer $checksum; return $installer }
        catch { Write-Verbose 'Installer in cache non valido: nuovo download.' }
    }
    $partial = $installer + '.download'
    try {
        if ($NoUi) {
            $client = New-Object Net.WebClient
            try { $client.DownloadFile($assets.Installer.browser_download_url, $partial) }
            finally { $client.Dispose() }
        } else {
            Initialize-BootstrapUi
            [FlashVsrRepository.DownloadWindow]::Download($assets.Installer.browser_download_url, $partial,
                (Join-Path $script:RepositoryRoot 'desktop\Assets\flashvsr.ico'))
        }
        # Validate using the final name: the checksum names the .exe, not
        # the temporary download. An existing cached copy is never executed
        # unless its content matches the checksum of the selected release.
        $expected = (Get-Content -LiteralPath $checksum -Raw).Trim()
        if ($expected -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$' -or $matches[2].Trim() -cne $assets.Installer.name) { throw 'Invalid release checksum.' }
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ine $matches[1]) { throw 'Downloaded file failed SHA-256 verification.' }
        Move-Item -LiteralPath $partial -Destination $installer -Force
        Assert-InstallerChecksum $installer $checksum
        return $installer
    } finally {
        if (Test-Path -LiteralPath $partial -PathType Leaf) { Remove-Item -LiteralPath $partial -Force }
    }
}

function Invoke-FlashVsrInstall {
    $installer = Get-FlashVsrInstaller
    if ($NoLaunch) { return $installer }
    # The graphical installer installs dependencies/models and creates the
    # desktop and Start-menu shortcuts. Its desktop option is enabled by default.
    $process = Start-Process -FilePath $installer -ArgumentList '--setup' -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw ('Installation failed (code ' + $process.ExitCode + ').') }
}

function Invoke-FlashVsrLaunch {
    $application = Find-FlashVsrApplication
    if ($null -eq $application) {
        if ($NoUi) { throw 'FlashVSR Desktop is not installed. Run Installa.bat.' }
        Add-Type -AssemblyName System.Windows.Forms
        $answer = [Windows.Forms.MessageBox]::Show(
            "FlashVSR Desktop is not installed. Open the setup wizard?",
            'FlashVSR Desktop', [Windows.Forms.MessageBoxButtons]::YesNo, [Windows.Forms.MessageBoxIcon]::Question)
        if ($answer -eq [Windows.Forms.DialogResult]::Yes) { Invoke-FlashVsrInstall }
        return
    }
    if ($NoLaunch) { return $application }
    Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application)
}

# Dot-sourcing loads only the functions, enabling tests without starting a UI.
if ($MyInvocation.InvocationName -eq '.') { return }
try {
    if ($Action -eq 'Install') { Invoke-FlashVsrInstall } else { Invoke-FlashVsrLaunch }
} catch [OperationCanceledException] {
    exit 0
} catch {
    if ($NoUi) { Write-Error $_; exit 1 }
    Add-Type -AssemblyName System.Windows.Forms
    [void][Windows.Forms.MessageBox]::Show(
        $_.Exception.Message + "`r`n`r`nInstaller on GitHub:`r`n" + $script:ReleasePage,
        'FlashVSR Desktop', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Error)
    exit 1
}
