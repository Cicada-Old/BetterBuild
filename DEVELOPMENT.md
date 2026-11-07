# 开发说明（构建前提）

> 本文件只写「怎么构建」。功能、架构、测试、贡献说明见阶段四文档。

## 1. 引用程序集（`libs/`，不入库）

构建需要四个 DLL，放在仓库根目录的 `libs/` 下（该目录已被 `.gitignore` 排除）。**分两档，按你的目的选**：

### A. 普通开发者：用游戏自带的程序集

| 文件 | 从哪取 |
|---|---|
| `Assembly-CSharp.dll` | `<游戏安装目录>\`（RimWorld 1.7 的托管程序集直接放在游戏根目录） |
| `RimWorld.dll` | `<游戏安装目录>\`（同上） |
| `UnityStubs.dll` | `<游戏安装目录>\`（同上） |
| `0Harmony.dll` | `<Harmony mod 目录>\1.7\Assemblies\0Harmony.dll` |

这样能编译，产物**功能等价**（此档未逐项实测，**仅验证能编译、未挂进游戏运行**）；但**字节与归档件 `80b5956514db783df6ddfd451b1fb366` 不同**（引用程序集的元数据不同会改变生成结果）。日常开发、调试用这一档即可。

### B. 要字节级复现归档件：用参考壳程序集

必须用与归档时同一套**裁剪参考壳**（`Assembly-CSharp.dll` / `RimWorld.dll` / `UnityStubs.dll`）与同套的 `0Harmony.dll`（开发期内部留存，不随仓库分发），**并保持现行 `BuildMonitor.csproj` 的编译顺序**（`ResearchStyleTabWindow.cs` 被 `Source\**\*.cs` 排除、单独 `Compile` 在末尾）。

参考壳不随仓库分发；只有做「产物与归档件逐字节一致」的核对时才需要它。换行的差异不是原因——换引用的程序集才是。

## 2. 构建

```
dotnet build BuildMonitor.csproj -c Release
```

- SDK：.NET 10（本机实测 `dotnet --version` = `10.0.301`）
- 产物：`bin\BuildMonitor.dll`
- 发布时把产物复制为 mod 的 `Assemblies\BuildMonitor.dll`

## 3. 验证

- **A 档（日常）**：确认编译无警告无错误、进游戏能正常加载与打开建造页/诊断页即可，不必对照归档 md5。
- **B 档（归档核对）**：`certutil -hashfile bin\BuildMonitor.dll MD5` 应等于 `80b5956514db783df6ddfd451b1fb366`。不一致时先查三件事：程序集版本是否被改、文件是否多编/漏编、是否重复编译；仍不一致再查**引用程序集是否 A 档**、**编译单元顺序是否被打乱**（详见 `ARCHIVE.md` 附 D）。**不要为了凑 md5 改代码。**

## 4. 目录说明

| 目录 | 内容 |
|---|---|
| `Source/` | 全部 C# 源码，11 个文件（含共享基类 `ResearchStyleTabWindow.cs`） |
| `About/` | `About.xml`、`Preview.png` |
| `Defs/` | Def 定义 |
| `Languages/` | 中英文 Keyed 文案 + 英文 DefInjected |
| `Assemblies/` | 已构建并验证的 DLL |
| `libs/` | 构建引用（不入库，需自行放置） |
