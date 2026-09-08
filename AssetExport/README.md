# 原尺寸图片导出

`PalAssetExport` 从明确选定的 MKF chunk 和 PAT 调色板导出 RGBA PNG。适合把旧敌图、角色图或背景交给现有作者工具；不启动游戏，不扫描玩家存档，不回写 MKF。

```powershell
dotnet build AssetExport/AssetExport.csproj -c Release
dotnet run --project AssetExport -c Release -- --mkf '输入/ABC.MKF' --chunk 1 --kind sprite --compression yj1 --family ABC --palette '输入/PAT.MKF' --palette-chunk 0 --variant day --output '输出/abc-00001'
```

输入路径和输出路径应为普通长路径，输出目录必须全新、位于两项输入各自的父目录之外。拒绝链接/junction、设备路径及 Windows 别名写法。失败时已产生的 `.partial-*` 目录保留，供检查；没有覆盖或自动清理行为。路径在开始写入和移动前复核，但不宣称抵御同时替换文件系统目录的其他进程。

格式显式选择：`yj1` 为 YJ_1、`yj2` 为 Win 压缩、`none` 为未压缩；不通过文件夹名称推测 DOS/Win。`sprite` 读取16位字偏移帧组，`rle` 读取单张 RLE，`bitmap320` 要求恰好64000字节背景。`family` 是作者选择的来源标签，支持 ABC/F/MGO/FIRE/RGM/FBP/DATA，不等于这些文件的每种变体都已支持。帧偏移越界、非连续或扩展大帧表会报错，不静默少导一帧。

透明度按 RLE 跳过区域恢复；字面索引0或255仍可见。读取可选DWORD前缀，允许合法字面游程跨行。保留原尺寸，不插值、不亮化、不量化；PAT 按 RGB6×4 转为 RGB8，`day/night` 必须明确。输出 PNG 是 RGBA8，格式可承载真彩；从256色源导出不会凭空增加颜色。没有通用白色抠图。

`export.json` 保留源文件/chunk/解压字节/所选调色板/输出PNG与RGBA哈希，另记实际解析程序集哈希。它是本工具的导出凭据，尚非跨仓 Native 合同，工坊不会自动采信。动作、对象身份、谱系、时长与分发权限须单独审查；文件编号不自动等于对象号。

复用同仓 `Common/Lib/Pal/{MkfReader,FileReader,UnpackDos,UnpackWin,UnpackRle,UnpackBounded}.cs` 和内存辅助，通过 Compile Link 保持解析代码一份。新的长度入口检查YJ读/写、块长度、回溯与结束标记；失败释放分配。旧指针入口保留，未获知长度的旧调用者仍不能保证输入读取边界。`UnpackRle(nint)` 默认255填充保持；新重载只增加填充值，导出入口在调用前完整检查RLE输入。未迁移其他游戏/GUI消费者，也未改变旧 Spirit 导出的色键策略。

可自带 .NET 运行时，使用者无需 Python、C++ 或 .NET SDK：

```powershell
dotnet publish AssetExport/AssetExport.csproj -c Release -r win-x64 --self-contained true -o '输出/独立工具'
```

新入口不引用 Avalonia、Tmds、PalLibrary.dll 或 NuGet 图形库。Windows x64 是当前实测目标，ARM/macOS/Linux 尚无实机结果。当前未裁剪的独立目录约77 MiB；游戏只读取转换后的素材，不依赖该工具。构建继承仓库的编译时间字段，因此程序集/凭据哈希可能变化；相同源和参数的 PNG 内容应一致。GPL及上游署名保持，资源的使用/分发权限不由工具许可证授予。

测试：`dotnet run --project AssetExport.Tests -c Release -- '全新的测试输出目录'`。包含独立编码器产生的合成YJ、截断前缀、RLE覆盖与帧表边界；真实资源和作者/运行时验证分开记录在 PAL Wanxiang 产品仓。
