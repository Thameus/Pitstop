param(
    [string]$Dist
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($Dist)) { $Dist = Join-Path $repo 'dist' }
$Dist = [IO.Path]::GetFullPath($Dist)

$version = ([xml](Get-Content (Join-Path $repo 'src\Directory.Build.props'))).Project.PropertyGroup.Version
$versions = Get-Content (Join-Path $repo 'third-party\VERSIONS.json') -Raw | ConvertFrom-Json
$tar = if ($IsWindows -or $env:OS -eq 'Windows_NT') { Join-Path $env:SystemRoot 'System32\tar.exe' } else { 'tar' }
if (-not (Get-Command $tar -ErrorAction SilentlyContinue)) { throw "tar not found: $tar" }

$windowsArchive = Join-Path $Dist "pitstop-$version-win-x64.zip"
$linuxArchive = Join-Path $Dist "pitstop-$version-linux-x64.tar.xz"
$obsoleteWindowsTar = Join-Path $Dist "pitstop-$version-win-x64.tar.xz"
$installers = @{
    'win-x64' = Join-Path $Dist "pitstop-$version-setup-win-x64.exe"
    'linux-x64' = Join-Path $Dist "pitstop-$version-linux-x64.run"
}
$releaseFiles = @($windowsArchive, $linuxArchive) + @($installers.Values)
if (Test-Path $obsoleteWindowsTar) { throw "obsolete Windows portable archive still present: $obsoleteWindowsTar" }
foreach ($a in $releaseFiles) { if (-not (Test-Path $a)) { throw "release file not found: $a" } }

$sumFile = Join-Path $Dist 'SHA256SUMS.txt'
if (-not (Test-Path $sumFile)) { throw 'SHA256SUMS.txt not found' }
$expectedHash = @{}
foreach ($line in Get-Content $sumFile) {
    if ($line -match '^([0-9a-fA-F]{64})\s+(.+)$') { $expectedHash[$matches[2]] = $matches[1].ToLowerInvariant() }
}
foreach ($a in $releaseFiles) {
    $name = Split-Path $a -Leaf
    $actual = (Get-FileHash $a -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expectedHash[$name] -ne $actual) { throw "SHA-256 mismatch: $name" }
}

$required = @(
    'LICENSE', 'NOTICE', 'LICENCAS.md', 'THIRD-PARTY-NOTICES.md', 'LEIAME.md',
    'third-party/VERSIONS.json',
    'third-party/dotnet/LICENSE.TXT', 'third-party/dotnet/THIRD-PARTY-NOTICES.TXT',
    'third-party/dotnet/LICENSE-INFORMATION-WINDOWS.md', 'third-party/dotnet/DOTNET-LIBRARY-LICENSE.html',
    'third-party/aspnetcore/LICENSE.txt', 'third-party/aspnetcore/THIRD-PARTY-NOTICES.txt',
    'third-party/windowsdesktop/winforms/LICENSE.TXT', 'third-party/windowsdesktop/winforms/THIRD-PARTY-NOTICES.TXT',
    'third-party/windowsdesktop/wpf/LICENSE.TXT', 'third-party/windowsdesktop/wpf/THIRD-PARTY-NOTICES.TXT',
    'third-party/avalonia/LICENSE.md', 'third-party/avalonia/NOTICE.md',
    'third-party/skiasharp/LICENSE.md', 'third-party/skiasharp/EXTERNAL-DEPENDENCY-INFO.txt',
    'third-party/skia/LICENSE', 'third-party/harfbuzz/COPYING', 'third-party/angle/LICENSE',
    'third-party/microcom/LICENSE', 'third-party/tmds-dbus/LICENSE'
)
$forbidden = @(
    '(^|/)\.env$', '(^|/)config/', '(^|/)cache/', '(^|/)logs/', '(^|/)bases/',
    '(^|/)src/', '(^|/)\.git/', '(^|/)\.claude/', '\.pdb$'
)

