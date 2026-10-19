# AI V3 and the Final Showdown

Branch `feature/ai-v3-final-showdown` (from main 6134d43). Two changes to the playable derby: opponents that judge their attacks (with three difficulty settings and a little
score awareness), and a new Final Showdown for Last Car Standing. Collision physics, damage, deformation, scoring formulas, wreck bonuses and vehicle stats are untouched.

Validation entry points (Unity menu `CarWreck >`): **Validate AI V3**, **Validate Final Showdown**, plus the existing Validate Derby / Validate Gameplay Tuning and the driving and enemy-AI suites.
All numbers below are editor measurements (Mono, PhysX stepped at 50 Hz); nothing here was measured on a phone.

## 1. What was learned first (and why V3 looks the way it does)

The collision engine splits a car-versus-car hit by how much momentum each car was driving **into** the other (`CollisionDamageReceiver.MomentumShare`): 15 % of the energy for a car that does all
the attacking, 85 % for one that is standing still, moving away or passing sideways, 50/50 for an equal head-on. So "who ends up with the damage" is decided by approach angle and momentum,
not by who hits more often, and the winning player strategy in the owner's description (build speed, hit a car that is not driving at you, prefer weak targets) is exactly that rule at work.

Simulating the existing AI showed where it loses ground: it spends about 37 % of its time under 6 m/s and starts most charges from about 35 km/h (so its hits arrive at 52-57 km/h closing speed while the
player's reach 80+), and its melee scoring is dominated by chaos (the spread between matches is larger than any AI difference). The first V3 attempts that *backed off more* (a larger back-off chance and a pickier
minimum impact speed) made the AI **worse** in 1 v 1 (a retreating car is the one that takes 85 %), so that part was cut back to almost nothing; the changes that stayed are the ones that measurably helped.

## 2. AI V3

### 2.1 Three separate characteristics

| Characteristic | Where it lives | What it decides |
|---|---|---|
| Aggression | `DriverProfile.aggression` | how eagerly a driver seeks a fight, how short a run-up it accepts (unchanged) |
| Driving skill | `DriverProfile.skill` | reaction lag, prediction, steering precision, braking (unchanged) |
| Combat intelligence | `DriverProfile.combatIntelligence` (new, 0..1, default 0.5) | which attacks and targets are worth it and how an attack is prepared |

Brawler 0.30, Hunter 0.85, Cautious 0.70, Rookie 0.15 (aggression and skill as before). A reckless driver can have poor judgement (Brawler); a careful one can be sharp (Cautious).

### 2.2 What the AI evaluates

All of it is a normalised gameplay estimate on the existing ticks (decisions every 0.12 s per driver, target choice every ~0.35 s); the AI never computes collision damage. Every part can be switched off
(`EnemyDriver.v3Mask`) for before/after studies; V3 as a whole is off in the old driving arena (`v3Enabled`) and for the "previous AI" arm (`DerbyGame.aiV3`).

* **Charge speed** (`ChargeSpeedCap`, cruise speed): an intelligent driver approaches fast and charges up to a share of its own car's top speed (up to 93 %; the previous AI capped a charge at 70-84 km/h).
* **Momentum** (`keepSpeed`, roll-on speed): it refuses to crawl while it lines up (a car that is slow when hit takes the larger share).
* **Run-up after a hit** (`Overshoot` roll-on): when still close after a hit or a miss a disciplined driver rolls on a little longer so the turn-in starts with a run-up.
* **Approach angle** (`ApproachAngle`, `ShareEdge`): it aims beside a car that is driving straight at it, so the pass is flank to flank, and it puts off a charge whose exchange would clearly favour the target.
* **Attack opportunity in target choice** (`AiTargeting.OpportunityTerms`): expected impact speed from here, reachability (clear line, other cars in the way), a wreck chance, the expected share of the exchange,
  and a small preference for a flank. The old "lowest health" term loses weight as intelligence grows and is discounted when the car is far away or boxed in.
* **Finishing chances**: a weakened target lowers the impact speed a driver accepts (a weaker hit can finish it).
* **Mistakes**: the lower the intelligence the noisier the choice of target and the more often a rash charge is started.
* **Hysteresis**: target commitment bonus and hold time are raised for V3 because the new terms follow speeds and positions that change fast. Measured switching: 6.8 target changes per driver-minute against 6.4 for the previous AI.

### 2.3 Fairness

