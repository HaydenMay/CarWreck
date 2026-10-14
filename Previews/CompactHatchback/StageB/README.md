# Compact Hatchback — Stage B

Archived Stage B evidence from `f870cf2`. Current Blender source, FBX and generator
now contain [Stage C](../StageC/README.md); the commands below describe the original
Stage B workflow at that commit. Stage B preview images and measurements are retained.

Refinement of the approved Stage A design at `4cea7f9`, on the isolated
`feature/compact-hatchback` branch. The original proportions, four axle locations,
three-door identity, black trim, dark wheels and twin stripes are retained.

## What changed

- Smooth Hermite profiles replace straight interpolation through the approved body keys.
- Hood crown increases from 2.4 to 4.6 cm; rounded shoulders blend into the fenders.
- Flares blend over a broader area, with curved door and quarter-panel cross-sections
  and a small deliberate waist crease rather than flat slabs.
- Arch lips are integrated into the continuous body shell; the separate overlapping
  Stage A strips are removed. Additional stations limit long edges around arch ends.
- Body end caps use quad grids, replacing Stage A's large cap polygons.
- Cabin, glass and pillars have denser matching grids, rounded edges, gently bowed side
  surfaces and a 4 cm roof crown. Roof edges share vertices with the side grids.
- Bumpers, valances, sills and spoiler use rounded, segmented geometry suitable for bending.
- Door seams and rubbing strips conform to the new body surface. Thin lamp bevels are
  clamped to avoid microscopic degenerate faces.
- Each wheel combines its rounded spokes and sidewall into one rigid mesh at its original
  axle pivot. This preserves the visual geometry and fixes nested FBX transform baking.

## Files

Source: `Blender/CompactHatchback.blend` and `Blender/Scripts/build_compact_hatchback.py`.
Production asset: `Assets/Vehicles/CompactHatchback/Models/CompactHatchback.fbx`.
Materials, shader, linear stripe mask and importer metadata are all vehicle-specific.

Blender reproduction, from the isolated project root:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup --python-exit-code 1 --python Blender/Scripts/build_compact_hatchback.py -- . --render
```

Optional `--primary R G B` and `--secondary R G B` use sRGB values from 0 to 1.
Without those arguments the generator preserves the colors saved in the existing source.
Source saving uses a temporary sibling followed by replacement; the approved source is
not overwritten if geometry validation fails. Source and export contain the vehicle only;
studio geometry, reference car and preview-only color changes are excluded from the FBX.

## Paint customization

In Blender, `Hatch_Paint` has a node labeled **Primary (Color1) / Stripe (Color2)**.
Those colors can be edited directly without rebuilding geometry. All colored vehicle
pieces use this material; wheels, trim, glass and lamps retain separate materials.

In Unity, `Hatch_Paint.mat` uses **CarWreck/CompactHatchback/Two Color Paint**:

- `_PrimaryColor`: body paint.
- `_StripeColor`: racing stripes.
- `_StripeMask`: reusable grayscale mask sampled from UV1 (`StripeProjection`).
- `_Metallic` and `_Smoothness`: paint finish.

The mask contains no baked paint colors. Color changes require no new FBX or geometry.
The shader uses URP PBR lighting, Forward+ clustered lights, SSAO, shadow, depth and normal
passes. Other materials use standard URP Lit, with lamp emission carried over from Blender.
UV0 is a usable projected mapping; it intentionally overlaps and is not a packed decal atlas.
UV1's vertical-face projection is intentionally one-dimensional for the stripe mask.

## Topology and measurements

| Component | Source vertices | Median edge | 90th percentile edge |
|---|---:|---:|---:|
| Main body | 29,616 | 3.24 cm | 3.59 cm |
| Cabin / roof / glazing | 13,750 | 2.86 cm | 3.79 cm |
| Total deformable components, including bumpers and painted accessories | 51,706 | — | — |

Source has 64,434 total vertices and 73 mesh objects. Main longitudinal station spacing
is 3.46 cm; curved edges and arch ends are locally denser. Body is 99.53% quads, with no
ngons. Both body and cabin have zero boundary edges, nonmanifold edges and zero-area faces.
The cabin has one small five-sided face hidden on its underside mounting edge; FBX
triangulates it. Very narrow bevel/well triangles affect about 0.1% of the exposed body area,
not the broad deformable panels. Preserve this distinction when assessing spacing.

Length: 4.043 m including bumpers/exhaust. Height: 1.425 m. Width: 1.892 m including mirrors,
approximately 1.70 m across the main flares. Wheelbase: 2.32 m. Blender +Y is forward;
the Unity import was checked with front wheels on +Z and left wheels on -X.

## Validation

`asset_inspection.json` records source counts, spacing, bounds, UV and topology assertions.
`unity_import_report.json` records the actual Unity 6000.6.0f1 import and render result.

The vehicle-scoped editor command checks readable imported meshes, normals, UV1, scale,
wheelbase, orientation, complete material remaps, the linear mask and shader errors.
It renders three color combinations and toggles an additional point light through the
project's URP renderer. Pixel comparisons prove separate paint/stripe changes and
additional-light response. Shader compilation is synchronous during these checks.

Repeat in the isolated project:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' -batchmode -projectPath . -executeMethod CompactHatchback.EditorTools.CompactHatchbackAssetValidation.PrepareAndValidate -logFile 'Previews/CompactHatchback/StageB/unity_import.log'
```

Or use **Assets → Compact Hatchback → Prepare and Validate**. The command preserves
existing paint colors. It creates no scene or prefab and uses no shared damage code.
Only vehicle assets and their metadata are delivered; Unity's unrelated global-settings
reserialization is excluded. Logs are local diagnostics rather than committed artifacts.

**CarWreck procedural destruction, repeated impacts and collision tests were not run.**
The topology is prepared for those tests, but actual damage-system compatibility remains
to be validated during integration. No collider, physics, scene or damage-profile changes
are included. Platform validation is Windows Editor / URP; player builds and other graphics
APIs were not tested.

## Rendered views

All Blender views use Cycles, 48 samples with denoising, at 1400 × 900.

| View | Image |
|---|---|
| Front three-quarter | [front34.png](front34.png) |
| Rear three-quarter | [rear34.png](rear34.png) |
| Full side | [side.png](side.png) |
| Front | [front.png](front.png) |
| Rear | [rear.png](rear.png) |
| Top | [top.png](top.png) |
| Body and cabin wireframe | [wireframe.png](wireframe.png) |
| Same-scale muscle-car comparison | [comparison.png](comparison.png) |
| Blue body / gold stripes | [color_blue.png](color_blue.png) |
| Ivory body / charcoal stripes | [color_ivory.png](color_ivory.png) |
| Stage A versus Stage B | [stage_a_vs_b.png](stage_a_vs_b.png) |
| View overview | [overview.png](overview.png) |

Additional actual Unity renders: `unity_red_white.png`, `unity_blue_white.png`,
`unity_blue_gold.png` and `unity_point_light.png`. Stage A previews remain intact in
the parent folder. Original muscle-car source, script, FBX, materials and all shared
damage/collision assets remain unchanged by this branch.
