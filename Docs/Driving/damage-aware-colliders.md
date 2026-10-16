# Damage-aware colliders in the driving arena

Background: [investigation](investigation-collision-geometry-and-progressive-deformation.md). The Collision Lab's `VehicleHitbox` (12 adaptive boxes per car that follow the
crushed mesh) existed but the arena never applied it after a hit. This change connects it. No collision-engine, deformation, damage, vehicle, AI or asset code was touched.

Measurements below are **runtime observations** from real PhysX (`Physics.Simulate` at 50 Hz) and the real deformed meshes in the Unity editor, taken with
`ColliderGapProbe`. Anything derived rather than measured is labelled.

## A. Root cause (confirmed against the current code)

`VehicleHitbox` only changes its boxes inside `ApplyPending()`. It has no `Update`; the model merely sets `HasPending` when an impact is added. In the Collision Lab the
controller calls `ApplyPending()` from `AfterPhysicsStep` (once the block has settled). `DrivingGame` called it only at spawn (`Setup`) and in `ResetSlot`. So in the arena the boxes
stayed at their undamaged size for the whole session while the mesh deformed. The investigation's other findings were confirmed by measurement (sections D and G).

## B. Implementation

`DrivingGame` (the only runtime file changed):

* `FixedUpdate()` calls `AfterPhysicsStep(Time.fixedDeltaTime)`. FixedUpdate runs before the physics step, so shapes are never edited while the solver is working. The edit-mode validation calls the same method after each `Physics.Simulate`.
* For each car (player and target, so the enemy too): if its `VehicleHitbox` has a pending refit, wait until the car has had no new damaging hit for `colliderSettleDelay` (0.2 s), or `colliderMaxDelay` (1 s) after the first of a burst, then call the existing `hitbox.ApplyPending()` once.
* `OnImpact` (damaging hits only) restarts the wait. `ResetSlot` still calls `ApplyPending()` and clears the timers.
* `refitColliders` (default on) switches the whole thing off, which reproduces the old arena exactly. It is the "before" setting used by the probe.

Why not the Lab's rule (relative speed of the block below 0.3 m/s): the arena has no sled; a driven car can keep pressing on a pinned car at low relative speed indefinitely, so that rule could wait forever. A timed delay after the hit gives the
solver the same quiet moment. Retraction only removes volume, so applying it while two cars press together cannot create an overlap (the pressed-pair test below confirms it is stable). Calling `ApplyPending()` directly inside the collision callback was avoided: it would edit
shapes during the solver step and, because a gathered vehicle-vs-vehicle event is applied one step after first contact, before the hit had finished resolving.

Files changed:

| File | Purpose |
|---|---|
| `Assets/Driving/Scripts/DrivingGame.cs` | Refit scheduling (`FixedUpdate`, `AfterPhysicsStep`, per-car timers), three inspector fields, per-car `refits` counter. |
| `Assets/Driving/Editor/ColliderGapProbe.cs` (new) | Diagnostic: measures collider-vs-visible gap, off and on. Editor only; menu `CarWreck > Diagnostics > Collider Gap Probe`. |
| `Assets/Driving/Editor/DrivingValidation.cs` | Calls `AfterPhysicsStep` in `Step`; 12 new checks. |
| `Docs/Driving/damage-aware-colliders.md`, `investigation-...md` | This report; the investigation brought over from its branch. |

## C. How the gap is measured

For 13 lanes across the nose x 3 heights: collider z = where a ray along the car meets the car's real `BoxCollider`s (`Collider.Raycast`); visible z = the front-most deformed vertex within 8 cm / 10 cm of the lane.
Gap = collider z - visible z (positive = invisible wall in front of the metal). Because the undamaged shell is a plain box around a rounded nose (mean 0.011 m), the figures are "excess gap" = gap minus the undamaged gap of the same lane.
Muscle car (player) vs compact pickup (target), head-on in open ground, each car at half the closing speed. Refit OFF is the arena before the fix.

## D. Before and after (metres)

Pickup (struck) / muscle car; "crushed lanes" are lanes with at least 2 cm of crush. Negative = the box has retracted slightly behind the metal.