# Toda biblioteca de runtime precisa estar coberta pelos notices versionados do pacote.
# Se aparecer um pacote novo no .deps.json, o release deve parar até a licença ser revisada.
$coveredLibraryPatterns = @(
    '^Pitstop(?:\.Core)?/',
    '^runtimepack\.Microsoft\.(?:NETCore|AspNetCore)\.App\.Runtime\.',
    '^Avalonia(?:\.|/)',
    '^SkiaSharp(?:\.|/)',
    '^HarfBuzzSharp(?:\.|/)',
    '^MicroCom\.Runtime/',
    '^Tmds\.DBus\.Protocol/'
)
function Assert-LibrariesCovered([string[]]$libraries, [string]$rid) {
    foreach ($lib in $libraries) {
        $covered = $false
        foreach ($pattern in $coveredLibraryPatterns) {
            if ($lib -match $pattern) { $covered = $true; break }
        }
        if (-not $covered) { throw "$rid dependency has no reviewed notice mapping: $lib" }
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$windowsRoot = "pitstop-$version-win-x64"
$zip = [System.IO.Compression.ZipFile]::OpenRead($windowsArchive)
try {
    $windowsEntries = @($zip.Entries | ForEach-Object { $_.FullName.Replace('\', '/').TrimEnd('/') })
    foreach ($rel in $required) {
        if ($windowsEntries -notcontains "$windowsRoot/$rel") { throw "win-x64 package missing $rel" }
    }
    if ($windowsEntries -notcontains "$windowsRoot/ferramentas.ps1") {
        throw 'win-x64 package missing ferramentas.ps1'
    }
    foreach ($pattern in $forbidden) {
        $bad = $windowsEntries | Where-Object { $_ -match $pattern }
        if ($bad) { throw "win-x64 package contains forbidden path: $($bad[0])" }
    }
}
finally {
    $zip.Dispose()
}

$linuxRoot = "pitstop-$version-linux-x64"
$raw = @(& $tar -tf $linuxArchive)
if ($LASTEXITCODE -ne 0) { throw 'tar list failed: linux-x64' }
$linuxEntries = @($raw | ForEach-Object { $_ -replace '^\./', '' })
foreach ($rel in $required) {
    if ($linuxEntries -notcontains "$linuxRoot/$rel") { throw "linux-x64 package missing $rel" }
}
foreach ($pattern in $forbidden) {
    $bad = $linuxEntries | Where-Object { $_ -match $pattern }
    if ($bad) { throw "linux-x64 package contains forbidden path: $($bad[0])" }
}

$linuxVerbose = @(& $tar -tvf $linuxArchive)
if ($LASTEXITCODE -ne 0) { throw 'tar verbose list failed for linux-x64' }
foreach ($rel in @('app/Pitstop','app/pit','instalar.sh','desinstalar.sh','pit')) {
    $line = $linuxVerbose | Where-Object { $_ -match ("/" + [regex]::Escape($rel) + '$') } | Select-Object -First 1
    if (-not $line) { throw "linux executable missing: $rel" }
    if ($line -notmatch '^-rwxr-xr-x') { throw "linux executable mode is not 0755: $rel ($line)" }
}

$tmp = Join-Path ([IO.Path]::GetTempPath()) ("pitstop-package-smoke-" + [Guid]::NewGuid().ToString('N'))
$oldRoot = $env:PIT_RAIZ
$oldAuto = $env:PIT_SEM_AUTOSTART
try {
    New-Item -ItemType Directory -Force $tmp | Out-Null
    Expand-Archive -Path $windowsArchive -DestinationPath $tmp -Force
    $pkg = Join-Path $tmp "pitstop-$version-win-x64"
    # .NET no Windows usa licenciamento misto. Falha fechado se surgir um binario com outra licenca.
    $dotnetLicenseInfo = Join-Path $pkg 'third-party\dotnet\LICENSE-INFORMATION-WINDOWS.md'
    $dotnetLibraryLicense = Join-Path $pkg 'third-party\dotnet\DOTNET-LIBRARY-LICENSE.html'
    $dotnetLibraryBinaries = @('coreclr.dll','Microsoft.DiaSymReader.Native.amd64.dll','PresentationNative_cor3.dll','vcruntime140_cor3.dll','wpfgfx_cor3.dll')
    $foundDotnetLibraryBinaries = @($dotnetLibraryBinaries | Where-Object { Test-Path (Join-Path $pkg ('app\' + $_)) })
    if ($foundDotnetLibraryBinaries.Count -gt 0) {
        if (-not (Test-Path $dotnetLicenseInfo) -or -not (Test-Path $dotnetLibraryLicense)) {
            throw "Windows package contains .NET Library License binaries but Microsoft license files are missing: $($foundDotnetLibraryBinaries -join ', ')"
        }
        $licenseInfoText = Get-Content $dotnetLicenseInfo -Raw
        $libraryLicenseText = Get-Content $dotnetLibraryLicense -Raw
        if ($licenseInfoText -notmatch 'coreclr\.dll' -or $licenseInfoText -notmatch 'Microsoft\.DiaSymReader\.Native') {
            throw 'Microsoft Windows .NET license mapping is incomplete or unexpected'
        }
        if ($libraryLicenseText -notmatch 'MICROSOFT SOFTWARE LICENSE TERMS' -or $libraryLicenseText -notmatch 'MICROSOFT \.NET LIBRARY') {
            throw 'Microsoft .NET Library License snapshot is missing expected terms'
        }
    }
    if (Test-Path (Join-Path $pkg 'app\D3DCompiler_47_cor3.dll')) {
        throw 'Windows package now contains D3DCompiler_47_cor3.dll (Windows SDK License). Add/review the Windows SDK license before release.'
    }

    $env:PIT_RAIZ = $pkg
    $env:PIT_SEM_AUTOSTART = '1'
    & (Join-Path $pkg 'app\pit.exe') status | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "packaged pit.exe status failed with exit code $LASTEXITCODE" }

    & $installers['win-x64'] --smoke
    if ($LASTEXITCODE -ne 0) { throw "Pitstop Setup payload smoke failed with exit code $LASTEXITCODE" }
    & $installers['win-x64'] --smoke-install
    if ($LASTEXITCODE -ne 0) { throw "Pitstop Setup install smoke failed with exit code $LASTEXITCODE" }
    & $installers['win-x64'] --smoke-root-path
    if ($LASTEXITCODE -ne 0) { throw "Pitstop Setup root-path normalization smoke failed with exit code $LASTEXITCODE" }
    & $installers['win-x64'] --smoke-update
    if ($LASTEXITCODE -ne 0) { throw "Pitstop Setup update/preservation smoke failed with exit code $LASTEXITCODE" }

    # Regressão: Windows PowerShell 5.1 falha em New-Item -Force na raiz de uma unidade já existente.
    # Usa SUBST para testar uma raiz real sem tocar em C:\ ou em outros discos do usuário/runner.
    $substRoot = Join-Path $tmp 'subst-root'
    New-Item -ItemType Directory -Force $substRoot | Out-Null
    $substDrive = $null
    foreach ($letter in @('P','Q','R','S','T','U','V','W','X','Y','Z')) {
        if (-not (Test-Path ($letter + ':\'))) { $substDrive = $letter + ':'; break }
    }
    if (-not $substDrive) { throw 'no free drive letter for root-path smoke' }
    try {
        & subst.exe $substDrive $substRoot
        if ($LASTEXITCODE -ne 0) { throw "subst failed for $substDrive" }
        $rootDest = $substDrive + '\'
        powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $pkg 'instalar.ps1') -Destino $rootDest -Silencioso -NaoAbrir -SemPath -SemMenu -SemRegistro
        if ($LASTEXITCODE -ne 0) { throw "instalar.ps1 root destination failed with exit code $LASTEXITCODE" }
        if (-not (Test-Path (Join-Path $rootDest 'app\Pitstop.exe'))) { throw 'root destination smoke did not install Pitstop.exe' }
    }
    finally {
        & subst.exe $substDrive /D 2>$null
    }

    $deps = Get-Content (Join-Path $pkg 'app\Pitstop.deps.json') -Raw | ConvertFrom-Json
    $libs = @($deps.libraries.PSObject.Properties.Name)
    Assert-LibrariesCovered $libs 'win-x64'
    $expectedLibs = @(
        "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/$($versions.dotnet)",
        "runtimepack.Microsoft.AspNetCore.App.Runtime.win-x64/$($versions.aspnetcore)",
        "Avalonia/$($versions.avalonia)",
        "Avalonia.Angle.Windows.Natives/$($versions.avaloniaAngleWindowsNatives)",
        "SkiaSharp/$($versions.skiasharp)",
        "SkiaSharp.NativeAssets.Win32/$($versions.skiasharp)",
        "HarfBuzzSharp/$($versions.harfbuzzsharp)",
        "HarfBuzzSharp.NativeAssets.Win32/$($versions.harfbuzzsharp)",
        "MicroCom.Runtime/$($versions.microcomRuntime)",
        "Tmds.DBus.Protocol/$($versions.tmdsDbusProtocol)"
    )
    foreach ($lib in $expectedLibs) {
        if ($libs -notcontains $lib) { throw "third-party version mismatch or dependency missing: $lib" }
    }

    $linuxRoot = "pitstop-$version-linux-x64"
    $linuxDepsText = (@(& $tar -xOf $linuxArchive "./$linuxRoot/app/Pitstop.deps.json") -join [Environment]::NewLine)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($linuxDepsText)) { throw 'could not read Linux Pitstop.deps.json' }
    $linuxDeps = $linuxDepsText | ConvertFrom-Json
    $linuxLibs = @($linuxDeps.libraries.PSObject.Properties.Name)
    Assert-LibrariesCovered $linuxLibs 'linux-x64'
    foreach ($lib in @(
        "runtimepack.Microsoft.NETCore.App.Runtime.linux-x64/$($versions.dotnet)",
        "runtimepack.Microsoft.AspNetCore.App.Runtime.linux-x64/$($versions.aspnetcore)",
        "SkiaSharp.NativeAssets.Linux/$($versions.skiasharp)",
        "HarfBuzzSharp.NativeAssets.Linux/$($versions.harfbuzzsharp)"
    )) {
        if ($linuxLibs -notcontains $lib) { throw "Linux third-party version mismatch or dependency missing: $lib" }
    }

    Write-Host 'PACKAGE SMOKE OK: hashes, privacy, licenses/notices, Linux modes, dependencies, Windows CLI, Setup EXE, isolated install and drive-root regression.' -ForegroundColor Green
}
finally {
    $env:PIT_RAIZ = $oldRoot
    $env:PIT_SEM_AUTOSTART = $oldAuto
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
