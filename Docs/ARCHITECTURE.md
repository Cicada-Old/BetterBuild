# 架构说明

[English](ARCHITECTURE.en.md) · RimWorld 1.7 · 更好的建造 1.0.0

面向想读代码或提 PR 的人。功能与玩家口径见 [功能说明](FEATURES.md)。

## 1. 代码结构（11 个源文件）

| 文件 | 职责 | 关键类型 |
|---|---|---|
| `BuildMonitor.cs` | 主文件：补丁初始化、建造权限、等级范围、订单顺序与设置、让路闸门、JobGiver 补丁 | `Bootstrap`、`BuildUtil`、`BuildPermsComp`、`BuildSkillRangeComp`、`BuildOrderComp`、`BuildPriorityGate`、`PatchHelper` |
| `BuildWorkerComp.cs` | 每种类型的执行名单（第三层过滤） | `WorkerEntry`、`BuildWorkerComp` |
| `MaterialBudget.cs` | 材料预算：在途账本、按名次分配额度、保护期与借用 | `MaterialBudget` |
| `OrderRegistry.cs` | 订单注册表：身份、阶段、名次、剩余需求；蓝图与框架之间承接订单号 | `OrderStage`、`OrderInfo`、`OrderLedger`、`OrderRegistry` |
| `SchedulingDiagnostics.cs` | 调度结论的判定码与存储，供诊断页读 | `VerdictCode`、`JudgeKind`、`OrderVerdict` |
| `MainTabWindow_BuildMonitor.cs` | 主窗口：建造页、诊断页、拖动排序、跳转、设置入口 | `MainTabWindow_BuildMonitor` |
| `Dialog_ScheduleSettings.cs` | 调度设置弹窗（保护期、老化期、行高） | `Dialog_ScheduleSettings` |
| `QualityBuilderRebuild.cs` | 拆除后兜底补放品质蓝图 | `Patch_DeconstructRebuild` |
| `RoofFirst.cs` | 拆前先拆屋顶：预判、拦截、标记恢复、风险提示 | `RoofGating`、`RoofPrediction`、`RoofMarkTracker`、`RoofRiskRegistry` |
| `ModNameLocalization.cs` | 英文环境显示 `Better Build` | `Patch_ModName` |
| `ResearchStyleTabWindow.cs` | 共用窗口基类（与「更好的制造」共用样式） | `ResearchStyleTabWindow` |

约定：**一个文件一个领域**。新功能按职责新建文件，不往主文件里追加无关类。

## 2. 调度主流程

```
蓝图生成
  → OrderRegistry 注册订单（分配稳定订单号）
  → 名次：按建造页顺序取下标（Rank）
  → 等待老化：久未进料则提权（EffRank = 最高名次之后一位）
  → 材料预算（每 15 tick 重建）：按名次分配额度
  → 送料任务：拿到全部材料额度才生成
  → 让路闸门：找出该小人当前能做的最高名次建造
  → 完工：过权限 / 等级 / 名单三层判断
  → 框架完成 → 订单结束
```

几处实现要点：

- **名次来源是「顺序列表」**，不是原版优先级。顺序变化会让缓存失效并触发重建。
- **提权写在 `EffRank`**，让路闸门与材料预算都读它，所以老化对两者同时生效。
- **预算每 15 tick 重建一次**；重建条件是「地图变了 / 顺序版本变了 / 距上次 ≥15 tick」三者之一。改保护期不改顺序版本，所以要等下一次重建。
- **让路闸门有缓存**：同一小人同一 tick 窗口内复用「当前最高可做建造」，缓存 45 tick；单次扫描最多检查 48 个候选，避免大基地里逐帧全表扫描。
- **诊断结论是旁路产物**：判定过程顺手写入结论对象，界面只读，不参与调度。

## 3. Harmony 补丁点

补丁分三类，合计 **静态 16 处 + 1 处前缀 + 一类动态挂载**：

**静态（16 处）**

