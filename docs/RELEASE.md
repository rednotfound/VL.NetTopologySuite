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

**nuget.org, checked 2026-09-25:** the four family IDs (`VL.NetTopologySuite`, `VL.GeoJSON`,
`VL.Mapsui`, `VL.Overworld`) do not exist there yet, so no ID is taken and the first push creates
the package under the pushing account. `VL.GIS` exists with six versions (`0.0.1` … `0.2.0-alpha`),
**all unlisted** — they no longer appear in search or in the maintainer's public profile, but each
is still installable by exact version. That account is the one this package will be published
from, and its existing API key may be scoped to `VL.GIS` only — see step 1 below.

**The gate ran green in full on 2026-09-25 after the release notes were written**, as its own step
before the commit: build, 126 tests, `Test-VLPackage`, `Test-VLPatch`, pack, `Compile-HelpPatches`
(15 patches), `Test-Install` (3 packages, 15 installed patches compile).

`Test-Install.ps1` is the strongest evidence available short of publishing. It is carried from
vl-geojson (which carried it from vl-mapsui) and was not negative-tested here; its "does not even
install" path was exercised there. It does **not** prove nuget.org behaves like a local feed, and
it does not prove the patches *run* — only the GUI round does.

---

## Versioning — a prerelease, in step with the family

Decided by the maintainer on 2026-09-25:

- **Every release is a prerelease until further notice.** The version always carries a
  `-suffix`; nuget.org then hides it from a default search and a user installs it with
  `nuget install VL.NetTopologySuite -pre`. There is no "正式版" planned yet, and nothing here
  should read as if there were.
- **The version is managed together with VL.Mapsui's, and the four family packages stay in step
  — at least to begin with.** All four (`VL.NetTopologySuite`, `VL.GeoJSON`, `VL.Mapsui`,
  `VL.Overworld`) are at **`0.0.1-alpha`** on 2026-09-25, and VL.Mapsui's nuspec declares its
  dependency on `VL.NetTopologySuite 0.0.1-alpha`. So the first release is **`0.0.1-alpha`**, the
  same suffix as the siblings (not `-pre`, not `-preview`), and the number the nuspec already
  holds. The earlier idea of `0.1.0-alpha` is withdrawn.
- **Right after publishing, the working version becomes `0.0.2-alpha`** (the rule vl-mapsui wrote
  down): the dev loop repacks the same version all day, and NuGet uses any cached copy whose
  version matches without looking at the feed — once a real `0.0.1-alpha` exists in someone's
  cache, a local one with that number is indistinguishable from it. Whoever bumps VL.Mapsui bumps
  this one the same day, and VL.Mapsui's dependency line follows.
- **One place holds the version:** `<version>` in `VL.NetTopologySuite.nuspec`. There is no
  publish workflow overriding it. The help patches pin `Version="0.0.0"`
  (`tools\Normalize-HelpPatches.ps1`) and never change.

## Decisions — settled and open

Settled by the maintainer on 2026-09-25:

- **The description keeps its first line, `EARLY - not ready for real work yet.`** It is true, and
  a prerelease page should say so.
