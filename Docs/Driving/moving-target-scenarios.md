# Moving-target collision scenarios

Extends the driving arena (`Assets/Driving/Scenes/DriveArena.unity`) with two moving-target test scenarios next to the existing stationary one.
Branch `feature/moving-target-scenarios` (built on `main` at `6c73a64`). No change to `Assets/DamageLab` (collision engine, deformation, receiver).

## What it is

| Scenario | Player starts | Target starts | Purpose |
|---|---|---|---|
| **PARKED** (existing, unchanged) | (0, -50) facing north | (0, -8), sideways, stationary | stationary collisions |
| **SAME WAY** | (52, -62) facing north | (52, -37), 25 m ahead, facing north, driving north | rear-end / same-direction hits |
| **HEAD-ON** | (52, -62) facing north | (52, 58), 120 m ahead, facing south, driving south | head-on hits |

Both moving lanes use x = 52 on the east side of the arena (all obstacles are 4+ m away). The target **drives with the same physics-driven
controller as the player** (`ArcadeCarController` + the vehicle's own `DriveProfile`, low-friction shell). `TargetDriver` only feeds throttle:
`Driving` (throttle follows desired speed minus current speed, steering held straight; the car also starts each run already at that speed as an
initial Rigidbody velocity), `Struck` (after a damaging collision it lifts off, no throttle or steering, so it just reacts to physics), `Stopping`
(at the end of the 95 m route it brakes to a stop and stays; a target that is never hit does not run into the wall). The target's transform is never written
by a script. It is not an AI: it does not look at the player or steer.

## How to test

1. Open the game (https://haydenmay.github.io/CarWreck/ once published, or `CarWreck > Build Driving Scene` and play `DriveArena.unity` in the editor).
2. In the menu: pick your car, the target car, the **behaviour row** (PARKED / SAME WAY / HEAD-ON) and the target speed with **- / +** (10-80 km/h in 10 km/h steps; default 40; the speed row is greyed for PARKED).
3. DRIVE. The target is already moving. Catch it from behind (SAME WAY) or drive at it (HEAD-ON).
4. **RESET** (button, R, gamepad Y) restores both cars, all damage and the scenario (the target is back at its start, driving again). CHANGE CAR / M reopens the menu.
Inspector: `DrivingGame.targetMode`, `targetSpeedKmh`, `routeLength`; `TargetDriver` shows the driver state.

## Validation

Automated (editor, real PhysX stepped by script; `CarWreck > Validate Driving`; 66/66 checks pass, 21 of them new):
* selection places both cars at the scenario spawn points (both modes);
* the target holds 30 and 60 km/h within 1.6 km/h on a straight line (lateral drift 0.01-0.02 m, heading drift under 0.1 deg) in both lanes;
* an unstruck target brakes to a stop at the end of its route (ran 98 m for a 95 m route, stayed inside the arena);
* same direction (muscle car at 92 km/h into a pickup at 39 km/h): closing speed 53 km/h as expected, both cars damaged through the existing receivers (12% / 54%), target velocity changes 39 -> 67 km/h, driver lifts off, the attacker takes the 15% momentum share;
* head-on (93 km/h into 39 km/h): closing 132 km/h as expected, strengths 87% / 98%, target velocity 39 -> -32 km/h, split 35% / 65% (both attacking);
* the target stayed within 0.02 m of its lane until impact; five repeated runs after reset are finite and consistent (same direction 12/12/11/12/10% and 54/54/52/54/51%; head-on identical every run);
* reset restores meshes, impact records and speed (target back at 38.9 km/h three seconds after reset, driver `Driving`);
* switching back to PARKED restores the original positions and a stationary target; the previous 45 stationary checks still pass.

Browser (desktop Chromium, local build of this branch, real mouse clicks on the new menu buttons, synthetic key events): SAME WAY and a 50 km/h target selected from the menu; DRIVE; target held 48.7 km/h; rear hit at 46 km/h closing damaged both cars (8% / 44%, deformation 22.7 / 15.3 ms), driver `Struck`; RESET cleared both cars and the target drove again; HEAD-ON at 140 km/h closing damaged both (92% / 98%, 22 / 36 ms, 58 ms for the pair); switching back to PARKED restored the stationary scenario. Not tested: a real phone, touch input on the new buttons (they use the same button class as the existing menu), the published site (this branch is not published).

## Notes for Opus (collision engine) and limitations

No engine problems were found; nothing in `DamageLab` was touched. Observations for the record:
* **Secondary impacts are real, not duplicates.** A same-direction hit usually pushes the target into the north wall (it was struck near the end of the lane); the log shows a second damaging event on the *target* "vs WallNorth" at 75 km/h (97%), then a lighter re-contact between the cars (23 km/h, 12%). In the head-on the target is also thrown back into WallNorth at 67 km/h. These are separate collisions with separate bodies.
* **Large combined hit cost.** Head-on at 140 km/h closing took 58 ms (22 + 36) on desktop; editor timings for the same hit are 77 / 169 ms. This exceeds the 40 ms hitch flag. Known deformation cost; nothing changed.
* **Near-saturation.** Head-on at 130-140 km/h puts both cars at 87-98% strength in one hit; very fast head-on hits cannot be told apart by damage.
* **Speed accuracy.** The target settles about 1.3 km/h under the setting (48.7 for 50) because the throttle follows the speed error without an integral term. Accurate enough for testing; documented rather than tuned.
* **Approach room.** The player needs roughly 5 s and 60-70 m to catch a 40 km/h target from 25 m behind, so same-direction contact happens in the northern half of the lane; faster targets move the contact north (a 60-80 km/h target may reach the route end before it is caught).
* The local test server returns one 404 (`version.json`, used only by the hosted page's stale-build check); no other console errors.
