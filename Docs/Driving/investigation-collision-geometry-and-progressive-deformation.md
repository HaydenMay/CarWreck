# Investigation: damage-aware collision geometry and progressive deformation

Investigation only. No code, scene, prefab or tuning was changed. All findings come from reading the code and the project's existing
validation notes; **nothing was reproduced at runtime** (see section J).

Evidence is labelled **[confirmed]** (read directly in the code), **[derived]** (follows from numbers in the code) or **[hypothesis]**.

## A. Executive summary

**Problem A, invisible gaps.** Damage-aware collision geometry already exists and works in the Collision Lab (`VehicleHitbox`: 12 adaptive
boxes per car that retract to follow the crushed mesh). **The driving arena builds it but never applies it after a hit.** In the arena the
only calls that refit the colliders are at spawn and on reset, so every car fights with its undamaged boxes forever. A crushed nose is
visually 0.1 to 0.6 m shorter than the collider that is actually touching the other car. That is the gap the player saw against the pickup at the wall.
This is an integration omission, not a missing feature. The fix is small and in Sonnet's area.

**Problem B, instant deformation.** Each impact is solved and the whole mesh is rebuilt synchronously inside one physics callback. The finished
dent is shown on the next rendered frame, about 40 ms after first contact, in one step. The engine deliberately bypasses the easing it has for the
slider damage ("Impacts appear at once (no easing)"). Cars also stop moving into each other within 1 to 2 physics steps (bounciness 0), so the dent
appears after the cars have already stopped. Progressive crush is feasible without re-solving the deformation each frame, but it needs changes inside the
deformation engine (Opus).

**Recommendation.** Fix A first by calling the existing refit at a safe point (Option A, a day or less). Then add progressive display as an
Opus change that interpolates between "before" and "after" vertex states while the expensive solve still runs once per impact (Option C, then D to
couple the colliders to the same progress value). Your proposed order (colliders, then progressive crush, then coupling) is right.

## B. Existing collision geometry

| Question | Answer |
|---|---|
| What does each car collide with? | 14 `BoxCollider`s on one dynamic Rigidbody, built by `VehicleRig.Build` (`VehicleRig.cs:109-149`): a fixed core, a fixed cabin, and 12 "regions" (front L/C/R, rear L/C/R, left and right front/mid/rear). The regions tile the body. Their depths come from the vehicle definition: `FrontDepthFraction` 14 % of body length (about 0.6 m hatchback/pickup), 0.7 m muscle front/rear, 0.45 m muscle sides. [confirmed] |
| Do shapes change when the mesh deforms? | They can. `VehicleHitbox` (`VehicleHitbox.cs`) fits each region's outer face to the median, across a 6 x 4 grid of cells, of the outermost deformed vertex in the cell. Faces only move inward, never past `inner + minDepth (0.06 m)`, and only inside `ApplyPending()` (`VehicleHitbox.cs:185`). It refits only faces near the new impact (`refitReach` 1.2 m). [confirmed] |
| Does the Collision Lab use it? | Yes. `CollisionLabController.AfterPhysicsStep` (`:456-464`) calls `ApplyPending()` once the block has settled (relative speed below 0.3 m/s), plus after launch, reset and repair. Its validations (`CollisionPersistenceValidationB`, `CollisionWholeVehicleValidation`, ...) cover it. [confirmed] |
| Does the driving arena use it? | It builds it, then **refits only twice**: `hitbox.Setup()` at spawn (`DrivingGame.cs:159`) and `ApplyPending()` inside `ResetSlot` (`:434`, to restore the undamaged shape). There is no call after an impact. [confirmed by searching every call site in `Assets/`] |
| Is it disabled by a flag? | No flag. `VehicleHitbox` has no `Update`/`FixedUpdate`; `MarkPending()` only sets `HasPending` when the model reports an impact (`VehicleHitbox.cs:92-117`). Nothing consumes it in the arena. [confirmed] |
| Is there collider debug in the arena? | The Lab has `PhysicsHitboxDebug`; the arena kills it at spawn (`DrivingGame.cs:155-156`: `Kill(rig.colliderDebug)`). [confirmed] |

History: `git log -S ApplyPending -- Assets/Driving` shows the two calls arrived with the Milestone 1 commit and never changed. None of the driving
validation or probe scripts (`DrivingValidation`, `SevereCrushProbe`, `ImpactWidthProbe`) reference the hitbox, so all arena test results so far
were taken against undamaged colliders.

