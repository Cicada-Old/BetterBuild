# Known Issues and Limitations (1.0.0)

> Applies to RimWorld 1.7 · Better Build 1.0.0
> This page lists issues that are known and not being fixed in this release. If you hit something that is not listed here, please open an Issue.

## 1. Detailed yield logging stops during long sessions

**What happens**: within one game session, detailed "yield" log lines stop after roughly 80 entries.

**Impact**: log detail only. Scheduling decisions, material allocation and the UI are unaffected; nothing players can observe changes. Reloading a save does not reset the counter, restarting the game does.

**Workaround**: restart the game when you need the yield log.

**Planned**: reset on load or a rolling time window in a later version.

## 2. Opening an old save with this mod disabled

**What happens**: about 16 component load failures are logged, and level ranges, builder lists and build order show default values.

**Impact and recovery**: the data is still in the save. **Re-enable the mod and load the same save again to recover.**

**Caution**: saving while the mod is disabled may write those unreadable defaults back into the save. **Do not save while the mod is disabled.**

**Note**: this is common to any component-based mod being disabled, not specific to this mod's logic.

## 3. The "yields to X (rank N)" hint disappears after a few seconds

**What happens**: after a yield occurs, the reason column shows "yields to <type> (rank N)" for about 4 seconds at 1× speed, then falls back to the regular reason. At faster speeds it is nearly invisible.

**Impact**: display duration only. The yield decision itself and the resulting material allocation are correct.

**Planned**: longer retention in a later version.

## 4. Three UI details (not fixed)

- **Builder list status row**: the row rectangle is measured with the Tiny font (16 px) but drawn with Small (needs 22 px), so **text may be partially clipped**. Values and behaviour are unaffected.
- **Window title**: needed height is 1 px more than the actual rectangle; not visible to the eye.
- **Row-height tooltip**: it ends with "only affects the type-group table on the Construction tab", but the **Diagnostics tab follows that setting too** (behaviour as documented on this page and in [Features](FEATURES.en.md)).

All three will be handled after 1.0.0 to avoid unfreezing the release candidate over a few pixels and one sentence.

## 5. Environment limitations (not this mod)

- Errors from unrelated third-party mods should be reported to those mods.
- This mod does not change vanilla construction speed, material stats or building durability.
