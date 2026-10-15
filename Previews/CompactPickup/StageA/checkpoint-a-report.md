# Compact Pickup — Checkpoint A

**Initial blockout; approved by the user.** Approved revision: `f230c5a`; image/source
hashes: `../approved_checkpoint_a.json`. Retain all approved images. Branch: `feature/vehicle-compact-pickup`,
based on main at `f6cf3b3`. No merge. Source: `Blender/CompactPickup_StageA.blend`.

## Renders and reference evidence

![Front three-quarter, side, rear three-quarter and true top comparisons](checkpoint_a_overview.png)

[Full-size front three-quarter](stage_a_front34.png) · [side](stage_a_side.png) ·
[rear three-quarter](stage_a_rear34.png) · [true top](stage_a_top.png)

![Hood/grille, bed, suspension clearance and front comparisons](checkpoint_a_details.png)

[Nose inspection](stage_a_nose_top.png) · [bed interior](stage_a_bed.png) ·
[low clearance view](stage_a_clearance.png) · [front](stage_a_front.png) · [rear](stage_a_rear.png)

Reference crops retain their original proportions. Comparisons fit both vehicles
into equal display areas without stretching. The three-quarter cameras use
perspective projection and approximate the supplied angles; exact lenses are
unknown. Side/front/rear use near-level orthographic cameras; top is genuinely
vertical. Lighting and materials differ from the supplied illustrations.

All three complete sheets and twenty panel crops are catalogued in
`../References/reference_catalog.json`. Primary ratios use Photo 3's side;
Photo 2 controls plan/front geometry; Photo 1 controls clearance and bed details.

## Dimensions measured from the saved mesh

| Measurement | Stage A |
|---|---:|
| Overall length, including bumpers/plates | 4.722 m |
| Painted body width | 1.940 m |
| Width including mirrors | 2.255 m |
| Height above lowest tire vertex | 1.821 m |
| Wheelbase | 2.950 m |
| Tire diameter including tread | 0.842 m |
| Bed side length | 1.945 m |
| Bed depth, liner floor to rail | 0.400 m |
| Roof footprint | 1.644 × 0.720 m |
| Front tire-to-arch air gap | 0.144 m |
| Rocker height | 0.505 m |
| Lowest differential surface | approximately 0.300 m |
| Meshes / total vertices | 85 / 20,056 |

Absolute scale is a design assumption, since the sheets provide no dimensions.
Model ratios are calculated from vertex bounds, not estimated from renders.
See [full ratio table and pixel evidence](proportion_comparison.md) and
`geometry_measurements.json` for coordinates and per-component role proposals.

The wheelbase/length differs from the primary side estimate by −0.31%, tire
diameter/length by −2.23%, roof length/length by −3.97%, and bed side/length by
−0.06%. Reference measurements are manual estimates with stated uncertainties.

**Flag:** the model's bed/length ratio is 0.4119, versus 0.4440 estimated from
Photo 2's plan view: **−7.23%**. The side and top sheets disagree. Stage A follows
Photo 3's side profile rather than silently averaging the two. This remains a
creative choice for the checkpoint review. **Also flagged:** wheel track is approximately 4.7%
broader relative to body width than the Photo 2 front estimate (uncertainty ~4%);
the tires remain somewhat more exposed than in that reference.

## Visual self-review and remaining differences

| Feature | Review |
|---|---|
| Silhouette | Two-door regular cab, long open bed, separate cab/bed gap, stock silhouette. Primary side ratios are recorded above. |
| Overall proportions | Side/top reviewed together. Long bed remains clearly longer than the greenhouse; Photo 2 bed-length conflict remains flagged. |
| Hood and cabin | Short roof and raked windshield; hood has a shallow convex crown and pressed shoulder hints. Roof is approximately 4% shorter than the primary pixel estimate. Glass/seals and A-pillars are still broad blockout forms. |
| Wheelbase and wheels | Wheel centers and overhangs measured; raised wheel openings have 14.4 cm front tire clearance. Tire diameter is 2.2% below the primary estimate. Tread and steel-wheel holes remain placeholders, not production detail. |
| Front / grille | Narrowed the first grille, moved lamps inward and made them more rectangular after comparison. Grille is shallowly integrated into the nose, not a projecting separate block. Bumper recesses and optical lamp internals are deferred. |
| Rear | Closed tailgate, narrow cream strip, separated dark bumper and vertical lamps. Rear bumper is a simple volume; stock step recess and small seams are deferred. |
| Fenders and curvature | True top view and measured hood widths show a gradual 1.940→1.870 m taper toward the nose, with no hourglass waist. Removed side-panel highlight ripples seen in the first pass. Fender lips and stamped panel contours remain simplified. |
| Suspension clearance | Separate frame/axles/differentials/shock placeholders. Ground clearance is visibly higher than the existing low cars. Physical suspension and axle articulation have not been implemented or tested. |
| Open bed | Distinct sides/floor/tailgate, black liner, longitudinal floor ribs and two raised wheel tubs. Floor-to-rail depth 40 cm. Tubs are deliberately broad simple masses; liner side/front ribs, seams and stamped details remain for B. |
| Color treatment | One paint material with independently editable green/cream colors; rest-space UV1 lower band continues across doors and bed sides. Bumpers, grille, steel wheels and liner use separate materials. Studio illumination affects apparent shade. |
| Stock appearance | No permanent roof/hood/bed accessories and no logos. Small ordinary trim is simplified. |

