# Unity project context for the attachment investigation

Analyzed 2026-10-14 at main `2a9ca8bebb296557ed75513c9ad892c8f5153d92`.
This scoped onboarding lives beside the isolated prototype documentation.

<!-- unity-onboarding:generated:start -->

## Confirmed environment

- Unity 6000.6.0f1, revision f7f8ed4d1e24, from ProjectSettings/ProjectVersion.txt.
- URP 17.6.0; QualitySettings assigns separate Mobile and PC render-pipeline assets.
  The global GraphicsSettings pipeline slot is empty, so package presence alone was not
  used to infer the active pipeline.
- Input System 1.20.0; activeInputHandler is 1.
- Unity Test Framework 1.8.0 installed. No tracked first-party test assemblies or .asmdef
  files were found in main. Existing custom editor validation is the useful baseline.
- No networking dependency or first-party multiplayer implementation was established.
- No Unity MCP package/configuration or callable Unity tools were detected. Editor
  connection, live Console, play mode, tests and player build state are unverified.
  No package installation is needed for the Blender-only investigation.

## Structure, scenes and architecture

- Blender/Scripts contains procedural vehicle generators and hatchback asset checks.
- Blender contains the authoritative source vehicles, tracked with Git LFS.
- Assets/DamageLab contains the deformation engine, inspection/collision labs, editor
  tooling and the muscle car; Assets/Vehicles/CompactHatchback is vehicle-specific.
- Enabled scenes: VehicleDamageLab, SampleScene, VehicleCollisionLab, in that order.
  Damage Lab is first in serialized Build Settings; actual player startup was not run.
- MonoBehaviour vehicle controllers own deformable parts and cached per-vertex data.
  VehiclePartRule matches component names; unmatched child MeshFilters are deformable
  by default. Wheels use a separate rigid-follow path.
- Main exposes a post-rebuild Deformed event and cached current vertex access.
  These are integration observations, not changes or requirements imposed on Claude's WIP.
- DamageLab namespaces and editor-only folders separate tooling from runtime.
  Representative code uses public serialized configuration, camelCase fields and braces
  on separate lines. No unrelated code conventions were imposed.

## Authoritative asset facts

The saved hatchback and muscle car both use metres, +X right, +Y forward, +Z up in Blender,
with identity roots. Unity's committed hatchback asset report confirms +Z forward and
-X left, readable meshes and metre-scale bounds. The hood belongs to Body on both vehicles.
Body and Cabin have applied mesh geometry. Wheel origins are axle-centred.
Bumper origins differ between the vehicles; sockets must not be inferred from pivots.
See evidence/inspection.json for exact mesh counts, bounds, transforms and source hashes.

## Validation and tooling

The live Blender 5.2 connection responds, but its addon reports protocol 5 versus expected
13. The open hatchback is dirty and in Sculpt mode. Only read-only live queries were made;
saved-source inspections ran in independent background Blender 5.2 processes.
The saved main source has 30,586 Body vertices, while the live unsaved mesh had 30,805.
Only the verified saved file is authoritative for this task.

Fresh evidence: saved-source inspection and existing-FBX reimport of selected parts.
Historical evidence: Previews/CompactHatchback/StageC/unity_import_report.json.
No Unity, collision, damage, repair, performance or new-accessory tests have run.

## Git and scope constraints

Primary checkout remains on vehicle-engine-wip with existing unrelated changes.
The hatchback worktree remains on feature/compact-hatchback.
A managed attachment worktree was created from main after GitHub main was verified at
2a9ca8b; its branch is feature/attachment-prototype.

The isolated checkout also reports the pre-existing TutorialInfo URP icon/LFS baseline
difference. It is excluded from this task's staging. Selected .blend/.fbx files were
materialized from the local LFS cache, and source hashes match main.
No C#, Unity scenes, packages, production vehicle files or source generators may change.

## Evidence paths inspected

- ProjectSettings/ProjectVersion.txt, EditorBuildSettings.asset, QualitySettings.asset,
  GraphicsSettings.asset, ProjectSettings.asset; Packages/manifest.json.
- Blender/Scripts/build_compact_hatchback.py and build_muscle_car.py.
- Blender/CompactHatchback.blend and MuscleCar.blend.
- Both production FBXs and import metadata.
- Assets/DamageLab/Editor/MuscleCarModelPostprocessor.cs.
- Assets/Vehicles/CompactHatchback/Editor/CompactHatchbackAssetValidation.cs.
- Main's DeformablePart.cs, VehicleDamageModel.cs and VehiclePartRule.cs.
- Assets/DamageLab/README.md and hatchback Stage C reports/documentation.

<!-- unity-onboarding:generated:end -->
