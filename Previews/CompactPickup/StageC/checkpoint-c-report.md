# Compact Pickup — Checkpoint C validation report

**Status: mandatory suites PASS (Revision 3); awaiting review and approval.** Asset generation, export and Unity integration are implemented. [Revision 2](Revision2/README.md) resolved adaptive-contact coverage and the front-indicator outlier. [Revision 3](Revision3/README.md) resolved the front-suspension fold outlier and the 0.1 mm perturbation failure, through two generic shared-runtime changes. Results from the final Revision 3 suite run are below; older measurements in this report are labelled as such. No merge was performed.

Feature branches: `feature/vehicle-compact-pickup` (Sol, through 3ac4a8d) and `feature/vehicle-compact-pickup-c` (Revision 3). Approved B baseline: `156d2673008b63212f53db4b23784e5bc78c617e`. Unity 6000.6.0f1; Blender 5.2.0 LTS. [Gallery](README.md), [result summary](Validation/validation_summary.json) (Revision 1 run), [image/source provenance](review_manifest.json).

## Vehicle and preserved design

Late-1980s/early-1990s two-door regular-cab compact 4×4 pickup, open standard bed, forest-green upper body and cream lower panels. Bumpers, grille, steel wheels, tires, glazing and liner remain separate materials. There are no permanent accessories.

| Measurement | Final |
|---|---:|
| Overall length / height | 4.722 / 1.821 m |
| Body width / mirror width | 1.940 / 2.255 m |
| Wheelbase / tire diameter | 2.950 / 0.842 m |
| Bed-side length / clear bed depth | 1.945 / 0.390 m |
| Roof footprint | 1.644 × 0.720 m |
| Front tire-to-arch gap / rocker height | 0.144 / 0.505 m |
| Mass | 1350 kg — game-design assumption, not a measured reference specification |
| Meshes | 56 exact-name rules; 52 deformable, four rigid wheels |
| Blender vertices | 120,506 total; 86,610 deformable |
| Unity imported deformable vertices | 114,694, including UV/hard-edge splits |
| Unity triangles | 228,330 |

All 14 B render cameras retain their positions, targets, projection and framing. All three reference sheets are used in matched comparisons. Geometry-derived proportion ratios retain the approved B values within floating-point precision; no drift exceeds 2%. Perspective reference matching is approximate because source lens parameters are unknown.

The accepted track estimate (+4.69% versus Photo 2) and secondary plan-bed estimate (−7.23%) are retained. Bed length still follows Photo 3's side estimate (−0.06%). No attempt was made to reconcile conflicting reference estimates.

| Feature | Final self-review |
|---|---|
| Cabin/bed relationship | Short regular cab and longer open cargo area remain visibly separate in side, rear and plan views. |
| Hood/fenders/grille | Stamped shoulders and paired hood ridges retained; monotonic plan taper has no hourglass waist or bulging grille. Recessed horizontal grille remains integrated into the nose. |
| Stance/wheels/suspension | Approved wheel roots, 2.95 m wheelbase, raised clearance, arches, all-terrain tread and steel-wheel geometry retained. |
| Cabin/roof | Green roof, windshield, pillars, glass, seals and roof edges retain B design. Broad pale reflections are lighting; the mask and pigment diagnostic select Color A. |
| Two-tone metal | Cream below authored Z=0.805 m continues across doors/bed sides. Body creases are built into the panels, without floating molding ribbons. |
| Bed | Ribbed liner, wheel tubs, substantial rails, tailgate and inward-facing walls remain open. Physics does not accurately represent that cavity. |

Dark opaque glass and the absence of a detailed interior remain B's stylization. Tread, lenses, grille bracing and small trim simplify the photographic targets. Fuel-filler detail, tie-downs and exposed tailgate hinge hardware remain omitted. Studio lighting differs from the reference lighting. The matched C fascia render also has cloudier, more horizontal lens banding than B's predominantly vertical fluting; retained front coordinates/depth do not establish identical optical appearance. Front signal emission also renders redder than the amber reference in the current Unity views.

## Topology corrections and rejected experiments

49 of 56 mesh signatures retain B geometry, UVs, material slots, transforms and roles exactly. The seven bounded corrections are recorded in [approved_b_preservation.json](approved_b_preservation.json): evenly spaced hood stations on the same analytic contour; concealed wiper backs, lower rear-rail cap and rear lens caps removed; front-indicator corner radius broadened. The hood surface differs by at most 1.25 mm under the recorded B-to-C nearest-surface sample measure. This is a one-direction sample metric, not a complete two-way surface proof. Exposed lens corrections remain below 5 mm; visible lens depth and reflector geometry are retained.

