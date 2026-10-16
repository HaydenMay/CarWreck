# Progressive Crushing V1

Dents now crush in over 60 to 240 ms instead of appearing in one frame. The damage solver is unchanged and still runs once per impact.
Only the way the result is **shown** changed. Background: [investigation](investigation-collision-geometry-and-progressive-deformation.md) (Option C, with
the collider coupling kept to "refit after the crush"), [damage-aware colliders](damage-aware-colliders.md).

**What was and was not run.** The authoring environment had no Unity editor, so **no Unity test, Collision Lab validation, driving validation or WebGL build
was run**. The evidence that exists is:

* the runtime code and the new validation type-check against Unity reference assemblies (2021.3; the only errors are the Unity 6 API names the
  existing code already uses);
* the **real** deformation code (`Assets/DamageLab/Scripts/Deformation/*.cs`, including the new ramp) was compiled and executed outside Unity on a
  synthetic subdivided body through a small UnityEngine stand-in ([progressive-crush-sim](progressive-crush-sim/)). The vehicle-level clock in that harness is a
  line-for-line mirror of `VehicleDamageModel.StartCrush` / `AdvanceCrush`, not the class itself.

Everything labelled *measured* below comes from that harness; nothing here is a Unity or WebGL number. `ProgressiveCrushValidation` (Unity) is written and
ready but has not been run yet.

## A. How it works

1. An impact arrives exactly as before (`CollisionDamageReceiver` -> `VehicleDamageModel.AddImpact`): footprint, accumulation and the full rebuild of `work`
   (the solved vertex positions and normals) all run once, synchronously, in the physics step.
2. With progressive crushing on, `DeformablePart.Apply` does **not** upload `work`. It copies what is on screen now into `rampFrom`, lists the vertices whose
   position or shading changes, and sets `Ramping`.
3. `VehicleDamageModel` runs one clock per car. Its `Update` calls `AdvanceCrush(dt)`, and each frame every moving vertex is shown at
   `from + (work - from) * ease`, normals likewise (re-normalised), then uploaded with `SetVertices` / `SetNormals`.
4. At the end the part uploads `work` / `workNormals` themselves, which are the same arrays an instant rebuild uploads. The final mesh is therefore the solver's result
   exactly (measured: bit-identical positions and normals in every harness case).

**Timing.** Crush time = `lerp(0.06 s, 0.24 s, travel / 0.6 m)`, where travel is the largest distance a vertex moves in this hit. Light dents are quick, deep ones
take longer, and a hard hit into already-crushed metal that barely moves is quick too. Elapsed game time drives it (`Time.deltaTime`). A single frame advances it by
at most 1/30 s, so the solve's own hitch on the impact frame cannot swallow the start of the crush. At 30 fps and above it follows real time exactly.

**Ease and stagger.** The ease is ease-out (`1 - (1 - u)^2`): the metal moves fastest at first contact and settles with zero speed, so the last frame does not
jump. Each vertex starts late by `stagger x clamp01(depth / D)`, where depth is the distance behind the impact point along the impact direction and
`D = max(0.15 m, travel)`. Every vertex still arrives at t = 1. The bumper and nose therefore lead and the hood and fenders follow. This is generic and
direction-based (front, rear, side and offset alike), with nothing specific to a vehicle.

## B. Architecture: where it sits

| Layer | Change |
|---|---|
| Damage solve (`ImpactField`, footprints, `AccumulateImpact`, `Apply`'s displacement and normals) | None. Same code, same order, same results. |
| `DeformablePart` | Optional `progressive` flag on `Apply`; ramp buffers and `ShowRamp` / `RestartRamp` / `FinishRamp`. `work` and `DeformedVehiclePosition` keep meaning "solved state". |
| `VehicleDamageModel` | `progressiveCrush` settings (off by default), one crush clock per car, `AdvanceCrush` from `Update`, diagnostics. Only `AddImpact` asks for a progressive rebuild. Clearing impacts, sliders and repairs stay immediate. |
| `DrivingGame` | Turns it on for arena cars (inspector fields). The collider refit also waits for the crush to finish. The page report has crush counters. |
| Physics, receivers, hitbox | None. |

The ramp runs only in play mode. Edit-mode tools (`Physics.Simulate` validations and probes) have no `Update`, so they keep seeing instant dents unless they
set `VehicleDamageModel.ProgressiveInEditMode` and call `AdvanceCrush` themselves (the new validation does this). The existing validations therefore run the
old code path.

## C. Implementation details

* **Targets.** The logical target is always the newest solved state (`work`). Hitbox fitting and the next hit's footprint read it (via `DeformedVehiclePosition` /
  `ImpactedVehiclePosition`), so repeated hits are solved against the true accumulated damage even while the screen is still catching up.
