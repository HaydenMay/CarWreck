# Gameplay tuning V1: pickup handling and AI breakaway

Branch `feature/gameplay-tuning-v1` (from `main` at `bd2b1a9`). Not merged, not published.

Two targeted changes. The pickup turns and accelerates better (its numbers, nothing else about the vehicle). AI drivers can now break away from a cluster that is going nowhere, open up a run-up elsewhere and come back through the normal combat loop. Collision, damage, deformation, scoring, respawn, targeting and the match modes were not touched.

## A. Summary

| | Before | After |
|---|---|---|
| Pickup turning radius at 15 / 20 m/s | 21.0 / 37.4 m (1.5x / 1.7x the muscle car, 1.9x / 2.1x the hatchback) | 16.0 / 26.5 m (1.16x / 1.18x the muscle car, 1.45x the hatchback) |
| Pickup U-turn at 15 m/s | 4.82 s over 42 m | 3.66 s over 32 m |
| Pickup 180 degrees from rest, full lock | 4.58 s | 3.80 s |
| AI in a clump | back off, charge, repeat | the same, plus a profile-dependent chance to leave for open ground and return with a run-up |

## B. Pickup handling

### Investigation

A new probe (`CarWreck > Diagnostics > Handling Probe`, `HandlingProbe.cs`) drives each vehicle on flat ground with full lock at set speeds, with a simple throttle controller holding the speed, and records yaw rate, turning radius, how much of the commanded yaw rate the body actually achieves, steering onset, U-turn, turn from rest and acceleration. Original numbers (commit `bd2b1a9`):

| | Muscle | Hatchback | Pickup (original) |
|---|---|---|---|
| radius at 6 / 10 / 15 / 20 m/s | 4.0 / 7.7 / 13.8 / 22.5 m | 3.2 / 6.1 / 11.0 / 18.2 m | 5.5 / 11.0 / 21.0 / 37.4 m |
| achieved / commanded yaw rate at 15 m/s | 88 % | 90 % | 79 % |
| steering onset (90 % of final yaw rate at 15 m/s) | 0.18 s | 0.14 s | 0.30 s |
| U-turn at 15 m/s | 3.12 s, 27.6 m | 2.48 s, 22.1 m | 4.82 s, 42.0 m |
| 180 degrees from rest | 3.42 s | 2.66 s | 4.58 s |
| 0-20 m/s | 1.82 s | 1.52 s | 3.00 s |

What explains it, in order of effect:

1. **The steering rates in the pickup's `DriveProfile`.** `steerRateLow 78` / `steerRateHigh 26` deg/s against the muscle car's 100 / 34. The controller fades the yaw rate linearly with speed over 0 to `maxSpeed`, and the pickup's lower `maxSpeed` (26) makes the fade start sooner (at 15 m/s the pickup is 58 % of the way to top speed, the muscle car 50 %). Both effects compound: at 20 m/s the pickup was commanded 40 deg/s against the muscle car's 58.
2. **Steering response.** `steerRise 3.2` /s took 0.31 s to reach full lock (muscle 5, 0.18 s); `steerResponse 22` against 28 left the body at 79 % of the commanded yaw rate.
3. **Acceleration.** `acceleration 9.5` (0-20 m/s in 3.0 s) made every reposition slow.

No bug in the shared controller: torque and forces are applied as accelerations, so mass and wheelbase do not enter the steering at all. One thing worth knowing, not changed: every vehicle achieves only 79-90 % of its commanded yaw rate because ground friction on the hull resists yawing. It is the same code for every vehicle.

### Changes (`Assets/Driving/Profiles/Pickup.asset`, mirrored in `DrivingSceneBuilder`)

| Setting | Original | New | Why |
|---|---|---|---|
| steerRateLow | 78 | 90 | the main cause; still below the muscle car's 100 |
| steerRateHigh | 26 | 36 | the main cause; muscle 34 at a higher top speed, so the pickup still turns wider at speed |
| steerRise | 3.2 | 4.5 | onset 0.30 -> 0.22 s so the truck answers the wheel (muscle 5, hatchback 7) |
| steerResponse | 22 | 26 | closes part of the gap between commanded and achieved yaw rate |
| acceleration | 9.5 | 11 | 0-20 m/s 3.00 -> 2.48 s (muscle 1.82 s): quicker repositioning, still the slowest car |
| reverseAcceleration | 8.5 | 9.5 | backing out of a clump |

Unchanged on purpose: mass, `maxSpeed` 26, braking 19, grip 7 / max grip 20 (the truck still slides wide and feels planted), coast, steering return, downforce. The muscle car and hatchback profiles are untouched.

### Result

