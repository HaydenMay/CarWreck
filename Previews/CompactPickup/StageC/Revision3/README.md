# Compact pickup — Checkpoint C, Revision 3

**Status: Checkpoint C mandatory suites PASS on all three vehicles. Stopped for review. No merge.** This revision closes the two failures left open by [Revision 2](../Revision2/README.md): the front-suspension fold outlier (Problem A) and the 0.1 mm perturbation amplification (Problem B). Both fixes are generic shared-runtime changes. Neither relaxes a limit, and neither changes the approved appearance, proportions or materials.

Branch `feature/vehicle-compact-pickup-c`, built on Sol's `feature/vehicle-compact-pickup` @ 3ac4a8d. Unity 6000.6.0f1.

![Front suspension, standard front hit: sections off (left) vs on (right)](Unity/Suspension/suspension_front_zoom_off_vs_on.png)

## Results

|Item|Revision 2|Revision 3|Evidence|
|---|---|---|---|
|Front suspension, standard front hit|**FAIL** 484/8,832 = 5.48% (limit 5%)|**PASS** 39/8,832 = 0.44%|[Integration](Validation/Vehicle2/integration.txt), [prototype](Investigation/tube_section_prototype.txt), [visual probe](Unity/Suspension/suspension_front_visual_probe.txt)|
|0.1 mm perturbation, pickup 8-impact counterexample|**FAIL** 26.582 mm (limit 20 mm)|**PASS** 2.906 mm|[Draft probe](Grazing/grazing_draft_probe.txt)|
|0.1 mm perturbation, seed-2026 Chaos, final suite|pickup 21.7 / hatch 25.6 mm **FAIL**|muscle 3.9 / hatch 2.5 / pickup 2.7 mm **PASS**|[Muscle](Validation/Vehicle0/chaos.txt), [hatch](Validation/Vehicle1/chaos.txt), [pickup](Validation/Vehicle2/chaos.txt)|
|0.1 mm perturbation, 15 recorded launches × 3 vehicles|not measured|**45/45 PASS**; worst muscle 3.917, hatch 10.381, pickup 3.248 mm|[Sweep replay](Investigation/PerturbationSweep/sweep_replay.txt)|
|Adaptive contact coverage|16/16 all vehicles|16/16 all vehicles|`Validation/Vehicle*/phase4.txt`|
|84 mixed hits, 33-hit overflow, exact replay, repair/reset|PASS|PASS on all three vehicles|`Validation/Vehicle*/`|
|Contact-contract fixtures|12/12|12/12|[Fixtures](Validation/contact_contract_fixtures.txt)|
|Vehicle switching|PASS|PASS|[Switching](Validation/switching.txt)|
|Manifest import check and round trip|PASS|PASS|[Import](Validation/manifest_import.txt)|
|WebGL build|21.2 MB, 0 errors|21.2 MB, 0 errors|[Build log](Validation/webgl_build.txt); browser not retested|

Chaos sanity (seed 2026, 50 impacts): folded fraction 3.8% muscle, 6.0% hatch, 4.2% pickup; zero NaN; replay difference 0.0 m.

## Problem A: coherent tube sections

**Diagnosis.** Revision 2 showed the 20 mm coil and strut wire rings going concave under the vertex-local field: neighbouring vertices around one small ring received different crush, so the ring folded through itself. Thicker wire, lower crumple or rigid reclassification would have hidden this, and all three were ruled out.

**Fix.** A new rule flag, `coherentTubeSections`, defaults to off. When it is on, `TubeSections.Build` finds the rings of each tube at load. At impact time, each ring moves as a section: its members share the minimum capacity, the ring sum is replaced by the mean, and slider crush is averaged per ring. A tube can still bend and crush along its length, but its cross-section stays a section. Only the pickup's suspension parts enable it, so existing vehicles are unchanged unless an author opts in.

**Prototype first.** [`tube_section_prototype.txt`](Investigation/tube_section_prototype.txt) compares the per-vertex and section-coherent fields without changing runtime state:

- front hit: 484 → 39 folds;
- front-left corner: 350 → 13;
- `Suspension_Rear` on a rear-corner hit: 134 → 105.

**Visual check.** Real Unity renders of the isolated front suspension, sections off then on (same hit): 5.48% folded, 28 mm max displacement → 0.44%, 27 mm. Crush depth is preserved; the folding is not. [Front](Unity/Suspension/suspension_front_off_vs_on_front.png), [oblique](Unity/Suspension/suspension_front_off_vs_on_oblique.png), [zoom](Unity/Suspension/suspension_front_zoom_off_vs_on.png). Memory cost is about 44 KB of lookup tables on the pickup.

## Problem B: grazing-contact draft

**Diagnosis.** The footprint is a box's leading surface. When a box face is nearly parallel to the car surface, the face that leads flips with a 0.1 mm shift, so a vertex's crush jumped by up to 27.8 mm. On the counterexample this produced 26.582 mm at impact 8 on `BedRail_L`.

