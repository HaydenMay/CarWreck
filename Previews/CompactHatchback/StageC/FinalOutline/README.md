# Final Stage C — remove the inward nose pocket

This is the final small silhouette correction to the approved asset at
`81adc9bf6da9aaac8dd180c37250b281719d20d2`. No additional cosmetic changes or
destruction-system integration are included.

The existing arch roll fell back toward the nose too quickly, making the plan
outline concave just ahead of the wheel. The generator now fills only that
short inward pocket with the smallest straight support from the arch shoulder
to the existing nose endpoint. The largest local addition is **3.78 cm per side**.

Only X coordinates of the existing hood/shoulder/side/well rows change, with
the adjustment fading into the inner well. Base loft Z values remain unchanged;
bevel recalculation introduces surface-edge variations below 0.2 mm. The two
front rubbing strips follow the revised surface through their existing fitting
step. The grille, headlights, lower fascia, bumper and splitter are unchanged.
Maximum body width, nose endpoint, arch widths and overall vehicle dimensions
are preserved. There is no whole-car widening or added hood crown.

## Required identical-condition comparisons

| View | Before versus final Stage C |
|---|---|
| True orthographic top-down hood/nose | [comparison_nose_plan.png](comparison_nose_plan.png) |
| Front three-quarter | [comparison_front34.png](comparison_front34.png) |
| Straight-on front | [comparison_front.png](comparison_front.png) |

[All three views on one sheet](final_outline_overview.png).

The before source is the actual saved `81adc9b` Blender file. Both columns use
the same cameras, lights, paint colours, material parameters, AgX settings and
48-sample denoised Cycles render at 1400 × 900. The top camera looks exactly down
world Z in orthographic mode; it has no pitch or perspective. Only labels are
added to the full-resolution pair boards. Source hashes and camera matrices
are recorded in `comparison_manifest.json`.

## Verification

- Body remains **30,586 vertices / 31,814 faces**, with the same loft grid and
  92.27% quad fraction. No new topology or density is added.
- All 23 deformation-tagged meshes have zero boundary, nonmanifold and zero-area
  elements. Median body edge remains 3.31 cm; 90th percentile is 3.65 cm.
- Actual projected mesh sections reduce the maximum inward deficit from the
  straight support envelope from **38.28 mm to 2.47 mm** (93.5% reduction).
  Remaining millimetre-scale variation comes from the retained grid and bevels;
  the validation tolerance is 3 mm, plus a required reduction greater than 90%.
- Arch widths and sampled hood centre heights are unchanged. Independent review
  found only the body and its two front rubbing-strip meshes changed.
- 69 fixed mesh coordinate/transform hashes match the before source, including
  the slim grille, headlight/fog fixtures, bumpers and splitter. Grille and lamp
  surfaces still clear the shell; body/tire checks pass.
- All 76 exported FBX mesh vertex counts match source. Stripe UV1 still follows
  the same car-space projection. The Unity import remains readable, metre-scale,
  correctly oriented and fully remapped to the same 11 vehicle materials.
- Actual Unity/URP renders verify independent primary/stripe colours and additional
  lighting. Shader checks pass; imported count is 97,188 after seam splitting.

Detailed results are in the parent folder's `asset_inspection.json`,
`geometry_validation.json` and `unity_import_report.json`. Earlier comparison
folders are historical evidence at their recorded commits.

Crash deformation has not been tested in this task. The next work is destruction
testing. No further cosmetic improvements are included.

## Reproduce the final comparisons

From the isolated project root, after regenerating the current source:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/render_hatchback_stage_comparisons.py -- . --baseline 'Previews/CompactHatchback/StageC/FinalOutline/.baseline/Before.blend' --baseline-commit 81adc9bf6da9aaac8dd180c37250b281719d20d2 --output 'Previews/CompactHatchback/StageC/FinalOutline' --views nose_plan front34 front --before-label 'Before final correction' --after-label 'Final Stage C' --samples 48
python Blender/Scripts/compose_hatchback_comparisons.py . --output 'Previews/CompactHatchback/StageC/FinalOutline'
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/verify_hatchback_stage_c.py -- .
```

The comparison renderer restores missing before-source Git LFS bytes into the
ignored `.baseline` folder, without changing branches or saving either source.
