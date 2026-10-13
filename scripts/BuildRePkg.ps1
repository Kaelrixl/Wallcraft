param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [string]$Dotnet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$submodule = Join-Path $root 'RePKG\repkg-ng'
$patch = Join-Path $root 'patches\repkg-etc2.patch'
$stage = Join-Path $root ('artifacts\repkg-source-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $stage 'source'
$archive = Join-Path $stage 'source.tar'
$normalizedPatch = Join-Path $stage 'repkg-etc2.patch'
if (-not (Test-Path (Join-Path $submodule 'RePKG\RePKG.csproj'))) { throw '请先初始化 RePKG 子模块' }
New-Item -ItemType Directory -Force -Path $source | Out-Null
try {
    & git -C $submodule archive HEAD -o $archive
    if ($LASTEXITCODE -ne 0) { throw 'RePKG 源码归档失败' }
    & tar -xf $archive -C $source
    if ($LASTEXITCODE -ne 0) { throw 'RePKG 源码展开失败' }
    $patchText = [IO.File]::ReadAllText($patch).Replace("`r`n", "`n")
    [IO.File]::WriteAllText($normalizedPatch, $patchText, [Text.UTF8Encoding]::new($false))
    $sourceRelative = [IO.Path]::GetRelativePath($root, $source).Replace('\', '/')
    & git -C $root apply "--directory=$sourceRelative" --check $normalizedPatch
    if ($LASTEXITCODE -ne 0) { throw 'ETC2 修正补丁与当前 RePKG 版本不匹配' }
    & git -C $root apply "--directory=$sourceRelative" $normalizedPatch
    if ($LASTEXITCODE -ne 0) { throw 'ETC2 修正补丁应用失败' }
    $decoderSource = [IO.File]::ReadAllText((Join-Path $source 'RePKG.Application\Texture\TextureDecoder.cs'))
    if (-not $decoderSource.Contains('uint[] tcw = { table1, table2 };')) { throw 'ETC2 补丁未写入实际编译源码' }
    & $Dotnet publish (Join-Path $source 'RePKG\RePKG.csproj') -c Release -f net8.0 -r win-x64 --self-contained true -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'RePKG 发布失败' }
    Move-Item -LiteralPath (Join-Path $OutputDirectory 'RePKG.exe') -Destination (Join-Path $OutputDirectory 'repkg-win-x64.exe') -Force
}
finally {
    $resolvedStage = [IO.Path]::GetFullPath($stage)
    $artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')).TrimEnd('\') + '\'
    if ($resolvedStage.StartsWith($artifacts, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolvedStage)) {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
