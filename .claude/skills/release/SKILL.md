---
name: release
description: Release NetOcc to nuget.org. Check the pending changelog entries, run the local checks, get the release PR ready (build.py release, release.yml), then follow the publish run after the user merges it.
disable-model-invocation: true
argument-hint: "[prerelease label: next (numbered) or e.g. preview.1; default: a release]"
arguments: [prerelease]
---

# Release NetOcc

**Versions** are `<OCCT version>.<revision>`, e.g. `8.0.1.2`: the OCCT version the bindings are generated for (netocc-gen stamps it into every module, and `vcpkg.json` must build the natives from the same) and NetOcc's revision, from 1 for each OCCT version. NuGet shows `8.0.1.0` as `8.0.1`, so revisions never start at 0. `build.py` computes and enforces the version; nothing else holds it.

**How a release happens:**
1. Pull requests add changelog entries, one file each: `python build.py change <added|changed|deprecated|removed|fixed|security> "what changed"` writes `netocc-core/changes/<name>.<type>.md`. `changelog.yml` fails a pull request that changes `netocc-core/src/` or `pack/` without one; the label `no changelog` skips it.
2. Entries on main start `.github/workflows/release.yml`: `python build.py release` writes them (and, the first time, the `[Unreleased]` section) into `CHANGELOG.md` as `## [<version>] - <date>` and deletes them, and the workflow opens or updates the pull request "Release NetOcc <version>".
3. Merging it publishes: build.yml finds a release in `CHANGELOG.md` without its tag, builds, tests, pushes the packages to nuget.org through Trusted Publishing and creates the GitHub release, which tags the commit `v<version>`.

You prepare and verify; **the user merges and pushes**. Never enter a PIN, never bypass signing; if signing fails, stop and ask.

## 1. What's pending

- `python build.py changelog` (in `netocc-core/`) prints the next release's notes. They must describe the release for package users: added, changed, fixed, removed; internal refactors stay out. Settle the wording with the user; entries are edited in `changes/`, or in the release PR.
- `python build.py version --latest` names the last release; the next revision follows it. A new OCCT version starts at revision 1.
- A prerelease (`$prerelease`, e.g. `preview.1`) gets the next revision with that label: `8.0.1.2-preview.1`. A bare label is numbered after the revision's last with it: `next` gives `8.0.1.2-next.1`, then `-next.2`. The release after them is `8.0.1.2`, and its notes list the prereleases' changes too; with no new entries, start it by hand (`gh workflow run release.yml`).
- **Prereleases from `next`** (changesets' pre mode): entries on `next` make release.yml open a PR into next for the next `-next.N`, and merging it publishes from next. When next is done, merge it into main and start the release there by hand; after each release, merge main back into next. The `release` environment must allow next (Settings > Environments > release > deployment branches).

## 2. Local checks (Windows)

In `netocc-core/`, logs in `log/`:

- **Build and test:** `python build.py generate`, `native --triplet x64-windows` and `x86-windows` if any `.i` changed, then `test --arch x64` and `test --arch x86`.
- **Package:** `python build.py pack --rids win-x64,win-x86`, then `test-package --arch x64` and `--arch x86`. Keep pack's default version (`<version>-local.<time>`): a local package with the release's version would shadow the published one in the NuGet cache.
- **Generated files:** if `netocc-generator/` changed, `generate --check` there must report up to date.
- Report the results and stop on any failure.

## 3. The release PR

- It's usually open already. If not, or for a prerelease, start release.yml by hand: `gh workflow run release.yml` (with `-f prerelease=$prerelease` for a prerelease; `--ref next` for next's, whose label defaults to `next`). Its push trigger filters on `changes/`, which needs normal commits: while `main` is still amended into its initial commit, run it by hand.
- Check the PR's `CHANGELOG.md` diff with the user: the heading's version and date, the sections, the link at the bottom. Its description (`build.py changelog --pull-request <branch>`) says what merging publishes and, on next, how the prerelease mode ends.
- The user merges it.

## 4. Follow the run

- The merge starts build.yml on main. Find it with `gh run list --workflow build.yml --limit 3` and follow it with `gh run watch <id>`. For failures, `gh run view <id> --log-failed`. `gh run view` prints logs only once the whole run is done; a finished job's log comes from `gh api --allow-escape-sequences repos/paulbuechner/netocc/actions/jobs/<job-id>/logs` (the flag: the logs hold color codes).
- **Duration:** about 10 minutes with the vcpkg cache, about an hour cold (OCCT on Windows). Runs on main queue rather than cancel each other, so a release run always finishes.
- If the `release` environment has reviewers, tell the user to approve the publish job.
- **A failed run before publish:** fix it on main; the next push publishes the same version, since it still has no tag.
- **Publish pushed some packages:** rerun the failed publish job (`gh run rerun <id> --failed`): `--skip-duplicate` skips what nuget.org has, and the GitHub release follows. The runtime packages go first, so until `NetOcc` itself is there, a new push to main publishes too. Once `NetOcc` is on nuget.org, only the rerun finishes that version (nuget.org versions are immutable, unlisting is the only fallback).

## 5. Verify

- The packages are listed on nuget.org: `https://www.nuget.org/packages/NetOcc/<version>`, plus the four `NetOcc.runtime.<rid>` packages. Validation and indexing take up to an hour.
- The release exists: `gh release view v<version> --repo paulbuechner/netocc`. Its notes are the CHANGELOG section, its assets the packages.
- Tell the user what was published, and link both.
- **After the first release:** the policy's scope can drop to "only new package versions". A release that adds a package ID needs "new packages" again.
