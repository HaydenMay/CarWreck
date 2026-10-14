# Stage C — straight nose and recessed fascia correction

Archived evidence at `81adc9b`. The current source also includes the small
[final outline correction](../FinalOutline/README.md); these comparisons retain
their original source hashes and images.

Corrects the initial Stage C at `c3010572702a251b993c8d9c681287ee176715ce`.
The prior generator widened the nose by 10 cm on each side and raised its
leading hood edge by 3 cm to fit the old grille/lamp dimensions. Those offsets
are removed. The original descending hood profile and nose taper now determine
the available space; fascia parts are sized to that body section.

## What changed

- Grille surround: 96 cm wide and 15.2 cm high, down from 112 × 21.4 cm.
  Depth is 2.1 cm, down from 4 cm. Its face is recessed 2.1 cm behind the nose.
- Headlight housings: 21.6 × 16.5 cm, with 2.2 cm depth rather than 4.1 cm.
  Lenses are 1.2 cm thick, about 2.5 cm behind the body face. The headlights,
  grille, trim and body remain separate components with intentional gaps.
- Smaller socket openings have the same continuous gridded backs, walls and
  3 mm chamfer. The grille bezel has 3 mm clearance above its lower socket wall.
  The body is not widened to provide that clearance.
- The lower carrier narrows from 1.67 m to 1.45 m, and its depth drops from
  7.9 cm to 3 cm. Smaller fog housings fit inside that carrier.
- The bumper and splitter retain their dimensions and forward projection.
  The Stage C arch sculpting, cabin, wheels, doors and rear details are retained.

## Matched visual evidence

These comparisons use the saved original Stage C source, not a reconstruction
made by toggling correction parameters. The left column is **Before correction**;
the right is **Corrected Stage C**. Both use identical cameras, 48-sample Cycles,
denoising, lights, paint, material parameters and colour management at 1400 × 900.

| View | Comparison |
|---|---|
| Front three-quarter | [comparison_front34.png](comparison_front34.png) |
| Full side / hood line | [comparison_side.png](comparison_side.png) |
| Full front | [comparison_front.png](comparison_front.png) |
| Near top-down nose | [comparison_nose_top.png](comparison_nose_top.png) |
| Front fascia close-up | [comparison_fascia.png](comparison_fascia.png) |

[Nose overview](nose_overview.png) combines the near top-down, front and three-quarter
views. `comparison_manifest.json` records the baseline commit, source hashes,
camera transforms and verified identical conditions. Internal `stage_b` manifest
keys refer to this before-correction baseline, as the column labels indicate.
The original seven Stage B/C comparisons in the parent folder are refreshed too.

## Measurements and validation

At y = 1.9249 m, just inside the front body face:

| Body measurement | Before | Corrected |
|---|---:|---:|
| Width at z = 0.64 m | 1.7220 m | 1.5210 m |
| Hood centre height | 0.8410 m | 0.8110 m |

Seven actual mesh ray samples from y = 1.70 to 1.9249 m confirm that the corrected
nose narrows continuously and the hood descends toward the front, without the
previous width swell or upturn. Both maximum arch widths are unchanged. Geometry
and transform hashes match the previous source for 39 unrelated components.

Final body: **30,586 vertices**, 3.31 cm median edge length, 92.27% quads.
All 23 deformation-tagged meshes have zero boundary, nonmanifold and zero-area
elements. Upper grille/headlight surfaces clear the shell; tire clearance,
stripe projection and all 76 FBX mesh counts pass. The actual Unity import has
97,188 vertices after normal/material/UV splitting; shader and paint render
checks pass. Detailed results are in the parent folder's inspection, geometry
validation and Unity reports.

Crash deformation remains untested. Small inherited mounting overlaps around
the lower carrier remain; this correction does not change the destruction system.

## Reproduce these comparisons

From the isolated project root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/render_hatchback_stage_comparisons.py -- . --baseline 'Previews/CompactHatchback/StageC/FrontCorrection/.baseline/Before.blend' --baseline-commit c3010572702a251b993c8d9c681287ee176715ce --output 'Previews/CompactHatchback/StageC/FrontCorrection' --views front34 side front nose_top fascia --before-label 'Before correction' --after-label 'Corrected Stage C' --samples 48
python Blender/Scripts/compose_hatchback_comparisons.py . --output 'Previews/CompactHatchback/StageC/FrontCorrection'
```

If missing, the renderer restores the before source as binary Git LFS content
from `c301057` into the ignored `.baseline` directory. It never changes Git branches
or saves either source. Run the parent-folder geometry verifier after restoring
this baseline to repeat the before/after mesh assertions.
