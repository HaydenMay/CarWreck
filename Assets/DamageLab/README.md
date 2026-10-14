# Vehicle Damage Lab

A focused prototype: a stylized muscle car built in Blender, imported into Unity 6 URP, whose body is crushed live by **Front / Rear / Left / Right** damage sliders. The sliders move real mesh vertices, and setting every slider to 0 restores the original mesh exactly.

## Running it

1. Open `Assets/DamageLab/Scenes/VehicleDamageLab.unity` and press Play.
2. Drag the sliders or type a value into a number box. **Reset All** clears every direction. **Wireframe** overlays the live deformed topology.
3. Camera: drag with LMB or RMB to orbit, drag with MMB or Shift + LMB to pan, and scroll to zoom. **F** or **Reset View** resets the camera.
4. The ◀ ▶ bar rotates the car model itself. A click turns it 45°; holding a button spins it.

The **Damage Lab** menu has these commands:
* **Build Lab Scene** regenerates the scene, materials, UI sprites and studio meshes.
* **Capture Comparison Screenshots** writes images to `<project>/Screenshots`.
* **Validate Reversibility** checks that the mesh restores exactly and doesn't drift.

You can also build headless:
`Unity.exe -batchmode -projectPath . -executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "build;verify;capture-set" -quit`

## Pipeline

| Step | Where |
|---|---|
| Procedural car (Blender Python) | `Blender/Scripts/build_muscle_car.py` |
| Blender source | `Blender/MuscleCar.blend` |
| Exported model | `Assets/DamageLab/Art/Models/MuscleCar.fbx` (+ `Art/Textures/CarPaint_Stripes.png`) |
| Import settings | `Editor/MuscleCarModelPostprocessor.cs`: read/write enabled, no mesh compression, imported normals, materials remapped to the lab's URP materials |

To regenerate the car, run `blender -b --factory-startup --python Blender/Scripts/build_muscle_car.py -- <project root>`. The body is a loft of about 190 cross-sections × 158 points, which gives an even quad grid of roughly 3–4 cm. Wheel wells are part of that same grid, so there are no boolean seams. Details are dense rounded boxes, sweeps and lathes, so they bend along with the body. The racing stripes come from a 1D texture on a top-down planar UV, so they stay attached to the metal as it deforms.

## Code layout

| Concern | Files |
|---|---|
| Deformation maths | `Scripts/Deformation/CrushField.cs`, `DeterministicNoise.cs` |
| Damage region definitions | `DamageRegion.cs`, `VehicleDamageProfile.cs` (asset: `Settings/MuscleCarDamageProfile.asset`) |
| Mesh handling | `DeformablePart.cs` (runtime mesh copy, cached originals, normals), `WireframeUtility.cs` |
| Vehicle setup | `Scripts/Vehicle/VehicleDamageModel.cs`, `VehiclePartRule.cs` (per-part stiffness, rigid wheels) |
| Inspection | `Scripts/Inspection/InspectionCameraController.cs`, `ModelTurntable.cs`, `HoldButton.cs` |
| UI | `Scripts/UI/DamageLabUI.cs`, `DamageSliderControl.cs`, `ViewportCenter.cs` |
| Editor tooling | `Editor/DamageLabSceneBuilder.cs`, `DamageLabUIBuilder.cs`, `DamageLabAssets.cs`, `DamageLabCapture.cs`, `DamageLabValidation.cs`, `DamageLabBridge.cs` |

## Deformation method

The system is a hybrid: a region-based crush with falloff, plus procedural buckling. It's computed per vertex in vehicle space and always from the original mesh.

For each direction the system sets up an impact frame: the outer face plane, an inward axis, and the face's tangent (u) and up (v) axes. For each vertex it records its depth `s` behind that face and its footprint position `(u, v)`.

1. **Footprint.** `P = base + (1 − base)·exp(−|du|^2k − |dv|^m)`. The radii grow with damage, so the dent spreads as the slider goes up. On the sides the vertical term is pointed (m ≈ 1.15), which folds the doors into a sharp horizontal crease instead of a round dimple. Low-frequency seeded noise makes the dent outline irregular.
2. **Crush.** The face is pushed in by `δ = D·P`. Material deeper inside moves by `δ·w(s)`, where `w = 1 − smoothstep(s/L)`. The crumple-zone length is `L ≥ 1.5·D`, which keeps `s → s + δ·w(s)` monotonic, so the crush can't push the mesh through itself along the impact axis.
3. **Folds.** Where the zone is compressed beyond an elastic limit, panels lying along the impact axis buckle. The fold amplitude is `λ·√strain/π`, which conserves panel arc length. The shape is mostly a triangle wave, giving flat facets and sharp crests, and ridge height varies along each ridge. This is what produces accordion hoods and trunks rather than a squeezed-balloon look. Surfaces facing the impactor, like the grille, are pushed back rather than rippled.
4. **Creases.** Domain-warped ridged noise, stretched along the face, is concentrated on the dent rim (`4g(1−g)`), giving thin crease lines. A weaker wrinkle term roughens the dent floor.
5. **Structure.** Surrounding metal is drawn toward the dent, and the roof sags a little near heavy hits. Wheels are rigid: they follow the crush measured at their leading edge, and pick up camber and toe.

All noise is seeded and sampled at the *original* vertex positions once, at start-up. At runtime a slider value only scales those precomputed constants, so the result is deterministic and doesn't depend on the order or path the sliders took. Directions are summed, so corners blend naturally (front + left crushes the front-left corner from both sides).

On the mesh side, each part gets a runtime `Mesh` copy, and the imported asset is never written. Vertices that are coincident in the import are displaced using a shared averaged normal, so seams can't tear apart. Shading normals are recomputed from the triangles (hard edges above 60° are preserved). They're applied as a *delta* on top of the imported normals, so the first slider step doesn't pop the shading. When every direction is at 0, the original vertex and normal arrays are copied back verbatim. The per-vertex loop runs in parallel chunks, and arrays are preallocated, so moving a slider produces no per-frame garbage beyond `Parallel.For`'s own small overhead. Tangents aren't recomputed because no material uses a normal map.

`Damage Lab ▸ Validate Reversibility` gives these results on this car (about 62k deformable vertices):
* After 200 random combined states, resetting to 0 leaves a max vertex error of **0** and a max normal error of **0**.
* Left 20 set directly matches Left 20 reached via 60 → 20 **exactly**. Front 80 + Left 60 reached by two different paths also matches **exactly**.
* A full rebuild with all four directions active takes about 15–25 ms in the editor.

## Click-to-damage (localized impacts)

The **Impact Testing** card adds three controls: an *Enable Click-to-Damage* toggle, an *Impact Strength* slider (0–100), and *Clear Impacts*. It also shows an *Active Impacts n / 10* counter.

* **Input.** `ImpactPicker` lives on the camera. A left click, or a one-finger tap, counts as an impact only when all of these hold:
  * it started off the UI;
  * it moved less than about 10 px (scaled with screen height);
  * it lasted under 0.6 s;
  * it never became multi-touch.

  Orbit, pan, and pinch therefore never apply damage, and the camera controller is unchanged.
