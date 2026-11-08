# Development (build prerequisites)

> This file covers **how to build** only. Features, architecture, testing and contribution notes live in the documents under `Docs/`.

## 1. Reference assemblies (`libs/`, not tracked)

The build needs four DLLs placed in `libs/` at the repository root (that directory is excluded by `.gitignore`). `0Harmony.dll` is MIT-licensed; the other three come from the game's install directory (RimWorld / Unity). All four are compile-time references only. **There are two modes — pick one depending on what you need.**

### A. Regular development: use the game's own assemblies

| File | Where to get it |
|---|---|
| `Assembly-CSharp.dll` | `<game install directory>\` (RimWorld 1.7 keeps its managed assemblies directly in the game root) |
| `RimWorld.dll` | `<game install directory>\` (same) |
| `UnityStubs.dll` | `<game install directory>\` (same) |
| `0Harmony.dll` | `<Harmony mod directory>\1.7\Assemblies\0Harmony.dll` |

This builds, and the output is **functionally equivalent** (not verified item by item in this mode — **compile only, never loaded in game**) — but its **bytes differ from the archived artifact `80b5956514db783df6ddfd451b1fb366`** (different reference metadata changes codegen). Use this mode for day-to-day work and debugging.

### B. Byte-identical reproduction: use the reference shells

You need the same set of **trimmed reference shells** the archive was built with (`Assembly-CSharp.dll` / `RimWorld.dll` / `UnityStubs.dll`) plus the matching `0Harmony.dll` (kept internally, not distributed with the repository), **and you must keep the current `BuildMonitor.csproj` compile order** (`ResearchStyleTabWindow.cs` is excluded from `Source\**\*.cs` and compiled separately at the end).

The reference shells are not distributed with this repository; you only need them when verifying byte-identical output. Line endings are not the cause of a mismatch — the reference assemblies are.

## 2. Build

```
dotnet build BuildMonitor.csproj -c Release
```

- SDK: .NET 10 (measured on the dev machine: `dotnet --version` = `10.0.301`)
- Output: `bin\BuildMonitor.dll`
- For a release, copy the output over the mod's `Assemblies\BuildMonitor.dll`

## 3. Verify

- **Mode A (daily)**: confirm the build has no warnings or errors and the game loads the mod with both tabs opening. No need to compare md5.
- **Mode B (archive check)**: `certutil -hashfile bin\BuildMonitor.dll MD5` should equal `80b5956514db783df6ddfd451b1fb366`. If it does not, check three things first: assembly version changed, a file compiled twice or missed, duplicate compilation. If it still differs, check whether you are on **mode-A reference assemblies** and whether the **compile unit order** was disturbed (see `ARCHIVE.md`, appendix D). **Never change code just to match the md5.**

## 4. Directory layout

| Directory | Contents |
|---|---|
| `Source/` | All C# sources, 11 files (including the shared base class `ResearchStyleTabWindow.cs`) |
| `About/` | `About.xml`, `Preview.png` |
| `Defs/` | Def definitions |
| `Languages/` | Chinese and English Keyed strings, plus English DefInjected |
| `Assemblies/` | Built and verified DLL |
| `libs/` | Build references (not tracked; place them yourself) |
