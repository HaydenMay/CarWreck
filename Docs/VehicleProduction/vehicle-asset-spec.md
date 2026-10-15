# CarWreck vehicle asset specification

The technical standard for every new destructible vehicle. It is derived from the two vehicles that work
through the shared engine today — the blue **Muscle Car** (`Blender/Scripts/build_muscle_car.py`, regression
baseline) and the red **Compact Hatchback** (`Blender/Scripts/build_compact_hatchback.py`) — and from the defects
found while integrating the hatchback. The engine reference is `Assets/DamageLab/README.md` ("Vehicle engine").

**MUST** = mandatory (the vehicle is not accepted without it). **SHOULD** = recommended practice (deviations are
allowed when the report explains them). Checks are listed in [vehicle-qa-checklist.md](vehicle-qa-checklist.md).

A new vehicle needs **configuration and validation, not destruction code**. If a vehicle seems to need a change to
the shared engine (`Assets/DamageLab/Scripts`), stop and raise it; never weaken the shared damage settings to hide a
geometry problem.

---

## 1. Units, orientation, hierarchy

| | Requirement |
|---|---|
| MUST | Metres, unit scale 1.0, object scale applied (1, 1, 1) on every exported object. |
| MUST | Blender vehicle space: **+X right, +Y forward, +Z up**; the car faces +Y. The FBX export below converts this to Unity +Z forward, +X right with an identity root. |
| MUST | One root empty named after the vehicle (e.g. `CompactHatchback`), every component parented under it. |
| MUST | Four separate wheel roots `Wheel_FL`, `Wheel_FR`, `Wheel_RL`, `Wheel_RR`, origin at the wheel centre (within 3 cm), left wheels at −X. Wheel detail (spokes, tyre) is parented under its wheel. |
| MUST | Overall size of a car (the QA accepts 2.5–7 m long, 1.2–2.8 m wide, 0.8–3 m high). Any shape and size within that is fine: the engine measures each vehicle; nothing assumes muscle-car dimensions. |
| SHOULD | Ground at Z = 0 under the tyres (the rig computes ground lift from the wheels either way). |

## 2. Components, names and roles

Every mesh is a **component** classified by an explicit rule in the vehicle manifest (§7). Rules match object
names by **token**: the name equals the pattern, or continues with a non-letter. `Body` matches `Body`, `Body.001`,
`Body_L`; it does **not** match `Underbody` or `BodyKit`. The first matching rule wins; a second match is reported
as *ambiguous*.

| | Requirement |
|---|---|
| MUST | Every exported mesh matches exactly one rule. Unclassified meshes are a failure (the engine would deform them as generic sheet metal and warn). |
| MUST | No ambiguous names; no reliance on legacy substring (`Contains`) matching. |
| MUST | At least one `Body`, one `Cabin` and the four `Wheel` components. |
| MUST | Names are stable between generator runs (they are the keys of the component rules and of future attachment sockets). |
| SHOULD | One object per component family: `<Family>`, `<Family>.<n>`, `<Family>_<side>`. A different family gets a different leading token (`HeadlightHousing` is not `Headlight`). |
| SHOULD | No more than ~80 deformable components (hatchback: 72). Merge tiny trims that have no separate role. |

Roles and the resistance (`crumple`, how strongly folds and creases develop) used by both shipped vehicles:

| Role | Use for | Crumple | Notes |
|---|---|---|---|
| `Body` | Main shell: hood, doors, fenders, hatch/trunk | 1.0 | Defines the outer impact faces and the physics shell. |
| `Bumper` | Bumpers | 0.25 | Also defines the outer impact faces. |
| `Cabin` | Roof, pillars, glass | 0.85 | Defines the cabin collider; the stiffer cabin crush limit starts at the cabin line. |
| `Wheel` | Wheel assemblies | 0 | Mode `Rigid`: follows the crush, defines wheel-mount zones. |
| `Panel` | Other painted sheet metal (valances, sills, spoilers, scoops) | 1.0 | |
| `Fascia` | Grilles, intakes, splitters | 0.55 | |
| `Light` | Lamps and housings | 0.45 | |
| `Trim` | Mouldings, handles, mirrors, wipers, plates | 1.0 (exhaust 0.4) | |
| `Underbody` | Floor pan | 1.0 | Never matched by `Body` rules. |
| `Ignore` | Helpers that must not deform | — | Not exported normally. |