* **Picking.** `VehicleDamageModel.TryApplyImpact(ray, strength)`:
  * casts a `Physics.Raycast` against `MeshCollider`s built once from the original meshes;
  * stores the hit point, the ray direction, and the surface normal in vehicle space, so impacts rotate with the turntable;
  * ignores misses, strength 0, and any click after the 10th.
* **Deformation.** `ImpactField` is the `CrushField` idea in a frame built from the ray. Depth `a` runs along the ray, and footprint distance `l` runs across it:
  * **Push:** `D·g(l)·w(a)`. `g` is a super-gaussian with a noisy outline. `w = 1 − smoothstep(a/L)` with `L ≥ 1.5·D`, so the dent stays monotonic along the push.
  * **Folds:** radial triangle-wave buckles on the dent wall and ridged creases on the rim.
  * **Lip:** a small outward roll just outside the dent.
  * **Draw-in:** pulls material toward the centre.
  * **Grazing hits** are made shallower (×0.45 when the ray is parallel to the surface).
* **Size.** Depth reaches 0.42 m and radius 0.16 → 0.60 m between strength 0 and 100.
* **Combination.**
  * Each part caches the summed raw displacement of all impacts. A click evaluates only the new impact.
  * The result is added to the four directional fields.
  * Everything is still evaluated on the original vertices, so Clear Impacts and Reset All restore the mesh exactly.
* **Saturation and structure.** Repeated hits get diminishing returns rather than a hard stop:
  * **Structural limit per vertex** (`ImpactField.Capacity`):
    * outer panels 0.50 m;
    * passenger compartment above the roof line 0.36 m;
    * within 0.5–0.95 m of a wheel centre, it fades down to 0.20 m, so fenders crumple around the wheel without leaving it behind;
    * stiff parts get less (×0.6–1 by crumple).

    The summed impact displacement is linear up to 60% of the limit and then eases toward it (tanh).
  * **Work hardening.** A new hit is scaled by `1 / (1 + 0.6 · earlier damage at that spot)`.
  * **Spreading.** The footprint widens by up to +22% on damaged metal, and its outward lip shrinks. Repeated hits widen the crushed area instead of drilling ever deeper.
  * **Order now matters.** The same clicks in a different order give a slightly different result (a few mm). The same sequence is always identical.
* **Validation:** *Damage Lab ▸* bridge commands `verify-impacts` and `verify-stress` (1/3/5 × strength 100 per location). Screenshots come from `impact-set` (`Screenshots/11_…`–`17_…`) and `stress-set` (`21_…`–`28_…`).

Click-to-damage limitations:

* **Picking uses the original shape.** After heavy damage, a click lands on where the undamaged surface was. The point can sit up to the dent depth in front of the visible metal, and a ray can hit a panel that has since been pushed out of view.
* **Wheels.** Wheels are pickable, and a hit there dents the surrounding bodywork. The wheels themselves don't move from impacts; they only move from the sliders. A deep corner hit can push the fender into the tyre.
* **The direction is the camera ray, by design.** A grazing click slides the metal sideways. This is reduced, but not removed, by the grazing factor.
* **Repeated extreme stacking.** Five strength-100 hits on one spot still fold some small triangles in the crumple (0.1–1.7% of the mesh, worst on the angled front corner). That's roughly half the count before saturation.
* **The structure map is spatial.** It uses the roof line and the wheel centres, not real panels or a chassis. The four directional sliders are unchanged and are not saturated against impacts.

## Collision Lab (physics-driven damage, phase 1)

The `Scenes/VehicleCollisionLab.unity` scene is built by *Damage Lab ▸ Build Collision Lab Scene*. An orange test sled, a 1.9 × 1.1 × 3.8 m Rigidbody, is launched into the car with a real velocity. PhysX resolves the collision, and the contact creates the damage. The Damage Lab scene is unchanged.

* **Car physics.**
  * The car root gets a Rigidbody: 1400 kg, centre of mass lowered, continuous dynamic collision detection.
  * Its physics shape is two boxes, body-to-tyres and cabin, with a tyre-like friction material.
  * `VehicleDamageModel.createPickingColliders = false`, because concave MeshColliders can't be part of a dynamic Rigidbody. The Damage Lab keeps its picking colliders.
* **Sled.** The sled has continuous dynamic collision detection and no gravity. Its height and its tilt are frozen. It's parked kinematic and launched by setting `linearVelocity` once; after that, only physics moves it.
* **Severity** (`CollisionDamageReceiver`):
  * All contacts with one other Rigidbody in one physics step merge into one event. The ground and other static colliders are ignored, and a 0.5 s cooldown per body blocks duplicates from resting contact and bounces.
  * The contact normal `n` is averaged and oriented into the car. `v` is `Collision.relativeVelocity`, and `vn = v·n` is the closing speed.
  * The reduced mass is `μ = m_car·m_sled / (m_car + m_sled)`. The normal-approach energy is `E = ½ μ vn²`.
  * Strength is `1 − exp(−(E − 1.5 kJ) / 60 kJ)`, and 0 below 1.5 kJ. Speed counts quadratically. Mass counts, but saturates toward the car's own mass. Scraping only counts through `vn`.
* **Into the existing damage.**
  * The push direction is `n·vn + 0.5·vt`.
  * The contact point sits on a box collider, so `VehicleDamageModel.SurfacePointAlong` moves it to the first original car vertex along that direction.
  * The result goes to `VehicleDamageModel.AddImpact(LocalImpact)` with the existing impact maths and saturation.
* **Reset** (phase 1; split into Reset Block / Repair Car / Reset All in phase 3) restores the car's pose and zeroes its velocities, re-parks the sled, clears cooldowns, and calls `ResetAll()`, which restores the mesh exactly.
* **Validation:** bridge command `collision-verify` runs the scenarios with `Physics.Simulate` and writes the `Screenshots/3x_…` files. WebGL: `collision-webgl` builds to `Builds/WebGLCollision`.

Collision Lab limitations:

* **The colliders don't deform.** The car collides as two fixed boxes. A crushed corner still collides where the undamaged box corner was, the sled stops at the box surface rather than inside the dent, and box edges don't match the rounded bodywork.
* **No suspension or wheels in the physics.** The car slides on its body box with tyre-like friction. Hard hits shove it many metres, because there's no rolling resistance model, no tyre grip direction and no structure absorbing energy.
* **One impact per event.** (See phase 2 for the impact shape.) A long push, such as the sled continuing to shove the car, adds no further damage after the first contact. A second strike needs a separate contact more than 0.5 s later.
* **The severity mapping is tuned by eye** for a 1400 kg car (the energy threshold and scale). It isn't crash-test calibrated.

## Collision Lab phase 2: geometry-aware impact footprints

**The problem.** Phase 1 averaged the contact points into a single point and fed it to the round localized dent. A 1.9 m flat sled hitting the nose squarely left a narrow central V. The impactor's size and shape never reached the deformation.

**Footprint** (`ImpactField.ResolveFootprint`, `Deformation/ImpactFootprint.cs`):

* The receiver records the other body's `BoxCollider` at its contact pose: centre, rotation, and size × scale. It passes this as `LocalImpact.impactor`, in vehicle space. Contact points are still used only for the normal, severity and direction. They're solver data and don't describe the patch.
* `BoxProjection` views the box along the impact direction. It gives:
  * a lateral frame;
  * the box's silhouette, a convex hull of the projected corners;
  * its *leading surface*: for each lateral position, how far along the direction the box reaches, from a slab ray test. A face-on box leads with a flat face, an angled one with an edge or corner.
