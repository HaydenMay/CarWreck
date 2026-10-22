# Baseline telemetry: Points Derby, Standard AI (milestone M0, generated)

**Loadout: FULL on all five cars (spiked_ram_plate, tow_hitch_bumper, roof_cage_rack).** Compare against the stock-car run of the same protocol.

30 AI-only matches (30 finished), 5 cars each, real DerbyArena rules and physics, stepped at 50 Hz in the editor (615 s wall time). Seat 0 (the player's seat) is driven by a bot; the player's vehicle rotates through the three bodies, so every match has all three chassis. Cells are mean ± standard error over car-matches. Nothing in the game was changed to produce this. Raw rows: the matching `matches-*.csv`.

## Per chassis (per car-match)

| Chassis | n | Score | Scored hits dealt | Hits taken | Energy dealt (kJ) | Energy taken (kJ) | Wrecks dealt | Times wrecked | Match wins |
|---|---|---|---|---|---|---|---|---|---|
| CompactHatchback | 50 | 1959.8 ± 53.8 | 31.3 ± 0.6 | 41.3 ± 0.8 | 1398.9 ± 34.5 | 1295.9 ± 41.9 | 3.1 ± 0.2 | 3.7 ± 0.1 | 10/50 (20%) |
| CompactPickup | 50 | 1653.0 ± 55.8 | 29.0 ± 0.7 | 40.5 ± 0.8 | 1204.8 ± 38.1 | 1401.0 ± 43.1 | 2.4 ± 0.2 | 2.8 ± 0.1 | 4/50 (8%) |
| MuscleCar | 50 | 2098.8 ± 57.4 | 31.8 ± 0.7 | 43.7 ± 0.7 | 1540.6 ± 35.8 | 1447.3 ± 36.9 | 3.6 ± 0.2 | 2.8 ± 0.1 | 16/50 (32%) |

A chance win rate for one car in a five-car match is 20 %.

## Scored hits by tier (share of each chassis' scored hits)

| Chassis | Glancing | Solid | Heavy | Devastating | Scored hits per minute |
|---|---|---|---|---|---|
| CompactHatchback | 36% | 21% | 27% | 15% | 10.44 |
| CompactPickup | 35% | 27% | 26% | 12% | 9.65 |
| MuscleCar | 34% | 24% | 23% | 19% | 10.60 |

## Finishing blows (wreck stealing)

A wreck is 'assisted' when the car that wrecked the victim had done less than half of the damage that victim took in that life. The wreck bonus (150 points) goes to the finisher either way.

| Chassis | Wrecks dealt | Solo | Assisted | Assisted share |
|---|---|---|---|---|
| CompactHatchback | 157 | 52 | 105 | 67% |
| CompactPickup | 120 | 42 | 78 | 65% |
| MuscleCar | 178 | 67 | 111 | 62% |

## Per AI personality (to show how much of the spread is the driver, not the chassis)

| Profile | n | Score | Scored hits dealt | Wrecks dealt | Times wrecked |
|---|---|---|---|---|---|
| bot-Hunter | 30 | 1932.3 ± 76.4 | 30.0 ± 0.7 | 3.2 ± 0.3 | 3.2 ± 0.1 |
| Brawler | 30 | 1981.0 ± 76.7 | 32.2 ± 0.9 | 2.9 ± 0.3 | 3.2 ± 0.2 |
| Cautious | 30 | 1786.7 ± 93.4 | 31.1 ± 1.0 | 2.8 ± 0.4 | 3.2 ± 0.2 |
| Hunter | 30 | 1891.3 ± 68.6 | 29.1 ± 0.8 | 3.5 ± 0.3 | 2.6 ± 0.1 |
| Rookie | 30 | 1928.0 ± 79.3 | 31.1 ± 0.9 | 2.8 ± 0.3 | 3.4 ± 0.2 |

## Match level

- Mean match length 180 s (sd 0); unfinished matches: 0.
- Draws: 0.
- Applied hits against non-car objects (environment): 1058 in total across all cars.

## How to read this, and its limits

- These are the current game's numbers with the full kit above on every car. Compare them only against a stock run of the same protocol (same mode, difficulty, match count and roster rotation).
- Standard errors assume car-matches are independent; cars in one match are not (they hit each other), so treat the intervals as optimistic and compare chassis only when differences are several standard errors.
- The seat-0 bot is built like `DerbyValidation.Balance` and does not get the game's hulk-avoidance hook, so it is a slightly different driver from the AI cars. The per-personality table shows how much that matters.
- Editor physics at a fixed 50 Hz step is not the WebGL build on a phone; this is a baseline of rules and balance, not of performance.
- Human play is not represented.