## C. Root cause of the invisible gaps

**Confirmed:** colliders never retract after hits in the arena (section B). A damaged car presents its original box faces to the other car and to walls while
the visible metal sits behind them. Size of the gap = crush depth at that spot: about 0.08 m at 60 km/h, 0.26 m at 100 km/h, 0.5 to 0.64 m at 140 to 170 km/h
(Opus's `SevereCrushProbe` figures, `83dad8b`). The pickup-at-the-wall observation matches: the player contacts the pickup's stale nose boxes (or its
own), not the crushed metal.

**Findings that explain related behaviour or will matter once the fix is in:**

1. **Recent high-speed crush made it worse, it did not cause it.** [derived] Before `83dad8b` dents capped near 0.3 m; they can now reach about 0.64 m per hit,
   and the arena raises crush capacity to 0.5 x 1.8 = 0.9 m (`DrivingGame.cs:76`, `LocalImpact.cs:49`). The mismatch was there since Milestone 1, but is now two to three times bigger.
2. **The width fix changes how the refit responds.** [hypothesis] The refit uses the median of the face cells. A narrow dent (under half the face) leaves the median at the
   undamaged value and the box would not have retracted even in the Lab. Opus's width fix makes dents cover the whole region, so the median will now move. Worth a check, not proven.
3. **Fixed core.** [derived] Region faces cannot retract past `inner + 0.06 m`, which is the front of the fixed core box (`coreFront = body.max.z - FrontDepth`). Crush deeper than the
   region depth (0.6 to 0.7 m; severe crush plus repeats can reach 0.9 m) leaves visible metal inside a fixed, invisible core wall. This is the opposite kind of mismatch and only appears in
   very heavy damage.
4. **Impact footprint maths assumes colliders are the contact surface.** [derived] `CollisionDamageReceiver` builds the next dent's footprint from the impactor's box colliders
   (`FillFace`, `combineImpactorBoxes`, `geometryAwareFootprint`). Today those boxes are stale; once they retract the crush depth of repeated hits will change (probably more accurate, but different).
   Opus should review this before the numbers are retuned.

## D. Existing deformation process

Trace, with `fixedDeltaTime` 0.02 s [confirmed from `TimeManager.asset`]:

1. Physics step k: `OnCollisionEnter` -> `Accumulate()` (`CollisionDamageReceiver.cs:162`). Contacts from the pair are gathered.
2. FixedUpdate k+1: `Step()` sees a vehicle-vs-vehicle event with `age < vehicleGatherSteps (1)` and waits one more step (this is Opus's width fix).
3. FixedUpdate k+2 (about 40 ms after first contact): `Process()` computes energy, momentum share and strength, then `model.AddImpact()` (`VehicleDamageModel.cs:233`):
   footprint resolution, then `AccumulateImpact` (per-vertex displacement added to a cached `impactSum`), then `OnImpactsChanged()` which calls `ApplyDeformation()` **synchronously** (`:328-333`).
4. `DeformablePart.Apply` (`:166-212`) writes `work[i] = base[i] + Limit(impactSum[i], cap[i])` for every vertex of each touched part, recomputes normals, and calls `SetVertices` once.
5. The next rendered frame shows the final dent. The hitbox is only told via `ImpactsChanged -> MarkPending` and is never applied (section B).

Why it looks instant:

- **One rebuild, final state.** The shape is a pure function of the impact sequence, so the engine goes straight to the end state.
- **Easing exists but is not used for impacts.** `VehicleDamageModel.Update` eases the four directional sliders with `responseSharpness`, and re-runs the full
  rebuild each frame (`:90-105`). Impacts bypass it on purpose (`:330`). Reusing it would not work anyway: a full rebuild per frame costs the whole solve every frame.
- **Not tied to contact duration.** Events are one `Process` per body pair with a 0.5 s cooldown; `OnCollisionStay` is not used. Holding a car pinned against a wall adds no further crush.
- **Rigid-body timing.** Cars use `bounciness 0` boxes, so most of the closing velocity should be gone within a few steps (derived from the material settings, not measured). The crush appears after the cars have already stopped, and the same frame carries the 18 to 58 ms solve cost (desktop WebGL pair measurements), which reads as a freeze followed by a pop.
- **Real contact time is too short to drive the animation.** Even if the engine could follow the contact, rigid boxes give 20 to 60 ms of contact. A visible duration (about 100 to 250 ms) has to be chosen by design, with the severity and closing speed available from the `Report` used to scale it. [derived]

Interruption: a second impact on the same car mid-animation is easy to support because the logical state is a deterministic function of the impact list. The display just restarts its ramp from what is currently shown.

## E. Proposed solutions

| Option | What | Pros | Cons / risks | Effort | Owner |
|---|---|---|---|---|---|
| **A. Repair existing** | Call `hitbox.ApplyPending()` at a safe point after each impact in `DrivingGame`. | Uses the validated Lab mechanism; tiny change; fixes gaps now; retraction only removes volume, so it cannot create overlaps. | Lab validation covered a sled, not two moving, driven cars; shape re-creation resets PhysX contact cache for a step; inertia tensor recalculates (auto); core wall and median-rule limits remain. | Hours to 1 day | Sonnet (Opus review only if validation shows jitter) |
| **B. Simplified colliders (new)** | Different collider scheme (convex hulls, more boxes). | Could be more accurate. | The 12-box scheme already does this job; replacing it is a core-engine change for no demonstrated need. | Large | Not recommended |
| **C. Progressive visual only** | Solve once as today, then blend displayed vertices from "before" to "after" over a chosen duration, with a per-vertex delay by depth so the bumper moves first and the hood follows. | Cheap per frame (one lerp plus upload); reuses the existing solve; handles interruptions. | Colliders would lag or lead the visuals unless coupled; per-vertex delay needs tuning to avoid "shrinking"; engine-internal. | 2 to 4 days | Opus |
| **D. Coordinated** | C plus the hitbox faces following the same progress value in a few discrete steps. | Visual and physical stay in step; best result. | Most moving parts; more validation. | C + 1 to 2 days | Opus (engine) + Sonnet (arena wiring, tests) |
| **E. GPU blend (alternative)** | Keep two vertex buffers and blend in a vertex shader with a progress property. | Near-zero CPU per frame. | Needs a custom shader for the URP Lit paint/stripes material; harder to maintain; only worth it if Option C costs too much on phones. | Large | Opus, only if measurements demand it |

## F. Recommended approach

1. **Option A now.** It is the missing half of an already-validated feature and the largest gameplay defect. Do it before progressive crush so that Phase 2 is tuned against correct colliders and so there is a measurable gap metric.
2. **Option C, then D.** Add a separate `shown` vertex buffer next to the logical `work` buffer in `DeformablePart`. `work` stays the final logical state (`DeformedVehiclePosition`, hitbox fitting and footprint resolution keep reading it). The solve cost stays where it is (one spike per impact); the ramp then costs about one lerp and upload per frame per affected car.
3. **Couple later (D).** Drive the hitbox faces from the same progress value: `face.current = lerp(previous, target, progress)` in two to four discrete updates, without re-measuring the mesh each time. Faces still only move inward, so no overlaps can be created.

Why not rebuild colliders every frame or replace the box scheme: the retract-only rule is what makes the current approach stable, and per-frame mesh/collider rebuilds would multiply a 20 to 60 ms solve cost by the number of animation frames on a single-threaded WebGL build.

**Safe point for Option A.** A reasonable rule: refit in `FixedUpdate` (before the physics step) once the car's last impact is at least 0.15 to 0.25 s old and a refit is pending, with a hard cap of about 1 s. Do not reuse the Lab's "relative speed below 0.3 m/s" rule as-is: in the arena a driven car can keep pressing on a pinned one at low relative speed forever. A refit while pressing is still safe because it only removes volume; the pusher then closes the small gap at its own speed (no lurch), and that closing is the behaviour the player expects.

## G. Implementation plan

| # | Milestone | Owner | Test |
|---|---|---|---|
| 1 | **Diagnostics (needs approval).** Re-enable `PhysicsHitboxDebug` in the arena behind a toggle, and add an edit-mode probe that runs the repeated head-on sequence and prints per-region gap (collider face vs deformed mesh) before and after the fix. | Sonnet | Baseline numbers from the current build. |
| 2 | **Refit at a safe point** in `DrivingGame` (per slot, after impacts; reset path unchanged). Add the gap, stability and reset checks to `DrivingValidation`. | Sonnet | Gap at most 3 to 5 cm after 1, 3 and 6 hits; no NaN or jitter; reset restores the undamaged boxes; existing 77 checks unchanged; Lab validations unchanged. |
| 3 | **Review of heavy-damage limits.** Core box vs crush deeper than the region depth; median rule for narrow dents; effect on repeated-hit crush depth. | Opus decides, Sonnet measures | Probe data from milestone 1. |
| 4 | **Progressive display in the engine** (`shown` buffer, ramp, per-vertex depth delay, interrupt handling, duration parameter; off by default so the Lab is unaffected). | Opus | Ramp reaches the exact logical state; repeat and reset exact; Lab results unchanged. |
| 5 | **Arena wiring.** Duration from impact severity and closing speed (roughly 100 to 250 ms), config exposed on `DrivingGame`. | Sonnet | Visual check on desktop and phone; per-frame cost. |
| 6 | **Couple colliders to the ramp.** | Opus + Sonnet | Gap metric stays within tolerance during and after the ramp. |

## H. Performance considerations

- **Solve spike (unchanged by A or C).** About 18 to 58 ms per pair on desktop WebGL; the asset spec quotes 55 ms/hit (muscle car, 62k vertices) and 85 ms (hatchback, 80k) in the editor, which runs `Parallel.For` (WebGL does not: `DeformablePart.cs:215-219`).
- **Hitbox refit.** Not measured in the arena. The code is sample-based over a limited face set and should be well under 1 ms per refit, but it allocates per call (`bool[]`, lists, `HashSet`, result arrays, interpolated summary strings) and re-creates up to 14 PhysX shapes. Measure ms, GC bytes and the physics step cost around it.
- **Progressive ramp (estimate, not measured).** About one lerp pass over the affected parts' vertices plus one `SetVertices` upload per frame (about 1 MB at 80k vertices), perhaps 1 to 3 ms per car on WebGL for 6 to 15 frames. Mitigations if needed: update the ramp at 30 Hz, only blend vertices that actually moved, interpolate normals instead of recomputing them, cap simultaneous ramps. Extra memory about 2 MB per car (two vertex arrays).
- **Several cars at once.** Impact solves still serialize on the main thread; spikes from several simultaneous hits add up. Measure with three or four cars damaged in the same second.
- **How to measure:** per-impact solve ms (already in `Report.deformMilliseconds`), per-frame ramp ms, hitbox ms and GC bytes, physics step ms; on desktop WebGL and on the target phone, which was the largest gap in earlier measurements.

## I. Testing strategy

- **Undamaged:** boxes match the visible bodywork within 2 cm; fresh car and repaired car identical to the original shape.
- **Repeated head-on:** 1, 3 and 6 hits on the same nose; after each, collider faces vs mesh gap and a push test that checks the cars touch where the metal is.
- **Heavily crushed front end:** severe-crush speeds (140 to 170 km/h) and capacity limit; check the fixed-core limit.
- **Offset impacts:** quarter and half overlap, narrow-dent median behaviour.
- **Side impacts:** side regions, T-bone, and a car pushed along a wall.
- **Pinned against the wall:** the reported pickup case; enemy continuing to drive in.
- **Progressive deformation:** ramp ends exactly at the logical state; a second hit during the ramp; reset during the ramp; frame-time trace.
- **Multiple simultaneous impacts:** three or four cars, wall plus car hits within one second.
- **WebGL performance:** desktop and phone; ms per frame during ramps, GC, memory.
- **Regression:** `DrivingValidation` (77), Collision Lab validations, `SevereCrushProbe`.

## J. Concerns and unresolved questions

- **Not reproduced.** The conclusion about the missing refit is complete from the code, but I did not run a before/after gap measurement or the repeated head-on sequence. That needs a new diagnostic (milestone 1), which I did not create without approval.
- **Lab stability does not automatically transfer.** The Lab's stability checks used a sled hitting a car; two driven cars pressing on each other, with ContinuousDynamic and the enemy's throttle, are untested against refits.
- **Inertia tensor.** Resizing colliders recalculates the Rigidbody inertia tensor automatically (the rig fixes only the centre of mass). The effect on handling was not checked.
- **Visual duration is a design decision.** Because cars stop in 1 to 2 physics steps, the ramp will play while the cars are already touching or sliding together. Do you want a purely visual ramp, or also a brief "hit-stop" style effect? I would start with the ramp only.
- **Heavy damage design.** Decide whether the core box should also follow very deep crush (Opus), or whether the "wreck stops crushing at about 0.7 m" behaviour is acceptable.
- **External research not done.** I did not research BeamNG or Wreckfest. I have not made any claims about their techniques.