* The footprint is resolved against the *original* car vertices:
  1. **First contact** is the vertex with the smallest gap to the leading surface.
  2. **The patch** is every vertex within the crush depth of that first contact. In other words, it's the overlap the impactor would sweep through.
  3. **The patch area** is the convex hull of the patch. The crush depth is scaled by `(0.5 m² / area)^0.35`, clamped to 0.6–1.6. The same energy over a small area crushes deeper, and over a large area shallower. Total energy and strength are not changed by area.
  4. **The stored result** is the area, a PCA-oriented rectangle (centre, axes, width × height), the outline, the crush depth, and the kind: face, edge or corner, by how many box corners lie within the crush depth.
* **Determinism.** The footprint is resolved once, when the impact is added. It's stored with the impact, so rebuilds and reset stay deterministic.

**Deformation** (`ImpactField.OneBox`). This is a second contribution type beside the radial dent, summed into the same per-vertex cache and saturated by the same structural limits:

* **Crush.** The leading surface advances past first contact by the crush depth. Bodywork it passes through is pushed back to that surface, keeping 15 % of its spacing as bunched metal. A face gives a broad flat crush, an edge a crease, a corner a pocket.
* **Uneven face.** Soft waves and vertical pleats, about 0.24 m apart, so the crushed wall isn't perfectly flat.
* **Accordion folds** on panels running along the crush direction.
* **Tenting.** The hood, fenders and door skin bow outward just behind the crushed zone.
* **Edges.** Ridged creases along the patch boundary. Metal just outside the impactor's silhouette is dragged along with a short falloff and pulled in toward the patch.
* **No folding through.** The noise that varies the crush depth uses lateral coordinates only, and out-of-plane offsets follow a smooth field away from the crush axis rather than each vertex's normal. Metal therefore never swaps order along the crush, and thin double-skinned parts (grille slats) don't fold through themselves.

**Other changes:**

* **Closing-speed sign.** `Collision.relativeVelocity` is used as the other body's velocity relative to the car. The phase 1 code flipped it whenever it pointed away. A separating contact is now `vn = 0` and no impact (`CollisionDamageReceiver.ClosingSpeed`). The validation confirms that every real approach had `v·n > 0`.
* **Click-to-damage** impacts carry no impactor, so they use the unchanged radial dent.
* **Debug view.** `ImpactFootprintDebug` draws runtime lines and editor gizmos: the patch outline on the crushed surface (cyan), the patch axes, and the incoming direction (yellow). Toggle it with *Show Contact Footprint*.
* **UI.** The *Impactor* buttons switch between the 1.9 m sled and a 0.5 m block of the same length, height and mass.

**Validation** (`collision-geometry`: real PhysX contacts, `Physics.Simulate`; screenshots in `Screenshots/4x_…`):

* **Wide vs narrow:** same location, 1500 kg at 12 m/s, both strength 57.

  | | Wide sled | Narrow block |
  |---|---|---|
  | Footprint | 1.88 × 0.49 m, 0.85 m² | 0.55 × 0.52 m, 0.26 m² |
  | Crush depth | 0.18 m | 0.28 m |
  | Damaged region (moved > 3 cm) | 1.86 m wide | 0.74 m wide |

* **No more central V.** On the wide hit, the crush 0.45–0.75 m either side of centre is 1.04 × the centre crush; the phase 1 point impact gave 0.11 ×.
* **Corner:** 0.87 × 0.53 m (0.40 m²), asymmetric.
* **Glancing scrape at 15 m/s:** closing 3.9 m/s, 2 cm, versus 15 cm for a direct door hit at 12 m/s.
* **Speed and mass still matter:** speed 6 / 12 / 20 m/s gives a crush of 5 / 18 / 30 cm. Mass 500 / 1500 / 3000 kg gives 11 / 18 / 22 cm.
* **Accumulation and saturation:** two hits accumulate. Four heavy hits saturate (0.28 → 0.40 → 0.47 → 0.49 m).
* **Reset and repeatability:** reset is exact, and the same launch repeated matches within 3 µm.
* **Folded triangles:** about 0.1–0.4 % of triangles for single hits (narrow block highest, mostly grille slats at the pocket edge), 1.7 % after four stacked heavy hits.

Phase 2 limitations:

* **Box impactors only.** Other collider types fall back to the phase 1 radial dent.
* **Pose at first contact.** The impactor pose is taken when the contact is reported. The sled's motion after that, and any rotation during the crush, are ignored.
* **Simplified car colliders.** The car still collides as two boxes. First contact is measured against the real mesh, but the physical stop happens at the box. For a rounded nose, the visual patch can differ by a few centimetres from where the bodies would really meet. A second hit on crushed metal measures the gap from the *original* surface, so it starts at the old contact plane rather than the dented one.
* **No energy accounting.** Crush depth comes from collision energy through the impact strength, scaled by contact area. It isn't an energy-absorbing structural model, and the car body doesn't get physically shorter.
* **Glancing scrapes** produce small, shallow damage, because only the normal closing speed counts. There's no scraping or smearing model along the surface.

## Collision Lab phase 3: persistent damage and a damage-aware front hitbox

**Checkpoint A: anchored car and persistent damage** (`CollisionLabController`)

* **Anchor Vehicle** (on by default) sets `RigidbodyConstraints.FreezeAll` on the car's dynamic Rigidbody.
  * The car can't translate or rotate, but it still collides with continuous detection and still gets collision events.
  * Off restores free physics.
  * While anchored, the receiver treats the car as immovable, so the reduced mass is the impactor's mass. The same 12 m/s 1500 kg hit therefore scores 83 instead of 57 when free. It isn't calibrated.
* **LAUNCH** no longer repairs the car. Damage accumulates, with saturation. The history is bounded: see *Impact history* below.
* **Reset Block** re-parks the block for the current settings and clears the collision cooldowns. The car is untouched. The block is placed relative to the car's current ground pose, which is the start pose when anchored.
* **Repair Car** restores the original mesh and the original hitbox, and keeps every setting. A block that would overlap the regrown hitbox is re-parked.
* **Reset All** restores everything, including the car's pose and velocities.
* Changing direction, impactor, speed, mass or **Aim Offset** never repairs anything.
  * Aim Offset (−0.8…0.8 m) shifts the aim point toward the car's right for front, rear and corner hits, and toward the front for side hits.

**Checkpoint B: damage-aware front hitbox** (`FrontHitbox`, `PhysicsHitboxDebug`)

* **Collider layout.** The single body box is replaced by:
  * a structural **core** box from the rear to 0.7 m behind the nose, just ahead of the front axle;
  * three front sections, **left, centre and right** (thirds of the width), from the core's front face to the bodywork.
  * The cabin box is unchanged, giving 5 boxes. Sections share faces exactly: no overlaps, no gaps.
* **Face position comes from the mesh.** Each section's frontal area is split into a 6 × 4 grid of cells.
  * The front-most *current deformed* vertex of each cell is found, about 19.5 k vertices sampled.
  * The section face is the median of those cell fronts, but never closer than 6 cm to the core.
  * Undamaged, that's the front contour; the bodywork's tip stands about 4 cm proud of it.
  * Crushed, the face retracts by about as much as most of the section's metal moved. No separate damage value is kept, so physics can't disagree with the visuals.
