# Compact Pickup: Stage A reference intake

User brief: late-1980s / early-1990s compact regular-cab 4x4 pickup, two doors,
standard open bed, stock body, slightly raised stance, forest green over warm
cream lower panels, dark bumpers, simple steel wheels, chunky all-terrain tires.
No permanent accessories, logos or badges. This task stops at Checkpoint A.

## Source priority and assumptions

- Photo 3 side profile is the primary length, wheelbase, wheel placement, cab,
  roof, hood, bed length and height source. Photo 3 front/rear three-quarter views
  define the overall character; front and rear elevations cross-check the stance.
- Photo 2 top view controls the convex hood plan, nearly parallel fenders,
  roof footprint and open bed. Photo 2 front elevation controls the grille/lamp
  relationship. Its high nose and hood/fender details cross-check surfacing.
- Photo 1 supplies the low-angle lift/clearance target, suspension/arch details,
  rear wheel, two-tone door treatment and bed interior/wheel-tub references.
- Every panel is catalogued with its original crop rectangle, projection and
  relevant features in `Previews/CompactPickup/References/reference_catalog.json`.
  Full original sheets are copied there without altering the source attachments.
- Absolute size is a design assumption: nominal 4.70 m long, 1.94 m body width,
  1.82 m high, 2.95 m wheelbase, 0.84 m tire diameter. Actual mesh bounds are in
  the checkpoint measurements. Mirrors and wheel extents are reported separately.
- Perspective photos have unknown focal lengths and should not override the side
  or top measurements. Pixel estimates carry uncertainty; they are not blueprints.
- Photo 2's top-view bed is proportionally longer than Photo 3's side-view bed.
  Preserve the primary side proportions; explicitly flag this conflict at A.
- Small production details such as wipers, seams, fuel door, steel-wheel hole
  construction, detailed suspension, liner side ribs and lamp internals are deferred.

## Stage A work and stop condition

- [x] Read the asset specification, QA checklist and canonical addacar workflow.
- [x] Review existing cars and generator conventions.
- [x] Create an isolated worktree from main, branch `feature/vehicle-compact-pickup`.
- [x] Build the separate hood/cab, bed sides, tailgate, floor/liner and wheel-tub volumes.
- [x] Render front three-quarter, side, rear three-quarter, true top, plus inspection views.
- [x] Compare the supplied references, measure mesh proportions, fix major discrepancies.
- [x] Save evidence and a feature checklist for Checkpoint A.
- [x] User approved Checkpoint A; preserve approved proportions and images for Stage B.

Reproduce the blockout and renders with:

```text
blender -b --factory-startup --python-exit-code 1 --python Blender/Scripts/build_compact_pickup.py -- . --render
blender -b Blender/CompactPickup_StageA.blend --python-exit-code 1 --python Blender/Scripts/inspect_pickup_stage_a.py -- .
python Blender/Scripts/compose_pickup_stage_a.py . --references <original attachment directory>
```

This is an asset blockout in the established production pipeline. No Unity assets,
engine interfaces or shared destruction code are changed. A production manifest,
FBX, palette, stripe-mask export and validated socket metadata belong after B.
