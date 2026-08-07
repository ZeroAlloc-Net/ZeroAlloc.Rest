# Releasing

Packages are published by `.github/workflows/release-please.yml`. When a
release PR merges, release-please tags the version and the workflow's publish
job — gated on `release_created == 'true'` — runs the tests, packs
`ZeroAlloc.Rest.slnx` and pushes every package to NuGet.

## Two ways this pipeline goes quiet

Both have happened. Neither announces itself, because in both cases `main` is
green and nothing is obviously broken.

### A failing test blocks the push after the tag is cut

The publish job runs `dotnet test` before `dotnet pack`. If a test fails there,
release-please has *already* tagged the version and created the GitHub release —
so the repository looks released while NuGet never received anything.

That is what happened to **v1.2.2** (16 June 2026). The tag, the GitHub release
and `.release-please-manifest.json` all said `1.2.2`; NuGet stayed on `1.1.3`.
The blocking test was later fixed, but nothing re-ran the publish, so the
release stayed stranded for two months.

**Check after any release:** the package version on NuGet, not the tag. A tag
proves release-please ran; it does not prove anything shipped.

### A history of only `chore:` commits never cuts a release

release-please treats `chore:` as non-releasable. A repo whose commits are all
`chore(deps): update …` — which is what a quiet, actively-maintained library
looks like — accumulates changes indefinitely without ever producing a release
PR.

After v1.2.2 this repo took **48 consecutive `chore(deps)` commits**, including
Refit v15 and ZeroAlloc.Resilience 1.3.0. No release PR was ever raised, so the
stranded release could not correct itself either.

Dependency updates do change what consumers restore. When they have piled up,
force a release rather than waiting for one:

```
fix(deps): release accumulated dependency updates

Release-As: 1.3.0
```

The `Release-As:` footer sets the version explicitly; the `fix:` type makes the
commit releasable in the first place.

## Rescue

`publish-from-manifest.yml` is a manually-dispatched workflow that packs and
pushes at the version in `.release-please-manifest.json`, with
`--skip-duplicate` so re-runs are safe:

```bash
gh workflow run publish-from-manifest.yml --repo ZeroAlloc-Net/ZeroAlloc.Rest
```

It packs from the default branch, so it republishes *current* `main` under the
manifest version. That is right when a publish failed and `main` has not moved.
When `main` has moved on, prefer cutting a new version, so the published version
matches its contents.
