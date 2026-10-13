param(
    [string]$Version = '0.1.6',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须为数字格式，例如 0.1.0' }
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts\build-' + [guid]::NewGuid().ToString('N'))
$tool = Join-Path $run 'repkg'
$package = Join-Path $run "Wallcraft-$Version-win-x64"
$app = Join-Path $package 'app'
& (Join-Path $PSScriptRoot 'BuildRePkg.ps1') -OutputDirectory $tool -Dotnet $Dotnet
$iconPath = Join-Path $root 'Assets\Wallcraft.ico'
& $Dotnet publish (Join-Path $root 'Wallcraft.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:BaseIntermediateOutputPath=$run\obj\" "-p:Version=$Version" "-p:RePkgRuntimeDirectory=$tool" "-p:PortableRootPath=$package" -o $app
if ($LASTEXITCODE -ne 0) { throw 'Wallcraft 发布失败' }
& (Join-Path $PSScriptRoot 'VerifyIcon.ps1') -ExecutablePath (Join-Path $package 'Wallcraft.exe') -IconPath $iconPath
$toolSource = [IO.Path]::GetFullPath((Join-Path $app 'repkg-ng'))
$toolDestination = [IO.Path]::GetFullPath((Join-Path $package 'repkg-ng'))
$packagePrefix = [IO.Path]::GetFullPath($package).TrimEnd('\') + '\'
if (-not $toolSource.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    -not $toolDestination.StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    (Test-Path -LiteralPath $toolDestination)) { throw '解包工具目录移动检查失败' }
Move-Item -LiteralPath $toolSource -Destination $toolDestination
foreach ($name in @('LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package
}
Copy-Item -LiteralPath (Join-Path $root 'RePKG\repkg-ng\LICENSE') -Destination (Join-Path $package 'repkg-ng\LICENSE')
New-Item -ItemType Directory -Force -Path (Join-Path $package 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'licenses\AssetStudio-LICENSE') -Destination (Join-Path $package 'licenses\AssetStudio-LICENSE')
$privateFiles = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -in @('settings.json', 'window.json', 'settings.json.tmp') }
if ($privateFiles) { throw '发布目录意外包含个人配置，已中止打包' }
$runtime = Get-Content (Join-Path $app 'Wallcraft.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks) { throw '发布结果不是自包含版本' }
$assets = Get-Content (Join-Path $run 'obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
foreach ($config in @((Join-Path $app 'Wallcraft.runtimeconfig.json'), (Join-Path $package 'repkg-ng\RePKG.runtimeconfig.json'))) {
    $configData = Get-Content $config -Raw | ConvertFrom-Json
    foreach ($framework in $configData.runtimeOptions.includedFrameworks) {
        $id = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
        $source = $packageRoots | ForEach-Object { Join-Path $_ "$id\$($framework.version)" } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $source) { throw "找不到运行库许可：$id" }
        $licenseDir = Join-Path $package "licenses\$id-$($framework.version)"
        New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
        Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' } | Copy-Item -Destination $licenseDir
    }
}
$start = [Diagnostics.ProcessStartInfo]::new((Join-Path $package 'Wallcraft.exe'), '--verify-package')
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
$diagnostics = $process.StandardOutput.ReadToEnd()
$errors = $process.StandardError.ReadToEnd()
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "便携入口校验失败：$errors" }
$check = $diagnostics | ConvertFrom-Json
if ($check.Configuration.TrimEnd('\') -ne $package -or
    $check.Runtime.TrimEnd('\') -ne $app -or
    -not $check.Forms.StartsWith($app + '\') -or -not $check.ToolExists) {
    throw "程序未从便携目录加载运行库：$diagnostics"
}
$process.Dispose()
Write-Output "便携入口校验通过：$diagnostics"
# 根目录只保留一个入口，避免误启动 app 内的入口而把配置写错位置。
Remove-Item -LiteralPath (Join-Path $app 'Wallcraft.exe')
$zip = Join-Path $root "artifacts\Wallcraft-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw "发布包已存在，未覆盖：$zip" }
Compress-Archive -LiteralPath $package -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ascii
Write-Output "发布目录：$package"
Write-Output "发布包：$zip"