- **`<readme>docs\README.md</readme>` is in the nuspec.** `nuget pack` accepts it (it refuses when
  the file is not in `<files>`; `README.md` was already packed to `docs\`), `Test-VLPackage` passes,
  and the packed nuspec carries the element — checked in the nupkg.
- **The public home.** The nuspec `projectUrl` and `repository`, and the git remote, all say
  `github.com/rednotfound/VL.NetTopologySuite`, and the repository is public (checked 2026-09-25,
  as are the other three and vvvv-gis). `<authors>` and `<owners>` read
  `VL.NetTopologySuite Contributors`.

Open: **who pushes, and how.** The maintainer wants to learn the process step by step before
choosing; the two ways are laid out below. Either way **the maintainer performs the irreversible
step**: a session prepares, validates and commits, then hands over the exact command and stops.

## Two ways to push, and which to learn first

Both end with the same HTTP request to nuget.org carrying an **API key** — a secret string from
the maintainer's nuget.org account that authorises publishing under that account. The difference
is where the key lives and who types the command.

### A. By hand, from this machine (recommended for the first release)

1. Sign in at https://www.nuget.org, then *Account → API Keys → Create*. Give it a name
   (`vl-family`), an expiry (a year), the scope **Push new packages and package versions**, and a
   **glob pattern** `VL.*` so the key can publish nothing else. Copy the key once; nuget.org never
   shows it again.
2. Put the key in an environment variable for the one shell session, never in a file in the
   repository and never on a command line that ends up in a shell history:
   ```powershell
   $env:NUGET_KEY = Read-Host -AsSecureString "nuget.org API key" | ConvertFrom-SecureString -AsPlainText
   ```
3. Push the packed file:
   ```powershell
   $nuget = .\tools\Find-Vvvv.ps1 -NuGet
   & $nuget push .\dist\feed\VL.NetTopologySuite.0.0.1-alpha.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_KEY
   ```
4. nuget.org answers within seconds; the package page appears in a few minutes and search finds
   it within an hour. A push of a version that already exists is refused (409), so a repeat is
   harmless.

What it teaches: exactly what a publish is, with nothing hidden, and it needs no repository
change. What it costs: the key passes through a local shell once, and every later release is a
manual step that someone must remember to run after the gate.

### B. GitHub Actions on a tag (the shape for the second release)

1. Create the same kind of key on nuget.org.
2. In the GitHub repository: *Settings → Secrets and variables → Actions → New repository secret*,
   name `NUGET_KEY`, paste the key. GitHub stores it encrypted and never shows it again; a workflow
   reads it as `${{ secrets.NUGET_KEY }}`.
3. Add `.github\workflows\publish.yml`: on a pushed tag `v*`, check out, install .NET 8, download
   vvvv (or use `dotnet pack` with the nuspec), run the gate, `nuget push` with the secret. vvvv's
   own `vvvvc` and `NuGet.exe` are not on a GitHub runner, so the workflow either installs vvvv
   there (slow, ~1 GB) or packs with `dotnet nuget` alone and trusts the gate that ran locally.
4. `git push origin v0.0.1-alpha` is then the irreversible step.

What it teaches: a release becomes `git tag` + `git push`, repeatable and logged, and the key never
touches a local machine — this is what the maintainer chose on vvvv-gis. What it costs: a
workflow to write and debug, a runner without vvvv, and a first failure that is harder to read than
a local one.

**Recommendation:** A for `0.0.1-alpha`, on both this package and VL.Mapsui, so the maintainer has
seen a publish end to end once. Then B for `0.0.2-alpha`, written once and copied across the family.

---

## The steps, in order

Every step is separate, and a validator never runs in the same command as a commit or a push.

```powershell
# 0. vvvv closed. The tree clean except for what this release changes.
git status

# 1. The version stays 0.0.1-alpha (see Versioning). Only the <releaseNotes> change: they still
#    say "First package. Not published." Same file, one commit.

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

# 4. Commit the release notes by name, tag, push the source.
git add VL.NetTopologySuite.nuspec
git commit -m "release: 0.0.1-alpha"
git tag v0.0.1-alpha
git push origin main --tags

# 5. The maintainer pushes the package. This is the irreversible step.
$nuget = .\tools\Find-Vvvv.ps1 -NuGet
& $nuget push .\dist\feed\VL.NetTopologySuite.0.0.1-alpha.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey <key>

# 6. Wait for indexing (minutes to an hour), then in a vvvv that has never seen the package:
#    vvvv's command line:   nuget install VL.NetTopologySuite -pre
#    Help Browser -> VL.NetTopologySuite -> the fifteen patches; F1 on a node.

# 7. A GitHub release on the tag, with the nuspec's release notes as its body.

# 8. The same day: bump the working version to 0.0.2-alpha in the nuspec (see Versioning), so a
#    local repack can never be mistaken for the published package.
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