* **When colliders change.** An impact marks the hitbox pending. The faces are refitted at a safe point:
  * once the block is no longer pressing into the car (relative speed < 0.3 m/s, or block parked);
  * and always in Reset Block, before the next launch.
  * Between repairs, sections only shrink, so a change never creates an overlap.
* **New hits use the current surface.** `ImpactField.ResolveFootprint` now finds first contact against the bodywork as damaged so far: original vertices plus their saturated impact sum. A box impact's per-vertex contribution is computed from the vertex's current depth. The mesh is still rebuilt deterministically from the original vertices and the impact sequence. An undamaged car behaves exactly as before.
* **Contact point.** `CollisionDamageReceiver` backs the contact point and impactor pose out by the reported penetration (`ContactPoint.separation`). A fast block is often reported one step after it entered the collider, so this gives where the surfaces first met.
* **Diagnostics.** A sub-threshold re-touch soon after a damaging hit doesn't overwrite the diagnostics. This happens when a block still creeping at a few cm/s meets the retracted face.
* **Show Physics Colliders** draws the current car colliders as a green wireframe (runtime lines, works in WebGL).

**Validation** (`collision-persist-a`, `collision-persist-b`, real PhysX; screenshots `Screenshots/50…57`):

* **Checkpoint A**
  * Five launches without repair (front, front, left, rear narrow, front-left corner): each is one damaging collision and adds damage.
  * Settings changes moved the mesh 0 m.
  * The anchored car drifts at most 0.03 mm (solver residual).
  * Reset Block keeps the mesh bit-identical. Repair Car and Reset All restore it exactly.
  * Free mode still pushes the car (3 m), and a second hit after Reset Block registers.
* **Same front-centre spot, narrow block, 12 m/s, identical start pose.** Physical contact moved from z 2.474 to 2.170 to 2.125 m; the hitbox retracted 35 cm in the centre, 4 cm on the left, 0 on the right. A 4th hit saturated: no further retraction.
* **Front-left vs front-right.** Two left hits retract the left face 24 cm. The centre and right faces don't move. A right hit then makes contact at 2.469, within 0.5 cm of the undamaged right face.
* **Wide hit.** All three sections retract by about 20 cm, and a second wide hit makes contact 19.5 cm deeper.
* **Repair and drift.** After repair and three more hit/repair cycles, the faces are identical to the undamaged fit.
* **Stability.** Updates were applied at ≤ 0.24 m/s relative block speed, with no impulse afterwards. Repairing with the block resting in the dent re-parks it, with no motion. Free mode with the split hitbox shows no spin.
* **Performance** (editor, this machine):

  | Cost | Time |
  |---|---|
  | Hitbox refit | 0.44 ms (5 colliders, only when damage changed) |
  | Visual deformation per impact | about 70 ms |
  | Physics step | 0.04 ms |

  The visual deformation is the bottleneck, not the hitbox.


**Impact history (fix for "no damage after 24 impacts").**

* **Cause.** Phase 3 gave the Collision Lab car `maxImpacts = 24`, and `AddImpact()` *rejected* every impact past the limit. That's the Damage Lab's click-limit behaviour.
  * Rejected hits fired no `ImpactsChanged`, so the mesh and the front hitbox stopped updating.
  * The receiver marked them `applied = false`, which the panel showed as "(below threshold)".
  * Its "keep the last damaging hit" rule then hid them entirely.
* **Fix.** `VehicleDamageModel.impactOverflow`: `Reject` stays the default (the Damage Lab still allows 10 clicks), and the Collision Lab uses `MergeOldest`.
  * **Merging.** At the limit, the oldest impact record is dropped. Its deformation is already in every part's per-vertex impact sum, which the mesh is rebuilt from, so the car keeps it. This is O(1), memory is bounded (24 records plus a fixed per-vertex sum), and per-hit cost doesn't grow.
  * **Saturation** still works per vertex. A saturated area barely moves, while new areas deform fully.
  * **Re-initialising** with merged impacts restores the saved sums instead of replaying records.
  * **Seeding.** Box impacts are now seeded by their ordinal in the sequence, not by a quantised contact position. Sub-millimetre physics jitter can't flip the wrinkle pattern, and replaying the same impacts gives the same mesh.
* **Diagnostics.**
  * A hit that has strength but is refused shows "(not applied)", never "(below threshold)", and is never hidden.
  * The panel shows `Impacts on car N (stored / merged)`.
* **Trade-off.** Hardening and spread on repeated hits look only at the last 24 records. Damage older than that is still in the shape and still saturates, but it no longer widens new dents. The debug view shows footprints for the stored records only.
* **Fast-block side effects, found by the 30 m/s regression.** At 30 m/s a block moves 0.6 m per physics step. It can end a step deep inside a crushed notch, and PhysX's depenetration can then push it sideways into the dent wall. That's a second "damaging" contact in the same launch, about 1 in 33 launches.
  * `CollisionDamageReceiver.oneImpactPerLaunch` (on) makes each launch one damaging impact. Later contacts from the same block are logged ("no damage (block already made its impact this launch)") until the block is re-armed by Launch, Reset Block or Reset All.
  * Contact points are now corrected per contact: a point is moved out by its penetration only when it lies inside the car's box. PhysX may report the points on either collider of the pair, and the phase 3 code assumed the block's.
* **Overflow validation results** (`collision-overflow`, 33 hits at 30 m/s and 4000 kg):
  * Hits 24, 25, 26, 30 and 33 all applied. Undamaged right side and rear deformed fully after 25 hits.
  * Front-left and front-right hitbox sections still retracted. The saturated centre moved ≤ 3 cm per hit.
  * Records stayed at 24.
  * Replaying the recorded impacts matched to 4e-16 m, and the physics re-run matched to 0.016 mm.
  * Repair Car and Reset All were exact.
  * Deformation cost per hit stayed about 70 ms before and after the limit.
Phase 3 limitations:

* **Front only.** The sides and rear still collide with the core and cabin boxes.
* **Three boxes per front.** Each section has one flat face at the median of its metal. Metal that sticks out (unhit parts of a section, the 4 cm tip of the undamaged nose) can visually overlap the block. A narrow hit near a section boundary moves the neighbouring section only if most of its metal moved. Section height doesn't change, so a crushed hood top still collides at its original height.
* **Between collisions, not during.** The hitbox changes after an impact is resolved, not during it. The block stops at the face it hit in that step.
* **Re-touches.** After a retraction, a block still creeping at a few cm/s may touch the new face again. These are below the damage threshold.
* **Anchored severity.** Anchored mode uses the impactor's mass as the reduced mass, so its severities aren't comparable to free mode.
* **Cost for 8–12 vehicles.** The hitbox and physics cost is negligible. The visual deformation cost (single-threaded in WebGL) is what needs Burst/Jobs or GPU work before many cars crash at once.

## Collision Lab phase 4: whole-vehicle damage-aware hitbox

`VehicleHitbox` replaces phase 3's `FrontHitbox`. It uses the same mesh-derived method, applied to the whole body.