**Fix.** The leading surface is now the upper envelope of 2° contact-draft cones raised from the box's 12 projected edges (`BoxProjection.GrazingDraftDegrees`). This is like a casting draft. A face within 2° of grazing blends continuously into its neighbour rather than switching abruptly. The rules the brief imposed are all met:

- the box keeps its true orientation, width and height;
- no orientation-free stamp is used;
- outside the box's silhouette there is no contact, so missed surfaces stay untouched;
- cabin protection and capacity limits are unchanged;
- noise is still active, and no final difference is clamped.

A second fault appeared in the sweep: muscle impact 8 failed 8 of 15 runs at 48.3 mm. A vertex just outside the silhouette read the drafted surface 2 mm inside it, a jump of 2 mm / tan 2° ≈ 57 mm. `OutsideLead` now reads at the nearest silhouette point itself. [Before](Investigation/MuscleImpact8/grazing_draft_probe_before_fix.txt), [after](Investigation/MuscleImpact8/grazing_draft_probe.txt).

**Draft angle.** The [draft probe](Grazing/grazing_draft_probe.txt) measures 1°, 2° and 3° on the failing fixture and both 50-impact replays. 2° is the smallest setting that clears the pickup counterexample with margin:

|Fixture|Exact surface|2° draft|
|---|---:|---:|
|Pickup 8-impact counterexample|**26.582 mm FAIL**|2.906 mm|
|Pickup 50 impacts|26.582 mm|3.598 mm|
|Hatch 50 impacts|7.776 mm|2.916 mm|

The draft changes 17/50 pickup and 16/50 hatch impacts. The [orientation probe](Grazing/grazing_orientation_probe.txt) and [tilt sweep](Grazing/grazing_tilt_sweep.txt) check that a box turned exactly parallel and boxes at the recorded tilts both move continuously.

**Published fixtures.** Failing: exact surface (`GrazingDraftDegrees = 0`) on the first 8 recorded pickup impacts. Passing: the same fixture at the default 2°. Inputs: [pickup](Grazing/vehicle2_seed2026_recorded.json), [hatch](Grazing/vehicle1_seed2026_recorded.json), [muscle impact 8](Investigation/MuscleImpact8/) and [15-run sweep](Investigation/PerturbationSweep/). All of them replay recorded impacts without PhysX, so they reproduce exactly. The design is written up in [the proposal doc](../../../../Docs/VehicleProduction/compact-pickup-suspension-deformation-proposal.md); commands are in [reproduce.md](Validation/reproduce.md).

## Performance

Warm-pass per-impact averages in the Editor (n = 12). Full table and method: [performance.md](Validation/performance.md).

|Vehicle|3ac4a8d baseline|Rev 3, no culling|Rev 3 final|
|---|---:|---:|---:|
|Muscle|54.06 ms|100.69 ms|53.54 ms|
|Hatchback|82.71 ms|158.43 ms|86.74 ms|
|Pickup|119.20 ms|222.36 ms|127.10 ms|

The first draft evaluated all 12 edge cones for every vertex, which roughly doubled impact cost. Culling now skips edges that cannot raise a vertex's envelope. The check reports came out byte-identical, and the remaining overhead is −1% to +7%.

## Known limitations (documented, not fixed)

- **Shared runtime changed.** Tube sections are opt-in. The 2° grazing draft is not: it applies to every vehicle. That is the point of the fix, but existing cars' damage now differs on near-grazing impacts. Unshifted impacts change by up to 277 mm on the pickup (impact 3) and 161 mm on the hatchback, relative to the exact surface. These are differences in where a grazing box is deemed to touch, not instability, and all regression suites pass. Approving Revision 3 means approving this behaviour change for the muscle car and hatchback too.
- **Remaining overhead** of about 5–7% per impact on the hatchback and pickup, from the draft envelope near box edges. No optimisation was pursued past culling.
- **Exact-surface read.** With the draft set to 0, the exact surface still uses Sol's original 2 mm inward nudge. This only affects the diagnostic failing fixture, not runtime.
- **Hatch sweep worst** is 10.381 mm across 15 launches. That is within the 20 mm limit but well above the single-run 2.5 mm.
- **WebGL** was rebuilt but not retested in a browser for this revision. Revision 2's browser evidence predates these changes.
- **Impact pause (high priority, future).** After approval, one impact in manual testing took about 157.7 ms of deformation processing. The root cause is not established. The concern is recorded in [the DamageLab README](../../../../Assets/DamageLab/README.md#limitations--next-steps) for the playable driving milestone.
- **URP warning.** The URP FSR warning in batch logs is pre-existing and unrelated.
- **Unchanged since Revision 2:** cargo resting on BodyCoreBox above the liner, socket seeds not runtime-ready, lamp colour and headlight microfluting differences.

## Review gate

Every mandatory Checkpoint C suite passes on all three vehicles, and the visual suspension check is in Unity. The pickup is ready for your review. A merge needs your explicit approval, because the draft changes shared behaviour for the existing cars. Nothing has been merged into `main` or `feature/vehicle-compact-pickup`.
