# 开发说明（构建前提）

> 本文件只写「怎么构建」。功能、架构、测试、贡献说明见阶段四文档。

## 1. 引用程序集（`libs/`，不入库）

构建需要四个 DLL，放在仓库根目录的 `libs/` 下（该目录已在 `.gitignore` 中排除）：

| 文件 | 本机实测来源路径 |
|---|---|
| `Assembly-CSharp.dll` | `D:\Game\RimWorld\Assembly-CSharp.dll`（游戏安装根目录） |
| `RimWorld.dll` | `D:\Game\RimWorld\RimWorld.dll`（同上） |
| `UnityStubs.dll` | `D:\Game\RimWorld\UnityStubs.dll`（同上） |
| `0Harmony.dll` | `D:\Game\RimWorld\Mods\Harmony\1.7\Assemblies\0Harmony.dll`（Harmony mod 的 1.7 分支） |

> 路径是本机 RimWorld 1.7（Waylith 引擎）实测值。你的安装位置不同就按同样的「游戏根目录 / Harmony mod」两处找同名文件。

## 2. 构建

```bat
dotnet build BuildMonitor.csproj -c Release
```

- SDK：.NET 10（本机 `dotnet --version` = `10.0.301`）
- 产物：`bin\BuildMonitor.dll`
- 发布时把产物复制为 mod 的 `Assemblies\BuildMonitor.dll`

## 3. 验证产物

```bat
certutil -hashfile bin\BuildMonitor.dll MD5
```

对照 `ARCHIVE.md` 里记录的 md5（`80b5956514db783df6ddfd451b1fb366`）。重建结果与之不一致时先查三件事：程序集版本是否被改、是否有文件被多编/漏编、是否重复编译同一个类——**不要为了凑 md5 去改代码**。

## 4. 目录说明

| 目录 | 内容 |
|---|---|
| `Source/` | 全部 C# 源码（含 `ResearchStyleTabWindow.cs`，原开发副本里位于 `src/shared/`） |
| `About/` | `About.xml`、`Preview.png` |
| `Defs/` | Def 定义 |
| `Languages/` | 中英文 Keyed 文案 |
| `Assemblies/` | 已构建并验证过的 DLL |
| `libs/` | 构建引用（不入库，需自行放置） |
