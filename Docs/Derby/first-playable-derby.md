# CarWreck first playable: free-for-all demolition derby

Branch `feature/first-playable-derby`. Scene `Assets/Derby/Scenes/DerbyArena.unity`. Nothing is merged or published.

Short version: five cars (the player and four AI drivers) fight in a purpose-built dirt arena. Every car is an opponent of every other car. Verified collision damage scores points and wears durability down; wrecked cars respawn (Points Derby) or are out (Last Car Standing, with a Final Showdown when two are left). The driving, damage, deformation, progressive crushing, damage-aware collider and AI code is reused unchanged except three small additions to `EnemyDriver` (below).

## A. What was built

| Area | Result |
|---|---|
| Match model | `MatchModel.cs`: `Participant`, `MatchRules`, `DerbyMatch` (pure C#, no scene objects): states, damage to durability and points, wrecks, respawns, winner rules. 39 checks run without a scene. |
| Participants | Identity, name, control type, vehicle, paint, driver profile, optional team id (not read by any V1 rule), durability, score, statistics. Any number of participants; no cap in the model. Default 5. |
| Vehicle colours | `VehiclePaint.cs`: per-colour cached material variants of each model's own paint material; windows, tyres, lights, trim keep their shared materials. |
| AI | `AiTargeting.cs`: each AI chooses an eligible opponent (any other active car, human or AI) with profile-weighted scoring, hysteresis and unreachable-target handling, and feeds it to the unchanged Enemy AI V2 combat state machine. |
| Arena | `DerbyArena`: 100 m dirt square, concrete perimeter with corner wedges, a pinwheel of barriers in the middle, four tyre stacks, two barrier pairs, stands and floodlights (scenery only). |
| Spawning | `SpawnPlanner.cs`: authored ring of 8 points, generated rings beyond that, farthest-point initial layout, clear-ground respawn picker. |
| Modes | Points Derby (default, 180 s, respawn) and Last Car Standing (no respawn, 60 s Final Showdown with two left). |
| UI | `DerbyUI.cs`: start menu (car, paint, mode), countdown, HUD, name plates, pause, results, touch controls. |
| WebGL | The derby is now the first scene of the WebGL build, so the hosted page opens the game menu. The driving arena is still in the same build (`?scene=drive`). |

## B. Existing systems reused

* `VehicleRig.Build` and `VehicleDefinition` (the muscle car, compact hatchback and compact pickup; no new models), `ArcadeCarController` with the three `DriveProfile` assets, `VehicleDamageModel`, `CollisionDamageReceiver`, `VehicleHitbox` and progressive crushing, configured with exactly the arena's damage mapping.
* Damage-aware colliders: `DerbyGame.AfterPhysicsStep` is the arena's collider-refit scheduler applied to every car (settle 0.2 s after the last hit, cap 1 s, waits for crushing).
* Enemy AI V2 (`EnemyDriver`) and the three `DriverProfile` assets (Brawler, Hunter, Cautious).
* `DriveInput`, `ChaseCamera`, `TouchHoldButton`, `LookDragPad`, the WebGL page template, `publish-preview.sh`.
* Changed in existing code: `EnemyDriver` only. `SetTarget(Rigidbody)` (re-point the driver at any body; a null target makes it coast), `carIsObstacle` (optional predicate so a wrecked hulk counts as an obstacle while living cars do not), and a null-target guard. No combat logic changed; the Enemy AI V2 validation still passes (see tests). `DrivingWebBuild` (scene list, product name) and the page template text were edited. `Assets/DamageLab` is untouched except new `Derby*.mat` materials created by the scene builder (the project's helper puts all generated materials there).

Design documents: the repository has no game-design or agent-role documents (`AGENTS.md` only points to the `addacar` workflow), so the confirmed requirements in the brief were used.

## C. Architecture

```
receiver.ImpactRegistered (per car, per verified hit)
        |  DerbyGame.OnImpact: applied hits only; attacker = the other body's name -> participant (or the environment)
        v
DerbyMatch.ApplyDamage(DamageEvent{victim, attacker, energy})
        |  durability, points (tiers), pair cooldown, wreck + bonus, state machine, winner
        v
events: Wrecked / Scored / RespawnDue / ShowdownStarted / StateChanged
        v
DerbyGame (hulk paint, respawn placement, camera, feed) and DerbyUI (HUD, banners, results)
```

* `DerbyMatch` reads no control type, no scene object and no clock but `Tick(dt)`. The same events and ticks always give the same result, which is what the 39 scene-free checks rely on.
* Eligibility goes through `IOpponentRule`; V1 supplies `FreeForAllRule` (every distinct participant). A team mode would supply another rule; `Participant.team` exists for it and nothing reads it yet. No networking, co-op or team UI.
* The AI never learns whether a car is human: `AiTargeting` takes a `Participant` and the car body, and `Score` has no control-type input (a validation check swaps the control types and compares the scores).
* Costs are bounded: targeting is one pass over the other cars per driver every ~0.35 s (staggered), nothing runs all-pairs every frame.

### Scoring and durability

Damage data used: `CollisionDamageReceiver.Report` of the **victim**: `applied`, `energy` (the energy that hit dissipated in that car after the two cars share it by momentum), `other` (the other body's name, unique per car). Raw collision speed is not used.

| Victim-side energy of one hit | Points |
|---|---|
| below 3 kJ | 0 (the receiver itself ignores hits below 1.5 kJ) |
| 3 - 15 kJ glancing | 10 |
| 15 - 40 kJ solid | 30 |
| 40 - 90 kJ heavy | 70 |
| 90 kJ and up devastating | 120 |
| wreck bonus | +150 to the car that dealt the final hit, or hit it within 4 s before a wall finished it |

* Only an active car, or one wrecked in the same step (the other half of a head-on), can earn; only an active car takes damage; wrecked cars score nothing and take nothing. Walls, obstacles and self hits wear durability at 40 % and score nothing.
* A repeated hit by the same attacker on the same victim scores again only after 0.75 s or if it is at least 1.5x harder, so a pinned, jittering contact cannot be farmed (a physical 5 s pressing test added 0 to 10 points).
* Both cars score in a head-on, each for the damage it inflicted.
* Durability is the **absorbed collision energy**, not a deformation percentage: 650 kJ for a 1400 kg car, scaled by mass (hatchback 441 kJ, pickup 628 kJ), x2 in Last Car Standing. The values are in `MatchRules`.

### Match rules

| | Points Derby | Last Car Standing |
|---|---|---|
| Duration | 180 s (`pointsDuration`) | until a winner |
| Wrecked car | hulk stays 3 s (`respawnDelay`), then respawns with fresh geometry at a safe spot; score kept | out for the match, hulk stays |
| End | timer: highest score; ties: more wrecks dealt, then fewer times wrecked, then a declared draw | one car left; with exactly two left a 60 s Final Showdown starts: survivor wins; at timeout the higher score wins, then more durability left, then a declared draw |
| Simultaneous wrecks | n/a | wrecks within 0.15 s count as simultaneous: higher score, then durability, then draw. The result is computed once; later hits are ignored. |

The match lifecycle is `Menu -> Preparing -> Countdown (3 s) -> Active -> FinalShowdown (LCS only) -> Results`. Nothing scores or moves (AI included) before the countdown ends. Rematch keeps the cars and resets them; "quit to menu" removes the AI cars.

## D. Vehicle colours

The three models use two paint styles, so each is handled by its own material rather than by a property block:

* Hatchback and pickup use the project's two-colour paint shader (`_PrimaryColor`, `_StripeColor`, stripe mask on UV1): both colours are set and the livery is kept.
* The muscle car's paint is a plain URP Lit material whose base map is a blue/white stripe texture (a tint would give the wrong hue). Its copy gets a neutral mask built from the same texture (white body, dark stripes) that the body colour multiplies, so it keeps its racing stripes in a darker tone of any colour.
* `VehiclePainter` swaps only the renderer slots whose material is called `*Paint`; windows, tyres, lights, chrome and trim keep their shared materials (checked: 43/43 and 80/80 other materials shared between two differently painted cars). Variants are created once per (source material, colour) and shared by every car that uses the colour (cache of 7 for five cars here); repainting 20 times adds no materials. Swapping a slot does not touch the mesh, so deformation, crushing and colliders are unaffected (the same hit on two differently painted cars gives byte-identical geometry; repainting a dented car keeps the dents).
* Eight colours (`PaintPalette`): Crimson, Cobalt, Signal Yellow, Lime, Orange, Violet, Arctic White, Hot Pink. The AI cars take the colours furthest (RGB distance) from the ones already in the match, so the five paints are always clearly different (smallest pairwise distance 0.69 with the default five). Wrecked cars turn charcoal.

## E. Arena

* 100 x 100 m dirt (generated seamless texture, no art assets), 2.2 m red-and-white concrete perimeter, four 45-degree corner wedges so nothing is pinned in a right-angle corner, a pinwheel of four 7 m barriers around the centre with 7 m gaps (no gap narrower than the cars need), four tyre stacks and two barrier pairs towards the edges, stands and floodlights outside the wall (no colliders). All obstacles are kinematic rigidbodies, so the damage receiver registers them and the AI's probes detour around them. Low-poly, no particles, no extra lights.
* Spawning: eight authored points on a 31 m ring facing the middle (each turned 22 degrees one way or the other so the opening charges are not all head-on). For other counts the planner generates rings (checked against obstacles) and takes the points furthest apart. 2, 5, 8 and 12 participants spawned without overlapping each other or obstacles (closest pair 62, 23.7, 23.7 and 12.2 m).
* Respawn: candidates on authored and generated rings, clear of cars, hulks and obstacles first (a car-footprint overlap test), then furthest from every other car; if none is clear the least bad is used.

## F. AI personalities

Combat is Enemy AI V2 as before (aggression and skill still drive attack willingness, alignment, run-up, back-off, patience). The new layer chooses whom to attack. Weights come from the same two numbers:

| Term | Brawler (0.85 / 0.50) | Hunter (0.65 / 0.90) | Cautious (0.25 / 0.50) | Rookie (0.60 / 0.25) |
|---|---|---|---|---|
| near (closer is better) | 1.37 | 1.19 | 0.83 | 1.14 |
| weak (damaged opponents) | 0.54 | 0.91 | 0.69 | 0.40 |
| ahead (easy interception) | 0.55 | 0.75 | 0.55 | 0.43 |
| head-on penalty | 0.12 | 0.28 | 0.60 | 0.32 |
| commitment bonus / minimum hold | 0.22 / 2.9 s | 0.32 / 3.4 s | 0.33 / 4.4 s | 0.23 / 3.5 s |

Common terms: a small crowd penalty (dogpiles are allowed, just not the default), a fading revenge bonus for whoever hit this driver in the last 6 s (no permanent rivalries), and a slowly changing per-pair noise term so identical situations do not make every driver choose alike. The current target gets the commitment bonus; inside the hold time only a much better opportunity wins; a landed hit releases the hold; a wrecked target releases it at once; no progress for 7 s drops the target for 6 s. A fourth profile asset, Rookie (aggression 0.6, skill 0.25), was added so the four AI cars are four different drivers.

## G. Game modes

See the table in C. Defaults: 5 participants, Points Derby, 180 s, 3 s countdown, 3 s respawn delay, 60 s showdown. `participantCount` is a field on `DerbyGame`; the page accepts `?cars=N` (2-16) for scaling checks.

## H. Physics dependencies and the reported damage exploit

* Everything scoring and durability needs is in the existing `CollisionDamageReceiver.Report`; no Opus-owned code was changed and no handoff blocks this milestone.
* Attribution uses the other body's name, so each car's root object is named `Car<id> <name>` and the receiver's `other` string maps to a participant; a contact with anything else is the environment. Limits: one report per (other body, 0.5 s cooldown) per car; a head-on produces one report on each car, which is what gives both cars their points. Suggested improvement for Opus (not needed now): put the other `Rigidbody` in the report instead of a string.
* The suspected exploit (stationary player 0 % strength, attacker 97 %) is **not fixed and not hidden**: no scoring multiplier was added. A scripted probe (`DerbyValidation` with `DERBY_ONLY=exploit`) drove two cars head-on at closing speeds of 11 to 30 m/s with the second car braking to a stop 3 to 16 m before contact, 8 combinations: the braking car always registered the hit and, as the momentum-share rule intends, took the larger share (for example 76.7 kJ against 29.0 kJ); it never took 0. The reported case was therefore not reproduced in edit-mode physics. If it does occur in play it affects fairness in a specific way: a car whose receiver reports no hit takes no durability loss and gives its attacker no points, while its attacker is damaged and the standing car earns points for it. What would be needed from Opus: confirmation of why a receiver can report nothing for a real contact (receiver cooldown, `Collision.rigidbody`, a separating normal) and, if it is real, a fix inside the receiver. Until then the scoring is only as reliable as the receiver reports.
* Known behaviour that matters for play: the momentum-share rule makes a stationary or retreating car take 85 % of a hit, so ramming a parked car pays well. Pressing against a car is not farmable.

## I. Tests

All scene checks run in the real DerbyArena with real PhysX stepped at 50 Hz (`CarWreck > Validate Derby`, or `DERBY_ONLY=pure|scene|perf|balance|exploit`); the rules checks need no scene. Final run on this branch: **79 PASS, 0 FAIL**. Arena physics in the editor is not repeatable run to run, so car-vs-car checks use margins, not exact counts. "Browser" means the built WebGL player run on localhost in the app's browser pane (a desktop browser, with an emulated phone viewport); no phone was used.

| Test | Result | Evidence |
|---|---|---|
| A. All three existing vehicle models instantiate | PASS | Muscle Car 1400 kg, Compact Hatchback 950 kg, Compact Pickup 1350 kg: 14-box shell, damage model, controller; five cars by default use all three |
| B. Same model, different colours | PASS | two muscle cars and two hatchbacks with different paints; 43/43 and 80/80 other materials shared; repainting 20 times adds no materials; smallest colour distance of the five paints 0.69 |
| C. Paint does not affect deformation | PASS | the same hit on two differently painted cars: identical geometry (difference 0.00000000); repainting a dented car keeps dents and mesh objects |
| D. Five participants spawn without overlap | PASS | also 2, 8 and 12: closest pairs 62, 23.7, 23.7, 12.2 m, none on an obstacle; nobody moves during the 3 s countdown |
| E. AI targets other AI | PASS | in a 60 s match every AI had both AI and player targets |
| F. AI targets the player | PASS | the player was a target for every AI at some point (19 % of target time in the 60 s run, 7 % in an earlier run) |
| G. No special priority for the player | PASS | scores identical when human and AI control types are swapped; over 3000 random layouts the player is the best target for 26 % of choices (others 18-19 %, one in four expected); the player's share of target time was 7-19 % |
| H. Profiles behave differently | PASS | same situation: Brawler takes the near car, Hunter the wounded one, Cautious avoids the car driving at it; same encounter, two 30 s runs each (charges / back-offs): Brawler 13 / 1, Hunter 11 / 0, Cautious 8 / 4, Rookie 10 / 2 |
| I. AI V2 obstacle recovery | PASS | pinned against the wall and the corner wedge the AI moved 52 m and 89 m in 14 s with 3 and 1 recoveries; Enemy AI V2 validation 14/14 |
| J. AI V2 charge behaviour | PASS | 5 charges, 4 hits, last at 48 km/h closing against a stationary player (210 kJ dealt) |
| K. All participants can receive damage | PASS | each of the five received a verified hit from another car (61-78 kJ each) |
| L. All participants can earn points | PASS | each of the five scored as the attacker; tiers, head-on and wall-assisted wrecks checked in the rules tests |
| M. Repeated contact gives no duplicate points | PASS | ten identical events in 0.2 s score once; 100 taps in 5 s score at most 8 times; pressing against a car for 5 s after a ram added 0 to 10 points |
| N. Wrecked vehicles are no longer targets | PASS | no AI keeps a wrecked car as its target; it takes no damage, scores nothing and counts as an obstacle |
| O. Points Derby respawn, AI and player | PASS | both respawn after the delay with full durability and their score |
| P. Respawn restores geometry and collision state | PASS | vertex checksum back to the fresh value, shell retraction 0.26 m before and 0 after, no impacts, no crush in progress, motion reset, paint restored |
| Q. Points Derby ends at the timer | PASS | 180 s default (state at 179 s Active, at 180.1 s Results); 8 s variant in the real game |
| R. Highest score wins, including an AI | PASS | an AI won in the real game and in the rules tests; ties go to more wrecks, fewer deaths, then a declared draw |
| S. Last Car Standing has no respawns | PASS | wrecked car stays out for 6 s and for 20 s |
| T. Final Showdown starts with two active | PASS | not with three; 59.7 s on the clock when it starts |
| U. Showdown ends after an elimination | PASS | the survivor wins once |
| V. Showdown timeout uses the tiebreaker | PASS | higher score; equal score: more durability; equal: a declared draw |
| W. Simultaneous eliminations | PASS | same step, 0.1 s apart (decided by score), everyone wrecked (draw), a late hit after the result (ignored, single result event) |
| X. Restart creates a clean match | PASS | scores, stats, damage and clocks reset; same car objects reused, no extra rigidbodies; browser: REMATCH gives a clean countdown |
| Y. Return to menu works | PASS | AI cars removed, player's car repaired and kept, five menu-match-menu rounds with constant rigidbody count (27), meshes (372) and materials (97); browser: QUIT TO MENU and PAUSE verified |
| Z. WebGL entry opens the playable game | PASS (local) | the built player opens the menu, STARTs a five-car derby, plays through countdown, active and results in both modes. Not checked on the hosted page: nothing is published |
| Regression: driving validation | PASS | 88 / 88 |
| Regression: Enemy AI V2 validation | PASS | 14 / 14 |
| Regression: Collision Lab validation (verify, collision-verify, collision-geometry, collision-persist-a/b, collision-overflow, vehicle-switch) | PASS | 62 PASS tokens, 0 FAIL |
| Regression: progressive crushing validation | PASS | 34 / 34 |
| Performance: five-vehicle match | PASS (editor); NOT MEASURED on a device | see J |
| Performance: repeated respawns | PASS | see J |
| Performance: browser smoke test | PASS for function; frame rate NOT MEASURED | see J |
| Mobile | landscape phone viewport (812 x 375) emulated: menu, HUD and touch controls fit; **no phone was used** | |

## J. Performance

Editor (Mono, edit-mode stepping; WebGL differs per call, so these show scale, not device numbers):

* Five-vehicle match, 30 s, four AI: 1.4 - 1.8 ms average per fixed step including physics, damage receivers, AI drivers, targeting and collider refits. The 99th percentile was 6.5 - 113 ms and the worst step 200 - 270 ms between runs: those spikes are the damage engine solving a hit (`VehicleDamageModel.AddImpact`, the same cost the arena has; the arena flags solves over 40 ms as hitches).
* AI decisions: the Enemy AI V2 validation measures 4 microseconds average and 0 bytes per call. Targeting: at most 0.8 ms for one pass in the editor (cold JIT), every ~0.35 s per driver, staggered; in the browser the worst targeting pass was 0.1 - 0.2 ms.
* Building a five-car match from the menu: 1.4 s in the editor, 0.73 - 0.86 s in the browser. A rematch reuses the cars and took 8 ms. A respawn (`ResetCar` on a damaged car) took 3.2 ms average, 4.5 ms worst.
* 40 respawns: meshes 372 -> 372, rigidbodies 27 -> 27, materials +3 once (the charcoal wreck paint is created once per model, then constant), managed memory +5 MB.
* Scaling smoke test (not a requirement, no claim of playability): 8 cars 2.8 - 3.9 ms per step, 12 cars 4.5 - 6.0 ms per step, no errors, targeting still under 1 ms. Hit solves, not the AI, are what get expensive with more cars.
* Browser smoke test: the page reported no deformation hitches (`hitches` 0) over three five-car matches; the WebGL heap grew 256 -> 369 MB with the first match and 369 -> 531 MB the first time the AI cars were rebuilt, then stayed at 531 MB through four more menu/match rebuilds (the build allows up to 1024 MB; a low-end phone may not give that much, so a phone check is recommended). The frame rate could not be measured: the browser pane renders in software and its reported frame rate decays from 30 to about 5 fps within half a minute even in the unchanged driving arena (`?scene=drive`), so those numbers say nothing about a device.
* Balance (informational, five AI-style drivers, defaults): Points Derby, two runs: each car wrecked 1 - 3 times in 180 s and scored 600 - 2000; Last Car Standing finished at 172 s and 155 s with the showdown starting at 146 s and 140 s. The Cautious profile won all four runs: it takes the least damage while still scoring, so low aggression is a good survival strategy under this scoring. If the owner finds that dull, the first dials are the scoring of hits against the wreck bonus, or the profile numbers.

## K. Files

New: `Assets/Derby/Scripts/{MatchModel,VehiclePaint,AiTargeting,SpawnPlanner,DerbyGame,DerbyUI}.cs`, `Assets/Derby/Editor/{DerbySceneBuilder,DerbyValidation}.cs`, `Assets/Derby/Scenes/DerbyArena.unity`, generated textures, `Assets/Derby/Settings/{PaintPalette,Rookie}.asset`, `Assets/DamageLab/Materials/Derby*.mat`, this document.
Modified: `Assets/Driving/Scripts/EnemyDriver.cs` (target setter, obstacle predicate, null guard), `Assets/Driving/Editor/DrivingWebBuild.cs` (scene list), `Assets/WebGLTemplates/CarWreck/index.html` (title and hint text), `ProjectSettings/EditorBuildSettings.asset` (derby scene first).

## L. Branch and commits

Branch `feature/first-playable-derby` (from `main` at 4ea5996). Commit hashes are in the final report; run `git log main..feature/first-playable-derby --oneline`. Not merged into `main`, not published.

## M. Risks and mitigations

1. **Not tested on a phone.** Layout, touch controls and the heap size are checked only in an emulated landscape viewport. Mitigation: owner playtest on the hosted preview before making it the default. The 531 MB heap is the thing to watch on low-end phones; the build already caps pixel density and memory growth.
2. **The reported damage exploit is not reproduced and not fixed** (section H). Scoring and durability are only as reliable as the receiver reports. Needs the Opus investigation already reserved.
3. **Hit solves can hitch** (up to a few hundred ms in the editor; the browser run showed none in three short matches). Five cars means more hits than the arena has, so this is the most likely thing to feel rough on a slow device. Unchanged engine code; a real-device number is needed before judging.
4. **Low-aggression drivers do well** (see balance). Easy to retune in the profile assets and `MatchRules`.
5. **Last Car Standing wait.** A player who is out early watches the rest finish (the camera follows the nearest survivor; no fast-forward or skip; pause and quit are available). A skip button would be the follow-up.
6. **Respawn is a teleport after a 3 s hulk.** No explosion, smoke or fade, and no sound anywhere in the project yet.
7. **AI limits carried over from V2:** head-on dodging is hard, a stationary player is rammed repeatedly, detours plan one obstacle cluster at a time. The new targeting adds no new combat behaviour.
8. **Diagnostics are only a URL away** (`?scene=drive`); the Collision Lab and Damage Lab keep their own builds and scenes. The page parameters (`cars`, `mode`, `time`, `showdown`, `hp`) are for testing and are not shown in the UI.

## N. Deployment readiness

The new scene is the first scene of the WebGL build, so the next approved publish (`DrivingWebBuild.Build`, then `bash Docs/Driving/publish-preview.sh`) opens the CarWreck menu instead of the driving test menu. Functionally it is ready for owner review: both modes run start to finish in a browser build, restart and menu return work, and every required check passes. It should not become the default experience before a real-phone playtest, because the frame rate, the memory footprint and the touch feel are the unmeasured parts. Not merged, not published.


> **Superseded in part:** the Last Car Standing Final Showdown described above (highest score wins on timeout, no repair) was replaced by the repaired-finalists duel decided by showdown damage; see `Docs/Derby/ai-v3-and-final-showdown.md`.