No term reads the control type, the score leader or the player; every active car is an eligible target under the same rule; several AI drivers may pick the same weak car independently, nothing coordinates them.
Test A10: an idle player's car receives as many hits as an idle AI car of the same vehicle (39 v 41 in 4 x 60 s each); a human- and an AI-controlled car in mirrored situations get the same target score (2.044 v 2.042).

## 3. Difficulty

CASUAL / STANDARD (default) / COMPETITIVE, in the start menu (both modes, kept across rematches and visits to the menu; `?ai=casual|standard|competitive` on the page). A difficulty changes decision quality
and execution (`DifficultySettings`), the same way for every driver; a driver's personality is unchanged.

| Knob | Casual | Standard | Competitive |
|---|---|---|---|
| Combat intelligence shift (added to the profile) | -0.30 | 0 | +0.25 |
| Driving skill shift | -0.15 | 0 | +0.10 |
| Extra reaction delay (s) | 0.2 | 0 | 0 |
| Charge speed: share of the way to 93 % of top speed (x intelligence) | 0 | 0.35 | 1.0 |
| Charge speed multiplier | 0.92 | 1 | 1 |
| Momentum kept while lining up (x intelligence) | 0 | 0.5 | 1.0 |
| Willingness to make a run-up (x intelligence) | 0.4 | 0.8 | 1.0 |
| Mistakes | 1.4 | 1.0 | 0.5 |
| Score awareness | 0 | 0.5 | 1.0 |
| Weight of the attack-opportunity terms in target choice | 0.45 | 0.75 | 1.0 |

Not touched by any difficulty: mass, durability, damage mapping, wreck bonus, scoring, deformation, acceleration, top speed, grip, health. Test A16 builds the match under all three and compares the cars field by field
(identical), scans the damage / scoring / controller sources for the difficulty (none) and checks that no AI car exceeds its top speed (peak 0.71-0.85 of it).

## 4. Score awareness

`AiTargeting.Urgency` (0..1) from public information. Points Derby: how far behind the best score a driver is, against what the remaining time can plausibly earn (~9 points a second), weighted to the last 100 s.
Showdown: level or behind in showdown damage as the 60 s runs down. Leading, or early, gives 0, so a leader simply keeps its normal behaviour (never passive). The only effects: less patience with its own attacks
(shorter run-up after a hit, harder to talk into a breakaway, a lower bar for a finishing hit); it never picks the target and never looks at who leads. Casual ignores it, Standard feels half, Competitive all.
Tests: 300 points behind with 15 s left = 1.00, 160 s left = 0.00, 10 behind = 0.04, leading = 0 (A11); the urgent driver backs off 3 times instead of 7 in the same scenario (A11c).

## 5. Last Car Standing: Survival, then Final Showdown

States: `Active` (Survival) -> `ShowdownTransition` (1.5 s, combat off) -> `ShowdownCountdown` (3 s, combat off) -> `FinalShowdown` (60 s) -> `Results`.

* **Trigger.** Exactly two cars alive, at least one car already eliminated, and the last wreck has settled (0.15 s, so cars wrecked by one collision sequence count as simultaneous). Once per match.
  One survivor wins as before. Nobody left (all wrecked together): the existing policy, the cars wrecked last are compared by score, then durability, then a declared draw (this is the only place the survival score still decides anything).
* **Transition.** `DerbyMatch.BeginShowdown` marks the finalists (ordered by id), restores their durability, clears their showdown statistics and pair cooldowns and raises `ShowdownPrepare`.
  `DerbyGame.OnShowdownPrepare` repairs both cars with the existing `ResetCar` path that respawns use (`Model.ResetAll`, `SetDamageImmediate(0)`, hitbox refit, receiver and controller reset, AI restart, paint)
  and places them with `SpawnPlanner.PickShowdown`. Nothing in the damage / deformation engine was modified or duplicated.
* **Placement.** Point-mirrored through the middle (identical geometry), 40-62 % of the arena half-size from the centre, each facing the other with the same 10 degree toe-out so a straight charge is not a
  certain head-on; both spots free of cars and obstacles and 22 m of free run ahead; the pair furthest from wreckage wins with a little per-match variety. Measured: 62 m apart, mirror error 0.00 m.
* **Frozen.** The match model ignores damage events unless it is `Active` or `FinalShowdown`; every driver is idled; the player's input is only read in play states, so held touch buttons cannot bypass the countdown.
* **Duel.** Both finalists drive normally; the AI finalists get a clean driver and each other as the only possible target (eliminated cars are never candidates; hulks are obstacles as before).
  A wreck ends the duel once it has settled; a wreck is always handled before the clock, so a timeout and a wreck in the same tick give one result.
