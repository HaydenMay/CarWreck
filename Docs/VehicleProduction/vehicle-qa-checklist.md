# CarWreck vehicle QA checklist

Use with [vehicle-asset-spec.md](vehicle-asset-spec.md). Every item is tagged:

| Tag | Meaning |
|---|---|
| **M** | Mandatory: the vehicle is not accepted until it passes. |
| **R** | Recommended: deviations allowed, explained in the report. |
| **AUTO-B** | Checked automatically in Blender by `Blender/Scripts/vehicle_asset_qa.py`. |
| **AUTO-U** | Checked automatically in Unity by a bridge command (batch mode). |
| **VISUAL** | Needs a human (or agent) looking at renders / screenshots. |
| **UNITY-ONLY** | Only the Unity import and the live destruction engine can show it. |

**Never report a destruction result from asset QA.** AUTO-B proves geometry and naming; it does not import into Unity
or crush anything. A report states which of AUTO-B, AUTO-U and the live tests were actually run.

How to run:

```text
blender -b Blender/<Vehicle>.blend --factory-startup --python Blender/Scripts/vehicle_asset_qa.py -- \
    --manifest Assets/Vehicles/<Vehicle>/vehicle.json --out Previews/<Vehicle>/asset_qa.json
Unity -batchmode ... -executeMethod DamageLab.EditorTools.DamageLabBridge.RunFromCommandLine -dlcmd "<commands>" -quit
```

Bridge commands take the vehicle's index in the Collision Lab list (0 muscle car, 1 hatchback, new vehicles after).

## 1. Identity, scale, orientation

| | Check | Tags |
|---|---|---|
| ☐ | Stable `id` (`carwreck.<name>`), `revision`, `displayName` in `vehicle.json` | M · AUTO-U (`vehicle-import`) |
| ☐ | Metric units, scale 1, car-sized overall dimensions | M · AUTO-B |
| ☐ | Forward +Y in Blender: front wheels at +Y, left wheels at −X | M · AUTO-B |
| ☐ | Imports in Unity facing +Z with left at −X, metre bounds | M · AUTO-U (`vehicle <i>` geometry line) · UNITY-ONLY |
| ☐ | Four wheel roots `Wheel_FL/FR/RL/RR`, origin at wheel centre (≤ 3 cm) | M · AUTO-B |

## 2. Classification and names

| | Check | Tags |
|---|---|---|
| ☐ | Every mesh matches exactly one rule (0 unclassified) | M · AUTO-B · AUTO-U (`vehicle-import`) |
| ☐ | 0 ambiguous names; no `Contains` rules | M · AUTO-B · AUTO-U |
| ☐ | `Body`, `Cabin`, `Wheel` roles present; Underbody not matched by Body | M · AUTO-B |
| ☐ | No stale rules (rules that match nothing) | R · AUTO-B |
| ☐ | 0 Unity validation warnings (vehicle panel / `vehicle <i>` "validation warnings: 0") | M · AUTO-U |
| ☐ | ≤ ~80 deformable components | R · AUTO-B |

## 3. Cabin protection and shell

| | Check | Tags |
|---|---|---|
| ☐ | Cabin line measured (manifest 0) or within 12 cm of the measured beltline | M · AUTO-B · AUTO-U |
| ☐ | Shell automatic unless justified; nose/tail/side depths and impact height reported | R · AUTO-U (`vehicle-import`) |
| ☐ | Cabin collider covers the greenhouse; hitbox wireframe matches the body | R · VISUAL (Collision Lab, "Show Physics Colliders") |

## 4. Deformation-ready geometry (known failure modes)

| | Check | Tags |
|---|---|---|
| ☐ | No zero-area faces; every deformable part has a UV layer | M · AUTO-B |
| ☐ | Deformable vertices ≤ 120k (R: ≤ 90k) | M · AUTO-B |
| ☐ | Body grid p90 edge ≤ 6 cm | R · AUTO-B |
| ☐ | No thin double skin (> 25 % of vertices with the opposite face within 6 mm) | R · AUTO-B |
| ☐ | No long sliver triangles (aspect > 12, > 5 cm; > 3 % of a part) | R · AUTO-B |
| ☐ | Trim / fascia / light p90 edge ≤ 10 cm | R · AUTO-B |
| ☐ | Small parts (< 35 cm) not built from a few hundred large triangles with high crumple | R · AUTO-B |
| ☐ | Non-manifold edges explained | R · AUTO-B |
| ☐ | **Part fold outliers: none** — no part folds > 5 % of its triangles in one 15 m/s wide hit; Body folds ≤ 20 triangles under each click dent | M for new vehicles · AUTO-U (`vehicle <i>` "PART FOLD OUTLIERS") · UNITY-ONLY |

Calibration on the existing cars (measured): AUTO-B flags the hatchback's `DoorSeam` (thin skin, slivers, sparse),
`MirrorGlass`/`SideIndicator` and the 188-triangle lamp / slat primitives (slivers, small coarse parts) and
`FrontValance` (slivers), and only 2 recommendations on the muscle car (`Bumper_Rear` slivers, `Grille` thin skin — a
false positive: that grille folds only 1–2 % in Unity, which is why these are recommendations to review, not failures). It does **not** detect `LicenseRecess` or the
Body folding under click dents; only the Unity fold-outlier check finds those. That is why the Unity check is mandatory.
The hatchback itself has documented outliers (asset spec §4) and is accepted as an existing exception.