The hood correction reduced click-dent reversed triangles from 120 to **0**, and standard front-hit hood reversals from 242 to **24**, without changing the approved hood shape. Wiper and rear-rail hidden skins were also investigated in actual Unity damage.

A [planar-lens experiment](Experiments/planar_lenses/README.md) removed lamp reversals but still showed the red indicator wing and reduced approved visible depth. It was rejected. Final lenses restore the fuller shape; the mandatory indicator fold failure and visible defect are honestly retained. No role, crumple resistance, rigid classification or QA threshold was changed to obtain a pass.

A [0.60 m front-shell experiment](Validation/collision-investigation.md) also failed overall coverage and was rejected. Final shell/cabin values remain automatic.

## Blender acceptance QA and configuration

[Unmodified vehicle_asset_qa.py results](asset_qa.json): **PASS, zero mandatory failures**. Body p90 edges are at most 0.050 m, below 0.060 m; no body n-gons, zero-area faces, collapsed UV faces or nonfinite vertices are reported. Metre units, orientation, applied scale, hierarchy, wheel roots and both UV layers are checked.

Two density recommendations remain: Cabin_RearDivider, 31 cm/384 triangles; TailgateHandle, 16 cm/244 triangles. Both retain their standard deformable roles and resistance. Supplemental single front/hood/bed-side/tailgate/liner cases are recorded per part; extreme Chaos can fold small trim, including the handle. These are disclosed recommendations, not rigid exceptions.

Manifest: `Assets/Vehicles/CompactPickup/vehicle.json`, revision 2. Every mesh matches exactly one Exact rule, with no generic fallback or ambiguity. Bed sides/front wall/tailgate are Body; bed floor and rails Panel; liner/tubs Trim. Cabin, Wheel, Bumper, Fascia, Light and Underbody use the established role table.

FBX uses the required mesh/empty-only axes, baked transforms, unit scale and applied modifiers. Dry-run and applied `vehicle-import` both pass with zero warnings. Read/write, no compression/optimization, imported normals and baked axes are applied. The selector includes the pickup after muscle car and hatchback.

Measured Unity Body/Bumper envelope is 1.990 × 0.836 × 4.700 m; this differs from the whole visual vehicle because the rig measures specific roles. Cabin protection starts at beltline 1.341 m and ends at approximately 1.75 m, below the 1.820 m roof. Automatic impact height is 0.923 m; automatic nose/tail/side depths are 0.658/0.658/0.438 m. Four wheel origins and the 3 mm computed ground lift are measured from this pickup, not copied from another vehicle.

## Unity paint and destruction

The material uses the existing UV1 two-color shader. Primary/secondary sRGB values are (0.08, 0.22, 0.145) and (0.78, 0.72, 0.60), stored correctly in linear color. Independent Color A and Color B edits pass; the other color is retained. The mask is linear, uncompressed and clamped. UV0/UV1 arrays remain unchanged during tested deformation. [Paint comparisons](unity_paint_comparison.png) include the boundary on dented metal. The Collision Lab's blue reflection cube gives the default editor paint a navy cast; [neutral diagnostic lighting](Unity/paint_neutral_lighting_diagnostic.png) confirms the pigment. No source material colors were changed for that diagnostic.

| Suite (Revision 3 final run unless stated) | Actual result |
|---|---|
| `vehicle 2` | Functional checks pass: six standard directions, deeper repeated front hits, 12-heavy-hit saturation, sliders, click dents, UV preservation, reset and optimized replay ([integration](Revision3/Validation/Vehicle2/integration.txt)). Standard front hit folds 0.11% of vertices; worst part HeadlightHousing_L 25/816 (3.1%). |
| `collision-phase4 2` | **PASS: 16/16 coverage targets**, 84/84 mixed hits, locality, saturation, bounded records, zero NaN, exact repair/reset ([phase4](Revision3/Validation/Vehicle2/phase4.txt)). Muscle car and hatchback also 16/16 and 84/84. |
| `collision-overflow 2` | 33/33 heavy impacts applied with stored/merged records bounded; other regions still deform; replay/reinitialize within tolerance. Same on muscle car and hatchback. |
| `collision-random 2 2026 50` | Seed 2026, 50/50 applied, recorded replay exact (0 difference), **0.1 mm perturbation 2.7 mm** (limit 20 mm), folded 4.2%, STOP/repair/switch pass. Muscle car 3.9 mm, hatchback 2.5 mm. |
| Contact-contract fixtures | 12/12 PASS ([fixtures](Revision3/Validation/contact_contract_fixtures.txt)). |
| `vehicle-switch` | PASS ([switching](Revision3/Validation/switching.txt)). |
| Manifest import | Import check and round trip PASS, 0 warnings ([import](Revision3/Validation/manifest_import.txt)). |
| WebGL | Revision 3 build succeeds: 21.2 MB, zero build errors ([build](Revision3/Validation/webgl_build.txt)). Not retested in a browser after Revision 3. The Revision 2 browser run loaded the pickup, deformed it on manual and Random Launch, completed 50/50 seeded Chaos and repaired it, with zero console errors ([browser evidence](Validation/webgl.md)). |

