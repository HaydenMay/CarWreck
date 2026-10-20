# Baseline telemetry: Points Derby, Standard AI (milestone M0, generated)

30 AI-only matches (30 finished), 5 cars each, real DerbyArena rules and physics, stepped at 50 Hz in the editor (635 s wall time). Seat 0 (the player's seat) is driven by a bot; the player's vehicle rotates through the three bodies, so every match has all three chassis. Cells are mean ± standard error over car-matches. Nothing in the game was changed to produce this. Raw rows: the matching `matches-*.csv`.

## Per chassis (per car-match)

| Chassis | n | Score | Scored hits dealt | Hits taken | Energy dealt (kJ) | Energy taken (kJ) | Wrecks dealt | Times wrecked | Match wins |
|---|---|---|---|---|---|---|---|---|---|
| CompactHatchback | 50 | 2008.6 ± 63.1 | 31.3 ± 0.6 | 40.2 ± 0.8 | 1463.7 ± 36.5 | 1271.7 ± 29.1 | 3.0 ± 0.2 | 3.6 ± 0.1 | 13/50 (26%) |
| CompactPickup | 50 | 1674.2 ± 52.7 | 29.7 ± 0.7 | 40.4 ± 0.7 | 1234.8 ± 38.6 | 1470.1 ± 42.4 | 2.4 ± 0.2 | 2.8 ± 0.1 | 0/50 (0%) |
| MuscleCar | 50 | 2113.4 ± 66.6 | 30.7 ± 0.7 | 41.5 ± 0.7 | 1553.3 ± 43.0 | 1510.0 ± 47.1 | 3.6 ± 0.3 | 3.0 ± 0.1 | 17/50 (34%) |

A chance win rate for one car in a five-car match is 20 %.

## Scored hits by tier (share of each chassis' scored hits)

| Chassis | Glancing | Solid | Heavy | Devastating | Scored hits per minute |
|---|---|---|---|---|---|
| CompactHatchback | 33% | 24% | 26% | 17% | 10.43 |
| CompactPickup | 35% | 28% | 25% | 12% | 9.89 |
| MuscleCar | 32% | 24% | 25% | 20% | 10.23 |

## Finishing blows (wreck stealing)

A wreck is 'assisted' when the car that wrecked the victim had done less than half of the damage that victim took in that life. The wreck bonus (150 points) goes to the finisher either way.

| Chassis | Wrecks dealt | Solo | Assisted | Assisted share |
|---|---|---|---|---|
| CompactHatchback | 152 | 44 | 108 | 71% |
| CompactPickup | 122 | 44 | 78 | 64% |
| MuscleCar | 181 | 74 | 107 | 59% |

## Per AI personality (to show how much of the spread is the driver, not the chassis)

| Profile | n | Score | Scored hits dealt | Wrecks dealt | Times wrecked |
|---|---|---|---|---|---|
| bot-Hunter | 30 | 2135.3 ± 88.4 | 31.9 ± 0.7 | 3.7 ± 0.3 | 3.1 ± 0.1 |
| Brawler | 30 | 1953.3 ± 79.9 | 31.2 ± 0.9 | 3.1 ± 0.3 | 3.1 ± 0.1 |
| Cautious | 30 | 1791.7 ± 82.2 | 30.1 ± 0.8 | 2.4 ± 0.3 | 3.3 ± 0.1 |
| Hunter | 30 | 2078.7 ± 86.2 | 30.3 ± 0.8 | 4.0 ± 0.3 | 3.0 ± 0.1 |
| Rookie | 30 | 1701.3 ± 67.1 | 29.2 ± 0.9 | 2.0 ± 0.2 | 3.2 ± 0.2 |

## Match level

- Mean match length 180 s (sd 0); unfinished matches: 0.
- Draws: 0.
- Applied hits against non-car objects (environment): 1001 in total across all cars.

## How to read this, and its limits

- These are the current game's numbers for the stock cars with no attachments. A later attachment build must be compared against the same protocol (same mode, difficulty, match count and roster rotation), never against a human or a different setup.
- Standard errors assume car-matches are independent; cars in one match are not (they hit each other), so treat the intervals as optimistic and compare chassis only when differences are several standard errors.
- The seat-0 bot is built like `DerbyValidation.Balance` and does not get the game's hulk-avoidance hook, so it is a slightly different driver from the AI cars. The per-personality table shows how much that matters.
- Editor physics at a fixed 50 Hz step is not the WebGL build on a phone; this is a baseline of rules and balance, not of performance.
- Human play is not represented.