| Scenario | Crush max / mean | Before: gap max / mean | After: gap max / mean | Result |
|---|---|---|---|---|
| Undamaged | 0 | 0 / 0 (box 0.011 m proud of the rounded nose) | 0 / 0 | unchanged, shell pristine |
| Moderate (60 km/h closing) | 0.069 / 0.059 (pickup), 0.063 / 0.037 (muscle) | 0.069 / 0.059, 0.063 / 0.037 | 0.021 / 0.008, 0.039 / 0.006 | gap closed |
| Severe (170 km/h closing) | 0.558 / 0.456, 0.563 / 0.480 | 0.558 / 0.456, 0.563 / 0.480 | 0.072 / -0.017, 0.051 / -0.028 | about 0.5 m gap removed |
| Repeated (4 x 100 km/h) | 0.532 / 0.407, 0.514 / 0.457 (after hit 4) | 0.532 / 0.407, 0.514 / 0.457 | 0.078 / -0.065, 0.031 / -0.018 | gap removed |
| Offset (100 km/h, 0.9 m) | 0.282 / 0.194, 0.260 / 0.197 | 0.282 / 0.194, 0.260 / 0.197 | 0.043 / -0.040, 0.046 / -0.017 | gap removed; only struck faces moved |
| Two damaged cars pressed nose to nose (driven, 3 s) | n/a | **0.72 m** of daylight between the front-most metal (0.985 m at low and mid height); colliders touching | colliders touching; **0.07 m** daylight at low and mid height, but the upper edge of the metal **overlaps by 0.20 m** (front-most -0.21) | the gap is gone; some upper-metal overlap appears (limitation 3) |

Repeated impacts meet the updated boundary: the struck car's contact z (where the colliders actually met) was 2.350 on every hit before the fix and is 2.350 -> 2.171 -> 1.999 -> 1.921 after it. The retracted shell changes the damage numbers only slightly
(second-to-fourth hit crush travel 0.239 / 0.154 / 0.138 m before, 0.238 / 0.148 / 0.132 m after).

## E. Validation

`CarWreck > Validate Driving`: all checks pass. 76 earlier PASS lines still pass (earlier notes said 77; the line count is 76 in this log) plus 12 new, 88 in total, 0 FAIL.