| 目标 | 作用 |
|---|---|
| `WorkGiver_ConstructFinishFrames.JobOnThing`（两处） | 完工前过闸门；其中一处用于 QualityBuilder 补放后恢复订单归属 |
| `WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing` / `HasJobOnThing` | 蓝图送料按预算放行 |
| `WorkGiver_ConstructDeliverResourcesToFrames.JobOnThing` / `HasJobOnThing` | 框架送料按预算放行 |
| `WorkGiver_ConstructDeliverResources.FindNearbyNeeders` | 已排序订单不再就近分流给别的需求者 |
| `WorkGiver_ConstructAffectFloor.HasJobOnCell` | 铺地板完工同样过闸门 |
| `WorkGiver_Scanner.get_Prioritized` / `GetPriority` | 建造类工作优先，内部按名次排序 |
| `Blueprint.TryReplaceWithSolidThing` / `SpawnSetup` / `DeSpawn` | 蓝图转框架时承接订单号；注册与注销 |
| `Frame.CompleteConstruction` | 完工后结束订单 |
| `JobDriver_Deconstruct.FinishedRemoving` | 拆除后兜底补放品质蓝图 |
| `ModMetaData.Name`（getter） | 英文环境显示 `Better Build` |

**前缀（1 处）**

- `Building.Destroy`：兜底拦截——真到了拆除销毁那一刻屋顶还在，就拦下来先处理屋顶。

**动态挂载（一类）**

- 启动时遍历已加载程序集，给**所有** `WorkGiver.JobOnThing` 挂后置补丁。原版「送料」与「完工」走两条独立通路，静态列表覆盖不全，所以动态扫。**挂载数量随已安装 mod 变化**（实测环境约 124 个），不是固定值。

## 4. 数据与存档

**真正落盘的数据（6 组，均为 `GameComponent`）**

| 数据 | Scribe 键 |
|---|---|
| 建造权限 | `BuildMonitorPerms` |
| 等级范围 | `BM_SkillRanges` |
| 订单顺序 | `BuildMonitorBuildOrder` |
| 订单计时 | `BuildMonitorBuildOrderSeen` |
| 保护期 / 老化期 / 行高 | `BM_ProtectDays` / `BM_AgeDays` / `BM_RowHeight` |
| 执行名单 | `BM_WorkerWhitelists` |
| 订单号承接（蓝图→框架） | `BuildOrderFrameToOrder` |

（权限、等级、顺序+计时、设置、名单、订单号承接共六类。最后一组在 `OrderRegistry.cs` 的 `OrderLedger` 里，跨存档保留蓝图转框架时的订单号映射。）

**不落盘、每次运行重建的**

- `OrderRegistry`（`MapComponent`）：订单表由地图上的蓝图/框架实时注册，读档后重建。
- `RoofMarkCleanup`：纯 tick 清理，没有 `Scribe_*`。
- `BenchPriorityComp`：保留的空壳，只用于吞掉旧版本数据（工作台优先级自 2.0 起已移出本模组），不写回。

**派生数据**：材料预算、诊断结论都是运行期缓存，不存档；读档后由前 15 tick 内的重建填回。

## 5. 关键常量与量级

| 项 | 值 | 位置 |
|---|---|---|
| 材料预算重建间隔 | 15 tick | `MaterialBudget.RebuildInterval` |
| 让路闸门缓存 | 45 tick | `BuildPriorityGate.CacheTicks` |
| 单次扫描候选上限 | 48 | `BuildPriorityGate.MaxChecksPerScan` |
| 行高范围 / 默认 | 22~64 / 24 | `BuildOrderComp.RowHeightMin/Max/Default` |
| 等级范围 | 0~20（整个区间即「不限」） | `BuildSkillRangeComp` |
| 保护期 / 老化期 | 0~30 游戏日，默认 1 | `BuildOrderComp` |
| 让路明细日志上限 | 80 条（不随读档复位） | `BuildMonitor`（见已知问题） |

## 6. 性能取向

- 预算**周期性重建**而不是每 tick 全算：15 tick 一次，按订单剩余需求做一次线性分配。
- 让路闸门**带缓存 + 候选上限**：避免每 tick 遍历全部订单；代价是同一 tick 窗口内的名次变化最迟 45 tick 反映。
- 诊断结论**复用对象**、字段覆盖写，不为每张订单分配新对象。
- 日志**分两级**：概览行按窗口节流，明细行有硬上限（见已知问题），避免大基地刷屏。

## 7. 扩展时注意

1. 新增可配置项 → 加到 `BuildOrderComp` 的 `ExposeData`，给默认值并做 `clamp`，再在 `Dialog_ScheduleSettings` 加一行。
2. 新增过滤层 → 放在「完工闸门」入口统一判断，不要散落到各个 WorkGiver 补丁里。
3. 改编译顺序或文件位置 → 会影响产物字节（见 `ARCHIVE.md` 附 D），改前先读那节。
4. 加 Harmony 补丁 → 优先静态特性；确需动态挂载时说明原因，并注意挂载数量随环境变化。
