# Compact pickup — Checkpoint C focused extension

**Status: NOT READY — architecture review required. No merge.** This revision resolves the adaptive-contact oracle failure and the standard front-indicator outlier, but mandatory suspension and perturbation failures remain. The user required stopping before a substantial shared-engine change. This is an evidence handoff at that boundary, not a passing Checkpoint C submission.

Work remains on `feature/vehicle-compact-pickup`, extending `3a755caa85affaf75e6f8d82dbbc31be672d68c4`. The shared runtime collision/deformation code, cabin protection, crumple values, acceptance limits and existing-car assets are unchanged by this extension.

![Reference comparisons](reference_overview.png)

![Actual Unity indicator comparison](front_15ms_no_overlay_before_after.png)

## Changes and remaining failures

|Item|Current result|Evidence|
|---|---|---|
|Adaptive contact coverage|**16/16 PASS on all three vehicles**; original pickup 14/16 was an oracle error|[Pickup](Validation/Vehicle2/phase4.txt), [hatch](Validation/Vehicle1/phase4.txt), [muscle](Validation/Vehicle0/phase4.txt)|
|Cabin protection and contact fixtures|12/12 positive/negative contracts PASS; genuine CabinBox contact retained|[Fixtures](Validation/contact_contract_fixtures.txt)|
|Front indicators, standard 15 m/s hit|L 50/1,324 = 3.78%, R 2/1,324 = 0.15%; below unchanged 5% part limit; former stationary outer fin removed|[Actual Unity comparison](front_15ms_no_overlay_before_after.png), [per-part data](Unity/supplemental_validation.json)|
|Front suspension, standard hit|**FAIL: 484/8,832 = 5.48%**, unchanged limit 5%|[Integration](Validation/Vehicle2/integration.txt), [candidate experiments](Investigation/topology_candidate_results.csv)|
|0.1 mm input perturbations, full seed-2026 suite|**FAIL: pickup 21.7 mm; hatch 25.6 mm**; limit 20 mm. Muscle passes|[Pickup](Validation/Vehicle2/chaos.txt), [hatch](Validation/Vehicle1/chaos.txt), [muscle](Validation/Vehicle0/chaos.txt)|
|Durable pickup counterexample|**FAIL: 26.582 mm at impact 8, BedRail_L**, reproduced from eight saved impacts without running PhysX|[Recorded 50 impacts](Investigation/ChaosReplay/vehicle2_seed2026_recorded.json), [probe](Investigation/ChaosReplay/saved_eight_impact_probe.txt)|

### Contact diagnosis

The old oracle compared a post-step averaged solver contact with a swept first-touch face. Those measurements have different integration phases. The second side shot can also legitimately touch the protected cabin before the retracted lower body, since the block slightly overlaps the cabin vertically. The corrected oracle transforms all eight actual cabin collider corners into vehicle space and rejects off-axis or degenerate cabin transforms. It measures the nearest swept collider/face and checks it against an independently reconstructed rest cabin or the expected adaptive face. It reports the swept face, parked pose, post-step contact and actual collider separately.

The 3 cm deeper-contact, 6 cm face-distance and 5 cm regional-retraction limits are retained. Cabin min/max must also match independent rest geometry within 1 mm; moving or expanding the cabin fails even if within the face tolerance. A rotated block's dimensions are projected into vehicle space. Negative fixtures reject a stale outer shell, moved/expanded/scaled/rotated cabin, wrong surface, unapplied hit and insufficient depth/retraction. Corner shots retain the existing two-face coverage contract, with no new analytical corner-face oracle claimed. See [original 14/16 evidence](Investigation/coverage.txt). After the transformed-cabin correction, fresh full adaptive/stress reruns also pass for [muscle](Validation/ContactGeometryFinal/Vehicle0/coverage.txt), [hatch](Validation/ContactGeometryFinal/Vehicle1/coverage.txt) and [pickup](Validation/ContactGeometryFinal/Vehicle2/coverage.txt), each 16/16 coverage with process exit 0.

### Indicator fix and visual preservation

Both tall indicators move 22 mm inward, keeping the same dimensions, radius, material and topology. A slightly yawed frontal sled previously missed the outermost strip of the right lamp's leading surface; 483 vertices stayed put while neighbouring geometry crushed. The inset puts that strip within the contact surface and removes the separate fin. The asset remains deformable with its Light role and crumple 0.45. The new front hit still has small local folds; the claim is that the former separation/outlier is fixed for this required case, not that every possible hit has zero folds.