* **Repeated impacts.** A hit during a running crush solves the new target, and every crushing part restarts from **what is on screen**. Nothing is reset, nothing
  snaps back, and no damage is lost. Measured: the jump at the moment of the hit is 0, the first frame afterwards stays within the eased limit, and the final state is
  bit-identical to an instant replay for: a repeat on the same region at 40 %, the rear at 30 % of a frontal crush, three regions in quick succession, five hits at 10 %
  intervals, and a hit after the crush ended. The clock is per car, so jobs never overlap uncontrolled.
* **Reset.** `ClearImpacts` and `SetDamageImmediate(0)` restore the original arrays at once and end the ramp. Measured: exact restore mid-crush; the next hit crushes from
  the undamaged shape and matches the instant result.
* **Buffers.** Each part allocates its ramp buffers on its first crush (4 x `Vector3[]` + `int[]`, 52 bytes per vertex) and reuses them afterwards. A crush frame
  allocates no managed memory (no `new`, no LINQ, list enumerators only).
* **Bounds.** At ramp start the mesh bounds are widened to hold both the start and the end state, which holds every displayed state. At the end they are recalculated.
* **Colliders.** See section H.

## D. Files changed

| File | Purpose |
|---|---|
| `Assets/DamageLab/Scripts/Deformation/ProgressiveCrush.cs` (new) | `ProgressiveCrushSettings` (enabled, min/max time, full travel, stagger) and the ease function. |
| `Assets/DamageLab/Scripts/Deformation/DeformablePart.cs` | Deferred upload and display ramp. Solve unchanged. |
| `Assets/DamageLab/Scripts/Vehicle/VehicleDamageModel.cs` | Per-car crush clock, `AdvanceCrush` / `CompleteCrush`, `IsCrushing`, `CrushDuration`, `CrushTravel`, timing diagnostics. |
| `Assets/Driving/Scripts/DrivingGame.cs` | Arena settings (on by default), refit waits for the crush, page fields `pcrushes` / `tcrushes` / `pcrushMs` / `tcrushMs`. |
| `Assets/Driving/Editor/ProgressiveCrushValidation.cs` (new) | Unity validation for scenarios A-L. Menu `CarWreck > Validate Progressive Crush`. |
| `Docs/Driving/progressive-crush-sim/` (new, outside `Assets`) | Off-Unity harness that runs the real deformation code. |
| `Docs/Driving/progressive-crushing-v1.md` (new) | This report. |

## E. Before and after

* **Before:** the dent was solved and uploaded inside the physics step and appeared complete on the next frame, after the cars had already stopped (it blipped in).
* **After:** the solve happens at the same moment, but the next frame shows only the start of the crush. A light tap settles in 3 to 4 frames at 50 fps; a highway
  head-on crushes in over about 12 frames, with the bumper visibly leading the hood. The final shape is the same as before.

Measured progress per 50 fps frame (synthetic body, travel-weighted, front third / back third at frame 1):

| Hit | Travel | Time | Progress by frame | Front / back at frame 1 |
|---|---|---|---|---|
| Light frontal | 0.036 m | 71 ms | 48, 81, 98, 100 % | 48 % / n.a. |
| Moderate frontal | 0.234 m | 130 ms | 28, 52, 71, 85, 95, 99, 100 % | 28 % / 0 % |
| Severe frontal | 0.857 m | 240 ms | 13, 26, 39, 51, 63, 73, 81, 88, 93, 97, 99, 100 % | 16 % / 0 % |
| Offset frontal | 0.494 m | 208 ms | 15 ... 100 % (11 frames) | 17 % / 0 % |
| Side | 0.280 m | 144 ms | 24 ... 100 % (8 frames) | 26 % / 0 % |
| Rear | 0.281 m | 144 ms | 25 ... 100 % (8 frames) | 26 % / 0 % |

