# Modular vehicle attachments: proposed Blender proof of concept

Date: 2026-10-14. Baseline: verified main `2a9ca8bebb296557ed75513c9ad892c8f5153d92`.
Branch: `feature/attachment-prototype`.

**Status: design proposal for review.** Source inspection and a Blender FBX round trip
have been performed. The three accessories, deformation demonstrations, final metadata,
and renders have not been built. Proposed behavior below is not a measured result.

## Purpose and boundaries

Prove mounting alignment, rotation, separation and following of a crumpling surface
using the existing red compact hatchback and blue muscle car as authoritative references.
Use isolated copies of their verified main assets. Keep prototype files under
`Prototypes/Attachments/`, outside Unity's Assets folder.

The full request covers simple models, a reproducible Blender demonstration, architecture,
concrete metadata, validation, previews, Claude integration guidance, and future vehicle
authoring requirements. The hatchback is the primary test car. Test a single unchanged
roof rack on both vehicles with explicit adapters.

Do not alter the source generators, production .blend or FBX files, C# code, VehicleRig,
VehicleDamageModel, VehicleHitbox, labs, packages, or vehicle-engine-wip. Do not merge main.
Do not implement gameplay customization, collision protection, force transfer or breakage
physics. Front/roof structural references in this experiment are authored reference frames;
the assets do not establish real chassis load paths.

## What inspection established

See [inspection evidence](evidence/inspection.json) and [project context](project-context.md).

| Saved source | Hatchback | Muscle car |
| --- | ---: | ---: |
| Vehicle mesh objects | 76 | 16 |
| Source vertices, all vehicle meshes | 68,446 | 77,454 |
| Body vertices / triangles | 30,586 / 61,168 | 31,284 / 62,564 |
| Cabin vertices / triangles | 13,750 / 27,496 | 3,180 / 6,252 |
| Wheelbase | 2.32 m | 2.72 m |

Both saved sources use metres, identity vehicle roots, +X right, **+Y forward, +Z up**.
Body and Cabin have identity local transforms and applied geometry, with no remaining
modifiers. The hood is a region of Body; neither car has an independent hood panel.
Wheels have axle-centred origins; the hatchback's Bumper_Front has a component origin,
whereas the muscle car's bumper geometry uses the vehicle origin. Never infer a socket
from an object's pivot or from a child-array index.

The muscle car already contains HoodScoop. Leave it unchanged. Use the roof rack for the
cross-vehicle test so the prototype scoop need not overlap or replace this production part.

The source files' SHA256 values match main's Git LFS object IDs. The open interactive
hatchback has unsaved sculpt edits and a different Body vertex count; it is not the source
for this experiment. Background inspection left that session untouched.

Existing FBXs reimport into Blender with the correct world geometry after a +90 degree
X root rotation. Counts and ordered loop-triangle vertex triples match for Body, Cabin and
Bumper_Front on both cars, and HoodScoop on the muscle car. Maximum corresponding world
vertex error is under 0.0000003 m. This is a narrow round-trip result, not a guarantee.

The committed Unity hatchback report records 97,188 imported vertices versus 68,446 source
vertices. Therefore no source-index contract is justified. The existing importer enables
welding and axis conversion; its mesh-order settings alone do not establish index identity.
Unity documents vertex/polygon reordering, vertex welding and root handling as import
options in [ModelImporter](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityeditor/modelimporter).

## Alternatives and recommendation

| Approach | Benefit | Limitation |
| --- | --- | --- |
| Named sockets with root parenting | Minimal authoring and runtime cost | Cannot follow vertices moving under an unchanged root |
| Surface binding for every attachment | Follows sheet metal | Wrong structural semantics for rigid chassis equipment; unnecessary binding work |
| **Named sockets + attachment-owned feet + explicit vehicle adapters** | Reuse geometry; distinguish structural and surface references; cache a few anchors | Requires one small fit record per supported vehicle/accessory combination |

Use the combination. No framework, universal automatic fitting, or accessory mesh
deformation is needed. An explicit supported-pair list rejects unknown combinations.
Vehicle adapters may position/rotate the asset and define a separately authored bracket
set. They must not silently scale or shear the accessory body.

