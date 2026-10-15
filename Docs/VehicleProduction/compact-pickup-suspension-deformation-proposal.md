# Coherent deformation of small tubular parts (implemented)

Status: implemented on `feature/vehicle-compact-pickup-c` for Checkpoint C review (not merged). The original proposal and evidence follow; the implementation and its measured results are in [Implementation](#implementation). The separate grazing-contact defect is covered in [Grazing contact](#grazing-contact-implementation).

## Evidence

The current pickup standard front hit folds 484 / 8,832 front-suspension triangles (5.48%, mandatory limit 5%). It has the original approved spring, damper and axle geometry, Underbody role and crumple 1. The 52 deforming / 4 rigid-wheel classification is unchanged.

The saved first-C mesh and per-vertex field explanations locate most affected geometry on the spring wire. Outward tent displacement varies around a 20 mm wire section with its surface normal. Peak spring displacements are about 19 mm and 27 mm on the two coils; a measured section widens from 20 mm to 33 mm and becomes concave. This is not evidence that the entire cross-section reverses: the measured ring area remains positive. The test specifically detects reversed triangle normals, and visual inspection shows distorted wire.

Nine isolated topology candidates kept the same 55 mm coil radius, 20 mm wire diameter, five turns and height. Increasing path samples from 160 to 320 and section sides from 8 to 16 left coil-only folds at 7.08–9.75%. These are diagnostic coil-only fractions, distinct from the full suspension's 5.48%; they do not replace the mandatory final-asset test. Added density does not address the normal-dependent displacement variation. The independent lamp inset solves its former outlier without changing the field.

See [focused investigation](../../Previews/CompactPickup/StageC/Revision2/Investigation/standard_front_vertex_explanations.txt), [topology experiment results](../../Previews/CompactPickup/StageC/Revision2/Investigation/topology_candidate_results.csv) and [current integration results](../../Previews/CompactPickup/StageC/Revision2/Validation/Vehicle2/integration.txt).

## Proposed bounded design

Add an opt-in generic deformation domain for small tubular components. Preserve the authored coil centreline and cross-section frames in model-local metres; bind imported vertices by rest-space position rather than Blender vertex indices. Evaluate the existing impact field at neighbouring centreline samples, transport the local section frame along the bent centreline, and interpolate positions for the wire surface. The entire spring remains deformable and can bend/crush along its length; each small wire section moves coherently instead of receiving incompatible surface-normal offsets.

Preserve existing impact strength, work hardening, saturation, cabin and wheel-mount capacity limits, record overflow and deterministic replay. Local section limits must be conservative with respect to every bound vertex's current capacity; they must not bypass protected regions. Default surface evaluation remains unchanged for all other components and existing vehicles. Do not change collision layout or implement attachment physics.

This would touch the generic manifest/import contract, DeformablePart binding and accumulation/rebuild paths, and source metadata for the pickup suspension. It is a shared deformation architecture change, which is why implementation stops for approval. A short standalone prototype should demonstrate the design on one coiled tube before any production integration; exact data format and frame transport still require that prototype, not speculative promises in this proposal.

## Required proof before accepting that change

Use the current recorded front impact as a failing fixture. Check that wire sections remain coherent, all parts meet the unchanged 5% standard-hit fold limit, click-dent Body limit remains <=20, and the original vehicle shape/materials are retained. Test oblique and side impacts, repeated mixed damage, capacity/cabin boundaries, 33-hit overflow, exact recorded replay and bounded 0.1 mm perturbations, repair, switching and seed2026 50-impact Chaos. Repeat all three vehicles and actual WebGL checks, measure added binding/memory/per-hit cost, and compare actual Unity close-ups. A passing count without acceptable wire appearance is insufficient.

## Scope kept out

No collider redesign, open-bed cargo support, attachment runtime integration or major performance rewrite. No thicker wire, reduced crumple, rigid reclassification, extra triangles solely to dilute the percentage, or relaxed validation limits.

## Separate remaining failure: grazing-contact perturbation sensitivity

The full regression suite fails the unchanged 20 mm limit for 0.1 mm impactor perturbations: pickup 21.7 mm and hatch 25.6 mm. A saved pickup run fails at 26.582 mm by impact 8 on BedRail_L. Replaying those eight recorded impacts reproduces the same result without new physics, with existing evaluation optimizations all off or all on. Exact unperturbed replay remains zero. A later hatch capture passes at 6.647 mm; this does not waive the failed run.

The saved per-vertex trace shows Lead changing 1.8305 to 1.8583 m (27.8 mm) for a 0.1 mm z shift near a grazing slab. Fold amplitude/phase changes at the same vertex as well. The box ray/slab intersection divides by the direction's projection onto a face normal, so near-parallel surfaces can have a steep depth gradient. This is evidence of excessive field sensitivity under the required robustness contract, not proof of an incorrect ray-box intersection formula. The precise contribution of footprint recomputation versus local fold phase still needs isolated tests.

Propose a separate bounded experiment before a runtime patch: use the recorded fixture; freeze footprint and fold terms in diagnostics one at a time; compare the exact box surface with a finite-thickness contact-band formulation that smoothly handles grazing faces. Any smoothing must retain true orientation/width/height, avoid spreading damage to missed surfaces, preserve protected cabin/capacity limits, and pass the original rotated-block counterexample as well as the 20 mm perturbation limit on all three vehicles. Do not simply increase the parallel epsilon, enlarge impactors, clamp the final difference, disable noise, or replace the box with an orientation-free stamp. Publish the design and failing/passing fixtures before choosing a shared formulation. This affects shared deformation semantics; no such runtime change is included or presumed authorized.

The tubular-domain proposal above addresses suspension wire coherence only. It is not presented as a fix for this separate BedRail/hatch sensitivity defect. Both must pass before Checkpoint C can be complete.

## Implementation

The prototype showed the authored-centreline plan above was unnecessary: the cross-section rings can be recovered from
the imported mesh itself, so no new source metadata, frame transport or Blender vertex indices are needed.

**Standalone prototype first.** `tube-prototype <vehicle> <part filter>` (`TubeSectionPrototype.cs`) evaluates the
shipped per-vertex field and the section-coherent alternative side by side on the same standard hits. It touches no runtime
state. On the pickup's standard 15 m/s wide front hit, it reduced `Suspension_Front` from 484/8832 folded triangles
(5.48%) to 39/8832 (0.44%). It also showed that the per-vertex field changes a section's radius by up to 216% (344% on
the front-left corner); that is the visible inside-out wire. Full output:
[tube_section_prototype.txt](../../Previews/CompactPickup/StageC/Revision3/Investigation/tube_section_prototype.txt).

