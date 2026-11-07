# Changelog

Public releases only. Internal development records are not part of this file.

## 1.0.0 — first public release

The release date follows the repository Release page and the Workshop page.

### Added

- **Build permissions**: allow quality and/or plain automatic construction per colonist; manual orders are never restricted.
- **Construction level range**: set a level interval per building type with a two-handle range bar; `0~20` means no limit.
- **Outstanding orders**: blueprints and frames merged by type; drag to reorder; materials reserved for higher rows when stock is short; lower rows will not steal the same material; jump-to-location.
- **Material budget and scheduling diagnostics**: allocation by rank, aging promotion, borrowing after the protection period; the Diagnostics tab gives each order one of five verdicts (granted / waiting for materials / yielding / nobody available / building disabled).
- **Row height**: per-pixel row height for the type table.
- **Quality build fix**: re-places the blueprint and carries the quality requirement when QualityBuilder does not.
- **Roof-first deconstruction**: removes ordinary roofs before deconstructing a load-bearing building; refuses overhead mountain; restores roof markers if the job is cancelled.

### Compatibility

- RimWorld **1.7**, requires **Harmony**.
- Tested in-game with an existing 211-mod list; no errors from this mod.

### Known limitations

See [Known issues and limitations](Docs/KNOWN_ISSUES.en.md).
