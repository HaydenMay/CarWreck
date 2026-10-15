# Isolated editor phase profile

Unity 6000.6.0f1, Ryzen7 5700 / RTX4060. Three six-direction passes per vehicle; first pass warm-up, remaining 12 measured. Own Blender render was finished before measurements. Other open Blender was idle (about 1 CPU-second over several minutes). No optimization change.

|Vehicle|Resolve|Accumulate|Rebuild|Total avg|max|
|---|---:|---:|---:|---:|---:|
|Muscle car|24.08 ms|18.91 ms|8.46 ms|51.45 ms|68.37 ms|
|Hatchback|34.60 ms|35.48 ms|12.25 ms|82.34 ms|104.83 ms|
|Pickup|42.32 ms|60.49 ms|13.80 ms|116.61 ms|149.03 ms|

Pickup cost is dominated by impact accumulation (~52%) and footprint resolution (~36%); mesh rebuild is ~12%. Existing instrumentation times those phases independently. Source inspection points to repeated per-vertex silhouette/slab queries and noise evaluation in ImpactField, and current-vertex footprint collection plus convex-hull sorting in ResolveFootprint. Child-function timing was not measured, so these are code-supported bottleneck hypotheses, not claimed profiler samples.

Imported pickup has 114,694 deforming vertices versus 62,014 muscle / 80,108 hatchback. No density/appearance compromise was made. A future investigation can profile those child loops before considering data-oriented evaluation or job/burst work; no rewrite is included.

GC.GetAllocatedBytesForCurrentThread returned 0 for every measured launch, including work known to create arrays/lists. This counter is treated as unavailable in this Unity Mono session, not proof of zero allocation. Memory budget/player frame times and other hardware/browsers remain unmeasured. Costs are synchronous impact-time hitches, not an FPS guarantee.
