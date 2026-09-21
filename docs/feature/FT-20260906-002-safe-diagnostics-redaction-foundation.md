---
id: FT-20260906-002
kind: feature
status: reviewed
area: infrastructure-diagnostics
stage: phase-1
tasks:
  - path-redaction-defense-in-depth
  - structured-safe-diagnostic-events
  - exception-normalization
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - src/FluoAxis.Infrastructure/
  - tests/FluoAxis.Infrastructure.Tests/
  - README.md
  - .github/workflows/ci.yml
  - docs/feature/FT-20260903-002-plan-completion-audit.md
  - docs/change_index.jsonl
contracts:
  - no-machine-paths-in-logs-or-diagnostics
  - no-raw-exception-in-persisted-diagnostics
  - no-raw-experiment-data-in-default-diagnostics
hotspots:
  - path-redaction-bypass
  - exception-message-leakage
  - structured-field-allowlist
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - redact drive UNC file URI and machine-rooted POSIX path forms
    - preserve HTTPS links safe display names logical IDs and project-relative paths
    - normalize exceptions without retaining message stack source or target site
    - reject unsafe operation error-code and relative-path fields
    - run focused Infrastructure and full repository gates
  tests_run:
    - Infrastructure focused suite (28 passed, 0 failed, 0 skipped)
    - repository MTP gate (596 passed, 0 failed, 0 skipped)
    - solution build (0 warnings, 0 errors)
    - repository path policy from repository cwd (245 text files passed)
    - repository path policy from foreign cwd (245 text files passed)
    - scientific source-boundary policy (10 first-party presentation files passed)
    - PlotHost JavaScript syntax and Node suite (7 passed, 0 failed)
  benchmarks_run:
    - not applicable; bounded validation and regex redaction are not yet connected to a persistent runtime sink
search:
  aliases:
    - safe diagnostics foundation
    - log path redaction
    - exception normalization
---

# Establish safe diagnostics and path redaction

## Summary

Add the first Infrastructure-owned diagnostics boundary: an allowlisted structured event that cannot carry raw experiment content or an original exception, plus a defense-in-depth text redactor for machine-local filesystem paths.

## Plan

1. Define a small immutable diagnostic event containing controlled identifiers only.
2. Normalize exceptions to bounded categories without copying messages, stack traces, source paths, target sites, or exception data.
3. Add path redaction for unavoidable third-party text before it can reach a diagnostic sink.
4. Prove both rejection and preservation behavior with runtime-constructed path fixtures.
5. Run focused and repository-wide gates and update completion evidence conservatively.

## Exploration log

- The checked-out Infrastructure project advertises logging and diagnostics ownership but currently contains only managed import adapters.
- The baseline forbids transient picker paths, raw exceptions, raw experiment content, and machine-local paths in logs, telemetry, audit, reports, crash metadata, and support bundles.
- Existing import adapters correctly normalize their own failures, but there is no central defense-in-depth redactor or safe diagnostic event contract.
- A free-form logging API would allow raw RFU rows or exception strings to cross the boundary, so the primary API must be structured and allowlisted. Text redaction is secondary defense for unavoidable third-party text, not permission to log arbitrary data.
- This slice does not select a logging provider, rotation policy, retention duration, support-bundle schema, or telemetry product.

## Root cause

Not applicable. This is a new Phase 1 infrastructure capability required by the logging/correlation baseline and later hardening rules.

## Implementation

- Added `SafeDiagnosticEvent`, an immutable allowlisted event with UTC timestamp, bounded event and operation tokens, non-empty correlation identity, optional Run identity, a strict logical-resource token, an already validated project-relative path, a bounded bare display name, and a closed failure category.
- Deliberately omitted free-form messages, raw data, original exceptions, stack traces, source/target metadata, and arbitrary property bags.
- Added exception classification into bounded categories without retaining exception content or recursively traversing inner exceptions.
- Added defense-in-depth redaction for recognizable drive-rooted, UNC, file-URI, and machine-rooted POSIX path forms while preserving HTTPS URLs and project-relative references. The redactor is not an authorization to accept arbitrary diagnostic text.
- Self-review found that the first unquoted-path patterns stopped at whitespace and could leave the suffix of a quoted machine path visible. A dedicated quoted-path pass now removes the complete quoted value, including paths containing spaces and the opposite quote character; the narrower unquoted pass remains for delimiter-free third-party text.
- Added rejection tests for invalid timestamps, identifiers, path-carrying fields, empty correlation IDs, and undefined failure categories. Path fixtures are constructed at runtime so forbidden machine-local path literals are not committed.

## Validation

- Focused Infrastructure tests pass 28/28 with no analyzer warnings.
- Full Microsoft.Testing.Platform discovery passes 596/596 across all 13 test assemblies, including the Windows Visualization assembly.
- `dotnet build FluoAxis.slnx --no-restore` completes with 0 warnings and 0 errors.
- Repository path policy passes from both repository and foreign working directories over 245 text files.
- Scientific source-boundary policy passes over 10 first-party presentation files. PlotHost syntax validation and all 7 Node tests pass.
- No performance claim is made because this foundation is not wired to a sink or a production hot path. Sink integration must establish throughput, bounded queue, memory, and failure behavior separately.

## Review outcomes

- Contract: the type surface is allowlisted and cannot carry an original exception or arbitrary property dictionary. Undefined enum values are rejected.
- Security/path: machine paths are excluded from structured identity fields; `RelativePath` remains the only filesystem-shaped value. Redaction is explicitly secondary defense, not a proof that arbitrary text is safe.
- Runtime: no provider, background worker, queue, persistence, rotation, retention, telemetry, crash capture, or support-bundle behavior was introduced. CPU and memory concurrency behavior is therefore unchanged.
- Compatibility: this is an additive Infrastructure API and does not change serialized frontend-backend, Method, Data, project archive, or persistence contracts.
- Scientific boundary: no Raw RFU, normalization, QC, threshold, classification, or reporting calculation changed.
- Manual review completed, including a quoted-path leakage regression discovered and corrected before closure. Two delegated review attempts returned stale or empty scope rather than current-file findings, so they are not counted as review evidence. GitHub review and external Codex review were not invoked in this environment.

## Follow-ups

- Select and qualify the persistent logging sink and rotation/retention policy as a separate dependency-backed change.
- Define default support-bundle contents separately; this event contract alone does not implement a support bundle.
