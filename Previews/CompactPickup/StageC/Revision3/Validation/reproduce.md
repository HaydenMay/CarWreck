# Reproduce Revision 3 evidence

Use Unity 6000.6.0f1 on `feature/vehicle-compact-pickup-c`. Close any Editor that has this project open first. Read the reports, not just the exit code: a process can exit 0 while a report says FAIL.

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
$project = (Get-Location).Path
```

## Full three-vehicle suite

```powershell
& $unity -batchmode -quit -projectPath $project -executeMethod DamageLab.EditorTools.PickupCheckpointCExtensionValidation.Run -extensionSuite 1 -evidenceDir Logs/Rev3Suite -logFile Logs/Rev3Suite.log
```

This writes the 12 contact-contract fixtures and, for each vehicle, integration, adaptive coverage/84-hit stress, overflow and seed-2026 50-impact Chaos (including the 0.1 mm perturbation test). It also writes switching and a phase profile. Add `-profileOnly 1` instead of `-extensionSuite 1` for the profile alone. The baseline comparison in `performance.md` ran that on commit 3ac4a8d.

Other bridge suites:

```powershell
& $unity -batchmode -quit -projectPath $project -executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "vehicle-import-check;vehicle-import-roundtrip" -logFile Logs/Rev3Import.log
& $unity -batchmode -quit -buildTarget WebGL -projectPath $project -executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "collision-webgl" -logFile Logs/Rev3WebGL.log
```

## Problem A: coherent tube sections

Standalone prototype, which compares per-vertex and section-coherent fields without changing runtime state:

```powershell
& $unity -batchmode -quit -projectPath $project -executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "tube-prototype 2 Suspension" -logFile Logs/Rev3Tube.log
```

Actual Unity before/after renders of the front suspension on its own (sections off in memory, then on):

```powershell
& $unity -batchmode -quit -projectPath $project -executeMethod DamageLab.EditorTools.CollisionPersistenceValidation.RunSuspensionVisualProbe -evidenceDir Logs/Rev3Suspension -logFile Logs/Rev3Suspension.log
```

## Problem B: grazing-contact draft

Copy `Previews/CompactPickup/StageC/Revision3/Grazing/vehicle*_seed2026_recorded.json` into a new directory such as `Logs/Rev3Grazing` (not `Temp/`, which Unity clears at startup). Then run each probe with `-evidenceDir Logs/Rev3Grazing`:

- `DamageLab.EditorTools.CollisionPersistenceValidation.RunGrazingDraftProbe`: the 8-impact counterexample and both 50-impact replays. It covers the exact surface (failing case) and 1°, 2° and 3° drafts, with footprint-frozen and fold-off isolations.
- `...RunGrazingOrientationProbe`: every grazing impact as recorded vs. turned exactly parallel.
- `...RunGrazingTiltSweep`: each grazing impact's tilt reduced towards zero.

None of these launch new PhysX shots. They replay the recorded impacts, so the numbers reproduce exactly.

`RunGrazingDraftProbe` also accepts `-diagnoseImpacts N`. For vehicles other than the pickup counterexample, it then runs the first N impacts with the per-draft diagnostics.

### Muscle impact-8 fixture (silhouette-read fix)

`Investigation/MuscleImpact8/` holds one recorded muscle run (sweep Run1). The muscle car failed 8 of 15 sweep runs with a 48.3 mm difference at impact 8. The cause: a vertex just outside the box silhouette read the drafted lead at a point nudged 2 mm inward, so the lead jumped by 2 mm / tan(2°) ≈ 57 mm across the edge. `ImpactFootprint.OutsideLead` now reads the drafted envelope at the nearest silhouette point itself.

- `grazing_draft_probe_before_fix.txt` is the failing output.
- `grazing_draft_probe.txt` is the output with the fix.

```powershell
& $unity -batchmode -quit -projectPath $project -executeMethod DamageLab.EditorTools.CollisionPersistenceValidation.RunGrazingDraftProbe -evidenceDir Previews/CompactPickup/StageC/Revision3/Investigation/MuscleImpact8 -diagnoseImpacts 8 -logFile Logs/Rev3Muscle.log
```

### Perturbation sweep replay

`Investigation/PerturbationSweep/Run0..Run14` each hold one seed-2026 Chaos recording per vehicle, from 15 separate Editor launches. PhysX contact sampling differs between launches, so each run is a different 50-impact sequence. This command re-judges every recording with the current code and the four 0.1 mm shifts, and writes `sweep_replay.txt`:

```powershell
& $unity -batchmode -quit -projectPath $project -executeMethod DamageLab.EditorTools.CollisionPersistenceValidation.RunGrazingSweepReplay -evidenceDir Previews/CompactPickup/StageC/Revision3/Investigation/PerturbationSweep -logFile Logs/Rev3Sweep.log
```
