# Compact Hatchback — Stage C

Refines the approved Stage B asset at `f870cf26e6f52d8d5b7f4b6716d1f05dda562a20`
on `feature/compact-hatchback`. The existing loft, arch station grid, cabin,
wheel geometry, pivots and boxy three-door silhouette are retained.

## Geometry changes

- A radial roll in the existing shell vertices gives the front fenders and rear
  quarters more mass. The flare continues through the shoulder into the same
  wheelwell mesh; there are no separate decorative arch strips.
- The maximum arch half-width increases by 5.98 cm at the front and 7.67 cm at
  the rear. The source body width grows from 1.697 m to 1.851 m; mirrors still
  define the unchanged overall width of 1.892 m. Axles and wheelbase are unchanged.
- The hood has longitudinal crown modulation and shallow pressed shoulders.
  Doors gain a restrained 1.4 cm convex shaping field, with a fuller rear quarter.
- The local nose shoulder wraps the outside lamp corners. Its raised leading
  edge provides actual painted metal over the grille and lamp recesses.
- Front and rear end-cap patches are fitted around component sockets. The
  main shell remains connected and closed, including socket backs and walls.
  Each socket has an explicit 3 mm chamfer rather than a microscopic global bevel.
- Grille, headlight, fog and taillight housings are open-centred, segmented
  bezels. The grille slats and lenses sit within the metal; they no longer form
  a block in front of it. Headlight lenses sit about 7 mm behind bezel fronts
  and 17 mm behind the painted nose face. Trim and lamp components remain separate.
- Both bumpers gain crown, lower chamfer, relief and curved end returns.
  The front bumper projects ahead of the metal and lamps. Its indicators follow
  the actual bumper surface. The splitter wraps its outer ends.
- Rear lamps and the plate sit in shell recesses. The spoiler blade is subtly
  pitched, its underside seated and two short mounts connect it to the hatch header.
  The existing exhaust location is retained: below the bumper, clear of tire/body.
- The underside material follows the inner-well loft band. Exterior fenders
  remain painted even where their new curvature faces downward.

## Comparisons

All pairs are actual geometry renders at 1400 × 900, Cycles 48 samples, denoising,
the same seed, AgX look, cameras, lights, paint colours and material parameters.
No exposure, finish or lighting improvements are used for Stage C.

The archived Stage B source is loaded from its original committed Blender file,
not approximated by disabling Stage C features. `comparison_manifest.json`
records source SHA256 hashes and checks matched conditions and camera transforms.
Each full-resolution board preserves the original pixels, adding only labels.
Overview sheets resize both columns equally.

| Requested view | Matched Stage B / C pair |
|---|---|
| Front three-quarter | [comparison_front34.png](comparison_front34.png) |
| Full side | [comparison_side.png](comparison_side.png) |
| Rear three-quarter | [comparison_rear34.png](comparison_rear34.png) |
| Front fender close-up | [comparison_front_fender.png](comparison_front_fender.png) |
| Rear fender close-up | [comparison_rear_fender.png](comparison_rear_fender.png) |
| Front fascia close-up | [comparison_fascia.png](comparison_fascia.png) |
| Refined body and cabin wireframe | [comparison_wireframe.png](comparison_wireframe.png) |

[Whole-car overview](comparison_overview.png) · [Detail overview](detail_overview.png).
Raw `stage_b_*.png` and `stage_c_*.png` files are included. Stage B archived previews
are retained. `unity_*.png` are separate import/paint tests, not comparison evidence.

## Topology measurements

| Measurement | Stage B | Stage C |
|---|---:|---:|
| Body vertices | 29,616 | **30,805** |
| Cabin vertices | 13,750 | 13,750 |
| Body median edge | 3.24 cm | 3.30 cm |
| Body 90th percentile edge | 3.59 cm | 3.67 cm |
| Longest body edge | 7.67 cm | 6.14 cm |
| Body quads | 99.53% | 92.12% |
| Body boundary / nonmanifold / zero-area counts | 0 / 0 / 0 | 0 / 0 / 0 |
| Deformation-tagged vertices, including accessories | 51,706 | 56,907 |
| All source vertices | 64,434 | 69,155 |
| Mesh objects | 73 | 76 |

The quad fraction changes because constrained triangles fit the front/rear cap
patches between sockets; the original hood/fender/door/well grid is retained.
Socket backs and walls are gridded. Every deformation-tagged component is checked
for manifold edges and zero-area faces, including the lower valance and bezels.
No new large body ngons are introduced. The unchanged cabin retains its one small,
hidden five-sided mounting face, triangulated by FBX.

Small bevel/well triangles remain from Stage B. Triangles with longest-edge to
altitude ratio over 20 account for about 0.065% of the measured outer-panel area
in C versus 0.073% in B. These counts include bevel detail; broad panels retain
centimetre-scale spacing. This is preparation for deformation, not proof of
crash behaviour.

Overall length is 4.050 m including bumper/exhaust, about 7 mm longer from bumper
surface shaping. Height remains 1.425 m and wheelbase 2.32 m. No tire rescaling,
axle movement, cabin rebuilding or proportion redesign was performed.

## Validation and limits

`asset_inspection.json` records saved-source topology, bounds and UV checks.
`geometry_validation.json` compares B/C arch dimensions and triangle distribution,
checks unchanged axle pivots, zero body/tire and body/fascia surface crossings,
and confirms all 76 FBX meshes match source vertex counts. Stripe UV1 still equals
the car-space X/Y projection (maximum numerical error below 0.000001).

`unity_import_report.json` records the actual Unity 6000.6.0f1 import and paint
render tests. Imported counts include vertices split by material/normal/UV seams.
The readable metre-scale import, normals, stripe UV1, 11 material remaps and
compiled URP paint shader are checked. Three colour combinations and an additional
point light verify independent body/stripe controls and Forward+ lighting.
Material values, paint shader and stripe mask are retained from Stage B.

**CarWreck destruction and repeated-impact tests were not run.** Separate fascia
components must participate in the eventual deformation integration along with
the shell; they are not made into one rigid facade. Thin bezels and intentional
mounting overlaps may still need tuning after actual crush tests. UV0 remains an
overlapping projection, not a packed texture atlas. Windows Editor / URP was
validated; player builds and other graphics APIs were not tested.

No shared deformation code, Damage Lab, Collision Lab, muscle car, physics or
driving assets are changed. No merge to main is included.

## Reproduce

From the isolated project root, using Blender 5.2:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/build_compact_hatchback.py -- . --samples 48
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/render_hatchback_stage_comparisons.py -- . --samples 48
python Blender/Scripts/compose_hatchback_comparisons.py .
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/verify_hatchback_stage_c.py -- .
& 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -batchmode -projectPath . -executeMethod CompactHatchback.EditorTools.CompactHatchbackAssetValidation.PrepareAndValidate -logFile 'Previews/CompactHatchback/StageC/unity_import.log'
```

The comparison renderer restores Stage B into ignored `.baseline/StageB.blend`
using `git cat-file --filters` as binary data if it is missing. Git LFS must have
access to the original blob. The Pillow composer uses regular Python, not Blender's
Python. `--stage stage_c` reuses a completed B manifest when rerendering only C.

The generator retains saved primary/stripe colours unless `--primary R G B`
or `--secondary R G B` is supplied. Comparison colours are fixed to the original
B source for both columns. Regeneration writes only vehicle-specific source,
FBX, palette/mask and previews; the export selection excludes studio geometry.
The Unity helper now writes Stage C reports, keeping Stage B evidence archived.