* **Layout.** 14 boxes that tile the body exactly, with shared faces, no overlaps and no gaps:
  * a structural core and the cabin (fixed);
  * **nose** FrontLeft / FrontCentre / FrontRight and **tail** RearLeft / RearCentre / RearRight: full-width thirds, 0.7 m deep. Their end faces adapt, and the four corner sections also adapt their outer side face;
  * **sides** LeftFront / LeftMid / LeftRear and the same on the right: the 0.45 m outer shell between nose and tail, in thirds along the length. Their outer side faces adapt.

  That's 12 adaptive regions with 16 adaptive faces.
* **Why this layout, not separate corner boxes.** Full-width nose and tail thirds mean the corners are part of the end sections, with two adaptive faces each. That gives a front hit, a side hit or an angled corner hit on the same corner one coherent box, with fewer colliders and no extra seams. The side shell between the axles carries side crush, and the core never moves, so a crushed region bottoms out on real structure.
* **Face fitting** is unchanged from phase 3:
  * each face's area is split into a 6 × 4 grid of cells, and the outermost *current deformed* vertex of each cell is found (about 72 k vertex samples in total);
  * the face goes to the median of the cells;
  * it is never moved outward of the undamaged fit, never moved outward again once retracted, and never closer than 6 cm to the region's inner face.

  Builder-time outer bounds are stored per region, so re-setup and fixed faces never drift.
* **Updates** happen at the same safe point as phase 3: when the block has stopped pressing, and before every launch.
  * Only faces within 1.2 m plus the footprint size of the new impacts are re-fitted (about 9 of 16 on average). A repair re-fits all of them.
  * Repair restores the exact original boxes and re-inserts them in a fixed order. PhysX re-creates a resized shape at the end of the body's shape list, so this keeps a repaired car's physics layout identical each time.
* **Debug.**
  * The green wireframe shows all current boxes; cyan footprints and yellow arrows are unchanged.
  * The diagnostics panel gains a *Hitbox update* line listing the regions that moved and by how much (for example "FrontLeft +Z -24 cm").
  * New direction: **Rear Corner (Angled)**. Aim Offset now ranges ±1.5 m, so sides can be aimed at their front, mid or rear third.

**Validation** (`collision-phase4`, real PhysX, anchored):

* **Coverage.** All targets, two identical narrow hits at 15 m/s from a repaired car: front L/C/R, rear L/C/R, left and right front/mid/rear, and (since the vehicle engine milestone) all four angled corners.
  * In every case the second hit lands 9–31 cm deeper. Since the vehicle engine milestone "deeper" is measured where it is exact: the parked block is swept (`Physics.BoxCast`) along its launch path against the car's current colliders. The averaged contact point and the recorded block pose are both reported after a 30 cm physics step (PhysX does not sweep a 4 m block moving 0.3 m per step), so they land 0–15 cm past the true first touch depending on step phase; at a corner the averaged point also slides between the two faces. That is why the hatchback's rear-right corner used to read 0 cm deeper although both of its faces retract.
  * For straight hits, it lands at the retracted face (within 6 cm).
  * The struck region retracts 15–44 cm.
* **Locality.**
  * Front centre ×25 at 30 m/s and 4000 kg saturates locally: the last hits move the mesh 1.5–1.9 cm and the face stops moving.
  * After that, front-left, front-right, left mid, right mid and rear centre still deform 0.21–0.46 m, and their regions retract 18–39 cm.
  * Left mid ×10, then rear left and right mid: still deform. Rear centre ×10, then front and left front: still deform.
* **Long run.** 84 mixed hits (14 targets × 6 rounds, narrow and wide alternating, 30 and 15 m/s, 4000 kg):
  * every hit applied and every diagnostic correct;
  * 24 stored and 60 merged impacts, no NaN, car drift 0.7 mm;
  * faces only ever moved inward;
  * deformation stayed about 73–84 ms per hit throughout.
* **Reset and repair.** After 84 hits, Reset Block leaves the mesh bit-identical. Repair Car restores the mesh and all 12 boxes exactly, and the next hit is impact #1. Reset All is exact.
* **Costs** (editor):

  | Cost | Time |
  |---|---|
  | Hitbox refit | 1.3 ms average, 2.6 ms max, per impact only |
  | Visual deformation | about 73 ms per hit |
  | Physics step | 0.048 ms, against 0.04 ms with phase 3's 5 boxes |

**Scaling** (estimate, not measured with several cars):

* **Physics** is negligible: 14 boxes per car is about 170 boxes for 12 cars. The cost that matters is the visual deformation.
* **Editor deformation per impact:**

  | Cars hit at once | Cost |
  |---|---|
  | 2 | about 140 ms |
  | 4 | about 290 ms |
  | 8 | about 0.6 s |

  That's a frame hitch on every crash unless the work is spread over frames or moved to Burst/Jobs or the GPU.
* **WebGL** is single-threaded, so its about 15–20 ms per impact is for one car with a light hit; heavy hits in the editor run 2–4× that.
* **Verdict.** The hitbox design scales fine. The visual rebuild is the bottleneck, and needs its planned Burst/Jobs work before 8+ cars crash simultaneously.

Phase 4 limitations:

* **Coarse sections.** Each region has one flat face at the median of its metal. A section only partly struck retracts only if most of its metal moved, and unstruck metal can visually overlap the block. A block straddling two sections, or within PhysX's 1 cm contact offset of a less-crushed neighbour, catches that neighbour's edge. That's why the tests aim the narrow block at ±0.65 m, not ±0.6.
* **Corners** adapt their end face and side face independently, so a diagonal crush is a stepped box, not a bevel.
* **Height.** The cabin box doesn't adapt, and section height doesn't change, so a crushed hood or roof still collides at its original height.
* **Thin side shell.** Sides are only 0.45 m deep, so a very deep door crush bottoms out on the core. That's deliberate.
* **Fast blocks still tunnel** up to a step (0.6 m at 30 m/s) before PhysX pushes them out; one impact per launch keeps that from double-counting. The averaged contact point reads 3–9 cm inside the face at 15 m/s; the box pose used for the footprint is corrected separately.
* **Physics repeatability is at the millimetre level.** Identical launches can differ by a few mm of contact because PhysX keeps internal contact history across resets. The deformation itself is exact for a given impact sequence. (The vehicle engine milestone found and removed four discontinuities in the box crush that could amplify such differences to tens of centimetres; see *Repeatability investigation* below.)
* **Many hits fold triangles.** After 84 heavy hits, 5 % of triangles are folded, from the visual system under extreme saturation, not the hitbox.

## Vehicle engine (reusable vehicles)

A vehicle is **data**. The destruction code is the same for every vehicle; a new vehicle needs a model, a definition asset and a validation run, not new code. This section is the technical reference for adding one (and for a future guided `/addacar` workflow).

**Working today:** the blue muscle car (regression baseline) and the red compact hatchback run through the same definition → rig → damage → hitbox path, in the editor and in WebGL, and can be switched at runtime.
**Not built yet:** an in-editor wizard or `/addacar` skill, per-panel stiffness maps, automatic profile tuning, driving/gameplay use. These are planned, not present.

### Pieces