Wheelbase, wheels, ride height, body dimensions, cabin/bed relationship, roof material, two-color layout and all measured B proportion ratios remain unchanged. Exposed indicator surface change versus approved B is at most 23.63 mm, including the earlier C corner-radius correction. The generator's B surface-drift disclosure allowance changes only for these lamps; it is not a destruction acceptance threshold. Full caps include hidden-surface changes. [Measured proportions](proportions.md) and [approved B comparison](approved_b_overview.png) make the deviation reviewable.

Direct top, hood/fenders/grille, wheel/clearance and bed views were compared with all three reference sheets. The nose remains straight and integrated, hood stamping is retained, the cabin roof is forest green, and the bed remains open with ribs/tubs. Accepted reference track (+4.69%) and conflicting top-sheet bed estimate (-7.23%; side estimate within 0.06%) remain. Studio highlights can make the green roof pale; Unity's blue environment makes the paint appear navy. Neutral-light paint evidence confirms green. Lamp color and simplified headlight microfluting still differ from the supplied reference. No permanent accessories were added.

### Suspension and perturbation investigations

Nine coil-only topology variants (8/12/16 section sides, 160/240/320 stations) retain the original dimensions but still fold 7.08–9.75% of coil triangles. These coil-only ratios do not replace the 5.48% full-part failure. Measured 20 mm wire sections become concave and up to about 33 mm wide under the normal-dependent field. Their signed area remains positive; this is not a claim that the entire ring inverts. [Section data](Investigation/suspension_sections.csv), [section diagram](Investigation/suspension_section_diagnostic.png), [front-hit context](front_suspension_close_before_after.png).

The later durable pickup capture fails at 26.582 mm; a later hatch capture measures 6.647 mm, which does not fix or waive the original 25.6 mm failure. Exact replay of unperturbed recorded impacts is zero difference. The eight-impact pickup fixture produces the same failure with evaluation optimizations both enabled and disabled. At the worst vertex, moving an impactor 0.1 mm changes the box leading surface by about 27.8 mm near a grazing face, with additional fold-phase variation. That local high sensitivity is established; a fully validated robust replacement is not.

[The proposed bounded shared-engine work](../../../../Docs/VehicleProduction/compact-pickup-suspension-deformation-proposal.md) covers coherent tubular sections and a separate investigation/prototype for grazing-contact sensitivity. Neither is implemented. Do not infer that the tubular design will automatically fix BedRail_L.

## Mandatory rerun evidence

All three vehicles were tested with real scripted PhysX integration, six single-hit directions, repeated and cumulative impacts, sliders and click dents, adaptive coverage/local saturation, 84 mixed hits, 33-hit overflow, recorded replay, seeded 50-hit Chaos, stop, repair/reset and switching. Complete outputs are retained rather than replacing failures with passing reruns:

- [Muscle car integration](Validation/Vehicle0/integration.txt), [adaptive/84-hit](Validation/Vehicle0/phase4.txt), [overflow](Validation/Vehicle0/overflow.txt), [Chaos](Validation/Vehicle0/chaos.txt).
- [Hatchback integration](Validation/Vehicle1/integration.txt), [adaptive/84-hit](Validation/Vehicle1/phase4.txt), [overflow](Validation/Vehicle1/overflow.txt), [Chaos: sensitivity FAIL](Validation/Vehicle1/chaos.txt). Existing hatch part-fold outliers are reported by the unchanged suite; they are not newly waived here.
- [Pickup integration: suspension FAIL](Validation/Vehicle2/integration.txt), [adaptive/84-hit](Validation/Vehicle2/phase4.txt), [overflow](Validation/Vehicle2/overflow.txt), [Chaos: sensitivity FAIL](Validation/Vehicle2/chaos.txt), [switching](Validation/switching.txt).

Pickup adaptive stress applied 84/84 hits with 0 NaNs, 24 stored/60 merged, exact repair and original boxes. Overflow storage/replay/repair checks pass (33 hits, 24 stored/9 merged); its diagnostic perturbation investigation also finds a 26.120 mm difference and is retained. All three 50-shot runs launch/hit 50 with no misses, all eight approach octants, bounded storage and exact unperturbed replay. Their Chaos outcome is still FAIL where perturbation exceeds 20 mm. A new PhysX run is informational, not bit-identical: use the recorded-impact fixture for deterministic field comparisons. Cumulative damage still exhibits folds and layer drift; passing finite/storage checks does not certify cosmetic quality after arbitrary severe crushing.

