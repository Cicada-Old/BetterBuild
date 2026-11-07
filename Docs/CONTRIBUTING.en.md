# Contributing

[简体中文](CONTRIBUTING.md) · RimWorld 1.7 · Better Build 1.0.0

Issues, bug reports and pull requests are welcome. Start with these three: [Features](FEATURES.en.md), [Architecture](ARCHITECTURE.en.md), [Known issues](KNOWN_ISSUES.en.md).

## 1. Reporting a problem

Open an Issue and include as much of the following as you can:

- Game version (1.7.x) and whether Harmony is installed
- Whether other construction-related mods are enabled (QualityBuilder, for example)
- Reproduction steps: where you clicked, what you built, what you expected, what happened
- A log excerpt: `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`, this mod's lines start with `[BM]`
- A screenshot of the Diagnostics tab — for "order not moving" reports the reason column usually shows which layer is blocking

**Check the known issues first**: the three listed limitations do not need a new report.

## 2. Pull requests

- One change per PR. Drive-by formatting or renames belong in a separate PR.
- Explain **why**: what problem it solves and which behaviour it touches. A PR that only says "refactored" will be closed.
- If player-visible behaviour changes, update `CHANGELOG.md` and the relevant documents (`FEATURES` / `KNOWN_ISSUES`) in the same PR.
- Changes to save data (`Scribe_*` keys) must describe the backward-compatibility plan; otherwise they are not accepted.

## 3. Code conventions

- **One file, one domain.** New features get a new file rather than another class appended to `BuildMonitor.cs`.
- Filtering and release logic belongs at the completion gate, not scattered across WorkGiver patches.
- New settings: add to the relevant `GameComponent.ExposeData`, give a default, clamp the range, then add a row to the settings dialog.
- Comments explain **why**, not **what**. Update stale comments while you are there — a comment disagreeing with its constant has already cost this project a review cycle.
- Do not add new third-party dependencies. If one is unavoidable, open an Issue first.

## 4. What is not accepted

- **Changing logic to make a test pass**: no special cases, clamps or inverted conditions added so that a number looks better, a screenshot does not overflow, or an assertion holds. Fix the root cause; if the expectation is genuinely wrong, change the test and the document.
- **Hiding known problems**: anything that cannot be fixed goes into `KNOWN_ISSUES.md` with its impact stated. Removing it from the docs is not an option.
- **Presenting plans as features**: unimplemented work may only appear as "planned" in the roadmap, never in the feature description.

## 5. Building and self-testing

```bat
dotnet build BuildMonitor.csproj -c Release
```

- Requires .NET 10; reference assemblies go into `libs/` (not tracked), see [Development](../DEVELOPMENT.en.md) for how to obtain them.
- Output is `bin\BuildMonitor.dll`; copy it to the mod's `Assemblies\BuildMonitor.dll` and check in game.
- Before opening a PR, at minimum: the game starts without errors, both tabs open, the touched feature works, and you have looked at the Chinese and English UI once each.
- Output bytes depend on compile order. Before moving files or changing `Compile` order in the `csproj`, read the reproduction section of [ARCHIVE](../ARCHIVE.md).

## 6. License

By submitting code you agree it is distributed under this project's [MIT license](../LICENSE). Do not submit code or assets from other mods or from unlicensed sources.