No vertex ever moved backwards along its path, and no frame had more inverted triangles than the solver's own final shape.

## F. Performance

| What | Result | Source |
|---|---|---|
| Crush frame, 73,926-vertex body, 31,108 vertices moving | **2.4 to 3.4 ms** (scalar loop; 6.1 ms with the first `Vector3`-operator version) | measured, Mono JIT in a Linux container, includes stand-in mesh copies (0.1 ms). **Not Unity, not WebGL.** |
| Allocations per crush frame | none by construction (the Unity validation measures the Mono heap) | code inspection; not measured in Unity |
| Extra memory | 52 bytes per vertex of each part that has crushed at least once (about 3.8 MB for a whole 74k-vertex car) | derived |
| Impact frame | about the same as before: the mesh upload moves to the crush frames, but the ramp start adds array copies and one pass over each rebuilt part | derived; the Unity validation prints both |
| Frames of work | only cars with a running crush do any work. A crush is 3 to 13 frames at 50 fps, then nothing | by design |
| Unity Editor / WebGL | **not measured** | run `ProgressiveCrushValidation` (section L) and a WebGL build (page fields `pcrushMs`, `tcrushMs` = worst crush frame per car since reset) |

Read the per-frame cost as 1 to 3 ms per crushing car on desktop until it has been measured in Unity. Two cars crushing at once on a phone is the case to check. If it is too slow
on mobile, the next steps are, in order: precompute each vertex's delay at ramp start, update the ramp at 30 Hz, and as a last resort a GPU blend (investigation Option E).

## G. Test results

PASS / FAIL below refer to the off-Unity harness. **No scenario was run in Unity.**

| Scenario | Status | Evidence |
|---|---|---|
| A. Light frontal | PASS (harness) / NOT TESTED in Unity | 71 ms, 4 frames, exact final |
| B. Moderate frontal | PASS (harness) / NOT TESTED in Unity | 130 ms, 7 frames, front leads |
| C. Severe frontal | PASS (harness) / NOT TESTED in Unity | 240 ms, last frame +1 %, exact final |
| D. Offset | PASS (harness) / NOT TESTED in Unity | progressive, exact final. Corner dominance is the solver's (unchanged); the Unity check measures it |
| E. Side | PASS (harness) / NOT TESTED in Unity | 144 ms, exact final |
| F. Rear | PASS (harness) / NOT TESTED in Unity | 144 ms, exact final |
| G. Repeated impacts | PASS (harness) / NOT TESTED in Unity | 5 sequences: no jump, exact accumulation; frame rate 20 to 144 fps: same final, done within one frame of the crush time |
| H. Simultaneous | PARTLY (harness: several regions on one car) / NOT TESTED in Unity | several cars at once only in the Unity validation |
| I. Player and AI | NOT TESTED | same code path for every car (`VehicleRig` + `VehicleDamageModel`); Unity check written |
| J. Collider alignment | NOT TESTED | rule implemented (section H); Unity check written |
| K. Reset | PASS (harness, model level) / NOT TESTED in the arena | exact restore mid-crush, then a clean next hit |
| L. Performance | PARTLY (Mono harness only) | section F |
| M. Collision Lab regression | NOT TESTED | Lab code path is unchanged (progressive is off by default and edit-mode stays instant); run `verify`, `collision-verify`, `collision-geometry`, `collision-persist-a/b`, `collision-overflow`, `vehicle-switch` |
| N. Driving regression | NOT TESTED | `CarWreck > Validate Driving` (88 checks) runs in edit mode, so instant dents, same code path as before |

**To run in Unity:** `CarWreck > Validate Progressive Crush`, or
`Unity -batchmode -projectPath . -executeMethod CarWreck.Driving.EditorTools.ProgressiveCrushValidation.Run -quit` (with graphics; `-nographics` is a known
problem for some Lab captures). It prints PASS / FAIL per check and a per-scenario summary A-L. It compares every final mesh with an instant replay of the same impact
records (tolerance 1e-6 m), checks monotonic progress, front-first ordering, crush time against travel, refit timing, reset mid-crush, four cars crushing at once,
allocations per crush frame, and per-vehicle crush-frame cost.

## H. Physics compatibility and colliders