[Blender QA](Validation/blender_qa.json): zero mandatory failures, two retained recommendations (384-triangle Cabin_RearDivider and 244-triangle TailgateHandle). No rigid reclassification or crumple reduction. [Manifest import](Validation/manifest_import.txt): dry and actual configuration PASS. 56 exact mesh classifications remain (52 deforming, four rigid wheels), with distinct bed floor/sides/rails/liner/tailgate roles and 20 optional sockets in seven groups. Socket metadata remains runtime-ready false; no mounting/attachment physics claim.

Two-color checks in actual Unity pass independently for primary/secondary, mask import and unchanged UVs through front, hood, bed-side, tailgate, liner and Chaos damage. [Default](Unity/paint_default.png), [primary only](Unity/paint_primary_only.png), [secondary only](Unity/paint_secondary_only.png), [deformed](Unity/paint_deformed.png), [neutral light](Unity/paint_neutral_lighting_diagnostic.png). Forest green/cream boundary follows mesh UVs; other materials stay separate.

Open-bed cargo still rests on BodyCoreBox about **0.421 m above the liner**. [Actual cargo test](Unity/open_bed_collision_cargo.png) and numerical data document this solid-body approximation. Geometry was not filled or compromised to hide it; collider architecture is unchanged.

## Performance and WebGL

[Isolated Editor phase measurements](Validation/performance.md), [raw CSV](Validation/phase_profile.csv): pickup averages **116.61 ms** per impact (42.32 resolve, 60.49 accumulate, 13.80 rebuild), maximum 149.03 ms over 12 measured six-direction samples after warm-up. Accumulation is ~52%, footprint resolution ~36%; child-loop bottlenecks are source-supported hypotheses, not child-function profiler captures. Standard seeded Chaos measures 136.2 ms average/199.6 ms max, adaptive stress 105.3/197.0 ms; workloads differ. No optimization rewrite. Allocation counter is unavailable, not zero allocation. Player frame times and memory budgets are not measured.

[WebGL build](Validation/webgl_build.txt) succeeds: 21.2 MB, zero build errors, 14 seconds. The real browser player loads all three vehicles; pickup front impact, seeded 50/50 Chaos and repair are captured. Browser pickup parameters were 20 m/s and 2450 kg (Editor mandatory standard hit is 15 m/s and 1500 kg). Hatch records a front hit; the last repair capture does not conclusively verify completion, so browser hatch repair is unverified; exact Editor repair passes. Muscle load and repair UI were exercised, but its saved hit screenshot may be in flight and is not a numeric hit assertion. This is a functional browser smoke check, not complete cross-browser certification.

[Browser console](Unity/pickup_rev2_webgl_console.json) records the same URP FSR stripped-shader warning once per player load (two loads), and no application error entries. The warning says postprocessing passes will not execute; it is retained, not suppressed or counted as a clean zero-warning run. It appears on initial muscle-car load; no pickup-only shader change was made. [Pickup Chaos completion](Unity/pickup_rev2_webgl_chaos50.jpg), [repaired](Unity/pickup_rev2_webgl_repaired.jpg), [hatch front impact](Unity/hatch_rev2_webgl_hit.jpg).

## Independent review

A fresh reviewer checked the extension diff and evidence. The reviewer found and verified correction of a broken proposal link and a cabin validation gap that used raw collider size despite child scaling. The corrected physical test rejects a 1.02x cabin child scale even though its 18.8 mm sweep-face deviation fits within the unchanged 6 cm tolerance; 12 fixtures pass. The reviewer found no remaining important issue in that correction and judged this package publishable as an explicitly incomplete handoff, with no Checkpoint C approval or merge readiness.

## Preservation and review gate

[Preservation audit](Validation/preservation.json): all 71 protected A/B files match their approved SHA-256 values; 97 shared-runtime/existing-vehicle baseline files match. Extension diff changes no shared runtime or existing-car asset. This confirms content preservation; the hatch perturbation failure prevents claiming an entirely green regression suite. First-C pending images are retained; new images are in Revision2. No approved images were removed.

No merge, collider redesign, attachment integration, warning suppression or reduced acceptance limits. Approval is needed for the concrete shared deformation prototype described in the proposal before proceeding toward a passing Checkpoint C.