| Piece | File | Role |
|---|---|---|
| `VehicleDefinition` | `Scripts/Vehicle/VehicleDefinition.cs`, assets in `Settings/Vehicles/` | Everything vehicle-specific: model, mass, damage profile, component rules, shell proportions, impact height, record limit |
| `VehiclePartRule` | `Scripts/Vehicle/VehiclePartRule.cs` | One rule per component family: name pattern, match mode, role, mode, crumple |
| `VehicleDamageProfile` | `Scripts/Deformation/VehicleDamageProfile.cs` | Directional crush regions, cabin line, localized-impact tuning |
| `VehicleRig` | `Scripts/Collision/VehicleRig.cs` | Builds a destructible physics vehicle from a definition (runtime-safe) |
| `VehicleDamageModel` | `Scripts/Vehicle/VehicleDamageModel.cs` | Classifies parts, owns the deformable meshes, impact history, warnings |
| `VehicleHitbox` | `Scripts/Collision/VehicleHitbox.cs` | Damage-aware 14-box physics shell |
| `VehicleDefinitionSetup` | `Editor/VehicleDefinitionSetup.cs` | Writes/refreshes the two shipped definitions (*Damage Lab ▸ Create or Refresh Vehicle Definitions*, bridge `vehicles-setup`) |

### Vehicle definition fields

| Field | Meaning | Automatic when 0 |
|---|---|---|
| `displayName` | Shown in the selector and diagnostics | — |
| `model` | Imported FBX prefab. Must face **+Z**, metres, **Read/Write enabled** meshes | — |
| `mass` | Rigidbody mass for free-physics collisions (kg) | — |
| `profile` | `VehicleDamageProfile` (crush regions, cabin line, impact tuning) | — |
| `components` | Ordered list of `VehiclePartRule` (first match wins) | — |
| `unknownComponentCrumple` | Crumple for unmatched parts (they still deform and are reported) | — |
| `frontSectionDepth` / `rearSectionDepth` | Depth of the adaptive nose / tail sections | 14 % of body length |
| `sideSectionDepth` | Depth of the adaptive side shell | 22 % of body width (max 45 %) |
| `cabinBoxScale` | Cabin collider size relative to the Cabin parts' bounds (width, length) | default 0.92 × 0.85 |
| `impactHeight` | Collision Lab impactor centre line above the car's base | body centre height, at least 0.6 m |
| `maxImpactRecords` | Impact records kept individually; older ones merge into the accumulated damage | default 24 |

### How `VehicleRig` builds a vehicle

1. Instantiate the model at the origin, identity rotation, unit scale.
2. **Measure** (`VehicleRig.Measure`) from the component roles: body bounds (Body + Bumper parts, or any rule with `definesImpactFaces`), cabin bounds (Cabin parts), wheel bounds and wheel centres (Wheel / Rigid parts), ground lift (puts the tyres on the ground). Missing roles fall back to the whole model and produce a warning.
3. **Physics shell**: 14 boxes tiling the body — a fixed core, a fixed cabin box, and 12 adaptive regions: nose and tail thirds (end face adapts; the four corner boxes also adapt their outer side face) and side-shell thirds (outer face adapts). 16 adaptive faces in total.
4. Rigidbody (mass, ContinuousDynamic, centre of mass at wheel base + 0.45 m), `VehicleDamageModel` (definition, profile, rules), `CollisionDamageReceiver`, `VehicleHitbox`, footprint and collider debug views.
5. `CollisionLabController.SelectVehicle` destroys the previous car (deactivated first, so its colliders leave the physics scene immediately), adopts the new rig, initialises the damage model and hitbox, collects warnings and resets the lab. Aim points and impactor height come from the new vehicle's measured geometry.

### Component roles and naming

| Role | Effect | Typical crumple |
|---|---|---|
| `Body` | Main shell; defines the outer impact faces and the physics shell | 1.0 |
| `Bumper` | Also defines the outer impact faces | 0.25 |
| `Cabin` | Greenhouse (roof, pillars, glass); defines the cabin collider | 0.85 |
| `Wheel` | Rigid (mode `Rigid`): follows the crush, defines wheel-mount zones | 0 |
| `Panel` | Other painted sheet metal (valances, sills, spoilers, scoops) | 1.0 |
| `Fascia` | Grilles, intakes, splitters | 0.55 |
| `Light` | Lamps and housings | 0.45 |
| `Trim` | Mouldings, handles, mirrors, wipers, plates (exhausts 0.4) | 1.0 |
| `Underbody` | Floor pan / underside | 1.0 |
| `Ignore` | Not deformed (e.g. wireframe overlays) | — |

* **Matching** is case-insensitive. `Token` (default): the name equals the pattern or continues with a non-letter — `Body` matches `Body`, `Body.001`, `Body_L`, but **not** `Underbody` or `BodyKit`. `Exact` and legacy `Contains` (substring) are available per rule.
* **First match wins.** More than one matching rule is reported as *ambiguous*.
* **Names for new models:** one object per component family, named `<Family>`, `<Family>.<n>` or `<Family>_<side>` (e.g. `Headlight`, `Headlight.001`, `Mirror_L`). A longer name that merely starts with another family's name is a different family and needs its own rule: `HeadlightHousing` does not token-match `Headlight`.

### Configuration validation

`VehicleDamageModel.Warnings` (shown in the Collision Lab vehicle panel and by `vehicle-<name>` validation) reports:
* **unclassified** components (deform as generic sheet with `unknownComponentCrumple` — never silently),
* **ambiguous** components (several rules match),
* **unreadable** meshes (Read/Write disabled),
* **no impact-face parts** (no Body/Bumper role) and **no wheels**,
* a damage-profile **cabin line more than 12 cm from the measured beltline** (cabin protection misplaced).

Both shipped vehicles produce **0 warnings**. The hatchback with the old muscle-car configuration produces 55 (the regression `vehicle-hatch-legacy` keeps that comparison).

### Damage profiles and structural zones

