# API conformance check for the provider clients: design

**Date:** 2026-10-28
**Status:** agreed with the owner on 2026-10-28. The owner chose spec diffs plus live calls, and the Anthropic key as a
repo secret.

## Goal

Detect when Anthropic's Messages API or Ollama's `/api/embed` changes in a way that affects `ZeroAlloc.Rest.Anthropic`
or `ZeroAlloc.Rest.Ollama`. When it happens, open an issue assigned to the maintainer, so someone acts before a user
hits it.

## Shape

- **Workflow:** `.github/workflows/api-conformance.yml`.
- **When it runs:** a weekly `schedule` (Monday 05:17 UTC) and `workflow_dispatch`. It never runs on `pull_request`, so
  forks never see the secret.
- **Permissions:** `contents: read` and `issues: write`.

Four check jobs each write a drift report as an artifact. A final `report` job runs with `if: always()` and turns
those reports into one issue.

| Job | Detects | How |
|---|---|---|
| `ollama-spec` | A changed contract for `/api/embed` | Fetch `docs/openapi.yaml` from `ollama/ollama@main`. Extract the `/api/embed` operation and every schema it references. Normalize: sorted keys, no descriptions or examples. Diff against `eng/api-conformance/ollama-embed.snapshot.yaml`. |
| `ollama-live` | The client no longer works against a real server | Start a service container `ollama/ollama` and pull `all-minilm`. Run the conformance runner's `ollama` mode: a batch of 2 inputs; expect 2 vectors of equal non-zero length, the model echoed back, and `prompt_eval_count` present. |
| `anthropic-live` | A changed response or stream shape | With the `ANTHROPIC_API_KEY` secret, send the 7 `TranscriptRequests` through `AnthropicClient`. Each must parse. Compare the live JSON *shape* with the recorded fixture: the set of property paths and value kinds, with arrays unified and values ignored. For streams, compare the set of event types and the shape of each event. A missing path is **breaking**; a new path or event type is **additive**. |
| `anthropic-sdk-watch` | An early warning that the API changed | Read the latest release tag of `anthropics/anthropic-sdk-csharp` and the git tree SHA of its Messages models directory at that tag. Compare with `eng/api-conformance/anthropic-sdk-messages.json`, which records the tag and tree SHA. A change is **review**, with a compare link. |

The conformance runner is a new console project, `tests/ZeroAlloc.Rest.Conformance`. It is not packable and not a
test project. It links `TranscriptRequests.cs` and reads the fixtures it compares against. It prints shapes and counts
only, never values from requests or headers, and never the key.

## The issue

The `report` job collects the reports.
- **Nothing found:** no issue, and the job is green.
- **Otherwise:** search for an open issue labelled `api-drift`.
  - If one exists, add a comment with the run's findings.
  - If not, create one with these properties:
    - **Title:** `API drift: <breaking|additive|review> in <Anthropic|Ollama>`.
    - **Labels:** `api-drift`. Add `P1` when anything is breaking.
    - **Assignee:** `MarcelRoozekrans`.
    - **Body:** each finding, the run link, and how to refresh the snapshots once handled.
- **A check job that fails** for its own reasons, such as a missing secret, a pull error or a runner crash, is reported
  as an `infrastructure` finding. A broken check never looks like "no drift".

## Snapshots

`eng/api-conformance/update-snapshots.sh` rewrites both snapshot files from the current upstream. A maintainer runs it
after acting on an issue, and commits the result with the fix. The Anthropic fixtures are refreshed with the existing
recorder, not by this workflow.

## Cost and safety

- **Anthropic:** 7 calls a week on `claude-haiku-5-5`, under one cent, using a dedicated key with a low spend limit.
- **Ollama:** free.
- **Secrets:** the key is only in the job's environment. The runner and the reports never print it, and the
  transcript scrub guard's patterns are reused on the reports before upload.

## Tests

- The shape comparer gets unit tests in `tests/ZeroAlloc.Rest.Anthropic.Tests`:
  - identical shapes give no findings;
  - a removed field is breaking;
  - an added field or event type is additive;
  - array element shapes are unified.
- The normalizer for the Ollama spec gets a unit test over a small YAML sample.
- Every new assertion carries a `Red:` line, applied once and seen failing.
- The workflow itself is proven by one `workflow_dispatch` run on the branch before merge. The run is temporarily
  pointed at a doctored snapshot, so that it opens a test issue; that issue is then closed by hand.

## Not in scope

- Auto-fixing the clients.
- Refreshing the fixtures in CI.
- Checking any API surface the clients do not use.
