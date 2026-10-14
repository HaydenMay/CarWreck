# Attachment prototype validation results

2026-10-14. Blender 5.2.0 LTS. Baseline main: `2a9ca8b`.
All work is isolated on `feature/attachment-prototype`. No Unity integration or merge.

## Result

The small Blender proof of concept demonstrates that cached panel anchors can drive a
rigid scoop through moderate translation and rotation. It also exposes the point where
a rigid accessory no longer fits a folded panel. The same unscaled roof rack fits both
authoritative vehicle models through explicit adapters.

The roof gap and frozen invalid mounts are **demonstrated limitations**, not acceptable
final gameplay behavior. They require later decisions about support motion, compliant
mounts, bending or detachment.

## Measured cases

Errors are maximum distances from rigid accessory feet to their desired surface positions
including the authored 3 mm normal clearance. They are geometric errors, not force or
strength measurements.

| Case | Best rigid-fit error | Applied foot error | Finding |
| --- | ---: | ---: | --- |
| Hatchback rack, undamaged | 0.255 mm | 0.255 mm | Four mounted feet, two crossbars |
| Muscle-car rack, undamaged | 0.580 mm | 0.580 mm | Same geometry hash and unit scale |
| Scoop, undamaged | 1.808 mm | 1.808 mm | Hood curvature already prevents mathematically perfect seating |
| Scoop, moderate deformation | 12.840 mm | 12.840 mm | Follows 88.424 mm translation and 9.348 degrees rotation |
| Scoop, severe fold | 121.652 mm | 389.869 mm | Invalid; retained moderate pose shows the lost connection |
| Scoop, repaired | 1.808 mm | 1.808 mm | Original vertices restored exactly; rest pose restored |
| Rack after roof dent | 3.664 mm | 160.248 mm | Invalid roof contact; no final gameplay approval implied |
| Push bar, undamaged | 1.037 mm | 1.037 mm | Two bracket assemblies, four mounting points |
| Push bar, asymmetric crush | 103.928 mm | 561.032 mm | Invalid supports; retained undamaged pose exposes bracket disagreement |

A root-parented scoop left at its initial pose would have a **143.508 mm** maximum error
in the moderate case, versus 12.840 mm using the anchors. The white wire rectangle shows
that root-only reference.

The severe scoop's H4 support moves deep into the crumpled panel. All four original
triangle/weight references are retained; no nearest-surface reprojection or manual
per-state accessory placement is used. The yellow H4 overlay stays on the tracked
surface, while the magenta foot remains with the last valid rigid pose. Even applying the
best new rigid pose would still leave 121.652 mm of error.

The hood experiment deliberately keeps the scoop rigid. It does not establish that
13 mm is production-quality seating: smaller distortion can still produce foot
interpenetration or visible gaps. A 25 mm soft error threshold, 60 mm hard error threshold,
25% pairwise spacing-change limit and 55-degree normal-spread/orientation-error limit are diagnostic
authoring choices, not calibrated material limits.

## What was actually tested

- Nine numerical tests cover known barycentric motion, proper rigid rotation/translation
  without scale, warped supports, stale revisions, invalid indices, collapsed triangles,
  collinear supports, nonfinite data, missing surfaces, unsupported pairs, missing sockets,
  jointly inverted support triangles and a coherent rotation beyond 90 degrees.
- 94 acceptance checks cover reported behavior, exact source hashes, independent FBX
  reimports, the actual reopened demonstration scenes, stored accessory-foot positions,
  anchors reconstructed independently from saved deformed meshes, and all 13 PNG outputs.
- All ten demonstration cases were reopened from the saved .blend. Each accessory instance
  and every surface-anchor/foot location matched its recorded result.
- All three standalone FBXs reimported with expected mesh counts and metre-scale bounds
  within 0.00001 m. This tests Blender reimport, not Unity indexing.
- Anchor rest reconstruction error was at most **0.000000137 m**.
- Two consecutive final builds produced the same geometry/binding/pose metadata signature:
  `9e417cb6f407e863897aa30d26aaa74c14304822ebcfdeb75e3e64711a6514d8`.
  This checks deterministic semantic inputs/results, not binary .blend bytes or render pixels.
- SHA256 checks preserved both source .blend files, both production FBXs, both vehicle
  generators and both lab scenes. Git scope checks exclude C# and production assets.
- The final renders were visually inspected for fitted geometry, tracked markers, visible
  separation and readable labels.

Raw measured values: [results.json](evidence/results.json).
Acceptance names: [acceptance.json](evidence/acceptance.json).
Source/FBX investigation: [inspection.json](evidence/inspection.json).

