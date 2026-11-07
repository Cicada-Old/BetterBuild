# 参与开发

[English](CONTRIBUTING.en.md) · RimWorld 1.7 · 更好的建造 1.0.0

欢迎提问题、报 bug、提改动。先读这三份：[功能说明](FEATURES.md)、[架构说明](ARCHITECTURE.md)、[已知问题](KNOWN_ISSUES.md)。

## 1. 报问题

在仓库提 Issue，尽量带上：

- 游戏版本（1.7.x）与是否装了 Harmony
- 是否与其他建造类 mod 同时启用（例如 QualityBuilder）
- 复现步骤：点哪里、造什么、期望是什么、实际是什么
- 日志片段：`%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`，本模组的行以 `[BM]` 开头
- 诊断页截图（如果是「订单不动」类问题，原因列能直接看出卡在哪一层）

**报之前先看一眼已知问题**，三条限制里的现象不用重复报。

## 2. 提改动（Pull Request）

- 一个 PR 只做一件事；顺手改的格式、重命名请分开提。
- 说明「为什么」：解决什么问题、影响哪些行为。只写「优化了代码」的 PR 会被退回。
- 新增或改变玩家可见行为时，同步改 `CHANGELOG.md` 与相关文档（`FEATURES` / `KNOWN_ISSUES`）。
- 涉及存档结构（`Scribe_*` 键）的改动必须说明向后兼容方案，否则不接受。

## 3. 代码约定

- **一个文件一个领域**。新功能按职责新建文件，不往 `BuildMonitor.cs` 里追加无关类。
- 过滤与放行逻辑集中在「完工闸门」入口判断，不要散落到各个 WorkGiver 补丁里。
- 新增可配置项：加到对应 `GameComponent` 的 `ExposeData`，给默认值并做范围夹取，再在设置弹窗加一行。
- 注释写「为什么」，不写「是什么」。已经过期的注释要顺手改掉——本项目吃过一次注释与常量不一致的亏。
- 不引入新的第三方依赖；必须引入时先开 Issue 讨论。

## 4. 不接受的做法

- **为了让测试通过而改逻辑**：不接受「为了让某个数字好看 / 某次截图不溢出 / 某条断言成立」而加特判、封顶、反向逻辑。要改就改根因；如果确实是测试预期错了，改测试与文档。
- **隐藏已知问题**：修不掉的问题写进 `KNOWN_ISSUES.md` 并说明影响面，不允许从文档里删掉。
- **把计划写成功能**：未实现的功能只能在路线图里标「计划」，不能写进功能说明。

## 5. 本地构建与自测

```bat
dotnet build BuildMonitor.csproj -c Release
```

- 需要 .NET 10；引用程序集放在 `libs/`（不出现在仓库里），获取方式见 [开发说明](../DEVELOPMENT.md)。
- 产物 `bin\BuildMonitor.dll`，复制为 mod 的 `Assemblies\BuildMonitor.dll` 后进游戏验证。
- 提 PR 前至少自测：启动无报错、建造页与诊断页能打开、改动涉及的功能走一遍、中英文界面各看一次。
- 构建产物字节受编译顺序影响，改动文件位置或 `csproj` 的 `Compile` 顺序前先看 [归档说明](../ARCHIVE.md) 的复现差异一节。

## 6. 许可证

提交代码即表示同意以本项目的 [MIT 许可证](../LICENSE) 分发。请勿提交来自其他模组或非授权来源的代码与素材。
