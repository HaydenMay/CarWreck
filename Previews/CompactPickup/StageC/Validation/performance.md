# Measured editor performance

Ryzen 7 5700 (8 cores / 16 threads), RTX 4060, Unity 6000.6.0f1, Windows. Real PhysX scripted at 50 Hz, anchored car.
Final suites ran after Blender rendering finished. These are editor per-impact costs, not frame-rate or player-build guarantees. Memory and player frame-time budgets were not measured.

- every hit applied PASS; deformation per hit 122.0 ms avg / 186.1 ms max over 15
- REBUILD OPTIMISATION (9 recorded impacts replayed): before 89.0 ms/impact (parts evaluated 52.0, rebuilt 52.0 of 52); after 78.5 ms/impact (evaluated 35.2, rebuilt 35.2); identical mesh: max difference 0.0E+000 m PASS; matches the live run 0.0E+000 m
- deformation per hit by block of 20: 124.2 / 122.4 / 124.6 / 122.5 / 126.8 ms; hitbox update 2.77 ms avg over 36 updates
- COSTS (editor, this machine): 14 car colliders; hitbox update 2.57 ms avg / 4.72 ms max over 98 updates (only after impacts; 9.5 of 16 faces re-fitted per update on average); visual deformation 107.7 ms avg / 189.2 ms max per hit; physics step 0.046 ms avg over 34200 steps
- Deformation cost per hit: 104.6 ms avg for hits 1-24, 109.9 ms avg for hits 25+ (merging is O(1); per-hit cost does not grow)
- PERFORMANCE (editor): 50-shot run 7.5 s wall time for 1911 physics steps (38 steps = 0.76 s simulated per shot); deformation 134.0 ms avg / 192.6 ms max per impact; hitbox update 2.31 ms avg; physics step 0.069 ms avg

The synchronous deformation costs can cause visible impact-time hitches. No claim of meeting a performance budget is made.