| | Muscle | Hatchback | Pickup (new) |
|---|---|---|---|
| radius at 6 / 10 / 15 / 20 m/s | 4.0 / 7.7 / 13.8 / 22.5 | 3.2 / 6.1 / 11.0 / 18.2 | 4.5 / 8.8 / 16.0 / 26.5 m |
| steering onset | 0.18 s | 0.14 s | 0.22 s |
| U-turn at 15 m/s | 3.12 s, 27.6 m | 2.48 s, 22.1 m | 3.66 s, 32.1 m |
| 180 degrees from rest / 90 degrees | 3.42 / 1.40 s | 2.66 / 1.10 s | 3.80 / 1.62 s |
| 0-15 / 0-20 m/s | 1.22 / 1.82 s | 1.04 / 1.52 s | 1.50 / 2.48 s |

The order at every speed is hatchback, muscle car, pickup. The pickup is 16 % / 18 % wider than the muscle car at 15 / 20 m/s (it was 50 % / 66 %), its U-turn takes 17 % longer and its acceleration is the slowest by a clear margin. It did not become twitchy: the highest lateral acceleration it can need (v x yaw rate, about 14 m/s2 at 20 m/s) stays under its grip limit of 20.

Attack viability (test D): the AI Hunter driving the pickup chases a hatchback circling the arena at 10 m/s, 6 runs of 40 s each, old handling against new handling in the same scenario:

* old handling: 32 charges, **0 hits**, none of 27 charges ended within 6 m of the hatchback, 31 stuck recoveries;
* new handling: 24 charges, **5 hits (all at 8 m/s closing or more, mean 49 km/h)**, 4 of 21 charges ended within 6 m, 19 recoveries.

Side effect, deliberate and visible: the enemy pickup of the old driving arena is now harder to dodge. In the driving validation's evasion test a late swerve (32, 26, 21 m) no longer makes it miss; an early one (44, 38 m) still does. The test now swerves at 44 and 38 m as well (see I).

## C. AI breakaway

All in `EnemyDriver` (a new `Breakaway` state and a few members), wired to the game by `DerbyGame.WireDriver`. In the driving arena the game supplies nothing, so breakaway is off there and the enemy behaves exactly as Enemy AI V2 did.

### Signals (a single number, "pressure", per driver)

