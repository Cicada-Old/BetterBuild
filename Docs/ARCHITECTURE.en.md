# Architecture

[简体中文](ARCHITECTURE.md) · RimWorld 1.7 · Better Build 1.0.0

For people reading the code or sending a pull request. Player-facing behaviour is in [Features](FEATURES.en.md).

## 1. Source layout (11 files)

| File | Responsibility | Key types |
|---|---|---|
| `BuildMonitor.cs` | Main file: patch bootstrap, build permissions, level ranges, order sequence and settings, yield gate, JobGiver patches | `Bootstrap`, `BuildUtil`, `BuildPermsComp`, `BuildSkillRangeComp`, `BuildOrderComp`, `BuildPriorityGate`, `PatchHelper` |
| `BuildWorkerComp.cs` | Per-type builder list (the third filter) | `WorkerEntry`, `BuildWorkerComp` |
| `MaterialBudget.cs` | Material budget: en-route ledger, rank-based quotas, protection period and borrowing | `MaterialBudget` |
| `OrderRegistry.cs` | Order registry: identity, stage, rank, remaining need; order id carried across blueprint → frame | `OrderStage`, `OrderInfo`, `OrderLedger`, `OrderRegistry` |
| `SchedulingDiagnostics.cs` | Verdicts and their store, read by the Diagnostics tab | `VerdictCode`, `JudgeKind`, `OrderVerdict` |
| `MainTabWindow_BuildMonitor.cs` | Main window: Construction tab, Diagnostics tab, drag ordering, jump, settings entry | `MainTabWindow_BuildMonitor` |
| `Dialog_ScheduleSettings.cs` | Scheduling dialog (protection period, ageing period, row height) | `Dialog_ScheduleSettings` |
| `QualityBuilderRebuild.cs` | Re-places the quality blueprint after deconstruction | `Patch_DeconstructRebuild` |
| `RoofFirst.cs` | Roof-first deconstruction: prediction, blocking, marker restore, risk alert | `RoofGating`, `RoofPrediction`, `RoofMarkTracker`, `RoofRiskRegistry` |
| `ModNameLocalization.cs` | Shows `Better Build` in English | `Patch_ModName` |
| `ResearchStyleTabWindow.cs` | Shared window base (same styling as the companion crafting mod) | `ResearchStyleTabWindow` |

Convention: **one file, one domain.** New features get a new file rather than another class appended to the main one.

## 2. Scheduling pipeline

```
Blueprint spawned
  → OrderRegistry registers the order (stable order id)
  → Rank: index in the Construction-tab sequence
  → Ageing: promote long-waiting orders (EffRank = right after the top rank)
  → Material budget (rebuilt every 15 ticks): quotas by rank
  → Hauling job: generated only when every material quota is granted
  → Yield gate: find the highest-ranked construction this colonist can do
  → Completion: pass permission / level / builder-list checks
  → Frame completes → order closed
```

Implementation notes:

- **Rank comes from the order list**, not from vanilla priority. Changing the sequence invalidates the cache and triggers a rebuild.
- **Promotion is written to `EffRank`**, read by both the yield gate and the budget, so ageing affects both at once.
- **The budget rebuilds every 15 ticks.** It is reused only when the map is unchanged, the sequence version is unchanged, and the last rebuild is within 15 ticks. Changing the protection period does not bump the sequence version, so it waits for the next rebuild.
- **The yield gate is cached**: the "best action right now" for a colonist is reused for 45 ticks, and a single scan checks at most 48 candidates, so large colonies do not scan the whole table every frame.
- **Verdicts are a side product**: the decision path writes them as it goes; the UI only reads them and never feeds back into scheduling.

## 3. Harmony patch points

Patches fall into three groups: **16 static patches + 1 prefix + one dynamic class**.

**Static (16)**

