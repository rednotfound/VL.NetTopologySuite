# Releasing VL.NetTopologySuite

The steps to a first release on nuget.org, what each one proves, and the decisions that are still
the maintainer's. Written 2026-09-25, when the node surface had been unchanged since 2026-08-28,
every help patch had been reviewed by hand, and the fake install passed for the first time.

**Nothing here is done automatically, and nothing is pushed without the maintainer saying so.**
A push to nuget.org cannot be undone — a version can be unlisted, never deleted — so the order below
is: prove locally, then push by hand, then automate for the *second* release.

Order across the sibling repositories (from vl-overworld's plan): **VL.NetTopologySuite and
VL.GeoJSON first**, then VL.Mapsui, then VL.Overworld. This package gates the chain.

---

## Where it stands (2026-09-25)

| step | proves | state |
|---|---|---|
| `dotnet test` | the arithmetic | ✅ 126 tests |
| `tools\Test-VLPackage.ps1` | the package can contribute nodes at all (`ImportAsIs`, the `.vl` at the root, the nuspec files list) | ✅ |
| `tools\Test-VLPatch.ps1` | every `.vl` well formed, `Help.xml` complete, every node F1-flagged | ✅ 16 documents |
| `pack.ps1` + `tools\Compile-HelpPatches.ps1` | every node in every help patch resolves against the **packed** nupkg, read from the generated C# | ✅ 15 patches |
| **`tools\Test-Install.ps1`** | `nuget install` from `dist\feed` brings NetTopologySuite and NetTopologySuite.Features along, and every help patch **inside the installed package** compiles with that install as the only package repository | ✅ first pass 2026-09-25 |
| the vvvv GUI | every category on a screen, every help patch computing what its notes say, F1 on all 39 nodes | ✅ 2026-08-14 … 2026-09-25 |
| the maintainer's own review in vvvv | the patches read right to someone who reads vvvv help daily | ✅ 2026-09-24/25, eleven patches adjusted by hand and committed |
| push to nuget.org | a stranger's vvvv can install it | ⬜ **not done** |
| install from nuget.org in a clean vvvv | nuget.org behaves like the local feed | ⬜ |
| GitHub release + tag | the source that matches the package is findable | ⬜ |

`Test-Install.ps1` is the strongest evidence available short of publishing. It is carried from
vl-geojson (which carried it from vl-mapsui) and was not negative-tested here; its "does not even
install" path was exercised there. It does **not** prove nuget.org behaves like a local feed, and
it does not prove the patches *run* — only the GUI round does.

---

## Decisions that are the maintainer's

None of these is made in the repository yet. Each is one line to change once decided.

1. **Version.** The nuspec says `0.0.1-alpha`. Proposed: **`0.1.0-alpha`** — six categories, three
   consumers (VL.Overworld's tutorials 08, 11, 13; VL.Mapsui; VL.GeoJSON), a stable surface since
   2026-08-28. One place to change: `<version>` in `VL.NetTopologySuite.nuspec`. The help patches
   pin `Version="0.0.0"` (`tools\Normalize-HelpPatches.ps1`) and need no change.
2. **Who pushes.** By hand from the maintainer's machine with their own API key, or with a key
   handed to a session. The first push should be by hand either way.
3. **The public home.** The nuspec `projectUrl` and `repository`, and the git remote, all say
   `github.com/rednotfound/VL.NetTopologySuite`. If that is the public repository, it must be public
   before the push, because nuget.org links to it. `<authors>` and `<owners>` currently read
   `VL.NetTopologySuite Contributors`.
4. **The description's first line.** It opens with `EARLY - not ready for real work yet.` That was
   true in 2026-08. Whether it stays for an alpha is a judgement about what the package page should
   say to a stranger.
5. **`<readme>`.** nuget.org shows a README on the package page only when the nuspec names one
   (`<readme>docs\README.md</readme>` — the file is already packed to `docs\`). Recommended, but it
   is a nuspec change and belongs in its own commit.

---

## The steps, in order

Every step is separate, and a validator never runs in the same command as a commit or a push.

```powershell
# 0. vvvv closed. The tree clean except for what this release changes.
git status

# 1. Decide the version (decision 1) and change it in ONE place.
#    VL.NetTopologySuite.nuspec: <version>0.1.0-alpha</version>
#    Also the <releaseNotes> for this version.

# 2. Everything green, from a clean build:
.\build.ps1
dotnet test test\VL.NetTopologySuite.Tests\VL.NetTopologySuite.Tests.csproj
.\tools\Test-VLPackage.ps1
.\tools\Test-VLPatch.ps1
.\pack.ps1 -NoBuild
.\tools\Compile-HelpPatches.ps1
.\tools\Test-Install.ps1                # the fake install; reads the version from the nuspec

# 3. One GUI round on the packed package, as a user would see it:
#    open the Help Browser, find VL.NetTopologySuite, open the Explanation, press F1 on a node.
.\tools\Open-HelpPatch.ps1 "Explanation"

# 4. Commit the version bump by name, tag, push the source.
git add VL.NetTopologySuite.nuspec
git commit -m "release: 0.1.0-alpha"
git tag v0.1.0-alpha
git push origin main --tags

# 5. Push the package BY HAND. This is the irreversible step.
$nuget = .\tools\Find-Vvvv.ps1 -NuGet
& $nuget push .\dist\feed\VL.NetTopologySuite.0.1.0-alpha.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey <key>

# 6. Wait for indexing (minutes to an hour), then in a vvvv that has never seen the package:
#    vvvv's command line:   nuget install VL.NetTopologySuite -pre
#    Help Browser -> VL.NetTopologySuite -> the fifteen patches; F1 on a node.

# 7. A GitHub release on the tag, with the nuspec's release notes as its body.
```

**Automation comes second.** A GitHub Actions workflow that packs on a tag and pushes with a
repository secret is the right shape for the *second* release, once the first has shown the
package page, the dependency resolution and the help browser all behave. vl-geojson has no such
workflow yet either; when one is written it should override the nuspec version from the tag.

---

## What the release does not change

- **The help patches pin `0.0.0`**, like every pack that ships with vvvv. They keep working across
  versions; `Normalize-HelpPatches.ps1` restores the pin after any GUI session.
- **The two `NugetDependency` lines in `VL.NetTopologySuite.vl`** (NetTopologySuite 2.6.0,
  NetTopologySuite.Features 2.1.0) and the two `<dependency>` lines in the nuspec must keep
  matching. The install test checks that both dependencies arrive.
- **The `VL` tag** in the nuspec is what lists the package at https://vvvv.org/packs.

---

## After the first release

- Tell the sibling repositories: VL.Mapsui and VL.Overworld's documents currently reach this
  package through `--package-repositories`; once it is on nuget.org they can declare it like any
  other package.
- The ROADMAP's open design question — reference-then-value rebuild detection in `SpatialIndex` and
  `Network` — changes a documented contract and is better decided *before* a release that people
  build on, or explicitly deferred to `0.2`.
- `VL.GIS 0.2.0-alpha` overlaps this package by roughly 40 nodes. What becomes of it is a separate
  decision, after this package is stable and proven — see the ROADMAP's last section.
