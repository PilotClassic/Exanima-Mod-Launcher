# Builds the launcher into release-files\, ready to attach to a GitHub release:
#   release-files\ExanimaLauncher.exe          the launcher
#   release-files\ExanimaLauncher.exe.sha256   its checksum (the launcher's updater checks downloads against it)
#   release-files\ExanimaLauncher.ini          the launcher's settings (update repository)
# Needs only Windows: it uses the C# compiler of .NET Framework 4.8.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$out = Join-Path $root 'release-files'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'ExanimaLauncher.exe'
$cscArgs = @('/nologo', '/target:winexe', '/optimize+', "/out:$exe",
             "/win32manifest:$(Join-Path $src 'app.manifest')",
             '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
             (Join-Path $src '*.cs'))
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed: $exe" }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$exe.sha256" -Value "$hash  ExanimaLauncher.exe" -Encoding ascii
Copy-Item (Join-Path $src 'ExanimaLauncher.ini') $out
$version = (Get-Item $exe).VersionInfo.FileVersion
Write-Host "Built ExanimaLauncher.exe $version into $out"
Write-Host "Release tag for GitHub: v$(($version -split '\.')[0..2] -join '.')"