* adds: a back-off (+1.0), a stuck recovery (+1.2), a weak contact below 5 m/s closing (+0.7), and 0.25 per second while **milling** (stayed within 8 m for the last 6 s with the target within 12 m);
* takes away: a real hit (7 m/s closing or more and at least the driver's minimum useful impact speed) removes 0.5 (Cautious) to 1.2 (Brawler), so a productive fight keeps the pressure low, most of all for aggressive drivers; and it decays by 0.1 per second.

Nothing is queried for this: it uses the driver's own events and a 12-sample position ring. Close range alone never triggers it.

### When it acts (checked on the existing 0.12 s decision tick, only while Pursue / LineUp / Overshoot)

1. pressure at or over the profile threshold, and not in cooldown;
2. a crowd: two or more other living cars within 12 m, or pressure at 1.1x the threshold (a two-car lock);
3. the profile's chance roll (a failed roll takes 40 % off the pressure and waits);
4. the game's cap: at most about a third of the field breaks away at once (`(count + 1) / 3`, so 2 of 5);
5. a destination exists.

### Destination

Candidate points about one charge distance (28-31 m) from the target, 6 to 12 bearings depending on skill. A candidate is dropped when: it is outside the arena margin, closer than 12 m to the driver, has a car within 10 m, the path (beyond the first 7 m) passes within 4.5 m of a car, the straight line is blocked by an obstacle (the existing sphere cast), or another driver is already heading within 14 m of it. The rest are scored: open space, a new angle on the target (weighted by skill), distance from the wall, less travel, less turning, plus noise that shrinks with skill. A driver pinned nose to nose with the destination behind it reverses straight out (steering mirrored) instead of crawling round.

### Execution, return and safeguards

* It drives to the point with the same steering, throttle, wall and obstacle code as Pursue, at 85 % of the speed setting; combat and detour planning are suspended. Obstacle recovery still works (a stuck recovery aborts the breakaway).
* It ends on arrival (within 7 m), on a timeout (8 s Cautious to 6 s Brawler), after 3 s without 1 m of progress, or on a stuck recovery. A failed attempt lengthens the next cooldown (10-14 s base, times 1 + failures).
* It then returns to **Pursue**, so line-up, charge, prediction and commit are the existing Enemy AI V2 code; the wait for a good attack is partly used up (more so for aggressive drivers) so they commit quickly.
* Target changes during a breakaway are allowed; the target is simply the new one at the end. The target selector does not count breakaway time as "no progress".
* No forces or teleports anywhere: a check scans the AI and game code for `AddForce` / `AddTorque` and finds none.

### Cost

One distance test per decision tick plus, only when a breakaway is considered, one pass over the other cars and 6 to 12 candidates with one sphere cast each (worst 0.05 ms). Per `Drive` call: mean 4.2 -> 4.4 microseconds, worst 0.12 -> 0.14 ms, 0 bytes allocated.

## D. Driver profiles

Everything derives from the two existing numbers (aggression `a`, skill `s`), in `EnemyDriver.ApplyProfile`:

| Value | Formula | Brawler (.85 / .50) | Hunter (.65 / .90) | Cautious (.25 / .50) | Rookie (.60 / .25) |
|---|---|---|---|---|---|
| pressure threshold | lerp(1.6, 3.4, a) - 0.6 s | 2.83 | 2.23 | 1.75 | 2.53 |
| chance when it is met | lerp(.95, .5, a) + .1 s | 0.62 | 0.75 | 0.89 | 0.71 |
| real-hit relief | lerp(.5, 1.2, a) | 1.10 | 0.96 | 0.68 | 0.92 |
| cooldown / longest time | lerp(14, 10, a) / lerp(8, 6, a) | 10.6 / 6.3 s | 11.4 / 6.7 s | 13.0 / 7.5 s | 11.6 / 6.8 s |
| destination bearings tried | lerp(6, 12, s) | 9 | 11 | 9 | 8 |
| destination noise | lerp(.6, .05, s) | 0.33 | 0.11 | 0.33 | 0.46 |

* **Brawler** leaves last (highest threshold, lowest chance, real hits clear its pressure), takes the shortest trip and returns having used up the most of its wait, so it commits quickest.
* **Hunter** tries the most bearings with the least noise and weighs a new angle on the target; it leaves after a moderate amount of poor fighting.
* **Cautious** leaves first and most reliably but still charges (see tests L).
* **Rookie** has its own threshold, the noisiest choice and the fewest candidates.

## E. Files changed

`Assets/Driving/Scripts/EnemyDriver.cs` (breakaway, pressure, milling, cost counters; Drive now wraps `DriveInner`), `Assets/Derby/Scripts/DerbyGame.cs` (`WireDriver`, nearby cars, cap, destination test, page report), `Assets/Derby/Scripts/AiTargeting.cs` (breakaway time is not "no progress"), `Assets/Driving/Profiles/Pickup.asset` and `Assets/Driving/Editor/DrivingSceneBuilder.cs` (pickup numbers), `Assets/Driving/Editor/DrivingValidation.cs` (evasion test distances), new `Assets/Driving/Editor/HandlingProbe.cs`, new `Assets/Derby/Editor/TuningValidation.cs`, this document.

## F. Test results

`CarWreck > Validate Gameplay Tuning` (`TUNING_ONLY=handling|pickup|ai|ffa|melee|perf`): 24 PASS, 0 FAIL in the full run, and the pickup test D passes in the final configuration. Real PhysX at 50 Hz in the editor; arena physics is not repeatable run to run, so statistical checks use margins and a control arm.

| Test | Result | Evidence |
|---|---|---|
| A. Pickup low-speed turning | PASS | 180 degrees from rest in 3.80 s (4.58 before), 6 m/s radius 4.5 m (5.5) |
| B. Pickup high-speed control | PASS | radius 16.0 / 26.5 m at 15 / 20 m/s, onset 0.22 s, never beyond its grip limit |
| C. Vehicle differentiation | PASS | hatchback < muscle < pickup at every speed; pickup U-turn 32.1 m against 27.6 and 22.1; slowest acceleration |
| D. Pickup attack viability | PASS | 0 -> 5 hits against a running hatchback, 0 -> 4 charges ending within 6 m |
| E. Other vehicles keep their handling | PASS | muscle 13.8 / 22.5 m and hatchback 11.0 / 18.2 m, identical to before; their profile assets untouched |
| F. Cluster detection | PASS | in a real clumped melee (10 x 40 s) the pressure passes every driver's threshold with the previous AI (peaks 3.0 - 8.0 against thresholds 1.75 - 2.83) |
| G. Breakaway | PASS | forced: every profile leaves (over 20 m away) and completes the manoeuvre in most runs (Brawler 2 of 3, Hunter 2 of 3, Cautious 6 of 6, Rookie 3 of 3) |
| H. Re-engagement | PASS | after a breakaway every profile charges again within 12 s in at least 2 of 3 runs (typically 2-8 s) and lands real hits afterwards (14-15 per profile) |
| I. No endless retreat | PASS | at most 2 breakaways in 30 s, 2-8 s in breakaway, 3+ charges per run; in melee 2.3-2.5 % of driving time is breakaway; all drivers keep charging |
| J. Brawler | PASS | no breakaway in a productive 22 m-run-up fight (6 strong hits); least eager profile; 1 breakaway in 10 melees against Cautious 7, Hunter 5, Rookie 3 |
| K. Hunter | PASS | returns from a bearing 63 degrees different from the contact bearing on average |
| L. Cautious | PASS | breaks away most (7 in 10 melees) and still charges as often (22 -> 27 charges); in the forced test 3 charges per run |
| M. Rookie | PASS | breaks away, returns, charges 3-6 times; its own threshold, chance and noise |
| N. Five-vehicle free-for-all | PASS | AI cars keep hitting one another (every driver 35-44 hits landed over 10 melees); hits per melee 50 -> 47 against a control arm that measured 46 on identical code: noise, not a reduction |
| O. Player targeting | PASS | no targeting code changed; the derby validation's targeting checks (swap of control types, 3000 random layouts) pass unchanged |
| P. Obstacle recovery | PASS | derby check I (wall and corner) and Enemy AI V2 validation 14 / 14 |
| Q. Match regression | PASS | derby validation 79 / 79 (both modes, showdown, results, restart, menu) |
| R. Scoring regression | PASS | the scoring and duplicate-contact checks inside those 79 |
| S. Respawn regression | PASS | respawn and fresh-geometry checks inside those 79 |
| T. Deformation regression | PASS | progressive crushing 34 / 34, Collision Lab 62 PASS / 0 FAIL, driving collider checks, derby paint and respawn geometry checks |
| U. Performance | PASS | decision cost 4.2 -> 4.4 microseconds mean, worst 0.12 -> 0.14 ms, 0 bytes per call; destination choice worst 0.05 ms |
| V. WebGL | PASS (local) | build 22.1 MB, 0 errors; launched in the browser pane with the pickup, five cars, countdown and play, page report shows breakaway counts; frame rate not measurable there, no phone used |
| Other regressions | PASS | driving validation 88 / 88 (after the evasion test change below) |

One existing test changed: the driving validation's evasion test (Enemy AI V2, pickup enemy) required that at least one of swerves at 32 / 26 / 21 m makes the enemy miss. With the more agile pickup, none of those three does. The test now also swerves at 44 and 38 m (both make it miss) and still requires at least one miss. This is the visible cost of the pickup change and is reported, not hidden.

## G. Gameplay observations

* **Scenario 1 (pickup against smaller AI):** the truck now reaches hatchbacks that run (5 hits in 240 s where it had none) and recovers from walls with 19 instead of 31 stuck recoveries, while still turning visibly wider than the muscle car and the hatchback.
* **Scenario 2 / 6 (clump, surrounded):** in clumped melees about one driver in five leaves in a given 40 s, Cautious most often, Brawler almost never; the others keep fighting.
* **Scenario 3 (all five in a pileup):** five cars started within 6 m of each other stayed together for 4.7 s and had spread beyond 25 m after 9.1 s, with no separating force anywhere.
* **Scenario 4 (Hunter pinned):** forced, it backs out, drives 30 m to an open bearing about 63 degrees off the contact angle, and charges again 2.5-6.7 s after the manoeuvre ends.
* **Scenario 5 (Brawler in a productive fight):** no breakaway in 25 s with 6 strong hits.

## H. Performance

See test U. Per-call decision cost is unchanged within measurement; no new per-frame queries (the one extra cast, behind the car, runs only on the decision tick and only during a breakaway); no allocations. Frame time in the browser and on a phone was not measured.

## I. Regressions and risks

1. **The effect of breakaway is deliberately modest.** In clumped melees milling time went from about 6 % to 4-5 % of driving time and 2-3 % of driving time is spent breaking away. If it is too subtle in play, the dials are `breakawayThreshold` and the pressure weights in `EnemyDriver.ApplyProfile` / `AddPressure`. Hit counts were within run-to-run noise (about 13 %) of the previous AI; the evidence is a control arm, not a precise equality.
2. **The old arena's enemy pickup is easier to follow a swerve.** See the evasion test. In the derby the same applies to the pickup AI only; the muscle car and hatchback AI are unchanged.
3. **Noise.** Single runs vary by more than the effects here; conclusions rest on 10-run sums and a control arm.
4. **Not tested on a phone.** No frame-rate measurement of the WebGL build (the browser pane's reported rate is not meaningful).
5. **Pickup feel is measured, not played.** The numbers put the truck between the muscle car and what it was; the owner's driving feel decides whether `steerRateLow` / `steerRateHigh` should move another few percent.
6. **Known and untouched:** the stationary-collision damage question is still reserved for Opus.

## J. Branch and commit

Branch `feature/gameplay-tuning-v1`; the commit list is in the final report (`git log main..feature/gameplay-tuning-v1`). Not merged, not published.

## K. Recommendation

Ready for gameplay review on the hosted preview after the owner approves a publish: play the pickup against the AI hatchbacks and watch a few clumps. Adjust from there.
