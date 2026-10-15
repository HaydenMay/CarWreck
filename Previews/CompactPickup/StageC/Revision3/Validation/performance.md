# Isolated editor phase profile

Unity 6000.6.0f1, Ryzen 7 5700 / RTX 4060, batch mode. Three six-direction passes per vehicle: the first pass is warm-up and the remaining 12 impacts are measured. All columns are warm-pass averages, except **max**, which is the slowest single measured impact. The three profiles ran back to back on the same machine with the same command (`-profileOnly 1`). The baseline ran on commit 3ac4a8d, the last commit Sol pushed.

|Vehicle|Build|Resolve|Accumulate|Rebuild|Total avg|max|
|---|---|---:|---:|---:|---:|---:|
|Muscle car|3ac4a8d baseline|24.71 ms|20.49 ms|8.86 ms|54.06 ms|71.72 ms|
||Rev 3, no culling|63.09 ms|29.53 ms|8.06 ms|100.69 ms|142.20 ms|
||**Rev 3, final**|**25.01 ms**|**20.46 ms**|**8.07 ms**|**53.54 ms**|**68.78 ms**|
|Hatchback|3ac4a8d baseline|34.51 ms|35.72 ms|12.49 ms|82.71 ms|106.62 ms|
||Rev 3, no culling|91.48 ms|54.80 ms|12.15 ms|158.43 ms|222.42 ms|
||**Rev 3, final**|**36.90 ms**|**37.83 ms**|**12.01 ms**|**86.74 ms**|**108.58 ms**|
|Pickup|3ac4a8d baseline|42.88 ms|62.39 ms|13.93 ms|119.20 ms|153.23 ms|
||Rev 3, no culling|111.28 ms|97.41 ms|13.66 ms|222.36 ms|307.41 ms|
||**Rev 3, final**|**45.50 ms**|**67.66 ms**|**13.94 ms**|**127.10 ms**|**160.67 ms**|

## Where the cost came from

The grazing-contact draft (Problem B) replaces the exact box surface with an upper envelope of 2° cones raised from the box's 12 projected edges. The first implementation evaluated all 12 cones for every vertex, in both footprint resolution and impact accumulation. That nearly doubled per-impact cost on all three vehicles (the "no culling" rows).

## Fix: exact edge culling

A cone can only raise the envelope within `(edgeTop − t) / k` of its edge's projected bounds, where `k = tan(draft)`. `Envelope` now skips any edge whose bounds are further away than that, plus a 0.1 mm margin. The skipped cones could never have been the maximum, so the output should be unchanged. Checked against the build without culling: the muscle impact-8 probe report and the 45-recording `sweep_replay.txt` are byte-for-byte identical before and after culling (both report figures to 0.001 mm).

In the final suite run, total average is −0.5 ms (−1.0%) against the baseline on the muscle car, +4.0 ms (+4.9%) on the hatchback and +7.9 ms (+6.6%) on the pickup. A standalone post-cull profile in the same session measured +2.6 / +5.1 / +8.0 ms, so the muscle-car figure is within run-to-run variation. Rebuild is unchanged. The remaining cost is the draft envelope that is still evaluated near box edges, together with the second silhouette query in `OutsideLead`. That is a small enough cost that removing the duplicate query was not worth the extra code; this is a judgement call, not a measured necessity.

## Coherent tube sections (Problem A)

Section lookup tables are built once at load, only for parts whose rule sets `coherentTubeSections` (on the pickup, only the suspension parts). They add about 44 KB to the pickup. At impact time, sections add one per-section minimum, a ring mean and a per-ring average. These are not separately visible in the profile: Accumulate grows by the same proportion on all three vehicles, and the muscle car and hatchback do not use sections.

## Caveats

- These are synchronous impact-time costs measured in the Editor, not a player frame-time guarantee. WebGL, other hardware and other browsers were not profiled.
- The machine was otherwise idle, but these are wall-clock timings from one session, not repeated statistical runs. Differences of about 1–2 ms are within session-to-session variation.
- `GC.GetAllocatedBytesForCurrentThread` returned 0 for every impact, as in Revision 2, so the counter is treated as unavailable in this Mono session, not as proof of zero allocation.
- The "Rev 3, final" rows are the warm-pass summary of `phase_profile.csv`, written by the final `-extensionSuite 1` run. The baseline and no-culling rows come from standalone `-profileOnly 1` runs.
