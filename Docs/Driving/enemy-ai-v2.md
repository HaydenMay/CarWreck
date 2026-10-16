# Enemy AI V2: combat positioning, ramming behaviour and driver profiles

Branch `feature/enemy-ai-v2`, based on `main` `e5b834b` (which includes the damage-aware colliders and progressive crushing). Evidence: Unity Editor with real PhysX (`Physics.Simulate` at 50 Hz),
and a local WebGL build smoke-tested in desktop Chromium. Nothing was merged or published.

**Project documents.** `CARWRECK_GAME_DESIGN.md` and `CARWRECK_AGENT_ROLES.md` are not in the repository (searched the working tree and git history), so they could not be reviewed. `AGENTS.md` (scope rules) was followed.

## A. Summary

The enemy used to steer at the player and ram. It now runs a small combat loop on top of the same driving code: it lines up, commits to a charge, backs off to make a run-up when it is too close for a strong hit,
goes round obstacles that block the way to the player, and drives with a personality (Brawler, Hunter, Cautious) made of two numbers, **aggression** and **skill**.
V1's driving inputs, arena-wall check and stuck recovery are unchanged. Nothing in collision, damage, deformation, progressive crushing, the damage-aware colliders, vehicle mass or handling was touched (`git diff main -- Assets/DamageLab` is empty).

## B. Existing systems reused (and what was not there)

| Need | What exists | Used |
|---|---|---|
| Steering, throttle, brake, reverse | `ArcadeCarController` (single Rigidbody, `Throttle` / `SteerInput`) | Unchanged. The AI only writes those two inputs |
| Target tracking | V1 `EnemyDriver`: low-pass filtered player velocity, capped linear lead | Kept, lead now scaled by skill |
| Stuck detection and recovery | V1 `Recover` state (throttle on but nearly stationary for 0.9 s, then reverse 1.3 s swinging the nose at the player) | Kept as it was, same for every skill. It now also counts braking for an obstacle at low speed as "stuck" |
| Arena-edge avoidance | V1 `WallAhead` | Kept (looks slightly further at speed, brakes in Overshoot) |
| Obstacle avoidance | **None** beyond the arena edge: V1 only reversed after getting stuck (measured below: 6 wall hits and 5 stuck recoveries in 25 s, never reaching the player) | New: sphere-cast probes and waypoint detours (no NavMesh, no new framework) |
| Collision awareness | `CollisionDamageReceiver.ImpactRegistered` | Used for hit counters |
| Scenario / reset plumbing | `DrivingGame`, `ResetAll`, `Restart()` | Reused; enemy settings gained a profile |

## C. Architecture

All in `EnemyDriver` (one component per enemy, no statics, nothing specific to the pickup or to one enemy).

States: `Waiting`, `Pursue`, `LineUp`, `Reposition`, `Commit` (the charge), `Overshoot`, `Recover`.

```
Waiting -> Pursue -> LineUp -> Commit -> Overshoot -> Pursue ...
              ^         |  ^
              |         v  |
              +------ Reposition (back off / swing wide, capped at 2.2 s)
 any driving state --stuck--> Recover (V1) -> Pursue
```