| Target | Purpose |
|---|---|
| `WorkGiver_ConstructFinishFrames.JobOnThing` (two patches) | Gate completion; one of them restores order ownership after QualityBuilder re-places a blueprint |
| `WorkGiver_ConstructDeliverResourcesToBlueprints.JobOnThing` / `HasJobOnThing` | Release blueprint hauling according to the budget |
| `WorkGiver_ConstructDeliverResourcesToFrames.JobOnThing` / `HasJobOnThing` | Release frame hauling according to the budget |
| `WorkGiver_ConstructDeliverResources.FindNearbyNeeders` | Stop ranked orders from being diverted to other needers |
| `WorkGiver_ConstructAffectFloor.HasJobOnCell` | Floor construction passes the same gate |
| `WorkGiver_Scanner.get_Prioritized` / `GetPriority` | Construction work goes first; ordering inside it follows rank |
| `Blueprint.TryReplaceWithSolidThing` / `SpawnSetup` / `DeSpawn` | Carry the order id across blueprint → frame; register and unregister |
| `Frame.CompleteConstruction` | Close the order on completion |
| `JobDriver_Deconstruct.FinishedRemoving` | Re-place the quality blueprint after deconstruction |
| `ModMetaData.Name` (getter) | Show `Better Build` in English |

**Prefix (1)**

- `Building.Destroy`: last-resort interception — if a roof is still there when the building is about to be destroyed, stop and handle the roof first.

**Dynamic (one class)**

- At startup the mod walks loaded assemblies and applies a postfix to **every** `WorkGiver.JobOnThing`. Vanilla has two independent paths ("deliver resources" and "finish frames"), and a static list does not cover both reliably. **The number of patched methods depends on the installed mod list** (about 124 in the tested setup); it is not a fixed number.

## 4. Data and saves

**Persisted (6 categories, 7 rows, all `GameComponent`)**

| Data | Scribe key |
|---|---|
| Build permissions | `BuildMonitorPerms` |
| Level ranges | `BM_SkillRanges` |
| Order sequence | `BuildMonitorBuildOrder` |
| Order timestamps | `BuildMonitorBuildOrderSeen` |
| Protection / ageing / row height | `BM_ProtectDays` / `BM_AgeDays` / `BM_RowHeight` |
| Builder lists | `BM_WorkerWhitelists` |
| Order-id carry-over (blueprint → frame) | `BuildOrderFrameToOrder` |

(Permissions, level ranges, sequence + timestamps, settings, builder lists, order-id carry-over — six kinds. The last one lives in `OrderLedger` in `OrderRegistry.cs` and keeps the order-id mapping across saves when a blueprint turns into a frame.)

**Rebuilt at runtime, not saved**

- `OrderRegistry` (`MapComponent`): orders are registered from blueprints and frames on the map, and rebuilt after loading.
- `RoofMarkCleanup`: a pure tick cleanup component with no `Scribe_*` at all.
- `BenchPriorityComp`: an empty shell kept only to swallow data from older versions (workbench priority moved out of this mod in 2.0); it never writes back.

**Derived data**: the material budget and verdicts are runtime caches, never saved; they are repopulated by the first rebuild within 15 ticks after loading.

## 5. Key constants

| Item | Value | Location |
|---|---|---|
| Budget rebuild interval | 15 ticks | `MaterialBudget.RebuildInterval` |
| Yield gate cache | 45 ticks | `BuildPriorityGate.CacheTicks` |
| Candidates per scan | 48 | `BuildPriorityGate.MaxChecksPerScan` |
| Row height range / default | 22~64 / 24 | `BuildOrderComp.RowHeightMin/Max/Default` |
| Level range | 0~20 (the full range means no limit) | `BuildSkillRangeComp` |
| Protection / ageing period | 0~30 game days, default 1 | `BuildOrderComp` |
| Yield log cap | 80 lines (not reset by loading) | `BuildMonitor` (see Known issues) |

## 6. Performance choices

- The budget **rebuilds periodically** instead of every tick: once per 15 ticks, one linear pass over remaining needs.
- The yield gate is **cached and capped**: no per-tick scan of every order; the price is that rank changes within a tick window surface within 45 ticks.
- Verdicts **reuse objects** and overwrite fields rather than allocating per order.
- Logging is **two-tier**: overview lines are throttled per window, detail lines have a hard cap (see Known issues), so large colonies do not flood the log.

## 7. When extending

1. New setting → add it to the relevant `GameComponent.ExposeData`, give it a default, clamp it, then add a row to `Dialog_ScheduleSettings`.
2. New filter layer → apply it at the completion gate, not scattered across WorkGiver patches.
3. Changing compile order or file placement → changes the output bytes (see `ARCHIVE.md`, appendix D); read that section first.
4. New Harmony patch → prefer static attributes. If dynamic patching is genuinely required, say why, and remember the count varies with the environment.