Use the role table as written. A component that folds badly is a **mesh** problem (§4), not a reason to invent a
lower crumple — except that a small rigid detail may use a lower crumple or `Rigid` when the report explains it.

## 3. Cabin protection and structure

| | Requirement |
|---|---|
| MUST | The damage profile's cabin line starts at the measured **beltline** (top of the Body/Bumper components) and ends about 7 cm below the roof. Leave `cabinLine` at 0 in the manifest to have it measured; an explicit value more than 12 cm from the beltline is a failure (the engine warns "cabin protection is misplaced"). |
| MUST | Cabin components (`Cabin` role) cover the greenhouse; the cabin collider is built from their bounds. |
| SHOULD | Leave the physics shell automatic: nose/tail sections 14 % of the body length, side shell 22 % of the width, impact height = body centre (≥ 0.6 m). Override in `shell` only for unusual crumple zones (the muscle car keeps its historical 0.7/0.7/0.45 m). |

The damage profile itself is the shared tuning (a copy of the muscle-car profile). Do not change crush depths,
limits or saturation per vehicle.

## 4. Deformation-ready geometry

The engine deforms vertices procedurally (directional crush, box-impactor crush with folds every ~14 cm, creases,
draw-in, saturation at 0.50 m panels / 0.36 m cabin / 0.20 m wheel mounts). It needs vertices where metal bends.

| | Requirement |
|---|---|
| MUST | Readable, manifold-ish meshes: no zero-area faces, no NaN, recalculated outward normals, a `UVMap` with no collapsed faces. |
| MUST | Deformable vertex budget ≤ 120,000 (SHOULD ≤ 90,000). Cost scales with it: muscle car 62k vertices ≈ 55 ms/hit, hatchback 80k ≈ 85 ms/hit in the editor. |
| MUST | Body shell as an even quad grid, **p90 edge ≤ 6 cm** (muscle ≈ 3–4 cm, hatchback ≈ 3.5 cm). Wheel wells folded into the same grid; no booleans or n-gons on the shell. |
| MUST NOT | Thin double-skin fascia or recessed panels whose front and back faces sit millimetres apart (hatchback `DoorSeam`: 67 % of vertices with the opposite face within 6 mm; folds 29 %). Give recesses real depth, or make them a single surface. |
| MUST NOT | Long, sparse trim: edges over 10 cm on deformable trim/fascia/light parts, or long sliver triangles (aspect > 12). They cannot carry a 14 cm fold and flip instead (hatchback `DoorSeam`, wipers, gutters; 188-triangle rounded-box primitives: mirror glass, grille and grille slats fold 34–49 % in single hits; lamps and indicators use the same primitive). |
| SHOULD | Small details (< 35 cm) built dense enough to bend (several hundred triangles), or classified `Rigid`/lower crumple with the reason in the report. |
| SHOULD | Layered parts (indicators on bumpers, seams on body) 2–5 mm proud of the surface they sit on, following it (the hatchback generator ray-casts them onto the body). |
| SHOULD | No part folds more than 5 % of its triangles under one standard 15 m/s wide hit, and the Body folds ≤ 20 triangles under a click dent (Unity check, §8). |

Known hatchback defects (not to repeat): `FrontValance` 17 %, `LicenseRecess` 17 %, `MirrorGlass` 34 %,
`SideIndicator` 18 %, `DoorSeam` 29 % of triangles folded by single hits; `Body` folds 142 / 57 / 83 triangles under
hood / door / hatch click dents (muscle car: 0). Reproduce with bridge command `hatch-defects`.