Full numbers and [measured editor performance](Validation/performance.md) are in the extracted suite reports and [validation_summary.json](Validation/validation_summary.json). Costs are synchronous per-impact measurements on a Ryzen 7 5700/RTX 4060, with final tests run after Blender rendering finished. They can cause impact-time hitches; no FPS, memory-budget or player-performance guarantee is made. Physics re-runs are informational: PhysX is not bit-repeatable. Recorded-impact mesh replay has its own exactness checks and passes; this does not establish deterministic launch re-simulation.

**Fold and visual criteria (Revision 3).** Revision 2 brought the front indicator below 5% (it is no longer among the top folded parts on the standard front hit). Revision 3 brought Suspension_Front from 484/8832 (5.48%) to 39/8832 (0.44%) using opt-in coherent tube sections on the pickup's suspension parts. The QA checklist's "no >5% single-hit outlier" criterion now passes for the pickup. Cumulative and extreme runs still produce some folds and layer drift, as on the existing vehicles; see actual front, bed and top screenshots. The package does not claim spike-free, floating-geometry-free production acceptance. Lamp appearance (colour, headlight microfluting) is left to reviewer judgement as in Revision 2.

## Collision and sockets

The actual cargo test drops a 0.20 m cube of mass 5 kg into the bed. It rests on BodyCoreBox around Y=1.343 m, while the visible central liner is around Y=0.923 m: approximately **0.421 m above the liner**. A raycast confirms the solid core contact. The fourteen-box rig also fills some visual cab/bed gap and lifted-underbody clearance. The bed remains visually open; no geometry was changed to hide this limitation. Separate meshes do not provide chassis articulation.

All **20 socket seeds in seven groups** retain `runtime_ready:false`: compact roof mount, hood scoop, front push bar, rear utility, left/right bed rails and bed floor. All Blender support/normal checks pass. The small roof uses a conservative 1.30 × 0.50 m footprint; a full-size rack is omitted. Metadata proves neither accessory fit nor attachment physics, strength, rebinding or Unity mounting behavior. None of those systems was implemented.

## Regression and approval boundary

Revision 3 **changes shared runtime code**. The coherent-tube-section flag is opt-in and only the pickup's suspension uses it. The 2° grazing-contact draft in `BoxProjection`/`ImpactFootprint` applies to every vehicle: near-grazing impacts on the muscle car and hatchback now deform differently from before (up to 277 mm on an unshifted pickup impact and 161 mm on the hatchback, relative to the exact surface). All muscle car and hatchback regression suites pass after the change. Editor per-impact cost is −1% (muscle), +4.9% (hatchback) and +6.6% (pickup) against 3ac4a8d ([performance](Revision3/Validation/performance.md)). Existing vehicle source assets, definitions and profiles are unchanged. Exact old/new fold-count equality is not claimed for separate PhysX runs. Unrelated serialization noise and old-vehicle screenshot overwrites are restored before commit.

An initial WebGL session showed a blank 3D viewport for muscle car and pickup. The final rebuild/reload rendered both correctly and the pickup completed its browser tests; the original blank view's cause was not established. Existing-vehicle regression confidence comes from unchanged source assets, the Revision 3 editor regression suites and the Revision 2 muscle-car browser load, not exhaustive every-platform testing.

This package stops for review. All mandatory Checkpoint C suites pass; the shared-runtime behaviour change above needs the user's explicit approval before any merge. No main merge is authorized. Approved A/B evidence is preserved; Revision 2 and earlier evidence are retained for comparison.
