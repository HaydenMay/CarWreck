# Baseline telemetry: Last Car Standing, Standard AI (milestone M0, generated)

**Loadout: FULL on all five cars (spiked_ram_plate, tow_hitch_bumper, roof_cage_rack).** Compare against the stock-car run of the same protocol.

30 AI-only matches (30 finished), 5 cars each, real DerbyArena rules and physics, stepped at 50 Hz in the editor (403 s wall time). Seat 0 (the player's seat) is driven by a bot; the player's vehicle rotates through the three bodies, so every match has all three chassis. Cells are mean ± standard error over car-matches. Nothing in the game was changed to produce this. Raw rows: the matching `matches-*.csv`.

## Per chassis (per car-match)

| Chassis | n | Score | Scored hits dealt | Hits taken | Energy dealt (kJ) | Energy taken (kJ) | Wrecks dealt | Times wrecked | Survival (s) | Reached Showdown | Showdown wins |
|---|---|---|---|---|---|---|---|---|---|---|---|
| CompactHatchback | 50 | 939.8 ± 57.0 | 16.4 ± 0.8 | 21.9 ± 1.0 | 810.4 ± 48.1 | 681.8 ± 21.6 | 0.6 ± 0.1 | 0.8 ± 0.1 | 95.1 ± 5.8 | 14/50 | 3/14 |
| CompactPickup | 50 | 815.0 ± 42.4 | 17.0 ± 0.7 | 25.3 ± 1.1 | 691.3 ± 32.0 | 889.6 ± 26.3 | 0.6 ± 0.1 | 0.5 ± 0.1 | 113.4 ± 5.9 | 21/50 | 6/21 |
| MuscleCar | 50 | 1063.2 ± 62.8 | 18.3 ± 0.7 | 25.3 ± 1.0 | 964.0 ± 52.1 | 894.3 ± 38.9 | 0.8 ± 0.1 | 0.6 ± 0.1 | 109.7 ± 6.1 | 21/50 | 19/21 |

A chance win rate for one car in a five-car match is 20 %.

## Scored hits by tier (share of each chassis' scored hits)

| Chassis | Glancing | Solid | Heavy | Devastating | Scored hits per minute |
|---|---|---|---|---|---|
| CompactHatchback | 33% | 20% | 28% | 19% | 6.49 |
| CompactPickup | 36% | 29% | 22% | 12% | 6.83 |
| MuscleCar | 33% | 23% | 23% | 21% | 7.40 |

## Finishing blows (wreck stealing)

A wreck is 'assisted' when the car that wrecked the victim had done less than half of the damage that victim took in that life. The wreck bonus (150 points) goes to the finisher either way.

| Chassis | Wrecks dealt | Solo | Assisted | Assisted share |
|---|---|---|---|---|
| CompactHatchback | 30 | 7 | 23 | 77% |
| CompactPickup | 28 | 7 | 21 | 75% |
| MuscleCar | 38 | 15 | 23 | 61% |

## Per AI personality (to show how much of the spread is the driver, not the chassis)

| Profile | n | Score | Scored hits dealt | Wrecks dealt | Times wrecked |
|---|---|---|---|---|---|
| bot-Hunter | 30 | 1084.7 ± 78.5 | 19.4 ± 0.8 | 0.9 ± 0.2 | 0.5 ± 0.1 |
| Brawler | 30 | 967.7 ± 60.5 | 17.5 ± 0.8 | 0.6 ± 0.1 | 0.7 ± 0.1 |
| Cautious | 30 | 809.0 ± 64.3 | 15.5 ± 1.0 | 0.5 ± 0.2 | 0.8 ± 0.1 |
| Hunter | 30 | 881.7 ± 60.5 | 17.5 ± 1.0 | 0.6 ± 0.1 | 0.6 ± 0.1 |
| Rookie | 30 | 953.7 ± 89.5 | 16.4 ± 0.9 | 0.6 ± 0.2 | 0.7 ± 0.1 |

## Match level

- Mean match length 150 s (sd 22); unfinished matches: 0.
- Final Showdown reached in 28 of 30 matches; Showdown decided by a win (not a draw) in 28 of them.
- Draws: 0.
- Applied hits against non-car objects (environment): 696 in total across all cars.

## How to read this, and its limits

- These are the current game's numbers with the full kit above on every car. Compare them only against a stock run of the same protocol (same mode, difficulty, match count and roster rotation).
- Standard errors assume car-matches are independent; cars in one match are not (they hit each other), so treat the intervals as optimistic and compare chassis only when differences are several standard errors.
- The seat-0 bot is built like `DerbyValidation.Balance` and does not get the game's hulk-avoidance hook, so it is a slightly different driver from the AI cars. The per-personality table shows how much that matters.
- Editor physics at a fixed 50 Hz step is not the WebGL build on a phone; this is a baseline of rules and balance, not of performance.
- Human play is not represented.