## 5. Materials, paint and stripes

| | Requirement |
|---|---|
| MUST | Paint and stripes stay attached to the deforming metal: they come from **mesh UV data**, never from world/object-space shading. Deformation moves vertices and keeps UVs (validated: "stripe/paint UVs unchanged by deformation"). |
| MUST | Two-colour paint uses the shared shader `CarWreck/CompactHatchback/Two Color Paint`: primary colour + stripe colour, stripe mask texture (linear, uncompressed, clamped) sampled from **UV1**. Export a second UV layer named `StripeProjection` on every painted part: a continuous car-space projection (hatchback: `u = 0.5 + x/2`, `v = 0.5 + y/4`), so stripes run across panel seams. |
| MUST | Material names are stable and listed in the generator's `palette.json` (name, linear colour, metallic, smoothness, emission; the paint entry adds `primary_srgb` / `secondary_srgb`). Unity materials are created from it once and then kept (user edits survive re-import). |
| SHOULD | URP/Lit for everything that is not paint; emission for lamps. No manufacturer marks. |
| — | The muscle car predates this: its stripes are a 1D texture on a top-down UV0 (`CarPaint_Stripes.png`). New vehicles use the two-colour paint path. |

## 6. Export and Unity import

| | Requirement |
|---|---|
| MUST | Generator script under `Blender/Scripts/build_<vehicle>.py`, deterministic, writing `Blender/<Vehicle>.blend`, `Assets/Vehicles/<Vehicle>/Models/<Vehicle>.fbx`, `Textures/`, `palette.json`, `vehicle.json` and preview renders under `Previews/<Vehicle>/`. Blender 5.2 CLI (`-b --factory-startup`). |
| MUST | FBX export: `object_types={'EMPTY','MESH'}`, `apply_unit_scale=True`, `apply_scale_options='FBX_SCALE_ALL'`, `bake_space_transform=True`, `axis_forward='-Z'`, `axis_up='Y'`, `mesh_smooth_type='FACE'`, `use_mesh_modifiers=True`, `add_leaf_bones=False`, `embed_textures=False` (the settings both vehicles use). Apply modifiers and convert curves before export. |
| MUST | Unity import (applied by `vehicle-import`): readable, mesh compression off, vertex/polygon optimisation off, weld on, normals imported, tangents MikkTSpace, scale 1 with file scale, axis conversion baked, materials remapped from `palette.json`. |
| MUST | Binaries (`.blend`, `.fbx`, `.png`) go through Git LFS (existing rules). |

## 7. Vehicle manifest (`vehicle.json`)

Written by the generator next to the FBX (`Assets/Vehicles/<Vehicle>/vehicle.json`); turned into a
`VehicleDefinition` by bridge command `vehicle-import <manifest>` (`dry` = check only). Reference manifests:
[examples/compact_hatchback.vehicle.json](examples/compact_hatchback.vehicle.json) (verified identical to the
hand-written hatchback configuration by `vehicle-import-check`) and
[examples/muscle_car.vehicle.json](examples/muscle_car.vehicle.json).