**Section detection (`TubeSections.Build`, generic, mesh only).**

- Coincident vertices are welded at 0.1 mm so each ring is one loop.
- Each curved vertex's local tube radius is the smallest |Δposition| / |Δnormal| over its edges. Its section centre is that
  distance behind it along the normal.
- Edges whose two ends share a centre (within a quarter of the edge length) are ring edges; edges along the tube are not.
  These ring edges are joined into groups.
- A group is accepted only with at least three members that all sit at about the same distance from the common centre.
- Flat faces, plates and anything that isn't a ring stay unbound and keep the shipped behaviour exactly. Cap vertices join a
  section only when all their neighbours belong to it.

On the pickup this finds 428 sections binding 7742/8138 vertices (95%) of `Suspension_Front`, and 110 sections binding
2646/4622 (57%) of `Suspension_Rear`, where the dampers and axle are plates and stay unbound.

**Runtime (`DeformablePart`, opt-in).** `coherentTubeSections` is a per-component manifest/rule field, default `false`.

- **Impact sum.** After each accumulated impact, the members of each section share their mean impact sum.
  - The ring translates as one unit: the tube still bends, crushes and saturates along its length, but its round section
    cannot turn inside out.
  - Sections an impact did not change are left bit-identical. Skipping a part the impact cannot reach therefore gives
    exactly the same result as evaluating it, so exact replay holds.
- **Capacity.** Every member gets the smallest capacity in its section, so a section is never stronger than its weakest
  vertex. Cabin and wheel-mount protection can only become more conservative, never weaker.
- **Slider crush.** The directional (slider) crush is averaged per ring in the same way.
- **What doesn't change.** Strength, work hardening, saturation, record overflow and merging, repair and replay use the
  existing code paths.
- **Other vehicles.** Nothing changes for any component without the flag. The muscle car and hatchback don't set it.
- **Pickup.** The pickup sets it on `Suspension_Front` and `Suspension_Rear` only. The geometry, materials, crumple
  (still 1) and the 52 deforming / 4 rigid-wheel classification are unchanged.

**Cost.** The binding is two int arrays per opted-in part, built once at load: about 44 KB for both suspension parts
(front ≈ 32.7 KB, rear ≈ 11 KB). Per-hit cost is one averaging pass over the bound vertices. Measured timings are in the
Checkpoint C report.

