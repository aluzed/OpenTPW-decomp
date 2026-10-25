# T-005 — Dependencies with known vulnerabilities

- **Priority**: 🟠 Medium
- **Type**: Security / maintenance
- **Confirmed by**: NuGet warnings at build time + `dotnet list package --vulnerable`
- **Status**: ✅ **Done.** `dotnet list package --vulnerable --include-transitive` now
  reports **no findings** on any project (was several High). Build green, tests green.

## Findings (and resolution)

Direct references (raised as build warnings):

```
NU1903 (high)     SixLabors.ImageSharp 3.1.6  → GHSA-2cmq-823j-5qj8   → bumped to 3.1.12
NU1902 (moderate) SixLabors.ImageSharp 3.1.6  → GHSA-rxmq-m78w-7wmc   → bumped to 3.1.12
NU1901 (low)      Zio 0.17.0                  → GHSA-h39g-6x3c-7fq9   → bumped to 0.22.2
```

Transitive references (found with `--include-transitive`, fixed in the same pass):

```
High  Newtonsoft.Json 9.0.1 (via Veldrid, all projects)  → central pin 13.0.4
High  System.Net.Http 4.3.0 (test project only)          → dropped by test-pkg bump
High  System.Text.RegularExpressions 4.3.0 (test only)   → dropped by test-pkg bump
```

## Fix applied

- **ImageSharp** `3.1.6 → 3.1.12` (latest patch on the 3.1 line) in
  `source/OpenTPW.ModKit/OpenTPW.ModKit.csproj`. **2026-10-25**: ImageSharp dropped. 3.1.12 had four new
  advisories (GHSA-j3p4-wp97-rph4, GHSA-j9gm-c75j-xc9q, GHSA-jjfr-hcj7-qf5w, GHSA-gwg2-r3hj-4w44), fixed only in
  4.1.2+, whose Release builds require a Six Labors licence key. The ModKit only wrote 16x16 PNG thumbnails, so it
  now box-filters them itself and writes them with `StbImageWriteSharp` (MIT), the sibling of the game's
  `StbImageSharp`.
- **Zio** `0.17.0 → 0.22.2` (advisory patched version) in
  `source/OpenTPW.Common/OpenTPW.Common.csproj`. Note: Zio is currently unused in code.
- **Newtonsoft.Json**: central security pin `13.0.4` in `source/Directory.Build.props`
  (overrides the transitive 9.0.1 pulled by Veldrid across every project).
- **Test packages** (`source/OpenTPW.Tests/OpenTPW.Tests.csproj`):
  `Microsoft.NET.Test.Sdk 16.11.0 → 18.6.0`, `MSTest.TestAdapter/Framework 2.2.7 → 4.2.3`,
  `coverlet.collector 3.1.0 → 10.0.1` — this removes the old `System.Net.Http 4.3.0`
  and `System.Text.RegularExpressions 4.3.0` shims.

## Acceptance criteria

- ✅ `dotnet list package --vulnerable --include-transitive` reports no findings.
- ✅ Build green, tests green (0 failed, 2 passed, 6 inconclusive).