| Field | Meaning |
|---|---|
| `format` | `carwreck.vehicle.v1` |
| `id`, `revision` | Stable dotted vehicle ID (`carwreck.<name>`) and integer revision (bump on topology changes). |
| `displayName` | Selector / diagnostics name. |
| `model`, `palette` | Project paths of the FBX and `palette.json`. |
| `paint` | `material` (the palette entry that uses the two-colour paint) and `stripeMask` (texture path). |
| `massKg` | 300–6000 kg. |
| `components` | Ordered rules: `pattern`, `match` (`Token`/`Exact`), `role`, `mode` (`Deform`/`Rigid`/`Ignore`), `crumple`, and optional `coherentTubeSections` (default `false`). Set it on thin tubes (coil springs, rods, pipes): each cross-section ring detected from the mesh then moves as one unit, so the tube bends and crushes along its length but its round section doesn't turn inside out. The part stays deformable. Strength and capacity limits are unchanged. |
| `unknownComponentCrumple`, `maxImpactRecords` | Defaults 1 and 24. |
| `cabinLine` | `start`/`end` in metres, 0 = measured (recommended). |
| `shell` | `frontDepth`, `rearDepth`, `sideDepth`, `impactHeight`, 0 = automatic (recommended). |
| `expected` | Written by the generator, Blender vehicle space: `dimensionsM` `[x, y, z]` of all non-ignored meshes and `wheels` (`name`, `position` of each `Wheel_*` root). `vehicle-import` checks the **imported** model against them (Blender x, y, z = Unity x, z, y; 3 cm size / 2 cm wheel tolerance), which proves scale and orientation through the real pipeline. |
| `sockets` | Optional attachment socket metadata (§9): `coordinates`, `runtime_ready: false`, `groups[]` of `socket_group`, `mount_mode`, `mesh_role`, `sockets[]`. Checked for semantic names, valid modes, known mesh roles and unique IDs. |

## 8. Validation the vehicle must pass

Listed in full in the QA checklist. Summary: Blender asset QA (`vehicle_asset_qa.py`, no mandatory failures),
`vehicle-import` with 0 unclassified / 0 ambiguous components and 0 warnings, and in Unity: `vehicle <i>`,
`collision-phase4 <i>`, `collision-overflow <i>`, `collision-random <i>`, `vehicle-switch`, with the part-fold
outlier report reviewed. Report asset QA and destruction testing separately; never claim destruction results
from asset QA.

## 9. Attachment sockets (metadata only)

Future accessories (roof racks, hood scoops, push bars) mount through **named sockets**. Sol's prototype
(`feature/attachment-prototype` @ `9351847`, not merged) showed this works with explicit per-vehicle adapters and
fails visibly when a supporting panel folds too far. Until the Unity runtime contract exists, vehicles only **emit
metadata**; nothing in Unity consumes it.

| | Requirement |
|---|---|
| MUST (if sockets are provided) | Stable semantic identity `(vehicle_id, socket_group, socket_id)`, e.g. `(carwreck.compact_hatchback, roof.rack, R1)`. IDs never encode FBX child indices. |
| MUST | Coordinates in Blender vehicle-local metres (+X right, +Y forward, +Z up); Unity points map as `(x, z, y)`. Quaternions `xyzw`. |
| MUST | Each socket names its supporting `mesh_role` component, a bounded rest-space seed region, an expected normal and a `mount_mode`: `chassis_rigid`, `panel_follow` or `multi_point_rigid`. |
| MUST | Optional groups (`roof.rack`, `hood.scoop`, `front.push_bar`, …) are **omitted** when unsupported. A missing group means "not supported", never a guessed location. |
| MUST NOT | Store Blender triangle/vertex indices as runtime bindings: Unity rebinds against the imported mesh. Mark `runtime_ready: false`. |
| — | Blender-side checks (seeds hit the named mesh, normals agree) prove the metadata is consistent. They do **not** prove live Unity attachment behaviour, mount strength or detachment. Diagnostic thresholds from the prototype (25 / 60 mm, 25 % spacing, 55°) are authoring aids, not calibrated limits. |

Socket record (inside `sockets.groups[]`):

```json
{ "socket_group": "roof.rack", "mount_mode": "chassis_rigid", "mesh_role": "Cabin",
  "sockets": [ { "id": "R1", "origin": [-0.53, -0.8, 2.0], "direction": [0, 0, -1],
                 "expected_normal": [0, 0, 1], "bounds": [[-0.65, -1.1, 1.34], [0.65, 0.15, 1.6]], "max_distance": 2.0 } ] }
```
