# Proposed next step: coherent deformation of small tubular parts

Status: proposal only. No shared runtime implementation is authorized or included in the focused extension. The pickup is still not ready for Checkpoint C approval or merge.

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
