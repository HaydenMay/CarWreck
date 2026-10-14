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

## Limitations / next steps

* **Performance.** About 20 ms per rebuild is fine for a lab with eased sliders, but not for gameplay. Next steps: port `Displacement` and the normal pass to Burst + Jobs (`MeshData` API), or move them to a compute/vertex shader using the precomputed per-vertex data.
* **The model isn't physics.** There's no material thickness, no part-to-part contact, and no plasticity history. Damage is a function of the four slider values, not of impact sequences. For gameplay, collision impulses would feed the same footprint/depth parameters, with accumulated damage stored per region.
* **Interpenetration.** Inter-part and self-intersection are reduced, but not prevented. At extreme combined values, tyres can poke through crushed fenders, and small details (lights, mirrors) can sink into the body.
* **Glass and detachment.** Glass doesn't shatter or detach, and nothing separates (by design for this milestone). Panel gaps and paint scrapes are a texture/shader follow-up, for example driving a damage mask from `|displacement|`.
* **Regions are spatial, not panel-aware.** Folds can cross panel boundaries. The Blender file carries vertex groups (Hood, Trunk, Door_L/R, fenders…) that could later drive per-panel stiffness.
* **Orientation.** The car is authored facing +Y in Blender, so it imports facing +Z with an identity root. The damage directions depend on that, and the scene builder logs an error if a model imports facing −Z.
