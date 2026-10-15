# Compact Pickup — Checkpoint C validation report

**Status: NOT ACCEPTED; review and further authorization required.** Asset generation, export and Unity integration are implemented. Mandatory adaptive-contact coverage, part-fold criteria and damaged-lamp appearance are unresolved. Passing numerical and browser functional suites below do not override those failures. No merge was performed.

Feature branch: `feature/vehicle-compact-pickup`. Approved B baseline: `156d2673008b63212f53db4b23784e5bc78c617e`. Unity 6000.6.0f1; Blender 5.2.0 LTS. [Gallery](README.md), [result summary](Validation/validation_summary.json), [image/source provenance](review_manifest.json).

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

| Suite | Actual result |
|---|---|
| `vehicle 2` | Functional checks pass: six standard directions, deeper repeated front hits, 12-heavy-hit saturation, sliders, click dents, UV preservation, reset and optimized replay. Visual lamp acceptance fails separately. |
| `collision-phase4 2` | **FAIL: 14/16 coverage targets**. Front-side second hits contact the protected CabinBox rather than the retracted fender face. Locality, saturation, 84/84 mixed hits, bounded records, zero NaN and exact repair/reset pass. |
| `collision-overflow 2` | 33/33 heavy impacts applied; 24 stored +9 merged; other regions still deform; replay/reinitialize errors remain within the unchanged tolerance. |
| `collision-random 2 2026 50` | Seed 2026, 50/50 applied, zero missed/overlapping launches, all eight octants, recorded replay exact, bounded 0.1 mm perturbation, STOP/repair/switch checks pass. |
| `vehicle-switch` | Existing pair passes; supplemental pickup-to-muscle-to-pickup check has fresh damage, collider, report and UV state. |
| WebGL | Final build succeeds: 21.2 MB, zero build errors. Pickup/two-tone paint load, manual front and Random Launch visibly deform it; seeded Chaos has 50 launched/50 hit/0 missed and repair restores appearance. Zero application console errors or warnings were returned after the final run. Known damage appearance failures remain. [Browser evidence](Validation/webgl.md). |

Full numbers and [measured editor performance](Validation/performance.md) are in the extracted suite reports and [validation_summary.json](Validation/validation_summary.json). Costs are synchronous per-impact measurements on a Ryzen 7 5700/RTX 4060, with final tests run after Blender rendering finished. They can cause impact-time hitches; no FPS, memory-budget or player-performance guarantee is made. Physics re-runs are informational: PhysX is not bit-repeatable. Recorded-impact mesh replay has its own exactness checks and passes; this does not establish deterministic launch re-simulation.

**Mandatory fold and visual failures:** front indicators/lenses separate or distort during a standard front crush; the indicator can appear as a large wing. The final standard report lists FrontIndicator_L 124/1324 (9.37%) and Suspension_Front 476/8832 (5.39%). The QA checklist makes absence of >5% single-hit outliers mandatory for new vehicles, even though the asset specification labels that target SHOULD. Cumulative and extreme runs produce additional folds and layer drift; see actual front, bed and top screenshots. The package does not claim spike-free, floating-geometry-free production acceptance.

## Collision and sockets

The actual cargo test drops a 0.20 m cube of mass 5 kg into the bed. It rests on BodyCoreBox around Y=1.343 m, while the visible central liner is around Y=0.923 m: approximately **0.421 m above the liner**. A raycast confirms the solid core contact. The fourteen-box rig also fills some visual cab/bed gap and lifted-underbody clearance. The bed remains visually open; no geometry was changed to hide this limitation. Separate meshes do not provide chassis articulation.

All **20 socket seeds in seven groups** retain `runtime_ready:false`: compact roof mount, hood scoop, front push bar, rear utility, left/right bed rails and bed floor. All Blender support/normal checks pass. The small roof uses a conservative 1.30 × 0.50 m footprint; a full-size rack is omitted. Metadata proves neither accessory fit nor attachment physics, strength, rebinding or Unity mounting behavior. None of those systems was implemented.

## Regression and approval boundary

[Regression integrity](Validation/regression_integrity.json) records protected A/B and tracked baseline hashes. Existing vehicle source assets, definitions, profiles, shared runtime and existing QA scripts are unchanged. Muscle/hatch integration and switching were run before and after pickup integration. Functional checks pass; the hatchback's documented fold outliers were present in the baseline. Exact old/new fold-count equality is not claimed for separate PhysX runs. The generated Collision Lab scene is intentionally rebuilt to add the selector entry; unrelated serialization noise and old-vehicle screenshot overwrites are restored before commit.

An initial WebGL session showed a blank 3D viewport for muscle car and pickup. The final rebuild/reload rendered both correctly and the pickup completed its browser tests; the original blank view's cause was not established. Existing-vehicle regression confidence comes from unchanged source/assets, before/after editor checks and the observed muscle-car browser load, not exhaustive every-platform testing.

This package stops for review with failures visible. Further investigation of shared contact behaviour or its validation contract requires the user's permission before changing the shared engine under addacar; any correction must preserve meaningful tolerances and existing-vehicle regressions. Lamp/suspension deformation also remains acceptance work. No main merge is authorized. Approved A/B evidence is preserved; pending C and rejected experiment evidence are retained for review.
