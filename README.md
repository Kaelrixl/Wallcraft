# Wallcraft

用于浏览 Wallpaper Engine 本地壁纸、恢复 PKG / MPKG 项目和提取原始图片、视频的 Windows 桌面工具，使用 C# 与 WinForms。

## 下载与使用

从 [Releases](https://github.com/Kaelrixl/Wallcraft/releases) 下载 `Wallcraft-版本号-win-x64.zip`，完整解压到可写目录，运行 `Wallcraft.exe`。

- 适用于 Windows 10 / 11 x64，发布包包含 .NET 桌面运行时，无需另外安装 .NET、Python 或开发工具。
- 请保留整个文件夹，包括 `app` 和 `repkg-ng` 子目录，不要只复制 EXE。
- “已安装”页从本机 Steam / Wallpaper Engine 目录发现壁纸；直接处理 PKG / MPKG 文件可以自行选择输入和输出位置。
- 解包后的实际播放需要 Wallpaper Engine，本程序不包含它。

## 功能

- 浏览本地壁纸并导出 PNG 或原始 MP4。
- 支持场景包和已解包场景目录；转换纹理副本，不修改原壁纸素材。
- 多选或拖入 PKG / MPKG，恢复项目；批量导出订阅媒体。
- 已存在的目标目录不覆盖。
- 三个输出路径分别记忆，保留上次关闭时的窗口屏幕与位置，支持多屏 DPI 缩放。

## 便携配置

配置文件与 `Wallcraft.exe` 放在同一目录，程序不使用 AppData 保存设置：

| 文件 | 内容 |
| --- | --- |
| `settings.json` | 已安装页保存路径、项目保存路径、媒体保存路径 |
| `window.json` | 窗口所在屏幕及位置 |

文件在保存时生成。整体复制程序文件夹即可带走配置。公开发布包不包含个人配置，Git 也忽略这两个文件。

## 从源码构建

需要 Windows 和 .NET 10 SDK：

```powershell
 git clone --recurse-submodules https://github.com/Kaelrixl/Wallcraft.git
 cd Wallcraft
 powershell -ExecutionPolicy Bypass -File scripts/Publish.ps1
```

已克隆但未下载子模块时执行 `git submodule update --init --recursive`。可通过 `-Dotnet` 指定 dotnet.exe 路径，通过 `-Version 0.1.2` 指定版本。

脚本从固定版本的 RePKG 源码构建解包工具，再生成目录式自包含程序（根目录放 Wallcraft.exe、repkg-ng、说明及许可文件；app 仅存放 .NET 运行库和主程序必需的托管文件），在 `artifacts` 下输出干净的便携目录、ZIP 和 SHA256 校验文件。首次构建需要联网还原 NuGet 包，不会把日常使用目录中的个人配置打入发布包。

`Assets/Wallcraft.png` 是公开版图标源图；`scripts/NewIcon.ps1` 将其转换为多尺寸 ICO。图标已随源码提供，正常构建无需运行此脚本。

## 限制

- 导出场景素材不等于渲染完整动画。TEX 内嵌的视频可以提取为原始 MP4，实时粒子、交互和场景特效不会合成为视频。
- 视频壁纸只有源文件为 MP4 时直接导出，不进行转码。
- 部分 Android MPKG 的纹理、特效或着色器与桌面版不兼容；恢复后请在 Wallpaper Engine 中检查。
- 部分 ETC2 纹理可能存在解码色点，导出后请检查图片。

## 许可证

Wallcraft 源码与原创图标采用 [MIT 许可证](LICENSE)。第三方组件保留各自许可证，详见 [第三方说明](THIRD-PARTY-NOTICES.md)。

本项目与 Wallpaper Engine、Steam 及 Valve 无隶属关系，不附带壁纸素材。壁纸素材的权利归原作者所有，软件许可证不授予素材再分发权。

