"""Stage A spatial checks only; explicitly NOT production asset QA or Unity testing.

blender -b Blender/CompactPickup_StageA.blend --python-exit-code 1 --python <script> -- <root>
"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector


def main():
    argv=sys.argv[sys.argv.index('--')+1:]
    root=Path(argv[0]).resolve(); out=root/'Previews/CompactPickup/StageA'
    spec=importlib.util.spec_from_file_location('pickup',root/'Blender/Scripts/build_compact_pickup.py')
    pickup=importlib.util.module_from_spec(spec); spec.loader.exec_module(pickup)
    pickup.PARTS=[ob for ob in bpy.data.objects if ob.type=='MESH' and 'engine_role_proposal' in ob]
    g=pickup.inspection()
    # Actual saved geometry measurements supersede parameter targets.
    front=bpy.data.objects['FrontFender_L']
    arch_points=[(front.matrix_world@v.co).z for v in front.data.vertices if abs((front.matrix_world@v.co).y-1.64)<.035]
    g['front_arch_bottom_at_axle_m']=min(arch_points)
    g['front_tire_to_arch_gap_m']=min(arch_points)-g['wheel_bounds']['Wheel_FL']['max'][2]
    (out/'geometry_measurements.json').write_text(json.dumps(g,indent=2)+'\n')
    camera=bpy.context.scene.camera; cameras={}
    for name,(pos,target,scale) in pickup.VIEWS.items():
        camera.location=pos; pickup.aim(camera,target); camera.data.type='ORTHO'; camera.data.ortho_scale=scale
        if name in ('front34','rear34'):
            camera.data.type='PERSP'; camera.data.lens=70
        if name=='top': camera.rotation_euler=(0,0,-math.pi/2)
        bpy.context.view_layer.update()
        pts=[world_to_camera_view(bpy.context.scene,camera,ob.matrix_world@v.co)
             for ob in pickup.PARTS for v in ob.data.vertices]
        cameras[name]={'position':pos,'target':target,'projection':camera.data.type,'scale':scale,'lens_mm':camera.data.lens,
                       'vehicle_pixel_bounds':[min(p.x for p in pts)*1440,(1-max(p.y for p in pts))*960,
                                               max(p.x for p in pts)*1440,(1-min(p.y for p in pts))*960]}
    (out/'cameras.json').write_text(json.dumps(cameras,indent=2)+'\n')
    # Raycasts deliberately exercise the bed cavity and the physical cab/bed gap.
    scene=bpy.context.scene; deps=bpy.context.evaluated_depsgraph_get()
    def cast(x,y):
        ok,loc,normal,index,ob,matrix=scene.ray_cast(deps,Vector((x,y,4)),Vector((0,0,-1)))
        return {'hit':ok,'object':ob.name if ok else None,'height_m':loc.z if ok else None}
    bed_center=cast(0,-1.28); cab_gap=cast(0,-.294)
    hood=bpy.data.objects['Body_Hood']
    by_y={}
    for v in hood.data.vertices:
        p=hood.matrix_world@v.co; by_y.setdefault(round(p.y,5),[]).append(p.x)
    widths=[max(xs)-min(xs) for y,xs in sorted(by_y.items())]
    monotone=all(b<=a+1e-5 for a,b in zip(widths,widths[1:]))
    checks={
      'four_unique_wheel_meshes':len(g['wheel_centers'])==4,
      'wheel_origins_within_3cm':all((ob.location-Vector(g['wheel_centers'][ob.name])).length<=.03
                                  for ob in pickup.PARTS if ob.name in g['wheel_centers']),
      'finite_vertex_coordinates':all(math.isfinite(c) for ob in pickup.PARTS for v in ob.data.vertices for c in v.co),
      'explicit_proposed_roles_for_all_meshes':all(ob['engine_role_proposal'] in ('Body','Cabin','Wheel','Panel','Bumper','Fascia','Light','Trim','Underbody') for ob in pickup.PARTS),
      'uv_layers_present':all('UVMap' in ob.data.uv_layers and 'StripeProjection' in ob.data.uv_layers for ob in pickup.PARTS),
      'open_bed_center_hits_liner_below_rail':bed_center['hit'] and bed_center['object'].startswith('BedLiner_') and bed_center['height_m']<1.0,
      'cab_bed_gap_open_above_chassis':cab_gap['hit'] and cab_gap['height_m']<.60,
      'hood_plan_width_monotone_no_hourglass':monotone,
      'tire_ground_error_under_5mm':abs(g['bounds']['min'][2])<.005,
      'front_tire_air_gap_over_10cm':g['front_tire_to_arch_gap_m']>.10,
      'grille_within_body_width':g['grille_bounds']['max'][0]<g['body_bounds']['max'][0],
      'all_nine_expected_renders_exist':all((out/f'stage_a_{v}.png').is_file() for v in pickup.VIEWS),
    }
    report={'stage':'A only','checks':checks,'passed':sum(checks.values()),'total':len(checks),
            'bed_center_ray':bed_center,'cab_gap_ray':cab_gap,'hood_width_range_m':[min(widths),max(widths)],
            'front_tire_air_gap_m':g['front_tire_to_arch_gap_m'],
            'source_blend_sha256':hashlib.sha256((root/'Blender/CompactPickup_StageA.blend').read_bytes()).hexdigest(),
            'production_asset_qa':'NOT RUN','unity_import':'NOT RUN','destruction_tests':'NOT RUN'}
    (out/'blockout_checks.json').write_text(json.dumps(report,indent=2)+'\n')
    print('STAGE_A_SPATIAL_CHECKS',json.dumps(report),flush=True)
    assert all(checks.values()),'Stage A check failed; inspect blockout_checks.json'


if __name__=='__main__': main()
