---
id: FT-20260903-007
kind: feature
status: validated
area: supply-chain-governance
stage: phase-1
tasks:
  - dependency-inventory
  - license-registry
  - sbom-generation
  - ci-policy-gate
priority: P0
branch: main
pr: null
paired_doc: null
paths:
  - DEPENDENCIES.md
  - build/
  - artifacts/compliance/
  - .github/workflows/ci.yml
  - tests/FluoAxis.Architecture.Tests/
contracts:
  - pinned-dependency-inventory
  - generated-sbom
  - release-evidence-skeleton
hotspots:
  - direct-versus-transitive-packages
  - license-provenance
  - deterministic-generation
  - ci-drift-detection
bug:
  repro: null
  root_cause_status: not_applicable
  root_cause_confidence: not_applicable
review:
  require_github_review: false
  require_codex_review: false
validation:
  tests_planned:
    - compare PackageReference and lock-file graph against dependency metadata
    - generate and schema-check dependency/license inventory and CycloneDX SBOM
    - prove deterministic output and cwd independence
    - run repository path, build, and MTP gates
  tests_run:
    - dependency evidence generated for 26 resolved components including 3 direct components
    - focused Architecture build passed with 0 warnings and 0 errors
    - focused Architecture tests passed 7 of 7
    - locked restore completed successfully
    - full solution build passed with 0 errors and 121 existing test-analyzer warnings
    - repository MTP gate passed 482 of 482 with 0 failed and 0 skipped
    - repository path-policy verification passed for 171 text files from repository and parent cwd
  benchmarks_run: []
search:
  aliases:
    - dependency governance
    - license inventory
    - cyclonedx sbom
    - supply chain gate
---

# Establish dependency, license, and SBOM evidence baseline

## Summary

Create a repository-native, deterministic Phase 1 dependency governance gate that distinguishes current direct dependencies from planned packages, inventories the resolved lock-file graph, records license evidence and unresolved provenance explicitly, and emits release-evidence skeletons without claiming release completeness.

## Plan

1. Reconcile project references, central pins, lock files, and the current prose registry.
2. Define a structured dependency metadata source with explicit provenance and planned/current state.
3. Generate deterministic dependency/license inventory and CycloneDX JSON under the repository artifact root.
4. Add fail-closed checks for missing pins, missing metadata, preview versions, graph drift, and non-deterministic output.
5. Integrate the gate into CI, validate from a foreign working directory, and update the completion audit conservatively.

## Exploration log

- `plan.md` requires a dependency/license/SBOM skeleton in Phase 1 and fully verifiable release artifacts only at G8. This change must not conflate those levels.
- Central versions and lock files already exist, but `DEPENDENCIES.md` mixes currently referenced packages with planned dependencies and does not provide structured license provenance, security source, or replacement plan.
- The user requested primary-agent-only execution. Contract, runtime, data/path, tests, docs, and review concerns are decomposed manually.

## Root cause

Not applicable. This is a bounded governance capability.

## Implementation

- Reconciled every current `PackageReference` with centralized exact pins and all repository lock files. The resolved graph contains 26 NuGet components, of which 3 are direct dependencies.
- Added reviewed metadata for every resolved component: SPDX license expression, HTTPS upstream source, owner, security source, upgrade policy, and replacement plan. Metadata was checked against the NuGet catalog for the exact locked versions.
- Corrected the previous `Microsoft.Data.Sqlite` license entry from Apache-2.0 to MIT; its SQLitePCLRaw transitive native components remain Apache-2.0.
- Split `DEPENDENCIES.md` into current direct dependencies and planned-but-unreferenced candidate pins, preventing planned WinUI/WebView2 packages from appearing in the current SBOM.
- Added a repository-native PowerShell generator that consumes central pins, project references, lock graphs, and reviewed metadata without adding a generator package. It emits deterministic `dependencies-and-licenses.json` and CycloneDX 1.5 JSON under `artifacts/compliance/`.
- The generator fails closed on floating/prerelease pins, inline project versions, missing pins, conflicting hashes, unresolved dependency edges, missing or stale metadata, duplicate resolved versions requiring framework-aware handling, non-HTTPS sources, and output outside `artifacts/`.
- Added a CycloneDX application root connected to the three direct packages, with transitive edges derived from lock files.
- Added CI generation after locked restore and before build, plus architecture tests for deterministic bytes, schema shape, current component counts, foreign-cwd execution, and missing-metadata refusal.
- Raised the fail-closed repository discovery floor from 480 to 482 tests.

## Validation

- `build/Generate-DependencyEvidence.ps1`: generated 26 inventory components, 26 CycloneDX components, 27 dependency nodes including the application root, and 3 root dependency edges.
- Two isolated generations from a parent working directory produced byte-identical inventory and SBOM files.
- The negative metadata test removed the direct SQLite package and proved generation fails closed rather than producing an incomplete inventory.
- `dotnet build tests/FluoAxis.Architecture.Tests/FluoAxis.Architecture.Tests.csproj --no-restore`: passed, 0 warnings, 0 errors.
- Focused Architecture run: 7 passed, 0 failed, 0 skipped.
- `dotnet restore FluoAxis.slnx --locked-mode`: passed after NuGet network access was permitted.
- `dotnet build FluoAxis.slnx --no-restore`: passed with 0 errors. A full rebuild exposed 121 existing test-analyzer warnings; no repository-wide zero-warning claim is made.
- Repository MTP gate with minimum 482: 482 passed, 0 failed, 0 skipped.
- `build/Verify-RepositoryPolicy.ps1`: passed for 171 text files from repository-root and parent-working-directory invocation.

## Review outcomes

- Contract: generated inventory describes the actual lock graph only. Candidate future packages remain explicitly non-current and cannot silently enter the SBOM.
- Runtime: generation is offline after locked restore, performs bounded scans of project/lock metadata, and adds no application runtime dependency.
- Data/path: committed metadata uses repository-relative paths and HTTPS URLs only; generated evidence is constrained beneath `artifacts/`, and tests use unique contained roots.
- Security: exact NuGet content hashes are converted from lock-file SHA-512 values into CycloneDX hashes. The GitHub Advisory Database is recorded as the security source, but this slice does not claim that a live vulnerability review has run.
- Compatibility: no production API, data schema, Method, or project archive contract changed. Duplicate resolved versions intentionally fail closed until the generator gains target-framework-aware graph modeling.
- Scope: this is a Phase 1 evidence skeleton, not a signed release attestation, legal opinion, vulnerability adjudication, or complete G8 evidence bundle.
- Manual primary-agent review covered graph integrity, license provenance, deterministic generation, path discipline, CI behavior, and scope claims. GitHub and external Codex review were not invoked.

## Follow-ups

- Release signing, vulnerability adjudication, and final SBOM attestation remain later release gates.
- Validate future Windows App SDK, WebView2, Plotly, PDF, and installer artifacts when their spikes introduce real dependencies.
- Add official CycloneDX schema validation and release signing only in the release evidence pipeline.