Each accessory has a stable semantic ID, a category, an asset-local mounting frame and
named feet. Each vehicle provides stable semantic socket IDs, their source mesh roles,
and authored rest-space references. A fit record maps feet to sockets and declares allowed
offsets, clearance and mounting limits. Strings are authoring constants, never Blender's
automatic .001 suffixes.

Keep mounting behavior and future failure capability separate:
- `chassis_rigid`: a structural reference frame; no following of sheet-metal vertices.
- `panel_follow`: a rigid accessory pose derived from a small panel-anchor set.
- `multi_point_rigid`: one rigid body fitted to multiple supports, with per-foot residuals.
- `failure_capability`: `none` or `detachable_future`, orthogonal to the mounting mode.

A detachable part may use structural or surface supports. This prototype only computes
diagnostic invalid/over-limit states. It never creates detached rigid bodies or joints.

## Surface binding and pose calculation

Author four labelled rest-space samples around the scoop footprint on Body. Limit
projection to the known hood patch, with a maximum distance and compatible surface normal,
so a bad adapter cannot bind to the underside or cabin. Resolve each sample once against
explicit triangulated geometry. Store three vertex indices and three barycentric weights
per sample, scoped to that exact mesh revision.

For current triangle vertices a, b, c, an anchor is
`p = w0*a + w1*b + w2*c`, with weights summing to 1. This follows the triangle continuously
without searching after deformation. Barycentric mapping is supported by
[Blender's geometry API](https://docs.blender.org/api/main/mathutils.geometry.html).

Use four corresponding local accessory feet and current panel anchors to compute a
least-squares rigid pose (centroid plus a proper rotation, determinant +1, no scale).
Four samples expose nonplanar distortion that a single triangle cannot reveal.
Keep a small authored clearance in the surface frame. Validate the rest fit before
testing damage; a transform cannot make a flat footprint match arbitrary curved metal.

Measure per-foot distance between the transformed accessory foot and its desired current
surface anchor, maximum/RMS residual, pairwise spacing change, and local normal disagreement.
This distinguishes following the average panel pose from actually seating every foot.
A rigid scoop cannot remain flush to a strongly folded hood at all four corners.

Reject nonfinite data, missing meshes, invalid indices, collapsed support triangles, or
an almost collinear support constellation. Detect degeneracy using dimensionless area or
singular-value ratios relative to the rest configuration. Use previous-pose continuity to
avoid frame flips; continuity must not hide a fold or inversion failure.

At a soft residual/spacing limit mark `strained`; at a hard geometric limit mark
`invalid_support`. In the demonstration, retain the last valid pose and draw the missing
connection/residual explicitly. The actual runtime policy for hiding or detaching remains
Claude's decision. Diagnostic thresholds are authoring values to be measured/tuned during
the prototype, not claims about metal strength.

Do not reproject anchors onto the closest surface after each impact. That would let them
slide to different metal. Rebind only after an explicit asset/cache revision change.

## Topology, import and coordinates

There are two products: a durable authoring description and an engine-specific binding
cache. Authoring data contains stable semantic IDs, named mesh role, rest-space positions,
normal/search constraints and adapter revision. A Blender cache is keyed by source content
and topology/triangulation hashes. Topology or triangulation changes invalidate it; rerun
the constrained binder and review the new fit. Do not claim old triangle indices survive
regeneration.

Unity must bind the rest samples against its own imported Mesh, not reuse Blender indices.
Prefer an editor/import-time bake; an initialization-time bounded search is an acceptable
first integration fallback. Cache submesh, vertex triple, barycentric weights and imported
mesh/import-settings signature. Reimport, vertex order changes, mesh replacement or settings
changes invalidate that cache. On an invalid cache, refuse mounting until a successful
rebind; do not guess. The Blender demonstration cannot validate Unity's future cache.

Store public authoring JSON in Blender vehicle coordinates, with an explicit coordinate
tag. Convert points and directions to Unity vehicle coordinates with
`(x_u,y_u,z_u)=(x_b,z_b,y_b)`. Let S be that axis-swap matrix; convert rotation matrices with
`R_u = S R_b inverse(S)` and then construct the Unity quaternion. Do not swap quaternion
components or Euler angles. Verify asymmetric left/front/up markers through the actual
Unity import before treating this mapping as integrated. Import-root transforms must be
accounted for separately; the Blender FBX round trip showed why.

Use unique mesh-role mappings (for example `body_shell -> Body`) validated once per
vehicle, rather than assuming FBX hierarchy paths are unchanged. Names/custom properties
can assist discovery, but neither hierarchy nor custom-property survival is guaranteed.
The muscle generator currently exports custom properties off; sidecar JSON is authoritative.

## Concrete metadata proposal

[metadata-example.json](metadata-example.json) defines stable accessory IDs and categories,
mount modes, asset-local feet, candidate vehicle compatibility, a complete hatchback scoop
adapter with constrained projection seeds, explicit alignment offsets, proposed diagnostic
limits and future strength fields. It is marked design-only and cannot be loaded as a
validated runtime asset. Its null caches mean no triangle binding has yet been generated;
the cache contract specifies the fields to populate separately in Blender and Unity.

Two push-bar brackets establish a line but cannot determine twist. Its adapter must also
provide a rest-up frame or a third noncollinear orientation support. Failure to constrain
that extra rotation must be a validation error, not an arbitrary solver choice.

## The three deliberately simple prototypes

| Accessory | Geometry | Mounting experiment | Visible limitation |
| --- | --- | --- | --- |
| Roof rack | Black metal frame, exactly two crossbars, four feet | Chassis-rigid four-socket adapter; unchanged rack on both cars | Sheet-metal roof dents leave chassis-frame feet separated from the roof |
| Hood scoop | Centred low rectangular scoop, shallow front opening, four labelled feet | Four barycentric anchors on Body; recomputed rigid pose | Severe hood warp makes some feet float/penetrate; residuals remain visible |
| Front push bar | Heavy tubular-steel loop/crosspiece and at least two solid brackets | Multi-point support frame; synthetic asymmetric front displacement | Fixed bracket spacing cannot satisfy divergent mounts; show strain/invalid state |

Use the car silhouettes and actual surfaces as the reference; no remodel or artistic
refinement. A nominal rack around 1.12 m wide and 0.85 m long is a starting envelope, not a
validated fit. Feet, bracket lengths, surface offsets and clearance must be established
from the measured copies. Cross-vehicle adaptation may adjust the socket frame or a
separate bracket piece, never stretch the rack.

Use a black rack, cyan scoop, steel-grey push bar, yellow named vehicle anchors and magenta
attachment feet. Use orange/red diagnostic connectors for residuals/failure. Labels and a
legend must identify marker meaning; colors alone are insufficient. Mark test images
"synthetic Blender deformation" and preserve the red/blue vehicle identities.

## Demonstration and acceptance evidence

Produce a saved demonstration .blend, standalone attachment-only model files/exports, a
reproducible isolated Blender script, concrete sidecar metadata and measured validation.
Saved copies can use separate test scenes or shape keys; all deformation must start from
the verified rest geometry, not accumulate floating-point edits across rebuilds.

| Case | Required evidence |
| --- | --- |
| Each installed accessory | Undamaged three-quarter view and mount close-up; labelled feet/sockets; rest clearances/residuals |
| Rack after roof dent | Chassis pose invariant; roof displacement and any foot-to-roof gap reported |
| Scoop moderate tilt/dent | Changed position and rotation; compare root-only frozen pose; anchor reconstruction and per-foot residuals |
| Scoop severe asymmetric fold | Actual hard-limit/invalid state; readable floating/intersection limitation; no fabricated perfect fit |
| Push bar front crush | Two or more mounting brackets; asymmetric support motion; rigid fit and per-foot disagreement |
| Repair | Exact restored vertices plus restored mount pose within stated numerical tolerances |
| Rack on both vehicles | Same accessory geometry hash, vehicle-specific adapters and measured spacing/clearance |
| Repeat build | Stable semantic IDs; same inputs reproduce fitting and geometry; changed topology invalidates cache |
| Error cases | Wrong mesh revision, missing socket, unsupported vehicle and degenerate anchors rejected |

At least one actual deformation case must exercise translation and rotation together.
Thresholds and success tolerances must be declared in metadata and reported with results.
Do not label geometry that was manually repositioned per rendered state as following.

## Cost and Claude integration boundary

Four scoop anchors need at most twelve cached vertex reads and one small rigid fit per
relevant geometry change. Multiple attachments can share cached mesh/vertex references.
Rigid chassis sockets need only transform propagation. Costs scale with anchor count,
not whole-body vertex count. These are algorithmic expectations; no runtime timings have
been measured for 8-12 vehicles.

Main already exposes `VehicleDamageModel.Deformed`, `Parts`, and
`DeformablePart.DeformedVehiclePosition(int)`. These are promising integration seams,
not a promise that Claude's unfinished architecture retains these APIs. After final
geometry rebuild/repair, update bindings for changed parts, then apply mount transforms.
A per-mesh revision counter can avoid updating unaffected attachments.

Claude's minimum future work:
1. Resolve a unique vehicle model frame/mesh-role map; load adapters and reject invalid fits.
2. Bake/validate imported-mesh anchor caches, separate from Blender caches.
3. Read cached deformed vertex positions after rebuild, including repair and model reset.
4. Fit a rigid pose, evaluate support quality and expose diagnostics.
5. Exclude attachment renderers from automatic body deformation, damage-face bounds and
   physical hitbox discovery unless intentionally integrated. Main currently scans child
   MeshFilters and defaults unmatched parts to deformable.
6. Later define force, failure and detached-body ownership in the destruction engine.

Strength metadata may declare force units (N), torque units (N m), failure group and future
policy, but use null values until designed and calibrated. Geometry residual in metres is
not a force estimate, and an over-limit marker is not simulated breakage.

## Requirements for future /addacar authoring

Every vehicle must provide:
- Stable vehicle ID, revision, metre scale, explicit axes and identifiable vehicle frame.
- Unique body/cabin mesh roles and surface-region constraints, plus source/export hashes.
- Named optional roof, hood and front structural/panel socket groups with rest poses and
  surface seeds as applicable. Omitted groups mean unsupported; no invented automatic fit.
- Explicit structural versus panel-follow semantics, local normals and mount footprints.
- Compatibility/adapter records with allowed transforms, clearances and quality limits.
- Regenerated mesh bindings after topology changes; asymmetric axis markers, fit/repair
  checks and a Unity imported-binding verification before runtime acceptance.

Blender automation can generate simple accessories, named sockets/adapters from known
procedural vehicle dimensions, constrained surface bindings, synthetic deformation cases,
diagnostic renders and validation files. A new vehicle's structural intent, acceptable
clearances and real failure strength still require authoring/review.

## Provisional answers to the seven mission questions

1. Surface anchors should follow crumpling translation/orientation; convincing moderate
   damage and severe-warp limits still require the requested prototype measurements.
2. Reuse one accessory through explicit per-vehicle adapters; the rack fit is not yet tested.
3. Vehicles need stable IDs/frames, mesh roles, named sockets, panel seed regions and
   revisioned compatibility data as listed above.
4. Blender can automate procedural geometry, bindings, synthetic cases and evidence.
5. Claude owns imported-mesh binding, runtime updates, repair/reset and eventual physics.
6. Mount reference mode and failure capability are separate fields.
7. The smallest proposed system is semantic sockets + accessory feet + small vehicle
   adapters + a few cached surface anchors; no broader customization framework.

## Evidence status

Confirmed now: verified source identity, saved hierarchy/origins/units, hood ray samples,
and the limited existing-FBX Blender round trip.
Historical only: Unity's committed import report.
Not tested: accessory mounting/deformation, cross-vehicle fit, failure behavior, runtime
performance, Unity anchor mapping, player builds, collisions or multiplayer.
