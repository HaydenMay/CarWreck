# Compact Pickup Checkpoint C Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate the approved pickup and publish measured Checkpoint C evidence without merging.

**Architecture:** Keep all shared destruction/runtime code and thresholds unchanged. Generate a final vehicle source, FBX, palette, paint mask and exact component manifest from the approved B authoring model. Use the existing Unity batch bridge and its real PhysX validation suites, with separate pickup-only diagnostics if an uncovered criterion needs observation.

**Tech Stack:** Blender 5.2 Python, Unity 6000.6.0f1 URP, existing DamageLab bridge, Pillow comparison boards, Git LFS.

**Spec:** `Docs/VehicleProduction/addacar-workflow.md`, `vehicle-asset-spec.md`, `vehicle-qa-checklist.md`, approved B at `156d2673008b63212f53db4b23784e5bc78c617e`, and the user's Checkpoint C instructions.

## Global Constraints

- Continue `feature/vehicle-compact-pickup`; no merge before explicit approval.
- Preserve approved A/B images and source; final changes use separate paths.
- No shared engine changes, threshold weakening, or rigid classification of deformable components to pass QA.
- Cabin line and shell dimensions automatic; shared damage tuning unchanged.
- Sockets remain metadata only, `runtime_ready: false`.
- Published report separates Blender QA, Unity tests and actual visual evidence.

## Review Focus

- Export axis/scale or UV split errors: compare imported bounds/wheel roots and runtime UV arrays.
- Folded hood/arches/liner or floating fascia: inspect per-part fold counts and actual damaged close-ups.
- Two-tone paint slipping: inspect independent color edits and the deformed UV1 boundary in Unity.
- Solid open-bed collider: measure actual physics contacts against the liner; report rather than hide it.
- Existing-vehicle regressions: capture equivalent baseline/post-integration tests and preserve existing asset/code hashes.

### Task 1: Baseline, approved B protection and export

**Files:** `Previews/CompactPickup/approved_checkpoint_b.json`, `Blender/Scripts/build_compact_pickup_final.py`, `Blender/CompactPickup.blend`, `Assets/Vehicles/CompactPickup/{vehicle.json,palette.json,Models/CompactPickup.fbx,Textures/LowerBodyMask.png}`, `Previews/CompactPickup/StageC/`.
**Interface:** Approved B meshes/roles/socket seeds → final exact-name manifest, expected bounds and wheel positions.

- [x] Record baseline revision, dirty files, code/existing-asset hashes and existing-vehicle Unity results before pickup import.
- [x] Record B approval and protect its evidence; update only approval status text.
- [x] Generate final source/export/configuration with 1350 kg design-assumption mass and the approved paint mask/palette. All four wheels rigid; other roles retain standard crumple.
- [x] Run unchanged manifest-based `vehicle_asset_qa.py`; investigate mandatory failures and recommendations without weakening checks.

### Task 2: Unity integration and destruction validation

**Files:** New pickup materials/definition/profile and generated Collision Lab scene; raw logs and screenshots under StageC.
**Interface:** FBX/manifest → `vehicle-import ... dry`, `vehicle-import ...`, `collision-build` → selector index and live test reports.

- [x] Dry-run/import with zero unclassified, ambiguous or validation warnings; confirm automatic cabin/shell dimensions and default damage tuning.
- [x] Run `vehicle <i>`, `collision-phase4 <i>`, `collision-overflow <i>`, `collision-random <i> 2026 50`, `vehicle-switch`.
- [ ] Inspect exact replay/reset/overflow/locality/saturation/perturbation results and per-part fold outliers; fix justified mesh issues under separate final source, then repeat affected validations.
- [x] Capture independent paint changes, deformed boundary, hood/fenders/grille/bed/tailgate/liner damage and actual open-bed physics behavior. Preserve socket metadata without attachment integration.
- [x] Measure test performance, run equivalent existing-vehicle checks, and build/browser-test WebGL when available. Restore only tool-generated unrelated serialization noise.

### Task 3: Final visual review and delivery

**Files:** Final generator/measurement/comparison helpers, StageC gallery, reference/B/final proportion tables, QA/Unity/performance/regression report.
**Interface:** Validated final source and real Unity evidence → reviewable hosted Checkpoint C package.

- [x] Render matched final views and compare against all references and approved B; call out any >2% drift.
- [x] Inspect damage screenshots and every remaining defect; report passed/failed/not-run criteria explicitly.
- [x] Verify A/B protected hashes, asset/code regression hashes, gallery links, image content and relevant diffs; obtain independent whole-change review.
- [ ] Commit/push only relevant files, verify hosted images and stop for Checkpoint C review.

## Execution ledger

User explicitly authorized implementation through C; no additional plan gate is needed. Existing validation suites provide the tests for this asset/configuration task. New diagnostic checks will be observation tools, not replacement thresholds or runtime behavior changes.

- Baseline: initial Unity run exposed unhydrated existing LFS pointers; hydrating the tracked assets corrected the environment. Existing muscle/hatch integration and switching tests then ran; known hatchback fold outliers were recorded before pickup integration.
- Export/configuration: 56 exact-name role rules, 1350 kg design-assumption mass, automatic shell/cabin, all 20 metadata-only sockets. Blender QA has zero mandatory failures, two small-detail density recommendations retained for live review.
- Mesh investigation: Unity dumps localized hood reversals to submillimetre duplicate grid columns. Evenly spaced stations on the same contour reduce hood click-dent reversals from 120 to 0 (surface deviation max 1.25 mm). Hidden wiper/tailgate-cap and lens rear skins are removed from the final source only; approved B stays unchanged. No crumple/rigid/engine threshold changes.
- Final validation: all standard bridge suites ran after Blender finished. Import, functional integration, overflow, 50-shot Chaos and switching pass. Phase4 coverage remains 14/16, and front indicator/suspension single-hit folds fail the mandatory new-vehicle checklist. The final source retains automatic shell values and full-depth lenses; failed shell and planar-lens experiments were rejected.
- Browser: final 21.2 MB WebGL build succeeds; observed 20 m/s, 2500 kg, seed 2026 Chaos: 50 launched / 50 hit / 0 missed; repair restores appearance, application console 0 errors. An initial blank viewport resolved after rebuild/reload.
- Regression: 295 tracked baseline files checked, 294 unchanged and only the generated selector scene intentionally changed; 25 A + 46 B protected files all match. Existing cars and shared engine/QA/workflow assets unchanged. Unrelated tool serialization and old-vehicle screenshot overwrites restored.
- Acceptance: C is NOT ACCEPTED. Shared contact/validation investigation needs authorization before engine changes. The pending package includes actual failures, performance, paint/socket/cargo evidence and rejected experiments. Publication/remote verification is performed at handoff; no merge is authorized.
