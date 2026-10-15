# Compact Pickup — Checkpoint B review

**Status: detailed model complete for review; awaiting user approval.**
Feature branch: `feature/vehicle-compact-pickup`. Approved baseline:
[`f230c5a`](https://github.com/HaydenMay/CarWreck/tree/f230c5a58f52547844f2e07ef79263e9b0d65bb4/Previews/CompactPickup/StageA).
The [approval record](../approved_checkpoint_a.json) protects 25 A source, image,
camera and measurement files by SHA-256. All remain unchanged.

## Review evidence

- [Reference comparisons: front ¾, side, rear ¾ and directly above](checkpoint_b_overview.png).
- [Approved A versus B, identical cameras and framing](approved_a_vs_b_overview.png).
- [Hood, recessed grille, wheels/suspension and bed details](checkpoint_b_details.png).
- [Cabin](comparison_cabin.png), [roof paint diagnostic](stage_b_roof_color.png),
  [front](stage_b_front.png), [rear](stage_b_rear.png),
  [nose from above](stage_b_nose_top.png) and [clearance](stage_b_clearance.png).
- [Measured reference/A/B proportions](proportion_comparison.md),
  [geometry](geometry_measurements.json), [modeling checks](modeling_checks.json),
  [render/source provenance](review_manifest.json).

All three supplied sheets were used: Photo 3 for the primary side silhouette and
¾ views, Photo 2 for plan geometry, hood shoulders and front fascia, and Photo 1
for suspension clearance, wheels and liner details. Perspective camera matching
is approximate because the source lenses are unknown. A/B share all nine baseline
cameras, projection, framing and studio lights. A used 24 render samples; B uses 32.

## Proportions preserved

| Measurement | Stage B |
|---|---:|
| Bumper-to-bumper length | 4.722 m |
| Body width / including mirrors | 1.940 / 2.255 m |
| Overall height | 1.821 m |
| Wheelbase | 2.950 m |
| Tire diameter | 0.842 m |
| Bed-side length | 1.945 m |
| Roof footprint | 1.644 × 0.720 m |
| Front tire-to-arch gap | 0.144 m |
| Rocker height | 0.505 m |
| Clear bed depth above central rib tops | 0.390 m |

All four wheel roots remain exactly at the approved authored coordinates, including
front axle Y=1.64 m, rear axle Y=−1.31 m and wheel-root Z=0.42 m. The measured body,
roof, bed length and overall dimensions retain the approved values within floating
point precision. No proportion-table drift exceeds 2%.

The accepted reference discrepancies remain: track/body width **+4.69%** versus
Photo 2's front estimate, and bed/overall length **−7.23%** versus Photo 2's plan.
The bed continues to follow Photo 3's side estimate (**−0.06%**). No changes were
made to resolve those conflicting estimates. The clear bed-depth comparison now
uses actual rib-top ray hits in both stages; the old A table used the lower base
sheet. This measurement correction does not represent a bed geometry change.

## Feature self-review

| Feature | Inspection and result |
|---|---|
| Silhouette and cab/bed relationship | Regular two-door cab and visibly separate longer open bed retained. Same side and top cameras show the approved envelope. |
| Hood and fenders | Continuous quad hood has subtle paired stamped steps, tapered ends, shoulder definition and seam grooves. Direct plan inspection and monotonic-width check show a clean nose without an hourglass waist or bulging grille. |
| Front end | Grille sits in a real fascia opening; slats sit about 45 mm behind the frame. Rectangular lamps have separate housings, reflector dishes and fluted lenses. Amber signals and stamped bumper vents remain separate materials. |
| Wheelbase and stance | Wheel roots unchanged; 144 mm front arch gap retained. Visible front coils, shocks, axles/differentials and flat rear leaf packs refine the raised 4×4 stance. |
| Wheels and tires | Dished steel wheels have ten actual ventilation holes and six exposed lug nuts. Tires use 48 pitches of four interlocking tread rows and shaped sidewalls. Four complete wheel meshes remain rigid-role candidates. |
| Cabin | Refined windshield/side/rear glazing, pillars, window seals, rounded roof edges, divider, mirrors, handles and parked wipers. Paint, seal and glass are connected surface regions without a hidden painted window skin. |
| Roof color | UV1 mask selects forest-green Color A on every roof loop. The diagnostic with paint specular disabled is green; pale areas in normal renders come from the large studio-light reflection. There is no cream roof material. |
| Two-tone body | Forest green above the rest-space Z=0.805 m cream boundary, continuous across doors and bed sides. Subtle body creases are displaced into the panels, without thin overlapping molding ribbons. |
| Rear and tailgate | Separate tailgate with stamped panel/handle recess, lamp stacks, rolled rear rail, bumper step and plate. Clean stock appearance retained. |
| Bed interior | Corrugated liner floor, inward-facing ribbed walls, integral wheel-tub surfaces, substantial rolled rails and tailgate liner. Cargo area remains open; its center ray reaches the liner at Z=0.920 m. |
| Physical separation | Cab/bed gap remains visible and clear above the chassis. The rear cabin bounds do not extend over the cargo bed. |

Corrections during self-review included reducing initially excessive hood ridges,
closing liner/tub edge gaps, aligning the roof/glass edge, parking the wipers,
exposing lug nuts, flattening the leaf packs, orienting liner/wheelhouse cavity
faces and welding the bumper stamp borders. The independent authoring review's
two material findings (cavity normals and bumper seams) were corrected and checked.

## Remaining visual differences

The lenses, tread blocks and small trim are interpretations in the game's clean
style rather than exact reproductions of the photographic detail. The grille has
simpler internal bracing. The current dark glazing does not reproduce the reference
images' transparent cabin and visible seats; no detailed cabin interior was added.
Small reference details such as the fuel-filler door, bed tie-downs and exposed
tailgate hinge hardware are omitted from this stock-style interpretation.
Studio reflections are broader and brighter than the reference lighting, notably
on the roof and hood. The roof diagnostic isolates its actual paint color.
The accepted track and plan-bed differences remain as described above.

## Authoring and modeling validation

The saved model contains **56 explicitly classified meshes**, **121,994 vertices**
in total, of which **88,098 are deformable** and 33,896 belong to the four rigid wheel
candidates. There are 52 deformable mesh components. The worst Body-role p90 edge
length is **0.050 m**, below the 0.060 m specification. Body meshes have no n-gons.
There are no zero-area faces or collapsed UV faces; all vehicle scales are applied.

**21/21 modeling diagnostics passed.** These include density, finite coordinates,
UV layers, preserved wheel origins, open cargo bed and cab gap, monotonic hood
plan, primary roof color, arch clearance, recessed grille, inward-facing cavity
surfaces, closed bumper shells and all socket seed/support normal checks.
These focused checks are not the later manifest-based production asset QA.

Every component has an exact-name proposal in [component_roles.json](component_roles.json):

| Component family | Proposed engine role |
|---|---|
| Bed sides, front wall, tailgate and other exterior shell | Body |
| Bed floor sheet metal and rolled bed rails | Panel |
| Bed liner, interior wheel tubs and nonstructural trim | Trim |
| Roof, pillars and glazing | Cabin |
| Four wheel roots | Wheel, rigid |
| Bumpers / grille / lamps / frame and suspension | Bumper / Fascia / Light / Underbody |

All parts have `UVMap` and `StripeProjection`. [palette.json](palette.json) and
[LowerBodyMask.png](LowerBodyMask.png) prepare the existing two-color shader's
UV1 mask path; the mask is also packed into the Blender source. Body colors remain
independently editable, while bumpers, grille, steel wheels, rubber and liner use
separate materials. Final Unity material translation and color customization still
need the approved integration stage. Blender lens/noise shader details do not
establish Unity material parity.

## Optional attachment metadata

[socket_authoring.json](socket_authoring.json) contains **20 bounded seeds in seven
groups**, with explicit support components, rest-space rays, expected normals,
rotation quaternions and target bounds. All 20 support/normal checks pass in the
saved Blender source ([results](socket_authoring_checks.json)).

- `roof.compact_mount`: four points within a conservative 1.30 × 0.50 m footprint.
  A full-size `roof.rack` group is omitted; existing racks have not been forced to fit.
- `hood.scoop`: four candidate points, 0.70 × 0.70 m footprint.
- `front.push_bar` and `rear.utility`: two points each.
- `bed.rail_l` and `bed.rail_r`: two points each, narrow rail supports.
- `bed.floor`: four points over sheet metal, avoiding the wheel tubs.

These are authoring candidates only, outside a production manifest, with
`runtime_ready: false`. They do not include baked Blender topology indices and
do not prove accessory fit, mounting strength or Unity attachment behavior.
No permanent accessories have been fitted.

## Open-bed collision limitation

The earlier code inspection of `Assets/DamageLab/Scripts/Collision/VehicleRig.cs`
still applies. Body and Cabin bounds are separate, so the shorter cab can retain
its own cabin protection bounds. The current rig creates a fixed core, twelve
adaptive shell boxes and a cabin box; its coarse solid body envelope fills the
visual cargo cavity, some lifted-underbody clearance and the cab/bed gap.

Cargo and downward impacts may therefore contact an invisible solid volume above
the liner floor. This is a code-based assessment, not a live Unity result. Separate
cab and bed meshes also do not provide independent chassis articulation. No shared
collision or destruction engine code was modified. Any engine change requires a
separate approval.

## Deferred checks and approval boundary

- Production manifest/configuration: **NOT WRITTEN**; role/socket files are authoring proposals.
- Manifest-based Blender asset acceptance QA: **NOT RUN**.
- FBX export, Unity import, live material/paint parity: **NOT RUN**.
- Unity destruction, phase4, overflow, random, switching and fold-outlier tests: **NOT RUN**.
- WebGL and runtime attachments: **NOT RUN**.

These phases follow B approval under addacar. The feature branch is published for
review only; it has not been merged. Approved A images remain protected and
available. Pending B evidence stays available, and approved checkpoint evidence
will be retained through completion and merge. Cleanup removes only unapproved drafts.

**Checkpoint B — wait for user approval before production configuration, QA or Unity integration.**
