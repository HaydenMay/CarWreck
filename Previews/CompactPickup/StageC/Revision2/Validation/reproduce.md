# Reproduce focused C evidence

Use Unity 6000.6.0f1 from the compact-pickup feature worktree. Close any Editor using that same project before batch runs. The tools collect evidence; a normal process exit alone does not mean mandatory QA passed. Read the generated reports and the explicit pending summary. Keep the recorded failing fixture unchanged when re-running physics.

PowerShell examples, from the project root:

```powershell
$pickupUnity = 'C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe'
$pickupProject = (Get-Location).Path
& $pickupUnity -batchmode -quit -projectPath $pickupProject -executeMethod DamageLab.EditorTools.PickupCheckpointCExtensionValidation.Run -extensionSuite 1 -evidenceDir Temp/PickupExtensionRerun -logFile Temp/PickupExtensionRerun.log
```

That collects all three integration/adaptive/overflow/replay/Chaos suites, switching and a phase profile. Current expected mandatory findings are pickup suspension >5% and pickup/hatch sensitivity >20 mm; do not reinterpret an execution exit code as a green result. The preserved report is the source of the observed values.

To reproduce the durable eight-impact counterexample without new PhysX launches, copy `Previews/CompactPickup/StageC/Revision2/Investigation/ChaosReplay/vehicle2_seed2026_recorded.json` to `Temp/PickupReplayProbe/vehicle2_seed2026_recorded.json`, creating the directory first, then:

```powershell
& $pickupUnity -batchmode -quit -projectPath $pickupProject -executeMethod DamageLab.EditorTools.CollisionPersistenceValidation.RunSavedChaosProbe -evidenceDir Temp/PickupReplayProbe -logFile Temp/PickupReplayProbe.log
```

The command writes `saved_eight_impact_probe.txt`: exact replay zero, shifted z 26.582 mm at impact 8, optimization-on/off agreement. The last two per-vertex probes isolate impact 3 and 8.

For the twelve independent contact contracts use the first command with `-contractFixtures 1` instead of `-extensionSuite 1`. Coverage-only mode uses `-coverageOnly 1 -vehicleIndex 2` and explicitly exits nonzero if coverage fails. These do not change shared runtime tuning.

Topology experiments: run `Blender/Scripts/probe_pickup_topology_candidates.py` in headless Blender with project root and output directory, then use the extension runner `-topologyOnly 1` with that directory. The raw candidate JSON is a disposable generated input, excluded from the review package. The committed CSV retains every candidate result, including failures.
