# Compact Pickup — Stage B modeling brief

The user approved Stage A at `f230c5a`, accepting its bed-length and track deviations
from conflicting reference views. Preserve its wheelbase, axle positions, cab/bed
relationship, lifted ride height and forest-green/cream lower-band layout.
The approved images, source and measured baseline are protected by
`Previews/CompactPickup/approved_checkpoint_a.json`.

Stage B uses a separate generator and source file. It refines the existing asset
without changing the shared damage engine, starting Unity integration, or overwriting
approved Stage A renders.

1. Rebuild the shell with dense quad grids and integral hood stamps, shoulder
   grooves, wheel-arch returns and restrained period panel definition.
2. Build a recessed grille pocket, lamp reflectors/fluted lenses, signals and
   stamped bumper vents while preserving the approved nose envelope.
3. Replace tire blocks and painted wheel-hole placeholders with interlocking
   all-terrain tread and actual perforated steel-wheel meshes. Refine visible
   springs, axles, shocks and leaf packs without moving the wheel mounts.
4. Refine the cabin/glass/roof and window seals as connected surface regions;
   inspect roof UV paint selection and render a neutral color diagnostic.
5. Form liner corrugations and wheel tubs as continuous interior surfaces;
   refine the tailgate, bed walls and rolled rails while retaining open cargo space.
6. Prepare explicit per-component role proposals, UV1 lower-band mask, palette
   and bounded socket authoring metadata outside the Unity Assets directory.
7. Measure geometry against references and Stage A, flag drift above 2%, render
   identical baseline cameras plus detail views, review and correct visible defects.
8. Publish the B gallery and report on the feature branch, verify hosted images,
   and wait for B approval before production configuration/QA and Unity integration.

Geometry checks during modeling establish density, finite/usable geometry, UVs,
roof color, open bed and proportion preservation. They are not the later full
asset acceptance QA or Unity deformation tests. The solid-box collision-bed
limitation remains documented; no engine modification is authorized here.
