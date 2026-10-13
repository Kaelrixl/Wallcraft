param([Parameter(Mandatory)][string]$ExecutablePath, [Parameter(Mandatory)][string]$IconPath)
$ErrorActionPreference = 'Stop'
if (-not ('WallcraftIconResources' -as [type])) {
    Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public static class WallcraftIconResources {
    delegate bool Callback(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
    [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, Callback callback, IntPtr param);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
    public static byte[][] Read(string path) {
        var module = LoadLibraryEx(path, IntPtr.Zero, 2);
        if (module == IntPtr.Zero) throw new Exception("Cannot read executable resources");
        var result = new List<byte[]>();
        try {
            Callback callback = (m, type, name, p) => {
                var resource = FindResource(m, name, type);
                var bytes = new byte[SizeofResource(m, resource)];
                Marshal.Copy(LockResource(LoadResource(m, resource)), bytes, 0, bytes.Length);
                result.Add(bytes);
                return true;
            };
            EnumResourceNames(module, new IntPtr(3), callback, IntPtr.Zero);
            return result.ToArray();
        } finally { FreeLibrary(module); }
    }
}
'@
}
$icon = [IO.File]::ReadAllBytes($IconPath)
$count = [BitConverter]::ToUInt16($icon, 4)
$resources = [WallcraftIconResources]::Read($ExecutablePath)
if ($resources.Length -ne $count) { throw 'EXE 图标数量与 ICO 不一致' }
$encoded = @($resources | ForEach-Object { [Convert]::ToBase64String($_) })
for ($i = 0; $i -lt $count; $i++) {
    $length = [BitConverter]::ToUInt32($icon, 6 + 16 * $i + 8)
    $offset = [BitConverter]::ToUInt32($icon, 6 + 16 * $i + 12)
    if ([Convert]::ToBase64String($icon, $offset, $length) -notin $encoded) { throw "EXE 内嵌图标与 ICO 不一致：第 $i 项" }
}
Write-Output "图标资源校验通过：$count 项均与 $IconPath 完全一致"
