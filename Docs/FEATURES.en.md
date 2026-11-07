# Features

[简体中文](FEATURES.md) · RimWorld 1.7 · Better Build 1.0.0

This page describes the features and their limits from the player's point of view. Implementation details are in [Architecture](ARCHITECTURE.en.md); known problems are in [Known issues](KNOWN_ISSUES.en.md).

## 1. Build permissions: quality / plain per colonist

- Each colonist can separately allow or deny two kinds of automatic construction: buildings that **require quality**, and **plain** buildings.
- Both are allowed by default.
- When you right-click and assign a colonist by hand, anyone can take the job — permissions gate automatic work only.

Purpose: keep low-skill colonists on plain walls and floors, so they do not turn quality furniture into low-quality output.

## 2. Construction level range: one interval per type

- Each building type has a construction level interval, set with a two-handle range bar, `0~20`.
- `0~20` means **no limit**.
- Only colonists inside the interval **and** holding the matching permission (see section 1) will build that type automatically.

## 3. Builder list: restrict a type to specific colonists

- Each type can have a builder list. Empty list = no restriction; non-empty = only listed colonists may do that type.
- The list and the level range are **two independent filters**; both must pass.
- The status line shows "N listed · M can build", and turns to a warning colour when `M = 0` with a non-empty list.
- The **builder list** on the right and the **colonist list** in the left pane can be sorted independently (**default / level ↑ / level ↓**); the two do not affect each other.

## 4. Build order: drag the `≡` handle

- The Construction tab merges orders by type, one row each, with blueprint and frame counts combined.
- Drag the `≡` handle on the left to change order; the order determines scheduling rank.
- Rank drives two things: **who gets materials first** and **who is released first**.
- When a higher-ranked type is short on materials, stock is reserved for it; lower-ranked types will not take the same material; orders that cannot proceed are skipped instead of blocking the queue.
- The "Jump" button centres the camera on that type's orders.
- Once a type has 5 or more outstanding orders, the jump slot shows **Too many** and asks you to clear some first, so the camera is not dragged between hundreds of identical orders.

## 5. Material budget: allocated by rank, borrowed after the protection period

- Each material first covers the remaining need of the **highest-ranked type**; whatever is left is allocated to lower-ranked orders by (rank, order) order.
- **An order is released only when it receives quotas for all of its materials.** Missing one means waiting.
- Inside the highest-ranked group, orders **compete freely** and each takes materials as it needs.
- **Material protection period**: materials held by the highest rank stay protected for a while (default 1 game day, adjustable 0~30), after which **half** opens up for lower ranks to borrow. Set to `0` = half is always open.
- The budget is a soft constraint: it does not reserve game jobs or deduct stock, it only decides whether a hauling job is generated.
- Changing the protection period takes effect **within 15 ticks (about 0.25 s at 1× speed)**, not instantly.

## 6. Ageing: long-waiting orders are promoted

- If an order receives no materials for a while (default 1 game day, adjustable 0~30, `0` disables), its scheduling rank is raised to **the position right after the top rank**, overtaking the rest of the queue.
- The promotion applies to **both** the yield gate and the material budget.
- Changes to the ageing period take effect on the **next registry refresh** (within about 60 ticks).

Purpose: in a long queue, a low-priority type at the tail never gets its turn otherwise.

## 7. Diagnostics tab: why each order is not moving

Each order carries one of five verdicts:

| Verdict | Meaning |
|---|---|
| **Granted** | Material quotas are in place; execution is allowed |
| **Waiting for materials** | The budget granted nothing; expand to see the arithmetic (need / available / stock / en route / protected / allocated) |
| **Yielded** | A higher-ranked order that this colonist can do exists, so this one steps aside |
| **No worker** | Permissions, level range and builder list together block every colonist |
| **Forbidden** | You forbade this blueprint or frame in the game |

Notes:

- "Waiting for materials" distinguishes **permanently short** (stock genuinely insufficient) from **temporarily taken** (claimed by other orders right now).
- When a yield occurs, the reason column briefly shows "yields to \<type\> (rank N)", then falls back to the regular reason (duration: see [Known issues](KNOWN_ISSUES.en.md)).
- Verdicts come from different judges (budget / yield gate / no-worker check / player), and the UI uses the source to decide how authoritative the line is.
- The **viewer** selector at the top can be switched to a specific colonist, showing "<colonist> should do: <type> (rank N)" — useful to see which type that person is being sent to right now.

## 8. Row height: fit different resolutions and font sizes

- Set the type-table row height in pixels from the scheduling dialog; default 24, range **22~64**.
- Row height affects **both the Construction and Diagnostics tabs** (the diagnostics font tier and row rectangles derive from it); the colonist list in the left pane keeps a fixed height.
- Out-of-range values are clamped; non-numeric input has no effect.

## 9. Search: filter by name

- A search box sits at the top right of the window. Typing in it **filters both the Construction and Diagnostics tabs by name**, keeping only matching type groups.
- The Construction tab matches against "quality prefix + type name" (for example "[Quality] wood wall"); the Diagnostics tab matches the same group labels.
- Clearing the box restores everything; the numbers shown always describe the full data set.

## 10. Roof before deconstruction

- Before deconstructing a load-bearing building that would collapse a roof, ordinary roofs are removed first.
- **Overhead mountain is refused outright**.
- If the deconstruction job is cancelled, previously placed roof markers are reverted, leaving nothing behind.

## 11. Working with QualityBuilder

- After a quality-set building is deconstructed (manually or automatically), if QualityBuilder does not re-place the blueprint, this mod **places it and carries the quality requirement over**.
- This is a compatibility behaviour: with QualityBuilder installed, it re-places first and this mod fills in when it does not.