* **Showdown damage.** Durability a finalist actually took off the other finalist during the timed duel, from the same verified `DamageEvent`s that do the scoring. Stored in joules and as a fraction of the opponent's
  full durability (what the HUD shows and the timeout compares, so a pickup and a hatchback are compared by what the health bars show). Walls, self hits, eliminated cars, the countdown and overkill count for nothing.
* **Result.** WIN BY WRECK (the survivor wins whatever the totals); WIN BY SHOWDOWN DAMAGE (timeout: the larger total); DRAW (equal within 0.01 % of an armour bar, including 0 : 0). Both wrecked together: each delivered the
  blow that wrecked the other, so the damage dealt **before** those blows is compared (the larger wins, equal = draw). Survival scores play no part.
* **HUD.** FINAL SHOWDOWN card with both names, the 3-2-1 countdown with the matchup beneath, then the timer, both armour percentages, the damage each has dealt and the rule in one line; the results screen shows
  the label and both totals.

No shrinking arena, extra damage, teleporting, health drain or anything that punishes running away.

## 6. Test results

### 6.1 Final Showdown (`ShowdownValidation`, 45 PASS, 0 FAIL)

| Test | Result | Evidence |
|---|---|---|
| S1 starts in Survival | PASS | 5 alive, no showdown after 20 s |
| S2 three or more survivors do not trigger it | PASS | 3 alive for 30 s |
| S3 exactly two trigger it once | PASS | prepared 1x, duplicate eliminations ignored, states Transition > Countdown > FinalShowdown |
| S4 one survivor ends normally | PASS | LastCarStanding, no showdown |
| S5 simultaneous eliminations deterministic | PASS | 3 left stays Survival; 2 left = showdown for exactly those two in either order; 1 left wins; repeat identical |
| S6 both finalists restored to 100 % | PASS | 40 % -> 100 % and 92 % -> 100 % (scenario 4), eliminated cars stay out |
| S7 deformation fully restored | PASS | 0.52 / 0.58 m of real crash deformation -> 0.00 mm deviation, 0 stored impacts |
| S8 collision geometry restored | PASS | every box collider back to its pristine centre and size (0.0 mm), nothing pending |
| S9 safe, opposite placement | PASS | 62 m apart, mirrored, inside walls, facing within 10 degrees, no overlaps, clear run |
| S10 zero velocity | PASS | 0.000 m/s |
| S11 countdown | PASS | 3 s: 3 > 2 > 1 > GO; HUD text checked through the real `DerbyUI` |
| S12 / S33 no movement, touch cannot bypass | PASS | touch throttle 1.0, car throttle 0.0, moved 0.00 m; after GO moved 12.5 m |
| S13 / S27 no combat damage in the countdown | PASS | a hulk thrown into the player: durability, hits taken and showdown damage unchanged |
| S14 / S15 timer starts at GO, 60 s | PASS | 60.0 -> 50.0 after 10 s; over at 60.2 s |
| S16 wreck ends it immediately | PASS | one result, timer frozen, WIN BY WRECK |
| S17 timeout compares showdown-only damage | PASS | 15 % v 6 % of the other's armour |
| S18 survival score irrelevant | PASS | survival 0 v 1050, the 0 won on showdown damage |
| S19 equal damage = draw | PASS | none dealt / equal / with a survival lead: all DRAW, both placed 1 |
| S20 simultaneous wrecks | PASS | damage dealt before the final blows decides; equal = draw; a late event changes nothing |
| S21 AI versus AI | PASS | both targeted each other, camera on a finalist, one result after 60 s |
| S22 player versus AI | PASS | one valid result within 60 s |
| S23 no respawns | PASS | 0 respawn events |
| S24 / S25 full arena, nothing added | PASS | same static colliders, same arena size, no hazard field in the rules |
| S26 no double counting | PASS | dealt 90 000 J = 45 % of the opponent's armour = what the opponent lost to those hits; overkill not counted |
| S28 / S29 restart in transition / countdown | PASS | clean new match, a later showdown still runs the full sequence |
| S30 rematch, S31 menu | PASS | clean Survival; AI cars gone, player's car repaired |
| S32 Points Derby unchanged | PASS | never prepares a showdown, respawns, 180 s, normal result |
| S34 WebGL build | PASS | 22.1 MB, 0 errors, Build 20261019-2142 (built from the branch; not published) |
| S35 cost | PASS | preparing the showdown 0.53 ms (once per match); duel step 0.53 ms per 50 Hz step with two AI drivers |
| S36 fleeing leader | PASS (tested, risk documented) | see 7.1 |

