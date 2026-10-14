"""Blender-side vehicle asset QA for CarWreck (Docs/VehicleProduction/vehicle-qa-checklist.md).

Run on a saved vehicle .blend with its manifest:
    blender -b Blender/<Vehicle>.blend --factory-startup --python Blender/Scripts/vehicle_asset_qa.py -- \
        --manifest Assets/Vehicles/<Vehicle>/vehicle.json --out Previews/<Vehicle>/asset_qa.json

Checks are split into MANDATORY (fail the asset) and RECOMMENDED (warnings with measurements). This is asset QA
only: it does not import into Unity and does not run destruction tests. Exit code 0 = no mandatory failure.
"""
import argparse
import json
import math
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

# Engine facts these thresholds come from (Assets/DamageLab, vehicle-asset-spec.md):
FOLD_WAVELENGTH = 0.14        # m, accordion folds of the box crush
EDGE_SOFTNESS = 0.03          # m, crease softening band
BODY_SPACING_MAX = 0.06       # m, p90 edge length on Body (muscle 3-4 cm, hatchback ~3.5 cm)
LONG_EDGE = 0.10              # m, edges longer than this cannot carry a fold (wavelength 0.14 m)
SLIVER_ASPECT = 12.0          # longest edge / altitude
THIN_SHELL = 0.006            # m, opposite surface closer than this behind a vertex
DEFORM_VERTS_MAX, DEFORM_VERTS_RECOMMENDED = 120000, 90000
DEFORM_PARTS_RECOMMENDED = 80
CABIN_LINE_TOLERANCE = 0.12   # m, same as the Unity warning


def token_match(pattern, name, mode):
    p, n = pattern.lower(), name.lower()
    if mode == 'Exact':
        return n == p
    if mode == 'Contains':
        return p in n
    return n.startswith(p) and (len(n) == len(p) or not name[len(p)].isalpha())


def classify(rules, name):
    hits = [r for r in rules if token_match(r['pattern'], name, r.get('match', 'Token'))]
    return (hits[0] if hits else None), len(hits)