**Result.** Standard-hit fold outliers on the pickup: none. `Suspension_Front` drops from 484/8832 (5.48%) to 39/8832 (0.44%)
and `Suspension_Rear` stays at 44/5280, unchanged. On the rear-corner hit `Suspension_Rear` goes from 134 (2.54%) to
105 (1.99%).

**Visual check.** `RunSuspensionVisualProbe` (`SuspensionVisualProbe.cs`) makes the same front hit in actual Unity with
sections off (in memory only) and then on. It renders the front suspension on its own, because the wheel and body hide it
in normal views. With sections off, the lower end of the coil is crimped and torn. With sections on it stays a round coil
with the same crush (max displacement 28 mm off, 27 mm on). See the
[zoomed comparison](../../Previews/CompactPickup/StageC/Revision3/Unity/Suspension/suspension_front_zoom_off_vs_on.png)
and the [probe output](../../Previews/CompactPickup/StageC/Revision3/Unity/Suspension/suspension_visual_probe.txt).

## Grazing contact implementation

**Cause, isolated on the saved fixture.** The bounded experiment above was run first, on the recorded seed-2026 runs
replayed without new PhysX sampling ([draft probe](../../Previews/CompactPickup/StageC/Revision3/Grazing/grazing_draft_probe.txt)).

- On the pickup's 8-impact counterexample, a 0.1 mm z shift moves BedRail_L by 26.582 mm.
- With the footprint frozen it is still 26.170 mm, and with the fold term off it is still 24.156 mm. Neither term is the
  cause.
- The cause is the leading surface itself. Impact 8's box has a 3.8 m face 0.174° off its direction. Projected along the
  direction, that face is a sliver about 12 mm wide across which the contact depth drops by the whole box length: a slope
  of about 1/tan(0.174°) ≈ 330. A 0.1 mm shift moves the depth there by centimetres (Lead 1.8305 → 1.8583 m at the worst
  vertex).
- The exact surface is also discontinuous in orientation: at exactly zero tilt the sliver disappears. The
  [tilt sweep](../../Previews/CompactPickup/StageC/Revision3/Grazing/grazing_tilt_sweep.txt) turns each grazing impact's
  face towards parallel. The exact result stays 133 mm from the aligned result down to 0.005° and then jumps.

**Design: a 2° contact draft (`BoxProjection`, `ImpactFootprint.cs`).** `Lead` is now the upper envelope of the exact
surface under a cone of half-angle 2° about the impact direction:

  `Lead(p) = max over q in the silhouette of [ExactLead(q) − |p − q| / tan 2°]`

This is the box swept with a 2° draft: a contact band of finite thickness. `ExactLead` is the previous function,
unchanged. The maximum is taken in closed form over the 12 projected box edges (`ExactLead` is concave and piecewise
linear, so the maximum is at `p` or on an edge), with no sampling or iteration.

How it meets the constraints:

- **True orientation, width and height.** The box, its rotation and its extents are the recorded ones. No epsilon,
  enlargement, clamp or orientation-free stamp is involved.
- **No damage outside the silhouette.** `Lead` is NaN wherever `ExactLead` is NaN, so the contact outline is unchanged and
  missed surfaces stay missed.
- **Never past the true front.** The envelope never exceeds the box's own frontmost point.
- **Ordinary contacts are unchanged.** It is active only when a face lies within 2° of the direction without being exactly
  parallel (`DraftLimited`). Otherwise, and for face-on and ordinarily angled boxes, it returns `ExactLead` exactly.
- **Bounded sensitivity.** A lateral shift δ changes `Lead` by at most δ / tan 2° ≈ 29 δ, instead of the unbounded
  1/tan(tilt).
- **Read at the silhouette, not 2 mm inside it.** For a vertex just outside the silhouette, the fold uses the lead at the
  nearest silhouette point. The original code reads it at a point nudged 2 mm inward. On the drafted envelope a 2 mm
  step can change the lead by 2 mm / tan 2° ≈ 57 mm, so a vertex crossing the silhouette edge jumped by that much.
  This was the cause of a 48.3 mm muscle-car failure (below). `ImpactFootprint.OutsideLead` reads the envelope at the
  silhouette point itself, where it is continuous. It applies only to draft-limited impacts; every other impact keeps
  the original read. The exact surface still uses the 2 mm-nudged read. That read is bounded by 2 mm / tan θ for a face
  at θ ≥ 2°, the same bound as before this change.
