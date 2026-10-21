# T-004 — `System.Drawing.Common` is Windows-only in the ModKit

- **Priority**: 🟠 Medium
- **Type**: Portability
- **Status**: ✅ Done (closed 2026-10-21: the ModKit is a class library (`OutputType Library`) loaded by the game,
  which runs on Linux with 0 exceptions; nothing to launch separately) — `System.Drawing.Common` was an **unused** reference
  (no GDI+ types used anywhere) and is now Windows-only in `OpenTPW.ModKit.csproj`, so
  the Linux build excludes it. Note: a **transitive** `System.Drawing.Common 6.0.0`
  still enters the main project via `System.Configuration.ConfigurationManager` →
  `System.Security.Permissions`, but 6.0.0 ships a Unix implementation and is never
  called, so it is harmless. Removing `ConfigurationManager` (see T-006) would drop it
  entirely.

## Context

`System.Drawing.Common 8.0.0` is referenced by **OpenTPW.ModKit**. Since .NET 7 this
package **throws `PlatformNotSupportedException` off Windows** (official Microsoft
decision). The ModKit therefore will not run on Linux.

## Proposed fix

- Migrate `System.Drawing` usages to **`SixLabors.ImageSharp`**, already present in the
  solution (`OpenTPW.Files`).
- Find usages: `grep -rn 'System.Drawing\|Bitmap\|Graphics\|Color\b' source/OpenTPW.ModKit`.

## Acceptance criteria

- ModKit launches and shows image viewers on Linux without exceptions.
- The `System.Drawing.Common` package reference is removed.

## Affected files

`source/OpenTPW.ModKit/OpenTPW.ModKit.csproj` + ModKit viewers using `System.Drawing`.
