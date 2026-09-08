# 图片导出测试来源

运行 `dotnet run --project AssetExport.Tests -c Release -- '<new-output>'`。不需要原版资源、网络、Python或PalLibrary.dll；输出保留测试清单及少量合成文件。

`YjFixtures.cs` 两个十六进制流是合成ASCII，经已有 `PalUtil.Encodeyj1` / `Encodeyj2(..., bCompatible:1)` 编码后固定留存。明文为 `PAL asset round trip 0123456789\n` 后接40次 `opaque 0 255; transparent skip;`；共1272字节，YJ1输出148字节，YJ2输出105字节。

生成基线为 `othercat/PalModTools-CS@2d1e72567b30480befbae76fae36e95b05f5e227` 的 `PalLibrary.Native/lib/win-x64/PalLibrary.dll`，SHA256 `5f38ee8f54d5024fc604b26c089e7c1edeb69d04e6795ce35c6bfdd8b2da5a37`。生成一次后运行测试不再调用该编码器；预期明文独立于受测解码实现。没有原游戏美术字节。

两种YJ各自检查全部截断前缀及不一致输出长度。YJ1另有无压缩块、超声明长度块、非法树指针和循环树输入。畸形数据仅进入新的带长度入口。没有向旧指针入口或旧原生DLL传畸形数据。