No collision, impulse, mass, energy, strength, depth, width, severe-crush, accumulation or AI code changed. Damage solves at the same physics step as before, against
the same logical state.

Collider synchronisation: the three options from the brief were weighed.

* **Chosen: refit once, after the crush (Option A).** `RefitIfDue` keeps the existing 0.2 s settle and also waits while `Model.IsCrushing`, under the existing 1 s cap. The
  crush (at most 0.24 s) roughly matches the settle delay already in place, so the refit lands 0 to about 40 ms later than before. The shell is therefore never
  retracted ahead of the metal on screen, and it only ever retracts (the rule is unchanged).
* **Staged refits during the crush (B), rejected.** The shell already holds still for 0.2 s after every hit today, so staging would only shorten a window that
  already exists. It would also cost up to about 1.4 ms (editor) and re-create PhysX shapes 2 to 4 times per hit while the two cars are pressed together, and it
  would change which shell the next hit's footprint sees mid-contact. It would also need `VehicleHitbox` to fit intermediate states it does not measure today.
  None of that is justified by a gap the settle delay already causes.
* **During the crush,** the shell stands where it was before the hit while the metal moves away from it. That is the same invisible-gap window as today's settle
  delay (cars stop within 1 to 2 physics steps, so they are already touching), but the metal is now visibly moving into it.

## I. Remaining limitations

1. **Interpolation cannot fold.** Each vertex moves in a straight line to its solved place. A hood buckles only as far as the solver's final shape buckles it, and the
   stagger only orders when the motion happens. Deeper folding would need a different deformation target, which is out of scope.
2. **Stagger vs. self-intersection.** On repeated hits a large stagger can briefly push leading metal through metal behind it (measured on the synthetic body: 0.3 adds
   nothing beyond the final shape's own folds, 0.35 adds 1 triangle in about 10,000, 0.5 adds 26). The default is 0.3 and the slider is capped at 0.5.
3. **Synthetic evidence only.** The harness body is a subdivided box, not a car. Real meshes, tube sections and per-part capacities are only exercised by the Unity validation.
4. **Shell gap during the crush.** Up to the crush time plus the settle delay, as before (section H).
5. **Low frame rates.** Below 30 fps the crush slows down rather than skipping (at most 1/30 s per frame). The crush time is a design value, not the contact time.
6. **Wheels.** Rigid parts follow only the directional sliders, never impacts (unchanged), so there is nothing to ramp.
7. **Lab.** The Damage Lab and Collision Lab still show instant dents (setting off). Turning it on there is one checkbox, but it was left off to keep the Lab's measurements comparable.

## J. Risks and mitigations

| Risk | Mitigation |
|---|---|
| Crush-frame cost on mobile WebGL with two large cars at once | Measure with the page fields first. Then precompute the per-vertex delay, run the ramp at 30 Hz, or use a GPU blend. `progressiveCrush` off restores the old behaviour exactly. |
| Unity behaviour differs from the harness (real meshes, mesh upload costs) | Run `ProgressiveCrushValidation` before merging. It checks the same properties on the real cars with real PhysX. |
| A hit on a car that is not being rendered (menu open, `timeScale` 0) | Paused time freezes both the crush and the physics. Reset ends any crush at once. |
| Memory (about 3.8 MB per heavily crushed large car) | Allocated lazily per part, never per frame. Could be halved by dropping normal interpolation if needed. |

## K. Branch

`feature/progressive-crushing-v1`, based on `main` (`92cf25f`). Not merged, no WebGL build. The DriveArena scene was not rebuilt: the new `DrivingGame` fields take
their script defaults when the scene loads.

## L. Recommendation

Ready for a Unity validation pass, and **not** ready for gameplay review until that pass runs. The code is contained (display only, off by default outside the arena,
one switch to turn it off) and the algorithm passed every harness check, including exact equality with the instant solver. Before review:

1. Run `CarWreck > Validate Progressive Crush`, then `Validate Driving` and the Collision Lab validations.
2. Build WebGL and read `pcrushMs` / `tcrushMs` on desktop and on a phone during a two-car head-on.
3. Tune by eye: `crushTimeLight` / `crushTimeHeavy` / `crushHeavyTravel` / `crushStagger` on `DrivingGame`.