* The profile holds the four directional crush regions (Damage Lab sliders), the **cabin line** (`roofLineStart`/`roofLineEnd`: above it the stiffer cabin limit applies) and the localized-impact settings.
* Per-vertex crush **capacity** (how far metal can move before it saturates): panels 0.50 m, cabin 0.36 m (blended over ±0.1 m around `roofLineStart`), wheel mounts 0.20 m within 0.5 m of a wheel centre (fading out by 0.95 m), scaled by `0.6 + 0.4 · crumple` for stiff parts. Saturation is linear to 60 % of capacity, then eases toward it.
* **Work hardening**: a hit on already-crushed metal (within an earlier dent's footprint and crush zone) crushes less and spreads wider.
* A new vehicle normally starts from a copy of an existing profile with its cabin line set from the measured beltline and roof (`VehicleDefinitionSetup` does this for the hatchback: 0.93–1.35 m).

### Adaptive collider configuration

* Section depths default to 14 % / 14 % / 22 % of the body; override them in the definition when a vehicle's crumple zones are unusual (the muscle car keeps its explicit 0.7 / 0.7 / 0.45 m).
* Faces sit at the median of the outermost *current* metal in a 6 × 4 grid of cells per face, only ever move inward, stop 6 cm short of the region's inner face, and re-fit only near new impacts (1.2 m + footprint). Repair restores the exact original boxes.
* Limits: one flat face per region (a partly struck section retracts only if most of its metal moved); corners are stepped (two faces), not bevelled; collider height never changes.

### Adding a vehicle (checklist)

1. **Model**: export facing +Z in metres, one object per component family with token-friendly names, Read/Write enabled meshes, wheels as separate objects.
2. **Definition**: create *Damage Lab ▸ Vehicle Definition* (or extend `VehicleDefinitionSetup`), set `displayName`, `model`, `mass`.
3. **Rules**: one `VehiclePartRule` per component family with its role and crumple (use the role table above).
4. **Profile**: copy an existing profile, set `roofLineStart` to the measured beltline and `roofLineEnd` about 7 cm below the roof.
5. **Shell**: leave section depths and impact height at 0 (automatic) unless the vehicle needs explicit values.
6. **Register**: save the definition asset in `Settings/Vehicles/`. The scene builder lists every definition there (muscle car and hatchback first); rebuild the scene (`collision-build`) to get its selector button.
7. **Validate** (bridge commands take the vehicle's index in the Collision Lab list): warnings must be 0; `vehicle <i>` (integration: every direction, repeats, saturation, Damage Lab paths, folded triangles per part, rebuild exactness), `collision-phase4 <i>` (coverage of all 16 targets, locality, 84-hit long run, repair), `collision-overflow <i>` (33 heavy hits, record merging, repeatability investigation), `vehicle-switch`.
8. **Review folds per part**: parts that fold far more than their neighbours under ordinary hits are usually mesh problems (thin recessed geometry, overlapping shells), to be fixed in the model, not by weakening the shared engine.

### Repeatability investigation (vehicle engine milestone)

**Symptom.** Running the 33-launch overflow sequence twice (real PhysX, same start state) gave meshes 17–25 cm apart, although the recorded impact inputs differed by under 1 mm. Before the milestone the same check gave 0.016 mm – 1.8 cm. Replaying identical recorded impacts was, and is, bit-identical.

**Method** (`collision-overflow`, `collision-overflow-hatch`, `repeat-probe`, `repeat-probe-hatch`; `Editor/CollisionRepeatabilityInvestigation.cs`):

| Test | What | Result |
|---|---|---|
| A | Replay the recorded impacts with every rebuild optimisation on vs off (stored and re-resolved footprints) | **0.000 mm**, 0 of 33 footprints differ |
| C | Each optimisation alone (part culling, selective rebuild, sweep part filter, silhouette prefilter, `AccumulateRange`) vs all off | **0.000 mm** each |
| B | Replay with every impactor shifted 0.1 mm (footprints re-resolved), optimisations off and on | **41 mm after one impact, 415 mm after 33** — identical with optimisations off, so the amplifier is in the crush function itself (unchanged from `main`) |
| D | Per-impact divergence, then the worst vertex's terms (`ImpactField.ExplainBox`) both ways | four discontinuities, below |
| E | Fixed vs pre-fix function (`ImpactField.LegacyBehaviour`) on the same impacts | what the fix changed (below) |

**Root causes** — all in `ImpactField` box-impact code that predates this milestone:

1. **Draw-in singularity.** Metal just outside the impactor's silhouette is pulled toward the dent by `(q − x)·drawIn·push / sd`, but `q` is nudged 2 mm *inside* the silhouette (so `Lead` never misses the box). `|q − x|` is therefore `sd + 2 mm`, and the pull grew without bound at the edge: ×6 at 0.4 mm, ×21 at 0.1 mm. A 0.1 mm impactor shift moved one vertex from sd 0.5 to 0.4 mm and its displacement by 44 mm. **Fix:** unit direction to `q`, faded in over `edgeSoftness` (3 cm), so the pull is continuous and bounded.
2. **Crease cut-offs.** The crease band (up to `rimCrease·travel`, several cm) was cut abruptly where the function stops (behind the crush zone at `over = −edgeSoftness`, and at 3ρ outside the silhouette). **Fix:** faded to zero over the last `edgeSoftness` and between 2ρ and 3ρ. Unchanged elsewhere.
3. **Hardening at an argmin vertex.** Work hardening was evaluated at the single vertex with the smallest gap. On flattened metal many vertices tie within fractions of a millimetre, so the chosen vertex (and the crush depth) jumped. **Fix:** a smoothly weighted contact point (Gaussian over the first 2 cm of gap).
4. **Hardening along an infinite line.** `Prior()` measured only lateral distance to each earlier impact's line, so 24 front-centre hits "hardened" the untouched rear on the same centre line: the rear crushed 0.12–0.18 m instead of 0.28 m, and sat on the steep flank of 24 stacked Gaussians (a 1.3 cm shift changed the crush by 6.5 cm). **Fix:** earlier damage counts within that dent's crush zone along its direction (as before for same-spot repeats), fading beyond it.

The milestone did not introduce these; it changed PhysX contact histories enough that sub-millimetre run-to-run differences reached them more often. I could not attribute that to one specific change, and with the amplifiers gone it no longer matters: sub-millimetre input differences now give millimetre-level mesh differences.

**Results** (measured, editor):

| Measure | Muscle car before → after | Hatchback after |
|---|---|---|
| 0.1 mm shift of every impactor, mesh difference after 33 impacts | 381–415 mm → 0.6–5.6 mm | 0.3–3.6 mm (pre-fix 42 mm) |
| Physics re-run of the 33 launches | 170–250 mm (WIP) → 0.7 and 4.8 mm (two final runs) | 0.3 and 31 mm (two final runs; the 31 mm is the edge case below) |
| Replay of identical recorded impacts | 1e-7 m (unchanged) | 6e-7 m |
| Rebuild optimisations vs none | 0.000 mm (unchanged) | 0.000 mm |

**What the fix changed** (Test E, same impacts): on a single front hit, 677 of 79,918 muscle-car vertices (0.85 %) and 513 of 97,188 hatchback vertices (0.53 %) move by more than 1 cm — these are the edge spikes (the old draw-in yanked edge metal sideways by up to 24 cm). Folded triangles after 33 heavy hits: 2.65 % vs 2.42–2.71 % pre-fix (muscle; the pre-fix count varies with the recorded run), 4.06 % vs 4.10 % (hatchback). A region hit after another region was hammered now deforms like a fresh car (rear after 25 front hits: 0.306 m vs 0.281 m fresh).

**Remaining physically sensitive cases** (continuous, bounded):
* Metal exactly at the impactor's silhouette edge: a few mm of block position decides whether a corner is inside the crushed patch or dragged as edge metal. The 1.9 m wide sled on the 1.85 m hatchback's rear has its edges on the rear corners: a 6 mm PhysX contact difference there gave 31 mm in one re-run (0.05 mm in another).
* A wide block that arrives yawed by ~1° leads with its side wall near its edge; that leading surface changes ~45 mm per mm of lateral position (a real property of the box, not a discontinuity).
* Footprint extents still change in small steps when a row of vertices enters the patch (2 cm patch grid); the crush depth barely moves (area enters as area^0.35, clamped).

### Performance (vehicle engine milestone)

Measured in the editor on this machine (real PhysX, `vehicle-muscle`, `vehicle-hatch`, `collision-phase4[-hatch]`). Browser figures are in the WebGL section below.

| | Muscle car | Hatchback |
|---|---|---|
| Deforming parts / vertices / triangles | 12 / 62,014 / 152,276 | 72 / 80,108 / 136,316 |
| Physics colliders | 14 boxes | 14 boxes |
| Deformation per hit, 15 mixed hits (avg / max) | 55 / 81 ms | 85 / 122 ms |
| — of a wide front hit: footprint resolve | 37 ms | 44 ms |
| — contribution accumulation (parts evaluated) | 36 ms (9 of 12) | 67 ms (50 of 72) |
| — mesh rebuild + normals (parts rebuilt) | 9 ms (9 of 12) | 12 ms (50 of 72) |
| Deformation per hit over the 84-hit long run | 49 ms | 77 ms |
| Adaptive hitbox update (after an impact only) | 1.3 ms avg, 2.4 ms max | 1.9 ms avg, 3.2 ms max |
| Physics step | 0.045 ms | 0.061 ms |
| Same 9 recorded impacts replayed, optimisations off → on | 31–33 → 28 ms (−11 to −15 %) | 52–54 → 48–50 ms (−8 %) |

* The rebuild optimisations (part culling, selective rebuild, sweep filter, silhouette prefilter, ranged accumulation) are exact (0.000 mm, Tests A and C above) and give a modest 8–15 % per replayed hit.
* **Bottlenecks** are the per-vertex box crush (procedural noise per crushed vertex: about 45–55 % of a hit) and the footprint resolve (current position and silhouette test per candidate vertex, then the patch hull: about 35–45 %). Mesh rebuild and normals are only 10–15 %. Physics and the adaptive hitbox are negligible.
* The hatchback costs about 1.5× the muscle car: 30 % more vertices and 6× more parts, and its front and rear parts sit inside more impact zones.
* Next step (not started, by design): move the box crush and the footprint resolve to Burst/Jobs, then the rebuild. That is where the time is.

### Remaining hatchback asset issues (for the asset author)

Measured after the engine fixes, with the hatchback's own configuration, from a repaired car. Reproduce with bridge command `hatch-defects` (close-ups in `Screenshots/80_Sol_*.png`). The shared engine was **not** weakened to hide these; the muscle car folds far less in the same tests.

| Object | Reproduction | Folded triangles | Muscle-car comparison | Close-up |
|---|---|---|---|---|
| `FrontValance` | Front, wide sled 1.9 m, 15 m/s, 1500 kg | 558 / 3,248 (17.2 %) | `Bumper_Front` 32 / 3,068 (1.0 %) | `80_Sol_FrontValance_FrontHit` |
| `LicenseRecess` | Rear, wide sled, 15 m/s, 1500 kg | 189 / 1,088 (17.4 %) | — | `80_Sol_LicenseRecess_RearHit` |
| `MirrorGlass` | Left side, wide sled, 15 m/s, 1500 kg | 64 / 188 (34 %) | `Mirrors` 85 / 1,824 (4.7 %) | `80_Sol_MirrorGlass_LeftHit` |
| `SideIndicator` | Left side, same hit | 33 / 188 (17.6 %) | — | `80_Sol_SideIndicator_LeftHit` |
| `DoorSeam` | Left side, same hit | 23 / 80 (28.8 %) | — | (same shot) |
| `Body` | Click dent from above on the hood at (0.22, 0.88, 1.38), strength 0.8 | 142 / 61,168 | hood dent: 0 | `80_Sol_Body_HoodDent` |
| `Body` | Click dent on the door at (−0.81, 0.58, −0.17), strength 0.8 | 57 | door dent: 0 | `80_Sol_Body_DoorDent` |
| `Body` | Click dent on the hatch at (−0.06, 1.09, −1.52), strength 0.8 | 83 (+38 `LicenseRecess`) | rear dent: 0 on the body | `80_Sol_Body_RearDent` |

Likely causes (not yet confirmed in the model): recessed panels whose front and back faces are only millimetres apart (valance, licence recess), very small parts made of few large triangles relative to the crease spacing (mirror glass, indicators, door seams), and long or uneven triangles in the body around the hood, door and hatch dents (the muscle car's body is an even 3–4 cm quad grid). Vehicle-space coordinates: +Z forward, +X right, metres.

### WebGL (vehicle engine milestone)

`collision-webgl` builds both vehicles into one 16.9 MB build (data 8.3 MB, up from 6.0 MB with the hatchback). Tested in Chromium (the desktop app's browser pane, 1920 × 1080), measured from the build's own console log:

* Both vehicles load from the selector: "Muscle Car ready: 12 deforming parts, 62,014 vertices, 14 colliders, 0 warnings" and "Compact Hatchback ready: 72 deforming parts, 80,108 vertices, 14 colliders, 0 warnings".
* All eight directions on both vehicles hit where the vehicle's own geometry is (front at z 2.51 / 2.05 m); damage accumulates; repeated narrow hits land deeper (muscle 2.25 → 2.13 m, hatchback 1.76 → 1.65 m).
* Repair Car and Reset All restore the car; switching vehicles gives a pristine car with no leftover damage.
* The hatchback's paint shader and both cars' stripes render correctly and stay on the deformed metal.
* Deformation per hit in the browser: muscle car 10–32 ms (15 m/s) and 10–20 ms (30 m/s, 4000 kg); hatchback 19–23 ms (15 m/s). That is lower than the editor's figures because the build is IL2CPP release code (single-threaded).
* Console: no errors; only the existing warnings that some URP post-process shaders (depth of field, Panini, FSR) are stripped from the build.
* Known, pre-existing (same code on `main`): in WebGL the speed / mass / offset number boxes look empty until they are first focused; the sliders and values work.

### Known limitations (vehicle engine)

* A new definition appears in the Collision Lab after a scene rebuild (`collision-build`); there is no runtime asset discovery.
* Damage profiles are copied and the cabin line set from geometry; there is no automatic tuning of crush depths per vehicle.
* The adaptive hitbox is coarse (one flat face per region, stepped corners, fixed height); see phase 4.
* The Collision Lab impactor is a box sled; vehicle-to-vehicle contact, driving and gameplay are separate milestones.
* Deformation costs 50–85 ms per heavy hit in the editor (10–30 ms in the browser) for one car; several cars crashing at once needs the planned Burst/Jobs work.

## Limitations / next steps

* **Performance.** About 20 ms per rebuild is fine for a lab with eased sliders, but not for gameplay. Next steps: port `Displacement` and the normal pass to Burst + Jobs (`MeshData` API), or move them to a compute/vertex shader using the precomputed per-vertex data.
* **The model isn't physics.** There's no material thickness, no part-to-part contact, and no plasticity history. Damage is a function of the four slider values, not of impact sequences. For gameplay, collision impulses would feed the same footprint/depth parameters, with accumulated damage stored per region.
* **Interpenetration.** Inter-part and self-intersection are reduced, but not prevented. At extreme combined values, tyres can poke through crushed fenders, and small details (lights, mirrors) can sink into the body.
* **Glass and detachment.** Glass doesn't shatter or detach, and nothing separates (by design for this milestone). Panel gaps and paint scrapes are a texture/shader follow-up, for example driving a damage mask from `|displacement|`.
* **Regions are spatial, not panel-aware.** Folds can cross panel boundaries. The Blender file carries vertex groups (Hood, Trunk, Door_L/R, fenders…) that could later drive per-panel stiffness.
* **Orientation.** The car is authored facing +Y in Blender, so it imports facing +Z with an identity root. The damage directions depend on that, and the scene builder logs an error if a model imports facing −Z.
