# ARCHIVE — 更好的建造（Better Build）1.0.0

> 技术归档说明。对应的发布前测试报告与版本冻结报告属内部资料，不随仓库分发。

## 1. 项目与标识

| 项 | 值 |
|---|---|
| 项目名 | 更好的建造 / Better Build |
| `packageId` | `Cicada.BetterBuild` |
| 发布版本 | `1.0.0`（`About/About.xml` 的 `<modVersion>`） |
| 仓库 | `Cicada-Old/BetterBuild`（本阶段仅建本地仓库，不配远端、不推送） |
| 许可证 | MIT，`Copyright (c) 2026 Cicada` |

## 2. 支持的 RimWorld 版本

`1.7.4977`（Waylith 引擎移植版）。

## 3. 运行环境与构建环境

| 项 | 值 |
|---|---|
| 操作系统 | Windows 11（10.0.26200） |
| 运行时 | .NET 10 |
| 实测环境 | 功能与兼容性 **211** 个已启用 mod；性能九批 **207** 个（临时停用 4 个改速度倍率的 mod）；存档为内部测试存档；中文/英文各一轮；1920×1080 与 1400×860 / 1280×720 窗口化 |
| 构建环境 | `dotnet` CLI（见第 4 项），无需 IDE |

## 4. .NET / 编译器版本

`dotnet --version` = **`10.0.301`**；目标框架 `net10.0`。

## 5. 第三方依赖

| 依赖 | 说明 |
|---|---|
| **Harmony**（`brrainz.harmony`） | 唯一运行时依赖。`About.xml` 用 `<modDependencies>` 声明（**1.7 没有 `<depends>` 字段**，写成 `depends` 会报 `doesn't correspond to any field in type ModMetaDataInternal`） |
| `loadAfter` | `brrainz.harmony`、`hatti.qualitybuilder` |

以上都是引用关系：上表为运行期依赖与加载顺序声明，四份构建期程序集见 `DEVELOPMENT.md` 第 1 节（`libs/` 不入库）。

## 6. 源代码与构建产物位置

| 项 | 路径 |
|---|---|
| 源码 | `Source/`（**11 个 `.cs`**，其中 `ResearchStyleTabWindow.cs` 为共享基类） |
| 工程文件 | `BuildMonitor.csproj` |
| 构建产物 | `bin/BuildMonitor.dll`（产物 md5 见下） |
| 随包 DLL | `Assemblies/BuildMonitor.dll` |

**全仓库入库文件 47 个**（以 `git ls-files` 为准）：

| 分组 | 个数 | 内容 |
|---|---|---|
| 根目录 | 11 | `.gitignore`、`ARCHIVE.md`、`BuildMonitor.csproj`、`DEVELOPMENT.md` / `.en.md`、`LICENSE`、`LoadFolders.xml`、`README.md` / `.en.md`、`CHANGELOG.md` / `.en.md` |
| `About/` | 2 | `About.xml`、`Preview.png` |
| `Assemblies/` | 1 | `BuildMonitor.dll` |
| `Defs/` | 1 | 主按钮定义 |
| `Languages/` | 3 | 英文 Keyed、中文 Keyed、英文 DefInjected |
| `Source/` | 11 | 全部 C# 源码 |
| `Docs/` | 18 | 5 份文档中英各一（10）+ 中英截图各 4（8） |

## 7. 构建方法与复现步骤

**先明确目的，两档走的步骤不同**（详见 `DEVELOPMENT.md`）：

- **A 档 · 只要功能等价**（日常开发）：1. 从游戏安装目录与 Harmony mod 取四个 DLL 放进 `libs/`；2. `dotnet build BuildMonitor.csproj -c Release`；3. 复制为 mod 的 `Assemblies/BuildMonitor.dll`。产物**能编译**（该版本未挂进游戏运行过），但**字节与归档件不同**（实测游戏根目录程序集 → `57315839…`），不要拿它去对 md5。
- **B 档 · 要字节级复现归档件**：引用程序集必须用与归档时同一套**裁剪参考壳**（开发期内部留存，不随仓库分发），并保持 `ResearchStyleTabWindow.cs` 的编译顺序（`Source` 组在前、该文件在后）——只有这组条件能得到 `80b5956514db783df6ddfd451b1fb366`；构建后校验 `bin/BuildMonitor.dll` 的 md5，再复制为 `Assemblies/BuildMonitor.dll`。