* **Pursue** (far, or the straight line is blocked): steer at the predicted player position, detouring round obstacles; speed set by the speed setting.
* **LineUp** (clear line, within about 45 m): the visible "I am coming for you". It brakes or crawls so the bearing to the target is nearly nulled *before* the charge range (speed limited to what the car can turn through in the distance that is left), then accelerates once lined up.
* **Attack evaluation** (every 0.12 s, from Pursue/LineUp): expected impact speed = own speed after accelerating over the remaining run-up, capped at the charge speed, minus the speed the player is moving away at. A charge starts when the player is inside charge range, the bearing is small *for that distance* (lateral error in metres, not just an angle), the car has speed, the expected hit is strong enough and the overall opportunity score reaches the profile's threshold.
* **Reposition**: when the car is too close for a strong hit (a tailgater at the player's speed gets almost no impact), it backs off (brake, then reverse while swinging the nose to one side) when the player is ahead, or drives round to a point on the far side when the player is beside or behind. Chance and cooldown depend on aggression; maximum 2.2 s, so it never retreats for long. **Anti-circling:** close to the player but pointing well away for 0.7 s forces a swing-wide reposition (V1 could orbit a stationary player).
* **Commit** (the charge): minimum 0.8 s, maximum 2.4 s. It keeps heading for the predicted position with a *limited steering authority* (45 to 70 % of full lock, by skill), so it corrects a little but cannot follow a late swerve. It ends on impact, when the player has been passed or lost, or on timeout. An emergency (a solid obstacle before the player and too close to stop) aborts it into Overshoot with braking.
* **Overshoot**: keeps rolling roughly straight for 0.55 to 1.0 s (skill), steering clear of walls and obstacles and braking for a wall, then Pursue turns it round.
* **Patience**: after `engageTimeout` without an attack (3 to 6 s by aggression) the driver gradually accepts worse angles, weaker hits and shorter run-ups, so even a cautious driver comes in regularly.
* **Obstacle awareness** (`obstacleAwareness`, default on; off = V1 behaviour): obstacles are kinematic bodies and dynamic bodies of 300 kg or more (walls, barriers, pillars, heavy blocks); cars, crates and the static ramp are ignored. Every 0.12 s a forward sphere cast (and two side probes) finds what is ahead, and a cast to the player says whether the line is clear. When it is not, `PlanDetour` takes the outline of the blocking obstacle and whatever touches it (a wall with wings is one obstacle), tries the cheapest one- or two-corner routes (checked with casts), keeps the chosen side (hysteresis) and replans if the leg is blocked or after 6 s. Stuck recovery clears the plan and allows the other side.
* **Prediction**: lead = min(1.6 s, time to arrive) x `predictionQuality`; the velocity the AI "sees" is low-passed (0.25 to 0.6 s).
* **Reset**: `Restart()` clears every counter, timer, detour and the random seed (`seed`), so a reset reproduces the same behaviour.

## D. Driver profiles

`DriverProfile` is a ScriptableObject with two sliders (0 to 1), independent of the vehicle. Assets: `Assets/Driving/Profiles/Drivers/{Brawler,Hunter,Cautious}.asset`
(created by the scene builder, never overwritten, so tune them in the Inspector). `DrivingGame.driverProfiles` + `enemyProfileIndex` pick one; with no assets assigned, built-in presets with the same numbers are used.

| Profile | Aggression | Skill | In play |
|---|---|---|---|
| Brawler | 0.85 | 0.50 | Attacks early and often (accepts about 14 degrees off the line and short run-ups), backs off least, hits at speed |
| Hunter | 0.65 | 0.90 | Leads the player best, lines up tightly (smallest lateral error), brakes in time, backs off to build speed when too close, strongest repeat hits |
| Cautious | 0.25 | 0.50 | Needs a better angle and a longer run-up, backs off about twice as often and starts its first charge later; still attacks every few seconds |

Derived values (`ApplyProfile`): skill sets reaction lag (0.6 to 0.25 s), prediction quality (0.4 to 1), full-lock angle (45 to 28 degrees), charge steering authority (0.45 to 0.7), overshoot time (1.0 to 0.55 s), obstacle look-ahead (0.75 to 1.25x), the turn rate the driver believes the car has (low skill overestimates it and carries too much speed into turns) and brake strength; it also raises the demanded run-up and impact speed slightly and tightens the alignment tolerance. Aggression sets the alignment tolerance, the shortest accepted run-up, the minimum expected impact speed, the charge range (20 to 26 m), the chance and cooldown of backing off, the patience timeout, the charge speed (1.0 to 1.2x) and the opportunity threshold.
Low skill is expressed by late braking, wide turns, short leads and loose alignment, never by random steering. The obstacle recovery is identical for all skills.

**Testing the profiles.** Menu: choose ENEMY, then press **DRIVER: BRAWLER** (the button appears in ENEMY mode on the speed row) to cycle Brawler, Hunter, Cautious, then DRIVE. In the Inspector use `DrivingGame.enemyProfileIndex`.
`CarWreck > Validate Enemy AI` runs the scenarios below.

## E. Files changed

| File | Purpose |
|---|---|
| `Assets/Driving/Scripts/EnemyDriver.cs` | Combat loop, probes, detours, profile-derived behaviour (V1 driving, wall check and recovery kept) |
| `Assets/Driving/Scripts/DriverProfile.cs` (new) | The two-number profile and the three presets |
| `Assets/Driving/Scripts/DrivingGame.cs` | Profile selection (`driverProfiles`, `enemyProfileIndex`, `CycleEnemyProfile`), `enemyObstacleAwareness`, page report fields; the three V1 enemy tuning fields (full lock, commit distance, reaction time) are now derived from the profile and were removed |
| `Assets/Driving/Scripts/DrivingUI.cs` | The DRIVER button on the enemy menu row (speed row slightly narrower) |
| `Assets/Driving/Editor/DrivingSceneBuilder.cs`, `Assets/Driving/Scenes/DriveArena.unity`, `Assets/Driving/Profiles/Drivers/*` | Profile assets and scene wiring |
| `Assets/Driving/Editor/EnemyAiValidation.cs` (new) | Tests A to O, each per profile |
| `Assets/Driving/Editor/DrivingValidation.cs` | One earlier collider check now holds the enemy still before it asks whether the last hit was refitted (the new enemy attacks continuously, so a hit could be inside its 0.2 s settle wait) |

## F. Representative behaviour (from the runs)

* **Brawler**, stationary player in open ground: first charge at 4.2 s from 23 m, lined up within 1 degree, 73 km/h closing; then roughly one charge every 5 s, many from 12 to 17 m at 45 to 60 km/h. Backs off twice in 30 s.
* **Hunter**: first charge 4.4 s, 65 to 73 km/h; after a hit it backs off (3 times in 30 s), re-aligns (mean 8.8 degrees off at the start of a charge, Brawler 12.8) and charges again from 15 to 22 m at 44 to 59 km/h. On a moving player it leads: 3 hits from 7 charges in 60 s.
* **Cautious**: first charge 6.4 s (14.5 s against a moving player), backs off 12 times in 90 s (Brawler 6), but still lands 11 hits in 90 s and 4 on the moving player; mean distance 18 m, so it never flees.
* **Obstructed player** (hidden behind the 16 m crash wall with wings): with V2 all three profiles reach the player (closest 3.2 m) in about 7 s after 1 to 2 detours and ram it at 46 to 59 km/h; with probing off (V1-style) none got closer than 16.6 m in 25 s and each rammed the wall 5 to 6 times (5 stuck recoveries).

## G. Test results (`CarWreck > Validate Enemy AI`: 14 of 14 PASS)

| Test | Result | Evidence |
|---|---|---|
| A. Basic pursuit | PASS | player on a 43 km/h loop, both directions: mean distance 24 to 25 m, closest 2.5 to 3.5 m |
| B. Obstacle recovery | PASS | facing the wall / nose in: 109 to 138 m away after 18 s, 1 to 2 stuck recoveries each |
| C. Obstructed player | PASS | see F: 1 to 2 detours, at most 1 wall hit, reaches the player; V1-style: 5 to 6 wall hits, never reaches it |
| D. Close-range positioning | PASS | the tailgater backs off in all three profiles (1 each), time within 9 m 0.8 to 1.1 s of 8 s |
| E. Charge attack | PASS | every profile lands at least a 69 km/h ram on a stationary player (best 70 to 74 km/h) |
| F. Missed attack | PASS | the player drives off as the enemy commits (and 0.2 s later): 6 of 6 avoided, and each time the enemy attacks again (2 to 4 charges) |
| G. Moving target | PASS | 19 charges, 9 hits in 3 x 60 s: it leads the player but not every charge connects |
| H. Charge commitment | PASS | 0 charges abandoned early, 0 back-offs dropped within 0.4 s, 0 back-off repeats within 1 s |
| I. Brawler | PASS | 15 vs 11 charges (stationary) and 7 vs 5 (moving) against Cautious; 6 vs 12 back-offs; first moving charge 7.6 s vs 14.5 s |
| J. Hunter | PASS | angle off the target at charge start 8.8 vs 12.8 degrees; 0.76 vs 0.73 hits per charge |
| K. Cautious | PASS | attacks less than Brawler, 11 hits in 90 s stationary, mean distance 18 to 25 m |
| L. Reset | PASS | all counters, state, detour, inputs cleared; the first states after the reset equal the first run's |
| M. Driving regression | PASS | `Validate Driving`: 88 PASS, 0 FAIL (V1 enemy checks A to H unchanged and passing) |
| N. Physics regression | PASS | no change under `Assets/DamageLab`; Collision Lab suites (`verify`, `collision-verify`, `collision-geometry`, `collision-persist-a/b`, `collision-overflow`, `vehicle-switch`): 52 PASS, 0 FAIL; Progressive Crush validation: 34 PASS, 0 FAIL |
| O. Performance | PASS (editor) | `E.Drive`: average 4 microseconds, worst call 0.82 ms (editor, Mono), 0.0 bytes allocated per call over 39,450 calls (detour planning allocates a small array a few times per obstructed second) |

Browser (local WebGL build `20261016-2145-f44901e`, desktop Chromium, software rendered so 2 to 32 fps): ENEMY mode, the DRIVER button cycles Brawler, Hunter, Cautious (page report `eprof`), and Hunter and Cautious each ran Waiting, Pursue, LineUp, Commit, hit, Overshoot, Pursue, LineUp. The worst `E.Drive` call in WebGL (`edrvMs` in the page state) was 0.4 ms (Hunter) and 0.8 ms (Cautious, measured at 2 to 4 fps).
**Not tested in a browser:** the obstructed-player scenario, long sessions, a phone, touch play against the new enemy, other browsers.

## H. Performance

Editor figures above. Obstacle probing runs every 0.12 s (a few sphere casts), detour planning only while the line to the player is blocked (at most every 0.4 s, 5 plans in the 25 s obstructed runs). No per-frame allocation. WebGL cost is reported by the page as `edrvMs` (worst decision call since reset); mobile was not measured.

## I. Concerns and limitations

1. **Head-on dodging is still hard.** At about 120 km/h closing speed a swerve cannot clear the car; the counterplay is driving off sideways before or as it commits (6 of 6 avoided in test F), or using obstacles. Dials: `chargeSteerLimit`, `reactionTime` derivations in `ApplyProfile`.
2. **A stationary player is hit again and again.** There is no damage cap or defeat condition (out of scope), so a parked player is rammed every few seconds.
3. **Profile differences are real but moderate.** In the stationary runs Brawler and Cautious differ by about 35 % in charges and 2x in back-offs, and by 7 s in the first charge against a moving player. If the owner wants them more different, lower Cautious aggression or raise Brawler's in the assets.
4. **Detour planning is local.** It handles one obstacle cluster at a time (one or two corners); a maze would need more. A very large obstacle cluster joined by touching colliders is treated as one box (a diagonal row of barriers becomes a big box).
5. **Reposition swing-wide ends facing away**, so the turn-round after it costs 2 to 4 s; that is why Hunter and Cautious sometimes sit 20 to 30 m back after a back-off.
6. **Static scenery is invisible to the probes** (the kicker ramp): an enemy parked against it is freed by the stuck recovery only.
7. **Run-to-run variation.** Arena physics in the editor is not repeatable run to run; the AI checks use margins and several starts per profile.
8. **Known physics issue (not touched).** The project owner saw a possible damage exploit: when the player stopped just before an anticipated head-on, the readout showed the attacker at 97 % strength and the stationary player at 0 %. Cause unconfirmed; reserved for a separate Opus investigation. This milestone changes no collision or damage code.

## J. Mitigations (suggested, not done)

Make the swing-wide point sit nearer the player's side so the turn-round is shorter; give Cautious a larger gap to Brawler by dropping its aggression to about 0.15; add a second waypoint pass for multi-obstacle detours; give a few static colliders a "blocker" tag for the probes.

## K. Recommendation

Ready for gameplay review. The behaviour changes are contained in one component plus a profile asset, can be disabled for comparison (`enemyObstacleAwareness` off), and every regression suite passes. Things to judge by feel: how dodgeable the charge is, whether Cautious is cautious enough, and how often the enemy backs off.