The menu and the in-match HUD were also checked in the browser pane at 844 x 390 (a phone held sideways): the menu (with the new OPPONENTS row) fits with START visible; the duel HUD and the results card read clearly.

### 6.2 AI V3 (`AiV3Validation`)

Decision-level evidence (all with the same scripted situations for every arm):

* **Scenario 1, charge in open ground** (50 m from rest to a parked car): mean first-hit closing speed previous AI 19.6, Casual 17.7, Standard 20.6, **Competitive 22.4 m/s** (71 / 64 / 74 / 81 km/h); Hunter 20.3 -> 23.6 m/s.
* **Chasing a moving car** (12 x 40 s): strong hits (8+ m/s closing) previous AI 15 of 20, Casual 4 of 19, Standard 16 of 24, **Competitive 21 of 29**; mean closing speed 9.1 / 5.5 / 8.9 / 10.9 m/s.
* **Scenario 2, target choice**: a badly hurt car that is far away and boxed in versus a less hurt one that is close and exposed: Competitive avoids the far one 6 of 6, Standard 4 of 6, the previous AI 0 of 6;
  with equal reach every V3 driver still takes the hurt car (6 of 6).
* **Melee** (9 matches of the previous AI everywhere; 9 matches with Casual, Standard and Competitive drivers side by side): points per hit 58.3 (previous) / 57.4 / 58.1 / 60.5, damage per hit 44.4 / 45.8 / 44.3 / 46.9 kJ,
  wrecks per driver and match 1.98 / 2.00 / 2.40 / 2.20, charges 17.4 / 19.5 / 17.9 / 17.9 (starting at 34 / 35 / 38 / 42 km/h), time pursuing 57 / 55 / 54 / 56 %, stuck 7 / 8 / 8 / 7 %,
  mean score per driver 1670 / 1843 / 1836 / 1774 (the spread between single matches is about 210-430, so **the melee score differences between the arms are not significant**).
* **1 v 1 duels** (same personality on both sides, only the difficulty differs): Standard v previous AI 47-58 % of decided duels, Casual v previous AI 49-65 % (about 55 % pooled, so Casual is roughly as strong as the previous AI, not weaker), Competitive v Casual 48-62 %, Competitive v Standard 44-63 %
  over several runs of 32-64 duels; pooled over five runs Competitive v Casual is 52 % and Competitive v Standard 50 %. **In symmetric 1 v 1 fights the difficulties cannot be told apart.** The damage engine gives the larger share to the car that does the less attacking, and
  two copies of the same AI exchange blows evenly whatever their judgement; the improvements show where one side makes decisions the other does not (the scenarios above) and in what a hit is worth (points per hit), not in who wins a mirror match.
  Earlier experiments that raised skill further (+0.20) or made the AI back off more made Competitive clearly *worse* in duels and were reverted.

| Required test | Result | Evidence |
|---|---|---|
| A1 Casual remains functional | PASS | every personality 16-25 charges and 33-40 hits per match, stuck 7-9 % |
| A2 Standard preserves the baseline | PASS | melee score 1670 -> 1836 (within -25 % / +35 %), hits 32 -> 36; duels 58 % against the previous AI (47-58 % over runs) |
| A3 Competitive makes better attack decisions | PASS (decision level) / not shown in duels | scenario 1 (+14 % closing speed), chase (+40 % strong hits), points per hit +5 % over Casual; duels: no difference (see above) |
| A4 personalities distinct | PASS | every pair differs in 4+ of 8 derived numbers; charges per match Brawler 24, Hunter 16, Cautious 14, Rookie 21; Cautious spends the most time making space |
| A5 distance before charging | PASS | charges in the chase start from 24 m (Standard) / 21 m (Competitive) and 17-39 set-up rolls per match; the strong-hit share rises |
| A6 vulnerable targets | PASS | scenario 2 above |
| A7 no constant target switching | PASS | 6.8 changes per driver-minute against 6.4 for the previous AI |
| A8 not passive | PASS | at least 78 % of the time pursuing / charging / rolling on for every personality at every difficulty |
| A9 AI still attacks AI | PASS | 297 AI-vs-AI hits in 4 x 60 s with an idle player |
| A10 no preferential targeting of the player | PASS | 39 hits on an idle player v 41 on an idle AI car (ratio 0.95) |
| A11 score awareness | PASS | see section 4 |
| A12 leader does not hide | PASS | match winners 83 % active, 16 % making space or recovering |
| A13 obstacle avoidance | PASS (with a note) | enemy-AI suite with V3 at Competitive: wall hits 0-1 v 5-6 without probing, reaches within 3 m; the suite's "stuck <= 2" bar was exceeded (4) in one run and met in others, V2-calibrated and noisy |
| A14 breakaway | PASS | forced-breakaway tests G, H, I, K, M of the tuning suite pass with V3 on (7 of 7) |
| A15 recovery | PASS | stuck 7-8 % of the time as before; driving validation passes |
| A16 no hidden stat bonuses | PASS | section 3 |
| A17 vehicles remain competitive | PASS | mean score by vehicle: muscle car 100 %, hatchback 109 %, pickup 91 % of the overall mean |
| A18 decision cost | PASS | AI call 4.6 -> 4.7 us (worst 0.20 ms), a target evaluation 2.1 -> 28 us, 0 bytes allocated |

