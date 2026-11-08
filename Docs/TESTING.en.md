# Testing

[简体中文](TESTING.md) · RimWorld 1.7 · Better Build 1.0.0

What was tested before 1.0.0, what the results were, and **what was not covered**. Raw test records are internal and are not published with this repository; this page is the externally checkable summary.

## 1. Environment

| Item | Value |
|---|---|
| Game | RimWorld 1.7 (64-bit, Windows) |
| Mod count | 211 enabled mods in the existing list |
| Test save | Purpose-built extreme save: material protection shortened to **half a game day** (half the default), 25 people with building enabled (from the test record), many outstanding orders |
| Resolution | 1920×1080 fullscreen, plus 1400×860 and 1280×720 windowed |
| UI language | One pass in Chinese, one in English |
| Prerequisite | Harmony |
| Build under test | code md5 `68ae0f54…` (the full suite ran on this build) |

> **Release vs. tested build**: the release build is `80b5956514db…`. Its differences from the tested build are three `packageId` strings (the two Harmony instance IDs and `SelfPackageId`), `About.xml`, and `ModsConfig.xml`; **the Chinese and English language files were not touched at all**. The release build received **smoke-level verification only** (starts cleanly, `packageId` and localized names correct, Harmony patches active, both tabs usable, 3 groups / 24 orders scheduling normally); the full functional and performance suites were **not** re-run on it.

## 2. Method

- **Functional**: each item is executed and evidenced (UI readings, log excerpts, screenshots); "looks fine" is not accepted as a result.
- **Performance**: same save, same load, fixed-duration sampling, "mod enabled" versus "mod disabled"; each configuration repeated over several runs, median or mean reported.
- **Compatibility**: only against the mod list above — no exhaustive matrix, no pre-declared third-party compatibility list.

## 3. Functional results

### 3.1 Automated items (10)

| Item | Result |
|---|---|
| Load a save without startup errors; window opens | Pass |
| Construction ↔ Diagnostics switching refreshes correctly | Pass |
| Controls interactive: tabs, settings dialog, row-height input, jump button | Pass |
| Decision logs (grant / wait / yield / builder list / level / protection) match baseline | Pass |
| Diagnostics data correct (grouping, summary, reason column) | Pass |
| No `Log.Error` from this mod | Pass (only 3 errors, all from third-party mods, no frames from this mod) |
| Key strings correct after switching Chinese/English | Pass |
| Regression of previously fixed defects | Pass (spot checks for live and save/load items) |
| State reset after loading | **Partial**: UI and diagnostics counters reset correctly; the yield-log counter does not reset on load (see [Known issues](KNOWN_ISSUES.en.md), item 1) |
| Install orders shown in the UI | Pass: install orders are listed (the material column shows "no material list", because install blueprints have none and are skipped when aggregating materials); their hauling and installing are subject to the builder list |

### 3.2 Manual items

| Item | Result |
|---|---|
| Slider dragging (press–drag–release) | Pass |
| Drag `≡` to reorder | Pass |
| Four row heights (24 / 31 / 42 / 64), last column not clipped | Pass |
| Builder count and warning colour (1 eligible = green, 0 = red) | Pass |
| Level tooltip text; independent sort button in the right pane | Pass |
| Yield priority: Diagnostics shows "yields to X (rank N)" | Pass |
| Builder list scrolling does not bleed; narrow screens (1400×860 / 1280×720) not clipped | Pass |
| "Uninstall" gizmo on a selected building | Pass |
| Full-screen English review | Pass (two clipped strings found and fixed) |
| Overall visual impression | Confirmed acceptable by the user; three UI details left unfixed (see Known issues, item 4) |

## 4. Performance results

**Conclusion: no measurable throughput cost was found.** Read the numbers together with the boundaries below.

- Nine batches, about 60 runs. At ×1 every batch is within **±0.3%**; at ×3 most are **±0.1%**; at ×6 the cross-batch range is **−5.4% to +5.7%** with the sign **flipping**, smaller than the same-side run-to-run spread (7.7%) — hence "not measurable", not "X% overhead".
- TPS is essentially capped at ×1 and ×3. At the fastest setting both groups drop together under CPU limits, and the difference is not monotonic.
- FPS and memory are the same order of magnitude for both groups; memory shows no monotonic rise over long runs.
- This mod's hot functions total about **0.26 ms/tick** (about 1.0 s per 3,900 ticks), with budget rebuilds around 44% of it.
- Diagnostic logging produces about 700–750 lines per 10 minutes at the fastest setting, throttled — no log flooding.

**Not covered (stated explicitly):**

1. Of the planned 8 "enabled × environment × load" combinations, only 4 were completed (both high-environment cells and both disabled-side pure-environment cells).
2. The "pure environment + mod enabled" cell was **invalid**: a third-party mod threw a null reference during startup, the game reset the enabled list, and this mod was never loaded.
3. The high-load cell of "pure environment + mod enabled" was **not run**.
4. Higher developer speed settings were **not tested**: the three regular settings did not saturate the CPU, and the user decided to stop there.
5. These gaps were closed by decision; **the conclusion applies to "three regular speeds in a high-load environment" only** and does not extend to the enabled side of a pure environment. The performance batches ran with **207 mods** (4 speed-changing mods temporarily disabled out of the 211, so the speed buttons stay at vanilla ×1/×3/×6); the functional and compatibility tests ran with 211.

## 5. Compatibility

- Loaded with the existing 211-mod list: `missingMods` empty, no load failures attributable to this mod.
- Startup log had 8,804 lines; the 3 ERROR lines all come from third-party mods. Lines mentioning this mod's name or log prefix numbered 7, with **0** exceptions.
- Both tabs open and read correctly in that setup.
- There is a **cooperative relationship** with QualityBuilder (the quality rebuild fix uses its data); without it, the rest of the mod still works.

## 6. Reproducing it yourself

1. On 1.7, enable Harmony and this mod, then load any colony save.
2. Open the **Build** entry and switch between the two tabs.
3. Place a batch of blueprints (same type and mixed types) and watch the Diagnostics verdicts change with materials.
4. Adjust the protection and ageing periods in the scheduling dialog and observe budget and promotion behaviour (protection changes take effect within 15 ticks).
5. Logs: `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`; this mod's lines start with `[BM]`.

## 7. Evidence and boundaries

- Raw records, per-run data, screenshots and log copies are internal and **not published**; this page is their public summary and every number traces back to those records.
- Testing used external automation tooling; its own limitations are unrelated to the mod and do not affect players.