| Test | Result | Evidence |
|---|---|---|
| A. Undamaged collisions unchanged | PASS | undamaged shells pristine; first-hit contact z 2.350 and strength 76.4 % identical with the refit off and on |
| B. Moderate damage | PASS | front-centre face retracted 6.0 cm (pickup) / 2.4 cm (muscle), gap to the metal 0.004 / 0.000 m, one refit each |
| C. Severe damage | PASS | faces retracted 50.3 / 51.0 cm, centre-lane excess gap -0.005 / -0.054 m |
| D. Repeated impacts | PASS | contact z 2.350 -> 2.178 -> 2.011; exactly 3 refits for 3 hits per car |
| E. Player and AI vehicles | PASS | the enemy is refitted like any car (2 refits after its attack). Its own attack hit only 14 % strength, 0.23 m wide, 0.065 m deep, which leaves the median-fitted face unmoved (limitation 2); when the player rams the enemy hard (88 %) its face retracts 21 cm |
| F. Offset impact | PASS | pickup front faces left 23 / centre 22 / right 0 cm, rear faces 0 cm, 1 refit |
| G. Reset x3 | PASS | shells, meshes, poses, velocities restored 3 of 3; the same hit retracts 50.3 / 50.4 / 50.3 cm |
| H. Collision Lab regression | PASS | no Lab code changed. `verify`, `collision-verify`, `collision-geometry`, `collision-persist-a/b`, `collision-overflow`, `vehicle-switch` run: 52 PASS lines, 0 FAIL (batch mode with graphics; `-nographics` crashes the screenshot step, an existing limitation) |
| I. Driving validation | PASS | see above |
| J. Performance | PASS (editor only) | see F |
| Scheduling | PASS | hit registered at step 8, shell refitted 0.20 s later (never inside the hit's own steps) |
| Switch | PASS | `refitColliders` off: no face moves and the refit stays pending; on: the pending refit is applied |
| Stability (pressed pair) | PASS | max angular speed 0.01 to 0.56 rad/s, max vertical speed 0.10 m/s, no NaN |

## F. Performance

* Refit cost (editor, one `ApplyPending` per damaging hit, 15 refits in the probe): **1.43 ms average, 2.3 ms maximum**; at most 9 faces refitted at once. The Lab measures 1.39 ms average / 1.56 ms maximum for the same call.
* Allocations (Mono used-size delta around the call, approximate): 0.5 KB average, 4 KB maximum per refit.
* Exactly one refit per damaging hit per car: 15 hits -> 15 refits on the target, 13 on the player (the pole hits only touched the target). No refits while no deformation is pending.
* Physics: no instability seen in any run, including the driven pressed pair. The physics step cost was not separately measured in the arena.
* **WebGL was not built or measured.** Editor timings use multithreaded code paths for the mesh deformation; the refit itself is single-threaded either way, but do not read these numbers as WebGL numbers.

## G. Remaining limitations (measured unless marked derived)

1. **Fixed core.** The front regions cannot retract past `core front + 0.06 m` (pickup z 1.752, muscle z 1.874). That is a retraction of about 0.60 m from the nose. One severe hit (about 0.56 m) stays below it; the second severe hit reaches it. After that the box face sits 3 cm
   in front of the centre metal and the largest lane gap grows 0.07 -> 0.135 m by the sixth severe hit (before the fix the same hits left a 0.55 to 0.73 m gap). Four repeated 100 km/h hits retract 52 cm, still below the limit. So it affects repeated or extreme damage only, with a remaining gap
   of at most about 0.14 m; ordinary hits are not limited by it. Whether the core should follow is a separate decision.
2. **Median fit and narrow dents.** A 0.15 m pole into the centre of the pickup's nose (dent 0.28 m deep) retracted the whole 0.6 m wide centre face by 19 cm: centre lane +0.09 m gap (box in front of the dent), and at the dent's shoulders (crush 0.02 m) the box sits 0.16 m behind the intact metal.
   The same pole into the middle of a 1.13 m long side region made a 0.34 m dent but retracted that side face by only 3 cm (the median cells barely moved), so about 0.3 m of invisible wall remains there (derived from dent depth minus retraction; not measured as a lane gap). The enemy's 0.23 m wide, 6.5 cm dent moved no face at all.
3. **Faces are flat from top to bottom.** Crush varies with height, so after a fit the upper edge of the metal can stick out in front of the box. Two heavily damaged cars pressed together overlap by about 0.2 m at that level while showing 0.07 m of daylight lower down. This is the existing fitting rule, working as designed.
4. **Not measured:** rear and side impacts between cars (only the pole side test and the front scenarios were), contacts with arena walls (a wall is an ordinary Rigidbody contact and uses the same path), the user's exact pickup-at-the-wall report (the pressed pair is the closest analogue), mobile.

## H. For progressive crushing (Opus)

* The refit reads `DeformedVehiclePosition`, which is the **final** solved state, and is triggered by `ImpactsChanged` (when the impact is added). If the display later lags the solve, the colliders would retract at the final state while the metal is still arriving unless the schedule moves. `colliderSettleDelay` (0.2 s) is a simple knob; a ramp longer than that needs a coupling (for example a few discrete refit steps tied to the ramp progress) rather than rebuilding every frame: one refit costs about 1.4 ms and recreates up to 9 PhysX shapes.
* Keep the retract-only rule. It is what makes refitting while cars press on each other safe (no overlap can be created); a collider that grew back or moved outward would not be.
* Receivers read the other car's box colliders for the dent footprint (`FillFace`, `combineImpactorBoxes`). Retracted boxes change that input for repeated hits (small so far: travel differs by up to 6 mm). A ramp that moves boxes mid-contact would change it during an event.
* `AddImpact` and the refit are both counted in the same fixed step schedule: the gathered vehicle event is applied 2 steps after first contact, the refit 10 to 11 steps after the hit. Moving either changes which hit sees which shell.
* Probe and validation both read `part.DeformedVehiclePosition`; if a second "displayed" buffer is added, that accessor is the one place to keep meaning "logical final state".

## I. Branch

`fix/driving-damage-aware-colliders`, based on `main` (0682310). Not merged, no WebGL build published. The DriveArena scene was not rebuilt or edited: the new inspector fields take their script defaults when the scene loads (the validation runs against that scene). A WebGL build would pick the same defaults; rebuilding the scene is optional.

## J. Recommendation

Ready for review. The change is small (one runtime file), reversible (`refitColliders`), and the measured gap drops from 0.06 to 0.56 m of invisible wall down to under 0.08 m in all front scenarios, with repeated hits colliding against the damaged shape. Review points: the 0.2 s settle delay, the upper-edge overlap and the narrow-dent behaviour (limitations 2 and 3), and a mobile/WebGL check before publishing.