def world_mesh(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.transform(obj.matrix_world)
    bm.normal_update()
    return bm


def part_metrics(obj):
    bm = world_mesh(obj)
    edges = sorted(e.calc_length() for e in bm.edges) or [0.0]
    m = {
        'vertices': len(bm.verts), 'faces': len(bm.faces),
        'nonmanifold_edges': sum(1 for e in bm.edges if not e.is_manifold and not e.is_boundary),
        'zero_area_faces': sum(1 for f in bm.faces if f.calc_area() < 1e-12),
        'ngons': sum(1 for f in bm.faces if len(f.verts) > 4),
        'edge_p50_m': edges[len(edges) // 2], 'edge_p90_m': edges[int(len(edges) * .9)], 'edge_max_m': edges[-1],
        'long_edges': sum(1 for e in edges if e > LONG_EDGE),
    }
    # Sliver triangles: long and thin (fold unnaturally when a crease crosses them).
    tri = bm.copy()
    bmesh.ops.triangulate(tri, faces=tri.faces[:])
    slivers = 0
    for f in tri.faces:
        a, b, c = (v.co for v in f.verts)
        ls = [(b - a).length, (c - b).length, (a - c).length]
        L = max(ls)
        area = f.calc_area()
        if L > 0.05 and area > 0 and L * L / (2 * area) > SLIVER_ASPECT:
            slivers += 1
    m['triangles'] = len(tri.faces)
    m['sliver_triangles'] = slivers
    tri.free()
    # Thin double skin: the surface behind a vertex (along its inward normal) closer than THIN_SHELL.
    tree = BVHTree.FromBMesh(bm)
    verts = bm.verts[:]
    step = max(1, len(verts) // 1500)
    thin = sampled = 0
    for v in verts[::step]:
        n = v.normal
        if n.length < 0.5:
            continue
        sampled += 1
        hit, _, _, dist = tree.ray_cast(v.co - n * 1e-4, -n, THIN_SHELL)
        if hit is not None:
            thin += 1
    m['thin_shell_fraction'] = thin / sampled if sampled else 0.0
    xs = [v.co for v in verts]
    lo = Vector((min(p.x for p in xs), min(p.y for p in xs), min(p.z for p in xs)))
    hi = Vector((max(p.x for p in xs), max(p.y for p in xs), max(p.z for p in xs)))
    m['size_m'] = [hi.x - lo.x, hi.y - lo.y, hi.z - lo.z]
    m['bounds'] = [list(lo), list(hi)]
    m['has_uvmap'] = 'UVMap' in obj.data.uv_layers or len(obj.data.uv_layers) > 0
    m['has_stripe_projection'] = 'StripeProjection' in obj.data.uv_layers
    bm.free()
    return m


def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    ap = argparse.ArgumentParser()
    ap.add_argument('--manifest', required=True)
    ap.add_argument('--out')
    args = ap.parse_args(argv)
    manifest = json.load(open(args.manifest, encoding='utf-8'))
    rules = manifest['components']
    mandatory, recommended, info = [], [], {}

    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and not o.hide_render and o.name not in ('StudioFloor',)
              and not o.name.startswith(('Studio', 'Backdrop', 'Floor', 'Camera', 'Light'))]
    # ------------------------------------------------------------- classification
    parts, unclassified, ambiguous = [], [], []
    for o in meshes:
        rule, n = classify(rules, o.name)
        if rule is None:
            unclassified.append(o.name)
            continue
        if n > 1:
            ambiguous.append(f"{o.name} ({n} rules)")
        if rule.get('mode', 'Deform') == 'Ignore':
            continue
        parts.append((o, rule))
    if unclassified:
        mandatory.append(f"unclassified components ({len(unclassified)}): {', '.join(sorted(unclassified)[:15])}")
    if ambiguous:
        mandatory.append(f"ambiguous components: {', '.join(ambiguous[:15])}")
    for r in rules:
        if r.get('match') == 'Contains':
            mandatory.append(f"rule '{r['pattern']}' uses legacy Contains matching")
        if not any(token_match(r['pattern'], o.name, r.get('match', 'Token')) for o in meshes):
            recommended.append(f"rule '{r['pattern']}' matches no mesh (stale rule)")
    roles = {r['role'] for _, r in parts}
    for role in ('Body', 'Cabin', 'Wheel'):
        if role not in roles:
            mandatory.append(f"no component with role {role}")

    # ------------------------------------------------------------- orientation, scale, wheels
    wheels = {o.name.split('.')[0]: o for o, r in parts if r['role'] == 'Wheel' and o.name.split('.')[0] in ('Wheel_FL', 'Wheel_FR', 'Wheel_RL', 'Wheel_RR')}
    if len(wheels) != 4:
        mandatory.append(f"expected four wheel roots Wheel_FL/FR/RL/RR, found {sorted(wheels)}")
    else:
        fl, fr, rl = wheels['Wheel_FL'].matrix_world.translation, wheels['Wheel_FR'].matrix_world.translation, wheels['Wheel_RL'].matrix_world.translation
        if not fl.y > rl.y:
            mandatory.append("front wheels are not at +Y (forward must be +Y in Blender)")
        if not fl.x < 0 < fr.x:
            mandatory.append("left wheels are not at -X")
        info['wheelbase_m'] = fl.y - rl.y
        info['track_m'] = fr.x - fl.x
        for name, w in wheels.items():
            bb = [w.matrix_world @ Vector(c) for c in w.bound_box]
            centre = sum(bb, Vector()) / 8
            if (centre - w.matrix_world.translation).length > 0.03:
                mandatory.append(f"{name} origin is {(centre - w.matrix_world.translation).length * 100:.1f} cm from its geometric centre (must be the wheel centre)")
    if bpy.context.scene.unit_settings.system not in ('METRIC', 'NONE') or abs(bpy.context.scene.unit_settings.scale_length - 1.0) > 1e-6:
        mandatory.append("scene units must be metric with unit scale 1.0")

    # ------------------------------------------------------------- per-part geometry
    metrics, deform_verts, deform_parts = {}, 0, 0
    body_tops, cabin_tops, lo_all, hi_all = [], [], [], []
    for o, r in parts:
        m = part_metrics(o)
        metrics[o.name] = dict(m, role=r['role'], crumple=r.get('crumple', 1.0), mode=r.get('mode', 'Deform'))
        lo_all.append(Vector(m['bounds'][0])); hi_all.append(Vector(m['bounds'][1]))
        if r['role'] in ('Body', 'Bumper'):
            body_tops.append(m['bounds'][1][2])
        if r['role'] == 'Cabin':
            cabin_tops.append(m['bounds'][1][2])
        if r.get('mode', 'Deform') != 'Deform':
            continue
        deform_verts += m['vertices']; deform_parts += 1
        if m['zero_area_faces']:
            mandatory.append(f"{o.name}: {m['zero_area_faces']} zero-area faces")
        if not m['has_uvmap']:
            mandatory.append(f"{o.name}: no UV layer")
        if m['nonmanifold_edges']:
            recommended.append(f"{o.name}: {m['nonmanifold_edges']} non-manifold edges")
        # Known hatchback failure modes (recommended: measured, reviewed visually and in Unity)
        if m['thin_shell_fraction'] > 0.25 and r['role'] not in ('Wheel',):
            recommended.append(f"{o.name}: thin double skin, {m['thin_shell_fraction'] * 100:.0f}% of sampled vertices have the opposite surface within {THIN_SHELL * 1000:.0f} mm behind them (unstable under the crush)")
        if m['sliver_triangles'] > max(4, 0.03 * m['triangles']):
            recommended.append(f"{o.name}: {m['sliver_triangles']} long sliver triangles of {m['triangles']} (aspect > {SLIVER_ASPECT:.0f}, longer than 5 cm)")
        if r['role'] in ('Panel', 'Fascia', 'Trim', 'Light') and m['edge_p90_m'] > LONG_EDGE:
            recommended.append(f"{o.name}: sparse geometry, p90 edge {m['edge_p90_m'] * 100:.1f} cm (> {LONG_EDGE * 100:.0f} cm cannot carry a {FOLD_WAVELENGTH * 100:.0f} cm fold)")
        if r['role'] == 'Body' and m['edge_p90_m'] > BODY_SPACING_MAX:
            recommended.append(f"{o.name}: body grid p90 edge {m['edge_p90_m'] * 100:.1f} cm (> {BODY_SPACING_MAX * 100:.0f} cm)")
        if max(m['size_m']) < 0.35 and m['triangles'] < 400 and r.get('crumple', 1.0) >= 0.45:
            recommended.append(f"{o.name}: small part ({max(m['size_m']) * 100:.0f} cm) with only {m['triangles']} triangles deforms with crumple {r.get('crumple', 1.0)}: consider a denser mesh, a lower crumple, or Rigid")
    info['deformable_vertices'] = deform_verts
    info['deformable_parts'] = deform_parts
    if deform_verts > DEFORM_VERTS_MAX:
        mandatory.append(f"{deform_verts} deformable vertices exceed the {DEFORM_VERTS_MAX} limit")
    elif deform_verts > DEFORM_VERTS_RECOMMENDED:
        recommended.append(f"{deform_verts} deformable vertices (recommended <= {DEFORM_VERTS_RECOMMENDED}; deformation cost scales with them)")
    if deform_parts > DEFORM_PARTS_RECOMMENDED:
        recommended.append(f"{deform_parts} deformable parts (recommended <= {DEFORM_PARTS_RECOMMENDED})")
    if lo_all:
        lo = Vector((min(p.x for p in lo_all), min(p.y for p in lo_all), min(p.z for p in lo_all)))
        hi = Vector((max(p.x for p in hi_all), max(p.y for p in hi_all), max(p.z for p in hi_all)))
        info['dimensions_xyz_m'] = list(hi - lo)
        if not (2.5 < hi.y - lo.y < 7.0 and 1.2 < hi.x - lo.x < 2.8 and 0.8 < hi.z - lo.z < 3.0):
            mandatory.append(f"overall size {list(hi - lo)} m is not a car-sized metre-scale model")
    if body_tops and cabin_tops:
        belt = max(body_tops)
        info['beltline_m'] = belt
        info['cabin_top_m'] = max(cabin_tops)
        cl = manifest.get('cabinLine', {}).get('start', 0) or 0
        if cl and abs(cl - belt) > CABIN_LINE_TOLERANCE:
            mandatory.append(f"cabinLine.start {cl:.2f} m is {abs(cl - belt):.2f} m from the measured beltline {belt:.2f} m")
    # Paint: stripes must come from a projection that deforms with the mesh (UV data, never world-space shading).
    painted = [o for o, r in parts if r['role'] in ('Body', 'Panel', 'Cabin') and r.get('mode', 'Deform') == 'Deform']
    if manifest.get('paint', {}).get('stripeMask'):
        missing = [o.name for o in painted if not metrics[o.name]['has_stripe_projection']]
        if missing:
            mandatory.append(f"stripe paint needs a StripeProjection UV layer on painted parts; missing on {', '.join(missing[:10])}")

    result = {'manifest': args.manifest, 'blend': bpy.data.filepath, 'mandatory_failures': mandatory,
              'recommended_warnings': recommended, 'info': info, 'parts': metrics,
              'scope': 'Blender asset QA only: no Unity import, no destruction test'}
    text = json.dumps(result, indent=2)
    if args.out:
        open(args.out, 'w', encoding='utf-8').write(text + '\n')
    print('VEHICLE_QA', json.dumps({'mandatory': mandatory, 'recommended': recommended, 'info': info}))
    print('VEHICLE_QA_RESULT', 'PASS' if not mandatory else 'FAIL')
    sys.exit(0 if not mandatory else 1)


main()