## 5. Materials and paint

| | Check | Tags |
|---|---|---|
| ☐ | `palette.json` lists every material; names stable | M · AUTO-U (`vehicle-import`) |
| ☐ | Painted parts carry a `StripeProjection` UV layer (two-colour paint) | M · AUTO-B |
| ☐ | Stripe mask linear, uncompressed, clamped | M · AUTO-U (`vehicle-import`) |
| ☐ | Paint / stripe UVs unchanged by deformation | M · AUTO-U (`vehicle <i>` "stripe/paint UVs unchanged") |
| ☐ | Stripes stay on the dented metal; paint renders in editor **and WebGL** | M · VISUAL · UNITY-ONLY |
| ☐ | Lamps emissive; no manufacturer marks | R · VISUAL |

## 6. Export and import

| | Check | Tags |
|---|---|---|
| ☐ | Generator script reproduces `.blend`, FBX, textures, palette, manifest, previews | M · VISUAL (re-run) |
| ☐ | FBX export settings as in the spec §6 | M · review of the generator |
| ☐ | Import settings applied: readable, uncompressed, order kept, normals imported, axis baked | M · AUTO-U (`vehicle-import`) |
| ☐ | `vehicle-import <manifest>` RESULT: PASS | M · AUTO-U |

## 7. Destruction validation (live engine)

All **M · AUTO-U · UNITY-ONLY**. Record the numbers, not only PASS.

| | Command | Must show |
|---|---|---|
| ☐ | `collision-build` | the vehicle in the selector |
| ☐ | `vehicle <i>` | every hit applied from all sides; repeated hits land deeper; saturation; Damage Lab sliders and click dents; Reset All exact; rebuild optimisation exact; 0 warnings; fold outliers reviewed |
| ☐ | `collision-phase4 <i>` | coverage of all 16 targets (second hit deeper), locality, 84-hit long run without NaN, repair exact |
| ☐ | `collision-overflow <i>` | 33 heavy hits past the 24-record limit all applied; undamaged regions still deform; replay exact |
| ☐ | `collision-random <i>` | seeded 50-shot Chaos Run: 0 overlaps, spread over the car, saturation, least-damaged regions deform, replay exact, 0.1 mm perturbation bounded, STOP / repair / switching |
| ☐ | `vehicle-switch` | no damage, colliders or reports carried between vehicles |
| ☐ | WebGL build (`collision-webgl`) | the vehicle loads, hits from several directions, Chaos Run, repair, paint; no console errors |

Physics re-runs (same launches twice) are informational: PhysX is not bit-repeatable, and symmetric hits can tip either
way (a ±1.1° yaw bifurcation on a square rear hit is documented). Replays of recorded impacts must be exact.

## 8. Visual review

| | Check | Tags |
|---|---|---|
| ☐ | Every reference image catalogued (view, perspective, features shown); side/top views used for proportions ahead of ¾ views | M · VISUAL |
| ☐ | Design-critical features missing from all references asked about, not invented; assumptions listed | M · VISUAL |
| ☐ | Matched-angle renders (orthographic side/front/rear/top for near-orthographic references; matched perspective for ¾) and a side-by-side or overlay comparison sheet at each checkpoint | M · VISUAL |
| ☐ | Proportion table at each checkpoint: reference vs model ratios (wheelbase, overhangs, height, wheel size and centre height, beltline, hood, cabin/roof length, glass rake, track/width, feature positions), model values measured from the geometry; > 5 % (wheelbase / wheel placement > 3 %) flagged | M · VISUAL (model values from the generator) |
| ☐ | Feature checklist at each checkpoint: silhouette, proportions, hood and cabin, wheelbase and wheels, front design, rear design, fenders and body curvature, distinctive features | M · VISUAL |
| ☐ | Discrepancies fixed or listed **before** asking for approval; no "perfect match" claim without a measurement or side-by-side image; estimates marked | M · VISUAL |
| ☐ | At each checkpoint, renders/comparisons/report committed and pushed to the vehicle feature branch; hosted gallery/image links verified and supplied. After approval, temporary images cleaned when no longer needed; approved comparison baselines and final evidence preserved | M · review |
| ☐ | **Checkpoint A** (blockout: front ¾, side, rear ¾ + comparison) approved by the user before detailed modelling | M · VISUAL |
| ☐ | **Checkpoint B** (detailed model vs references **and** the approved blockout; drift > 2 % explained) approved before configuration and Unity work | M · VISUAL |
| ☐ | **Checkpoint C** (final renders, comparison, QA results, Unity destruction screenshots when available, known limitations) approved before any merge | M · VISUAL |
| ☐ | Damaged screenshots from `vehicle <i>` (front, side, corner, 9 cumulative, sliders 100) look like crushed metal: no spikes, torn seams, floating trim | M · VISUAL |
| ☐ | Close-ups of any fold outliers | M · VISUAL |

## 9. Attachment sockets (only if provided)

| | Check | Tags |
|---|---|---|
| ☐ | Semantic IDs `(vehicle_id, socket_group, socket_id)`; unsupported groups omitted | M · review |
| ☐ | Seeds hit the named `mesh_role` component inside their bounds, normal within 55° of `expected_normal` | R · Blender-side only |
| ☐ | `runtime_ready: false`; no Blender indices used as runtime bindings | M · review |

Blender-side socket checks do **not** prove live Unity attachment, mount strength or detachment behaviour.