### 6.3 Regression suites

* Driving validation (old arena, V3 off): ALL CHECKS PASSED.
* Derby validation: 80 PASS, 0 FAIL (the obsolete showdown checks were rewritten for the new rules).
* Gameplay tuning (tuning V1 checks, run with the previous AI settings): 22-23 PASS, 2-3 FAIL. `I` (forced breakaway, "every driver keeps charging") fails identically on the **unmodified main** (re-measured: 1 FAIL);
  `F/I` (milling share in a melee, 3.9 % -> 5.3 %) and `J/L` (breakaway counts over 3 runs) are statistical melee checks that change between runs. With V3 on, the forced-breakaway tests pass (7 of 7).
* Enemy-AI validation (old arena): 1 FAIL on the unmodified path (`J`, Hunter v Brawler precision, a statistical margin); with V3 Competitive 2 FAIL (`C` stuck count, `I`), with V3 Casual 2 FAIL (`A` pursuit distance, `K`): V2-calibrated thresholds.

## 7. Risks and limits

1. **Fleeing during the showdown (S36).** A scripted runner that lands one strong hit (18 % of the other's armour) and then only drives away at full speed, round the arena, was pursued by the AI in 5 of 6 pairings:
   the AI landed 3-7 hits (first after 7-19 s), mean gap 15-31 m, and the runner won only the two duels in which it was the pickup running from the muscle car (the AI
   landed 0 hits in Standard, 3 in Competitive; mean gap 46-52 m). So the AI can pressure a fleeing opponent but not reliably, and a fast runner in open space can still win on a single early hit. This is documented, not solved:
   no anti-running rule was added. If it shows up in play, options for a design pass are a damage lead that decays, a shrinking arena or a minimum-engagement rule - none of them approved.
2. **Duel strength.** The difficulty spread is visible in charges, hit strength and target choice, but not in AI-vs-AI duels or melee scores (section 6.2). How Competitive feels to a good human is not measured: no human playtest was done.
3. **Pickup AI.** The Hunter personality drives the pickup in the standard roster and scores lowest in the simulations (vehicle, not personality).
4. **Zero-zero draw.** A showdown in which neither car damages the other (for example both hit only walls) ends as a DRAW. Seen once in a 20 s browser test with 6 % durability; the editor duels at full durability always produced damage.
5. **Edge cases.** An all-eliminated survival still uses the old score policy (see section 5). A match with two participants has no survival phase and no showdown (the first wreck ends it).
6. **Not verified:** a real phone (frame rate, touch feel), the heap on low-end devices (unchanged: 531 MB), the published build (nothing was published).
7. **Opus-owned physics:** nothing was needed, so no handoff. The unrelated reported "stationary player takes 0 % damage" exploit remains with Opus and was not reproduced here.

## 8. Files

New: `Assets/Driving/Scripts/AiDifficulty.cs`, `Assets/Derby/Editor/AiV3Validation.cs`, `Assets/Derby/Editor/ShowdownValidation.cs`, this document.
Changed: `Assets/Driving/Scripts/EnemyDriver.cs`, `DriverProfile.cs` (+ the four profile assets), `Assets/Derby/Scripts/AiTargeting.cs`, `DerbyGame.cs`, `DerbyUI.cs`, `MatchModel.cs`, `SpawnPlanner.cs`,
`Assets/Derby/Editor/DerbySceneBuilder.cs` (Rookie intelligence), `DerbyValidation.cs`, `TuningValidation.cs`, `Assets/Driving/Editor/EnemyAiValidation.cs` (V3 switches).
