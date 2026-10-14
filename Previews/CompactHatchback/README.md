# Compact Hatchback — Stage A

**Archived Stage A documentation and previews**, describing commit `4cea7f9`.
The approved model has since been refined. Current production files, reproduction
instructions and validation are documented in [Stage B](StageB/README.md).
The current generation script produces Stage B; the notes below describe the old checkpoint.

Visual approval model for CarWreck, based on the supplied compact-hatchback reference board.
**Stage B requires the user's approval.** This asset has not been integrated or tested in
the procedural destruction system. No FBX or Unity assets are produced at this checkpoint.

## Review images

- `front34.png`: front three-quarter.
- `side.png`: long-lens side profile.
- `rear34.png`: rear three-quarter.
- `comparison.png`: the original muscle car and new hatchback at the same scale,
  under the same lighting. The original source is appended for this render only.

All four are actual Blender Cycles renders at 1400 × 900, 48 samples with denoising.

## Source and reproduction

- Script: `Blender/Scripts/build_compact_hatchback.py`
- Editable scene: `Blender/CompactHatchback.blend`
- Measurements and validation: `stage_a_inspection.json`

From the isolated project root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python Blender/Scripts/build_compact_hatchback.py -- . --render
```

The comparison requires the existing `Blender/MuscleCar.blend` and
`Assets/DamageLab/Art/Textures/CarPaint_Stripes.png` to be downloaded through Git LFS.
The generator rewrites only the new hatchback scene, images and inspection report.

## Modeling approach

The hatchback is built from new dimensions and profiles, rather than a rescaled muscle car.
Its short wheelbase, upright hatch, long three-door side glazing, squared roof and compact
overhangs distinguish it from the muscle car. A moderate quad loft folds into wheel wells;
the arches are actual openings with inner wells, with red lips following the same profile.
Black bumpers, rectangular lamps, five-spoke wheels and twin cream-white stripes carry
the sporty miniature style. There are no manufacturer marks.

The body loft has approximately 5,600 vertices; ordinary station spacing is about 5.8 cm,
with extra arch stations. Rigid accessories use modest geometry and bevel modifiers.
Four `Wheel_FL/FR/RL/RR` roots have their origins at the wheel centres, with spokes and
sidewall detail parented under the appropriate wheel.

Units are metres, +X right, +Y forward and +Z up, matching the existing authoring convention.
Overall length is approximately 4.04 m including bumpers/exhaust, height 1.42 m,
and wheelbase 2.32 m. Main body width at the flares is approximately 1.72 m;
overall width including mirrors is 1.89 m. The muscle-car source body is 4.84 m long.

`UVMap` uses non-collapsed dominant-face projections for this blockout. `StripeProjection`
is a separate car-space XY mapping consumed by the paint shader, so stripes stay aligned
across panels and do not appear on mirrors or sills. Normals are recalculated; panel edges
use small bevels and weighted normals where appropriate.

## Production concerns for Stage B

- Refine the coarse loft into approximately 3–4 cm spacing where deformation requires it.
- Redistribute sidewall rows around arches: Stage A spacing is uneven there.
- Replace large end-cap polygons with an intentional quad layout.
- Refine/join the cabin and body transition and choose production panel separation.
- Convert curve details and bevels only as needed for the eventual deformation setup.
- Pack a production UV atlas if texture painting requires it. The current UVs overlap.
- Translate the procedural stripe shader into the established Unity URP material/texture
  workflow; Blender shader nodes do not transfer automatically through FBX.
- Export and verify scale, orientation, readable mesh import and new material assignments.
- Validate actual localized/repeated damage separately; export success would not prove it.

## Project evidence and isolation

Inspected baseline: `90fca71ce719356a2ec0c7c3f7f33595b94fef5a`, available `origin/main` on
2026-10-13. Work branch: `feature/compact-hatchback`, in a managed isolated worktree.

Relevant repository facts: Unity 6000.6.0f1, URP 17.6.0, Input System 1.20.0 and Test Framework
1.8.0 (`ProjectSettings/ProjectVersion.txt`, `Packages/manifest.json`). Existing scene builders,
deformation and collision validation are under `Assets/DamageLab/Editor`; model and damage
code are under `Assets/DamageLab/Scripts`. The existing muscle importer applies only to
`Assets/DamageLab/Art/Models`, so a future new-folder FBX needs its own import settings.
No connected Unity MCP was available; no Unity scene, material, package, physics or damage
profile was modified. The live Blender connector was unavailable; Blender 5.2 CLI produced
and rendered the model. Existing `.blend`/image LFS rules apply to all delivered binaries.

References inspected: the muscle-car generation script and Blender source, existing FBX
and texture paths, `MuscleCarModelPostprocessor.cs`, Damage Lab README, project/package
settings, build-scene settings and `Screenshots/30_CollisionLab_Before.png`.
