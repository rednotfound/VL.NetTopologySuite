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
| publish to nuget.org | a stranger's vvvv can install it | ✅ **2026-09-26, `0.0.1-alpha`**, browser upload by the maintainer (way A), validated and indexed within the hour |
| install from nuget.org into a fresh folder | nuget.org behaves like the local feed | ✅ 2026-09-26: `nuget install VL.NetTopologySuite -Version 0.0.1-alpha -PreRelease` from nuget.org alone brought NetTopologySuite 2.6.0 and NetTopologySuite.Features 2.1.0; all 15 shipped help patches compiled with that folder as the only package repository; `HowTo Find the shortest path` opened from it in vvvv and drew the town and the path |
| tag `v0.0.1-alpha` on GitHub | the source that matches the package is findable | ✅ 2026-09-26, pushed with main |
| `Test-Install.ps1 -FromNuGetOrg -Version 0.0.1-alpha` | the same, repeatably, with no local feed and no cache: the installed nupkg carries nuget.org's `.signature.p7s` | ✅ 2026-09-26, 3 packages, 15 of 15 compile; the two guards (non-empty folder, missing `-Version`) negative-tested |
| listed at vvvv.org/packs | the `VL` tag did its job | ✅ 2026-09-26, both VL.NetTopologySuite and VL.Mapsui appear |
| GitHub release on the tag | release notes readable without opening the nuspec | ⬜ the maintainer, in the browser — text below |
| the README's install path, walked by the maintainer in vvvv | the one check no script can make: a person follows "Install and your first geometry" against the published package | ⬜ the maintainer (VL.Mapsui's was walked 2026-09-26: "可以用") |
| working version bumped to `0.0.2-alpha` | a local repack can never be mistaken for the published package | ✅ 2026-09-26 |

**The GitHub release** (repository → Releases → Draft a new release → choose tag `v0.0.1-alpha` →
title `VL.NetTopologySuite 0.0.1-alpha` → tick **Set as a pre-release** → Publish). Body:

```markdown
The first release, a prerelease. **EARLY — not ready for real work yet:** the node
surface may still change between prereleases.

- 39 nodes in six categories — NTS.Geometry, NTS.Feature, NTS.Operation, NTS.IO, NTS.Index, NTS.Network
- 126 tests, 15 help patches, and F1 on every node opens one
- Geometry from coordinates, inspection and validation, buffer / overlay / predicates / distance,
  WKT in and out, a spatial index, and a shortest path over a street network
- Coordinates are copied on the way in and out, so a geometry cannot be moved behind your back
- Wraps NetTopologySuite 2.6.0; verified in vvvv gamma 7.4

Install in vvvv (Quad menu → Manage Nugets → Commandline):

    nuget install VL.NetTopologySuite -pre

NuGet: https://www.nuget.org/packages/VL.NetTopologySuite/0.0.1-alpha
```

Optional, and the same for VL.Mapsui: the GitHub repository has no **description** or **topics**
yet (Settings, or the gear beside *About*). A description such as *NetTopologySuite geometry nodes
for vvvv gamma* and topics `vvvv`, `vl`, `nettopologysuite`, `geometry`, `gis` are what GitHub
search and the vvvv community find a repository by.

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
choosing; the three ways nuget.org offers in 2026 are laid out below. Whichever it is, **the
maintainer performs the irreversible step**: a session prepares, validates and commits, then hands
over and stops.

## The three ways to publish in 2026, and which to learn first

Checked against the official documentation on 2026-09-26. The nuget.org account page now labels
**API Keys "Not Recommended"**, and the reason is a policy change announced on the .NET Blog on
2026-08-03 (*Strengthening NuGet Supply Chain Security: Reducing API Key Lifetime*):

- "Starting August 17, 2026, new API keys will be limited to 30 days. A 365-day duration will no
  longer be available for new API keys."
- "All existing API keys created before that date will expire on November 1, 2026." So the key
  that published VL.GIS, whatever its expiry says, stops working on that date.
- For CI: migrate to **Trusted Publishing** (OIDC, launched September 2025).
- For manual publishing: "Package publishing through the NuGet.org web interface remains available
  for manual scenarios."

### A. Upload in the browser — no key at all (recommended for `0.0.1-alpha`)

1. Sign in at https://www.nuget.org and select **Upload** in the top menu.
2. Browse to `dist\feed\VL.NetTopologySuite.0.0.1-alpha.nupkg`, open it.
3. nuget.org reads the package and shows a **Verify** section with every field from the nuspec —
   id, version, description, tags, dependencies, licence — and, because the nuspec names a
   `<readme>`, a **Preview** button that renders `docs\README.md` exactly as the package page will.
   If the ID were already taken this is where it would say so (it is not; checked 2026-09-25).
4. Anything wrong: change the nuspec, repack, upload again — nothing has been published yet.
5. **Submit.** This is the irreversible step. Validation and indexing "usually take less than
   15 minutes"; until then the package sits under *Manage packages → Unlisted Packages* with a
   "not yet published" notice, then an email confirms it.