差异原因与逐项对照见 **附 D**。

## 8. 游戏内测试

目标、环境、步骤与逐项结果见**内部发布前测试报告的「最终结论」一节**：A 类 10 项、B 类人工项、兼容性专轮、性能九批结论、问题汇总、发布结论（阻断项 0）。

**验证范围必须一并读**：全套测试跑在被测件 `68ae0f54…` 上；换成 `80b59565…` 后只做了冒烟级验证（启动无错、ID 与中英文名、Harmony 生效、两页可用、3 组 24 单读数正常）。

> 归档阶段的那次「副本作为 mod 加载」是**一次性人工检查，无独立日志留存**（当时原部署件临时移出 `Mods`、副本挂载、启动验证后还原）。旁证：部署件已还原且 md5 相符、副本 `About.xml` 与部署件同 md5、副本重建产物与归档件一致。

> 内部测试报告与版本冻结报告**不随仓库分发**（含内部路径与研判细节）；面向公众的测试与已知问题说明在 `Docs/TESTING.md` / `Docs/KNOWN_ISSUES.md`。

## 9. 已知问题与限制

| 编号 | 内容 | 处置 |
|---|---|---|
| 让路明细日志上限 | 「建造让路」明细日志累计 80 条后，同一游戏进程内不再输出、读档不复位；**不影响调度判定与界面显示** | 1.0.0 之后修；发布说明披露 |
| 停用后读旧存档 | 停用/卸载本 mod 后打开旧存档会刷 16 条组件报错、等级范围/名单/订单顺序显示默认值；**重新启用后数据恢复** | 不修；停用期间不要保存 |
| 让路提示时长 | 诊断页「让给 X（名次 N）」只保留 240 tick（约 4 秒），玩家几乎看不到；判定本身正常 | 1.0.0 之后修（延长保留或改显示「最近一次让路」） |

## 10. 对应 Git 提交

| 项 | 值 |
|---|---|
| 归档主体 / 首次提交 | `a50e391`（代码、产物、文档；该提交内本文档的 hash 字段仍是占位符） |
| 说明性修订 | `007cb36` → `93c1b0c` → `012459b` → `e735647`：只改本文档（hash 回填、附 D/E、措辞），不含代码与产物变更 |
| **阶段四文档与截图** | **`bd71aa8`**：`README` / `CHANGELOG` / `DEVELOPMENT` 中英版、`Docs/` 下 5 对文档 + 8 张截图，共 **25 个文件**（23 新增 + 2 修改）；不含代码与产物变更 |
| **阶段六 P2 修订** | **`158f5e2`**：源码注释去掉内部引用，`.gitignore` 补 `*.rws`、`*.rws.bak`、`*.ps1`、`.workbuddy/`，§3 与 README 截图说明各一处；**产物未变**（Source 仍 5809 行，重建仍得 `80b59565…`） |
| 本文档的后续修订 | 只改本文件的措辞与 hash 回填——**最新值以 `git log -- ARCHIVE.md` 为准**，不在本文内写死 HEAD |
| 分支 | `main` |
| 标签 | **未打**（`v1.0.0` 留阶段八，指向通过验收的冻结提交） |

---

## 附 A. 三组四文件 md5（互不替代）

