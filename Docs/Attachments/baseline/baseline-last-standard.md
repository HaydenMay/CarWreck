# Baseline telemetry: Last Car Standing, Standard AI (milestone M0, generated)

30 AI-only matches (30 finished), 5 cars each, real DerbyArena rules and physics, stepped at 50 Hz in the editor (397 s wall time). Seat 0 (the player's seat) is driven by a bot; the player's vehicle rotates through the three bodies, so every match has all three chassis. Cells are mean ± standard error over car-matches. Nothing in the game was changed to produce this. Raw rows: the matching `matches-*.csv`.

## Per chassis (per car-match)

| Chassis | n | Score | Scored hits dealt | Hits taken | Energy dealt (kJ) | Energy taken (kJ) | Wrecks dealt | Times wrecked | Survival (s) | Reached Showdown | Showdown wins |
|---|---|---|---|---|---|---|---|---|---|---|---|
| CompactHatchback | 50 | 878.4 ± 48.3 | 15.7 ± 0.7 | 19.9 ± 1.0 | 722.4 ± 38.4 | 649.0 ± 17.9 | 0.6 ± 0.1 | 0.8 ± 0.1 | 89.3 ± 5.5 | 10/50 | 1/10 |
| CompactPickup | 50 | 859.8 ± 51.3 | 16.2 ± 0.6 | 22.5 ± 0.8 | 732.8 ± 44.1 | 910.1 ± 32.8 | 0.5 ± 0.1 | 0.7 ± 0.1 | 99.6 ± 5.8 | 16/50 | 8/16 |
| MuscleCar | 50 | 1218.8 ± 58.6 | 19.2 ± 0.7 | 26.3 ± 0.9 | 1112.8 ± 58.7 | 1008.9 ± 40.4 | 0.9 ± 0.1 | 0.5 ± 0.1 | 119.0 ± 5.6 | 30/50 | 19/30 |

A chance win rate for one car in a five-car match is 20 %.

## Scored hits by tier (share of each chassis' scored hits)

| Chassis | Glancing | Solid | Heavy | Devastating | Scored hits per minute |
|---|---|---|---|---|---|
| CompactHatchback | 32% | 24% | 27% | 17% | 6.45 |
| CompactPickup | 30% | 28% | 28% | 14% | 6.57 |
| MuscleCar | 29% | 20% | 26% | 25% | 7.95 |

## Finishing blows (wreck stealing)

A wreck is 'assisted' when the car that wrecked the victim had done less than half of the damage that victim took in that life. The wreck bonus (150 points) goes to the finisher either way.

| Chassis | Wrecks dealt | Solo | Assisted | Assisted share |
|---|---|---|---|---|
| CompactHatchback | 31 | 2 | 29 | 94% |
| CompactPickup | 27 | 7 | 20 | 74% |
| MuscleCar | 43 | 16 | 27 | 63% |

## Per AI personality (to show how much of the spread is the driver, not the chassis)

| Profile | n | Score | Scored hits dealt | Wrecks dealt | Times wrecked |
|---|---|---|---|---|---|
| bot-Hunter | 30 | 984.3 ± 70.9 | 18.1 ± 1.1 | 0.6 ± 0.1 | 0.7 ± 0.1 |
| Brawler | 30 | 1132.0 ± 82.1 | 17.9 ± 0.9 | 0.7 ± 0.1 | 0.6 ± 0.1 |
| Cautious | 30 | 790.7 ± 55.1 | 15.3 ± 0.8 | 0.5 ± 0.1 | 0.8 ± 0.1 |
| Hunter | 30 | 1041.3 ± 72.2 | 17.5 ± 0.8 | 0.9 ± 0.2 | 0.5 ± 0.1 |
| Rookie | 30 | 980.0 ± 78.7 | 16.3 ± 0.8 | 0.7 ± 0.1 | 0.8 ± 0.1 |

## Match level

- Mean match length 146 s (sd 17); unfinished matches: 0.
- Final Showdown reached in 28 of 30 matches; Showdown decided by a win (not a draw) in 28 of them.
- Draws: 0.
- Applied hits against non-car objects (environment): 608 in total across all cars.

## How to read this, and its limits

- These are the current game's numbers for the stock cars with no attachments. A later attachment build must be compared against the same protocol (same mode, difficulty, match count and roster rotation), never against a human or a different setup.
- Standard errors assume car-matches are independent; cars in one match are not (they hit each other), so treat the intervals as optimistic and compare chassis only when differences are several standard errors.
- The seat-0 bot is built like `DerbyValidation.Balance` and does not get the game's hulk-avoidance hook, so it is a slightly different driver from the AI cars. The per-personality table shows how much that matters.
- Editor physics at a fixed 50 Hz step is not the WebGL build on a phone; this is a baseline of rules and balance, not of performance.
- Human play is not represented.