## Renders and saved models

- [Scoop: undamaged, moderate, severe and repair](previews/hood_scoop_comparison.png).
- [Rack: hatchback, roof dent and the same rack on the muscle car](previews/roof_rack_comparison.png).
- [Push bar: fitted brackets, front view and asymmetric failure](previews/push_bar_comparison.png).
- [All three installed](previews/01_hatch_installed.png).
- [Undamaged hood, top view](previews/02_scoop_rest_top.png).

Yellow crosses are surface anchors, magenta rings are rigid accessory feet, and orange
connectors show disagreement. Labels H1-H4, R1-R4 and B1-B4 remain stable between states.
The overlay is explicitly X-ray: anchors obscured by the scoop or bumper remain visible.
Raw images in renders/ contain only the rendered geometry; previews add projected
diagnostics, captions and comparison layout.

`demonstration.blend` contains ten case scenes plus three standalone asset scenes.
Select a numbered scene and use its camera to inspect the case. Mesh marker objects have
viewport "in front" enabled; their names identify the socket/foot ID.
Standalone sources/FBXs are in models/:
- rack: 112 source vertices, 14 simple box pieces, four feet and exactly two internal crossbars.
- scoop: 48 source vertices, six pieces, 95 mm maximum accessory-local height.
- pushbar: 112 source vertices, two tubular pieces and two solid brackets; two points per
  bracket provide the additional orientation information a single two-point line lacks.

## Cross-vehicle fitting and physical limits

The rack's lateral mount spacing is 1.06 m; front-to-rear mount spacing is 0.60 m.
Its frame is about 1.14 by 0.96 m. The hatchback roof adapter centres at Y = -0.50 m,
while the muscle-car adapter centres at Y = -0.20 m. The shorter usable muscle-car roof
required that fore-aft mounting footprint. No rack mesh, pivot or scale changes between
vehicles. This supports explicit adapters, not universal automatic compatibility.

Rack chassis references are authored from the undamaged roof fit and held fixed for the
dent test. The roof's cached surface points are observed only to measure the gap. This
does not prove the vehicle has a corresponding physical chassis structure at these points.

Push-bar support anchors are on the existing Bumper_Front mesh. They are **geometric
support proxies**, not validated chassis/load-bearing hardpoints. A runtime design must
choose proper structural supports. The simple square brackets are intended only to make
the mounting relationship visible. Rest-fit checks sample the feet; full accessory/body
intersection, real fastener geometry and collision clearance were not validated.

## Untested

No Unity anchor caches, C# changes, runtime update integration, actual CarWreck collision
deformation, force redistribution, armor protection, mount strength, breakage/detachment
physics, driving, multiplayer, player build or 8-12 vehicle performance tests were performed.
The synthetic displacement is not a simulation of material stress or collision force.

The current Unity importer may split/reorder vertices and change roots. A previous
committed Unity hatchback report records more imported vertices than the source. Existing
Blender FBX index correspondence is not a cross-tool guarantee; Claude must rebind against
the actual imported Unity mesh.

## Answers to the seven mission questions

1. **Can accessories follow crumpling panels?** Yes for moderate pose changes in this
   experiment: the rigid scoop follows both position and rotation. Severe folds cannot be
   satisfied by a rigid footprint; the measured failure must trigger a future policy.
2. **Can one accessory fit different vehicles?** Yes with explicit adapters: the identical
   rack fits both vehicles with under 0.6 mm maximum rest error, without scaling.
3. **What must future vehicles provide?** Stable vehicle/frame and mesh-role IDs; named,
   semantically classified socket groups; constrained panel seeds; revisioned source
   identity; compatible adapter fits, clearances and limits.
4. **What can Blender automate?** Simple geometry, semantic mounts, vehicle-specific
   fitting seeds, source-triangle caches, synthetic states, diagnostic renders and checks.
   Structural intent and actual failure limits still need authoring.
5. **What must Claude implement?** Unity-side binding/cache validation, post-deformation
   anchor updates, proper rigid fits, support-quality decisions, repair/reset handling
   and eventual compliant/breaking/detached behavior.
6. **How are behavior types separated?** Mounting mode selects chassis_rigid, panel_follow
   or multi_point_rigid. Future failure capability is separate metadata.
7. **Smallest reliable system?** Named semantic sockets + attachment-owned feet + explicit
   supported adapters + four cached panel samples where needed. No garage, inventory,
   generalized customization framework or new physics subsystem is required here.
