# Milestone 3: first enemy driver

> **Superseded in part by [Enemy AI V2](enemy-ai-v2.md)**: the enemy now has a combat loop (LineUp, Reposition, Charge), obstacle-aware approaches and driver profiles. The text below describes V1; V1 driving, the arena-wall check and the stuck recovery are unchanged.

Branch `feature/enemy-driver` (built on `main` at `9c0ecb6`, which includes Opus's car-vs-car dent-width and high-speed crush work). Nothing in `Assets/DamageLab` was changed.
Preview: https://haydenmay.github.io/CarWreck/ (build `20261016-0741-b062b23`).

## What it is

A fourth target behaviour next to PARKED / SAME WAY / HEAD-ON: **ENEMY**. One AI driver in the pickup (or whichever car is picked as the target) tries to ram the player. It is the same kind of vehicle as before:
the target slot is built by `VehicleRig` and driven by its own `ArcadeCarController` with the vehicle's `DriveProfile`; `EnemyDriver` only writes the controller's throttle/brake and steering inputs, exactly like the player's keys.
The enemy's transform is never written, there are no special collision rules, and both cars use the same receivers (momentum split, severe crush, footprint union).

## Controls

Menu: pick your car, the **target car** (the enemy defaults to the Compact Pickup; any of the three works), press **ENEMY** in the behaviour row (PARKED / SAME WAY / HEAD-ON / ENEMY), optionally set the enemy's top speed with **- / +** (10-80 km/h, default 70), then **DRIVE**.
Start: player at the normal start pad (0, -50) facing north; enemy 61 m away at (-18, 8) pointing at the player, waiting 1.5 s. **RESET** / R / gamepad Y restores both cars (poses, velocities, all damage) and restarts the enemy; CHANGE CAR / M reopens the menu. The other three behaviours are unchanged.
Inspector (`DrivingGame`, "Enemy encounter"): `enemySpeedKmh` (70), `enemyStartDelay` (1.5 s), `enemyFullLockAngle` (35 deg, smaller = twitchier), `enemyAttackCommitDistance` (14 m), `enemyRecoverDuration` (1.3 s), `enemyReactionTime` (0.4 s, higher = easier to fool with a swerve).

## AI behaviour (`EnemyDriver`)

* **Pursue:** steers toward where the player will be shortly (the player's velocity as the enemy sees it through a 0.4 s low-pass, led by at most 0.6 s), full lock at 35 deg of heading error, throttle toward the speed setting. Eases off (or creeps) when it is pointing well away from the player so it can swing round instead of running wide. Closer than 10 m it softens steering so it does not spin in circles.
* **Commit:** inside 14 m, within 25 deg of the target line and at speed over 7 m/s it locks the attack line and stops re-aiming (only 0.35x steering corrections), full throttle. The line can be dodged. It ends on impact, when the player has been passed, or after at most 2.2 s.
* **Overshoot:** after a hit or a miss it keeps rolling about straight for 0.6 s (no teleport, no instant U-turn), then goes back to Pursue and turns round.
* **Recover:** throttle on but speed under 1.5 m/s for 0.9 s (wall, obstacle, nose-to-nose, pushed against something) triggers 1.3 s of reversing while swinging the nose toward the player, then Pursue again.
* **Walls:** if the car's position about a second ahead is within 7 m of the arena edge it aims at the middle of the arena and caps its throttle.
* A flipped enemy just sits there (no teleporting). The enemy never writes its transform and uses no NavMesh.

## Tests (editor simulation, real PhysX, `CarWreck > Validate Driving`; 77 of 77 checks pass; the previous 66 plus 11 new)

| Test | Result | Detail |
|---|---|---|
| ENEMY mode spawns exactly one driven pickup 61 m from the player | PASS | one `EnemyDriver`, same controller |
| A. pursuit of a player driving away at 43 km/h | PASS | distance 61 m to 43 m after 6 s, best heading error 7 deg |
| B. attacks a stationary player | PASS | first hit after 5.6 s, 2 attacks committed, 72 km/h closing; enemy 21% / player 76% (momentum split) |
| C. evasion (head-on approach, swerve at 32 / 26 / 21 m) | PASS (with caveat) | swerve at 32 m missed; at 26 m and 21 m still hit; in all three runs the enemy overshot/recovered and came back (attacks 1-2, closest approach 6-7 m) |
| D. wall recovery (5 m from the wall facing it; pressed against the wall nose-in) | PASS | 112-125 m away after 18 s; the nose-in case needed 1 stuck recovery |
| E. enemy hit damages both through the existing receivers | PASS | impact records 2 / 2, deformation 0.28 m / 0.22 m |
| F. player rams the enemy | PASS | both damaged; the enemy then drove off at 64 km/h and kept fighting |
| G. reset x3 | PASS | meshes, impacts, poses, velocities, enemy state restored; exactly one `EnemyDriver` each time |
| H. vehicle selection / parked / moving scenarios | PASS | enemy vehicle can be changed (hatchback); switching back to PARKED restores the stationary scenario |

Browser (local build, then the published site; desktop Chromium; real clicks on the menu, synthetic key events): ENEMY mode selected from the menu; the enemy waited 1.5 s, accelerated to 68 km/h, committed and hit the idle player at 72 km/h closing (player 76%, enemy 21%, 29 + 17 ms = 46 ms total); a player who swerved at 30 m was still hit by the tracking enemy in the Pursue state; reset (twice) restored both cars and put the enemy back to Waiting with its counters cleared. Not tested: a real phone, touch play against the enemy, long sessions.

## Known issues and notes

* **Dodging is hard head-on.** With the player and enemy driving at each other the closing speed is 100+ km/h, so a swerve under ~0.7 s before contact cannot clear the car. The AI is aggressive on purpose; how dodgeable it is needs human feel testing. Easy dials: `enemyReactionTime`, `enemyFullLockAngle`, `enemySpeedKmh`, `enemyAttackCommitDistance`.
* **Ramming from the receiving side:** the enemy's attack at 72 km/h leaves the player at about 76% and the enemy at about 21% (the attacker takes the small momentum share). This is the engine behaving as designed, but the owner wants to judge whether being rammed is frustrating.
* **Deformation cost:** the first enemy hit took 46 ms for the pair on desktop WebGL (29 + 17), above the 40 ms hitch flag. Editor timings 69-132 ms. No change.
* A flipped enemy stays flipped until RESET; there is no right-itself behaviour. The enemy can push the player into a wall repeatedly with no damage cap (no game over).
* Enemy hits during Pursue (not only Commit) are common against a player who is already moving across its path.