- **What it does change.** Inside the silhouette, near a grazing face, the depth falls off at the 2° draft rather than at
  the grazing angle. The band reached early is at most travel × tan 2° (about 3.5% of the travel) wide. On the recorded
  runs, 16/50 hatchback and 17/50 pickup impacts have such a face. Each of them changes by up to 277 mm at its worst
  vertex (pickup impact 3, 0.41° tilt): see the "exact vs draft" column of the
  [orientation probe](../../Previews/CompactPickup/StageC/Revision3/Grazing/grazing_orientation_probe.txt).
- **Unchanged paths.** Strength, capacity, cabin protection, saturation, overflow and replay are untouched.
- **Shared change.** `GrazingDraftDegrees` is a shared default (2°). It applies to every vehicle, because the hatchback
  fails the same test and the fix belongs in the shared contact. Setting it to 0 restores the exact surface.

**Results (0.1 mm perturbation, recorded replays, limit 20 mm).**

| Run | Exact surface | 2° draft |
|---|---|---|
| Pickup saved 8-impact counterexample | 26.582 mm FAIL | 2.906 mm PASS |
| Pickup seed-2026 50 impacts | 26.582 mm FAIL | 3.598 mm PASS |
| Hatchback seed-2026 50 impacts | 7.776 mm | 2.916 mm |
| Muscle sweep Run1, 50 impacts ([fixture](../../Previews/CompactPickup/StageC/Revision3/Investigation/MuscleImpact8/)) | 11.188 mm | 48.3 mm FAIL before the silhouette fix, 3.917 mm PASS after |

On the pickup counterexample, the 1° draft gives 5.445 mm and 3° gives 2.064 mm. On the muscle fixture, 1° gives 5.683 mm and 3° gives 3.080 mm (63.8 and 31.5 mm before the silhouette fix). 2° was chosen as the smallest round value with a wide margin.

**Perturbation sweep.** One recorded run passes on its own, but PhysX contact sampling differs between Editor launches. So
15 launches' seed-2026 runs, 45 vehicle runs in all, were recorded and each was re-judged with the four 0.1 mm shifts. Before
the silhouette fix the muscle car failed 8 of 15 (48.3 mm). With it, all 45 pass. The worst cases are muscle 3.917 mm,
hatchback 10.381 mm and pickup 3.248 mm
([sweep replay](../../Previews/CompactPickup/StageC/Revision3/Investigation/PerturbationSweep/sweep_replay.txt)).

**Orientation continuity.** In the tilt sweep, the draft result approaches the aligned result smoothly as the tilt goes to
zero. The exact surface stays at its full difference until it jumps. Each row below runs from the recorded tilt down to
about 0.001 times it:

- pickup impact 8: draft 133.9 → 130.2 → 70.4 → 33.2 → 10.3 → 3.6 → 0.3 mm; exact 133.2 … 124.6 → 43.6 → 0.0 mm;
- pickup impact 3: draft 403.0 → 337.6 → 171.5 → 70.8 → 23.6 → 7.6 → 0.8 mm; exact 407.5 (four steps) → 48.3 → 1.7 mm;
- hatchback impact 6: draft 257.1 → 257.1 → 256.5 → 120.5 → 38.9 → 13.0 → 1.3 mm; exact 265.7 … 232.6 → 0.1 mm.

At the recorded tilts both formulations are far from aligned, because a 0.2–0.7° tilt on a 3.8 m box is a real change of
geometry.

**Cost.** Evaluated naively over all 12 edges for every vertex, the envelope roughly doubled per-impact cost
(pickup 119 → 222 ms average). Each edge's cone can only raise `Lead` within `(edgeTop − t) / k` of the edge's
projected bounds, so `Envelope` skips edges whose bounds are further away. The skip margin is 0.1 mm of cone, far above
float rounding, so the result is unchanged. The muscle impact-8 probe report and the 45-recording sweep replay report are byte-identical
before and after culling (figures to 0.001 mm). With culling, the pickup averages 127 ms against a 119 ms baseline. Measured figures are in the Checkpoint C
report.

**Fixtures.** The recorded runs
([hatchback](../../Previews/CompactPickup/StageC/Revision3/Grazing/vehicle1_seed2026_recorded.json),
[pickup](../../Previews/CompactPickup/StageC/Revision3/Grazing/vehicle2_seed2026_recorded.json)) and the probes
`RunGrazingDraftProbe`, `RunGrazingOrientationProbe` and `RunGrazingTiltSweep`
(`Assets/DamageLab/Editor/Grazing*.cs`) reproduce every number above. The failing case is the exact surface
(`GrazingDraftDegrees = 0`) on the first 8 pickup impacts; the passing case is the same fixture with the default draft.
The full three-vehicle regression results are in the Checkpoint C report.
