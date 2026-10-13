# 第三方组件说明

Wallcraft 自身代码和 Assets/Wallcraft.ico 原创图标采用根目录 MIT 许可证。第三方组件保留原作者版权和许可证，不受 Wallcraft 许可证替代。

## RePKG

- 源码：https://github.com/addallno/repkg-ng
- 固定版本通过 RePKG/repkg-ng Git 子模块记录。
- 基于 notscuffed 的 RePKG，许可证为 MIT，完整正文在子模块 LICENSE 中，发布包包含 repkg-ng/LICENSE。
- RePKG 使用 CommandLineParser、K4os.Compression.LZ4、Newtonsoft.Json、SixLabors.ImageSharp 等组件。上游提供的版权和许可证正文随发布包保留在 repkg-ng/THIRD-PARTY-NOTICES.txt。
- 当前源码使用 ImageSharp 2.1.9；其许可证按该版本的 NuGet 包和上游声明保留，不应套用其他版本的许可条款。
- ETC2 的 H 模式修正参考了 AssetStudio 的纹理解码实现；其 MIT 许可和版权声明见 `licenses/AssetStudio-LICENSE`。

## .NET

目录式自包含发布包含 Microsoft .NET / Windows Desktop 运行库，以及 RePKG 使用的 .NET 运行库。运行库自身的许可证与第三方说明随发布文件保留；这些文件不应从再分发包中移除。

## 素材与商标

发布包不包含壁纸、个人路径配置或旧的 Monterey 图标。Wallpaper Engine、Steam 等名称属于各自权利人，仅用于说明兼容性。
