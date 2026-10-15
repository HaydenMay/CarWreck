# Pickup Checkpoint C focused extension

User authority: focused extension approved 2026-10-15; Checkpoint B shape approved. Existing feature/vehicle-compact-pickup only; never merge. Spec: Docs/VehicleProduction/vehicle-asset-spec.md, vehicle-qa-checklist.md, addacar-workflow.md and latest user priorities.

## Global constraints
Preserve A/B artifacts and approved proportions/material layout. No weakened thresholds, warning suppression, rigid classification workaround, cabin compromise or open-bed collider redesign. Runtime changes require demonstrated generic defects; stop before a substantial architectural change. Keep new evidence in StageC/Revision2 and raw diagnostic logs untracked. Publish pending results on the existing feature branch, never main.

## Task 1: Reproduce and distinguish failures
Add editor-only focused probes: real standard front impact, per-vertex explanations for stationary indicator vertices and folded coil regions; real 16-target coverage baseline; independent phase timing. Record failing outputs before changes. Compare current physical sweep, solver contact, target face and measured cabin. Preserve original C evidence.
Interfaces: existing model diagnostics, CollisionPersistenceValidation, exact component names.
Completion: saved root-cause evidence or explicit unresolved hypotheses; no speculative runtime fix.

## Task 2: Correct contact validation contract
Use swept first contact for first-touch assertions, retaining independent expected geometry and existing 3 cm deeper / 6 cm follows / 5 cm regional retraction criteria. Identify intentional cabin protection from measured cabin geometry and actual swept footprint. Reject stale shells, displaced/expanded protection and wrong surfaces through negative fixtures. Keep post-step pose/contact informative. Run red/green checks and real 16 targets across vehicles.
Interfaces: Task 1 probe data; existing hitbox layout and geometry definitions. No runtime collider changes unless evidence requires them.

## Task 3: Correct visual deformation
Prefer targeted source topology preserving B surface, color and lamp depth. Establish regression fixture from stationary indicator/coil evidence. If a small generic runtime math defect is proven, fix minimally with failing/passing regression. Profile remaining field behavior; stop with proposed design if correction requires architecture changes. Rebuild source, QA, export and matched-angle reference/B/current comparisons for each accepted candidate.
Interfaces: Task 1 evidence; deterministic final generator, manifest, importer, core deformation APIs.
Completion: mandatory fold limits and visual separation resolved without hacks, or explicit architectural stop.

## Task 4: Full validation and review package
Run pickup and existing vehicle suites including repeated adaptive impacts, overflow, recorded replay, seed2026 50-hit Chaos, repair/switch; Blender QA, import, WebGL build and actual browser checks. Measure timings without major rewrite. Verify protected A/B hashes and baseline vehicle asset integrity. Add final renders, actual Unity damage comparisons and truthful report/limitations. Request one fresh-context whole-branch review, address findings, verify diff, commit/push the existing branch and hosted images. Stop for user review; no merge.
Interfaces: Tasks 2/3 accepted changes feed all suites and report, protected image manifests unchanged.
Completion: all mandatory gates supported by fresh evidence; otherwise publish a clear incomplete result and architecture proposal if required.