| 对象 | md5 |
|---|---|
| `Assemblies/BuildMonitor.dll` | `80b5956514db783df6ddfd451b1fb366` |
| 语言资源 `Languages/English/Keyed/BuildMonitor.xml` | `8cdb5b6e7f1858a8533416984f584479` |
| 语言资源 `Languages/ChineseSimplified (简体中文)/Keyed/BuildMonitor.xml` | `cfa8c0baed375c33875f170685bfb72e` |
| `About/About.xml` | `ea315a738402a28a4b9e027351eb54b2` |

> 另有 `Languages/English/DefInjected/MainButtonDef/BuildMonitor.xml`（英文主按钮标题注入）与 `Defs/MainButtonDef.xml`，随副本原样归档。
>
> **该 DLL md5 仅对「用归档时那套裁剪参考壳 + 现行编译顺序」这一种方式成立**；用游戏安装目录的程序集能编出功能等价的 DLL，但字节不同（见附 D）。
>
> 磁盘上可存在 `libs/`、`bin/`、`obj/`（构建引用与产物），均已由 `.gitignore` 排除，**合规与否以 `git ls-files` 为准，不以磁盘目录有无判定**。

## 附 B. 产物变更史

| 阶段 | DLL md5 | 验证强度 |
|---|---|---|
| 冻结候选（测试期） | `68ae0f5478dfe27836113448ec90d3e8` | 全套：A 类 10 项、B 类人工项、性能九批、兼容性专轮 |
| 最终产物（本归档） | `80b5956514db783df6ddfd451b1fb366` | **仅冒烟**：启动无错、`packageId` 与中英文名、Harmony 生效、建造页/诊断页可用、3 组 24 单读数正常 |

两者差异**只有** packageId 相关字符串 3 处（`BuildMonitor.cs`、`ModNameLocalization.cs`、`RoofFirst.cs` 各 1 处）+ `About/About.xml` + `ModsConfig.xml`，不涉及调度、界面与判定逻辑。

## 附 C. md5 ↔ commit 对应

- `80b59565…` = 首次提交 `a50e391`。
- 该 md5 只对应这一次提交；**后续任何提交都不得声称与该 md5 一致**——产物变了就要重新记录 md5 与 commit。

## 附 D. 重建差异排查记录（2026-11-07 自检第 1 条）

清单要求「从副本重建，产物 md5 必须仍是 `80b59565…`」。首次实检不符，逐项排查：

| 变量 | 产物 md5 |
|---|---|
| 游戏根目录原版程序集作引用（`Assembly-CSharp.dll` 15.5 MB） | `57315839969ded9fc74adc23551ee668` |
| 归档时那套裁剪参考壳作引用（约 6.4 MB） | `25a0acc05cd569a703e6598d06af8770` |
| 再把 `ResearchStyleTabWindow.cs` 的**编译顺序**恢复成「Source 组在前、该文件在后」 | **`80b5956514db783df6ddfd451b1fb366`** ✅ |

**结论**：

1. 差异**不是源码** —— 副本 `Source/*.cs` 与开发副本逐一 md5 相同。
2. 主因是**编译单元顺序**：把共享文件并入 `Source\**\*.cs` 后，它按字母序排到中间；C# 编译器按文件顺序写出类型元数据，DLL 字节随之变化，**功能等价**。
3. 引用程序集不同也会改变产物：裁剪参考壳与游戏原版程序集不是同一份（约 6.4 MB vs 15.5 MB）。

**处置**：为让归档判据「md5 可复现」成立，`BuildMonitor.csproj` 把 `ResearchStyleTabWindow.cs` 从 `Source\**\*.cs` 中排除、单独 `Compile` 一行放在末尾，并加注释说明原因。**源码一行未改**，改的是构建配置。

**`libs/` 该放哪一份**：用归档时那套裁剪参考壳的四份（含同套的 `0Harmony.dll`）才能产出 `80b59565…`；用游戏原版程序集同样能编译（未挂进游戏运行过），只是字节不同。`DEVELOPMENT.md` 给普通开发者写的是「从游戏目录取」，那是能编译的路径；要**字节级复现归档产物**则需**裁剪参考壳那份**（开发期工作区才有，不公开分发）。
