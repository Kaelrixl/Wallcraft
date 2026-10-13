param(
    [string]$Version = '0.1.0',
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '版本号必须为数字格式，例如 0.1.0' }
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root ('artifacts\build-' + [guid]::NewGuid().ToString('N'))
$tool = Join-Path $run 'repkg'
$package = Join-Path $run "Wallcraft-$Version-win-x64"
$toolProject = Join-Path $root 'RePKG\repkg-ng\RePKG\RePKG.csproj'
if (-not (Test-Path $toolProject)) { throw '请先执行 git submodule update --init --recursive' }
& $Dotnet publish $toolProject -c Release -f net8.0 -r win-x64 --self-contained true -o $tool
if ($LASTEXITCODE -ne 0) { throw '解包工具发布失败' }
Move-Item -LiteralPath (Join-Path $tool 'RePKG.exe') -Destination (Join-Path $tool 'repkg-win-x64.exe')
& $Dotnet publish (Join-Path $root 'Wallcraft.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" "-p:RePkgRuntimeDirectory=$tool" -o $package
if ($LASTEXITCODE -ne 0) { throw 'Wallcraft 发布失败' }
foreach ($name in @('LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $root $name) -Destination $package
}
Copy-Item -LiteralPath (Join-Path $root 'RePKG\repkg-ng\LICENSE') -Destination (Join-Path $package 'repkg-ng\LICENSE')
$privateFiles = Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -in @('settings.json', 'window.json', 'settings.json.tmp') }
if ($privateFiles) { throw '发布目录意外包含个人配置，已中止打包' }
$runtime = Get-Content (Join-Path $package 'Wallcraft.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks) { throw '发布结果不是自包含版本' }
$assets = Get-Content (Join-Path $root 'obj\project.assets.json') -Raw | ConvertFrom-Json
$packageRoots = @($assets.packageFolders.PSObject.Properties.Name)
foreach ($config in @((Join-Path $package 'Wallcraft.runtimeconfig.json'), (Join-Path $package 'repkg-ng\RePKG.runtimeconfig.json'))) {
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
$zip = Join-Path $root "artifacts\Wallcraft-$Version-win-x64.zip"
if (Test-Path -LiteralPath $zip) { throw "发布包已存在，未覆盖：$zip" }
Compress-Archive -LiteralPath $package -DestinationPath $zip
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $([IO.Path]::GetFileName($zip))" | Set-Content "$zip.sha256" -Encoding ascii
Write-Output "发布目录：$package"
Write-Output "发布包：$zip"