What it teaches: every field a stranger will see, checked by eye before the one click that cannot
be undone, with no secret created and nothing stored anywhere. What it costs: a browser step per
release, so it does not scale — which is fine for the first one.

### B. A 30-day API key from the command line — the old way, still working

*Account → API Keys → Create*: name `vl-family`, expiry at most 30 days now, scope **Push new
packages and package versions**, glob pattern `VL.*`. Copy once. Then, with the key in an
environment variable for the one shell session and never on a command line that reaches a history:

```powershell
$env:NUGET_KEY = Read-Host -AsSecureString "nuget.org API key" | ConvertFrom-SecureString -AsPlainText
$nuget = .\tools\Find-Vvvv.ps1 -NuGet
& $nuget push .\dist\feed\VL.NetTopologySuite.0.0.1-alpha.nupkg -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_KEY
```

It works, and a repeat push of an existing version is refused (409), so it is safe to retry. But
a key now lives 30 days, so every release month needs a fresh one — the exact chore Trusted
Publishing removes. Use this only if the browser upload is unavailable for some reason.

### C. Trusted Publishing from GitHub Actions — no stored secret (the shape from `0.0.2-alpha`)

The workflow asks GitHub for a short-lived OIDC token that says "this is repository
`rednotfound/VL.NetTopologySuite`, workflow `publish.yml`"; nuget.org checks it against a policy
the maintainer registered and hands back a **temporary API key valid for one hour**, used once.
Nothing is stored in GitHub secrets except, optionally, the nuget.org username.

1. On nuget.org: username → **Trusted Publishing** → add a policy. *Repository Owner*
   `rednotfound`, *Repository* `VL.NetTopologySuite`, *Workflow File* `publish.yml` (file name only),
   *Environment* empty, scope **Push new packages and package versions**, glob `VL.*`, owner: you.
   For a public repository the policy is active at once; a private one is "temporarily active" for
   seven days until the first successful publish locks it to the repository's IDs.
2. In the repository, `.github\workflows\publish.yml`, triggered by a pushed tag `v*`, with the two
   lines the docs insist on:
   ```yaml
   jobs:
     publish:
       runs-on: windows-latest
       permissions:
         contents: read
         id-token: write            # lets GitHub issue the OIDC token
       steps:
         - uses: actions/checkout@v4
         - uses: actions/setup-dotnet@v4
           with: { dotnet-version: 8.0.x }
         # build src\, then pack with the nuspec (nuget.exe or dotnet pack) into dist\feed
         - name: NuGet login (OIDC -> temporary API key)
           uses: NuGet/login@v1
           id: login
           with:
             user: ${{ secrets.NUGET_USER }}   # the nuget.org USERNAME, not the email
         - name: push
           run: dotnet nuget push dist\feed\*.nupkg --api-key ${{ steps.login.outputs.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json
   ```
3. `git push origin v0.0.2-alpha` is then the irreversible step.

What it costs, and why it is not the first release: a GitHub runner has no vvvv, so `vvvvc`,
`Compile-HelpPatches` and `Test-Install` cannot run there — the workflow packs and pushes, and the
gate stays a local step that must be green *before* the tag. The runner also packs with a
different NuGet than vvvv's, so the first run should be compared against a local `pack.ps1` output
(same file list, same nuspec inside). Written once, the same file serves VL.Mapsui, VL.GeoJSON and
VL.Overworld with one policy each on nuget.org.

**Recommendation:** **A** for `0.0.1-alpha`, on this package and then VL.Mapsui — the maintainer
sees every field before the click, and no key is created that expires on 2026-11-01 anyway. Then
**C** for `0.0.2-alpha` across the family. **B** only as a fallback. Whatever key published VL.GIS
can be deleted now; it dies on 2026-11-01 regardless.

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

# 5. The maintainer publishes the package: nuget.org -> Upload -> dist\feed\VL.NetTopologySuite.0.0.1-alpha.nupkg
#    -> Verify (Preview the readme) -> Submit. This is the irreversible step. (Way A below; B and C
#    are the command-line and the GitHub Actions alternatives.)

# 6. Wait for validation and indexing (usually under 15 minutes), then in a vvvv that has never seen the package:
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

**How the first one went (2026-09-26).** Browser upload, way A: the Verify page showed every nuspec
field and the README preview; Submit; the package page said "not been published yet ... validation
and indexing may take up to an hour"; the registration and flat-container endpoints answered 404
for about ten minutes, then the confirmation email arrived and both answered 200. Nothing needed a
retry. The install-back check above ran within the hour.

- Tell the sibling repositories: VL.Mapsui's nuspec already declares `VL.NetTopologySuite
  0.0.1-alpha`, and from 2026-09-26 that resolves from nuget.org; VL.Overworld's documents still
  reach this package through `--package-repositories` and can now declare it like any other
  package.
- The ROADMAP's open design question — reference-then-value rebuild detection in `SpatialIndex` and
  `Network` — changes a documented contract and is better decided *before* a release that people
  build on, or explicitly deferred to `0.2`.
- `VL.GIS 0.2.0-alpha` overlaps this package by roughly 40 nodes. What becomes of it is a separate
  decision, after this package is stable and proven — see the ROADMAP's last section.