## Role and paint preparation

Each vehicle mesh has a stable name and an explicit `engine_role_proposal` custom
property, also enumerated in the geometry report. These are blockout proposals,
not a production manifest or a claim of Unity classification validation.

| Component family | Proposed existing engine role | Reason |
|---|---|---|
| `Body_Hood`, `Body_Nose`, `FrontFender`, `Door` | Body | Main impact-bearing exterior panels. |
| `BedSide`, `Tailgate`, `BedFrontWall` | Body | Bed exterior must contribute to the vehicle's outer body bounds. |
| `BedFloor` | Panel | Separate floor sheet metal. |
| `BedRail` | Panel | Separate painted rail/cap sheet metal. |
| `BedLiner` including interior wheel tubs/ribs | Trim | Separate non-structural liner; does not enlarge the cabin or outer physics shell. Production mesh density and deformation behavior still need validation. |
| `Cabin` / glazing | Cabin | Greenhouse only, excluding the cargo bed. |
| `Wheel_FL/FR/RL/RR` | Wheel | Four independent meshes with wheel-center origins; later rigid wheel configuration. |
| `Bumper`, `Grille`, lamps, small trim, underside | Bumper, Fascia, Light, Trim, Underbody | Existing roles, no generic fallback intended. |

Stage A's Blender paint uses the `StripeProjection` UV layer to locate a horizontal
cream band at rest-space Z=0.805 m. Both colors are editable in the paint node.
The existing Unity shader samples an arbitrary UV1 mask and mixes its two colors,
so a lower-panel mask can use the same shader without racing stripes. Baking the
mask, exporting the palette and proving paint behavior in Unity are later work.

## Potential attachment areas — planning only

No accessories have been fitted and no runtime socket claims are made. Final
semantic IDs, bounded seed regions and normals should be emitted only after the
approved geometry is stable, with `runtime_ready: false`.

| Area | Candidate support and constraints |
|---|---|
| Cabin roof | `Cabin_Roof`, 1.644 × 0.720 m overall. A conservative interior footprint is roughly 1.30 × 0.50 m; a longer existing rack must be omitted. Multi-point mount feasibility needs accessory dimensions. |
| Hood | `Body_Hood`, 1.270 m longitudinal extent, approximately 1.87–1.94 m wide. Small panel-follow groups are plausible; stay away from front edges and shoulder ridges. |
| Front bumper | `Bumper_Front`, narrow 0.180 m depth. Front-facing mounting candidates only after clearance checks; do not assume existing push-bar brackets fit. |
| Rear bumper | `Bumper_Rear`, 0.164 m depth. Rear-facing mount candidates, with plate/step and tailgate access preserved. |
| Bed rails | `BedRail_L/R`, approximately 0.105 m wide. Only narrow clamp/support areas; no wide feet over the open bed. |
| Bed floor | `BedFloor` / liner surface, approximately 1.68 × 1.86 m interior. Central mounts must avoid wheel-tub volumes; usable corridor between tubs is about 1.18 m. |

These are authoring observations, not validated socket metadata, attachment
strength, runtime bindings or a guarantee that any accessory fits.

## Collision-system assessment — code inspection only

`Assets/DamageLab/Scripts/Collision/VehicleRig.cs` measures Body/Bumper bounds and
Cabin bounds separately. It can therefore size the short greenhouse without
extending the cabin box across the bed, provided the roles above are used.

The same code constructs one fixed core plus twelve adaptive shell boxes over the
entire body envelope, from the tire minimum height up to the body beltline. It
fills the bed cavity, space beneath the lifted chassis, and the small cab/bed gap
with coarse solid collision volume. The open bed's visual geometry is supported;
an actually hollow collision bed and authentic underbody/axle clearance are not
represented by this rig. Cargo or a top-down impact can therefore contact an
invisible solid envelope instead of the liner floor. This is an inference from
the current code and must be checked in Unity later.

The truck remains a single rigid vehicle; separate cab and bed meshes do not add
independent chassis articulation. Wheel mounting and cabin-protection behavior
need the standard live tests, especially for the raised stance. No shared
destruction code has been changed or weakened.

## Validation status

Final verification: **12/12 Stage A spatial checks passed**, recorded in
`blockout_checks.json`. The saved scene has finite geometry, explicit proposed
roles and UV layers, four correctly centered wheel meshes, an open bed raycast,
a cab/bed gap, a monotonic hood plan, grounded tires, adequate front arch clearance
and all nine inspection renders. All three Python scripts compile. These checks
do not replace the production QA checklist.

- Production Blender asset QA: **NOT RUN** — Stage A surfaces are intentionally coarse.
- FBX export / Unity import / production manifest: **NOT RUN / NOT WRITTEN**.
- Unity destruction tests, WebGL and live attachment behavior: **NOT RUN**.
- Some production components must be consolidated and remeshed at B; the current
  85 meshes and coarse trims are not presented as production-ready.

Corrections already made during self-review: side highlight rippling, raised
wheel-hole placeholders, over-wide grille, narrow/tall lamps, excessive camera
elevation and unequal reference display scaling. The remaining differences are
listed above rather than presented as an exact match.

**Checkpoint A approved.** Proceed to B while preserving the wheelbase, wheel positions,
cab/bed relationship, raised stance and two-tone layout. The bed-length and track differences
were explicitly accepted. No Unity integration until Checkpoint B approval.
