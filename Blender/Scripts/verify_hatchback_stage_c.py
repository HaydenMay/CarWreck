"""Measure saved B/C shell geometry, component fit, stripe projection and FBX parity.

Run in background Blender with --python-exit-code 1 -- <project root>.
Does not save either opened Blender source or modify the export.
"""
import json
import hashlib
import math
from pathlib import Path
import sys

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

def tree(obj):
    return BVHTree.FromPolygons([obj.matrix_world@v.co for v in obj.data.vertices],
                               [list(p.vertices) for p in obj.data.polygons])

def measure():
    body=bpy.data.objects['Body']; surface=tree(body)
    points=[v.co for v in body.data.vertices]
    arch_widths={name:2*max(p.x for p in points if abs(p.y-axle)<.48 and p.z>.35)
                 for name,axle in (('front',1.16),('rear',-1.16))}
    triangle_stats={}
    body.data.calc_loop_triangles()
    for region in ('outer_panels','hidden_well_or_underside'):
        total=poor=0; area=poor_area=0
        for tri in body.data.loop_triangles:
            verts=[body.data.vertices[i].co for i in tri.vertices]
            centre=sum(verts,Vector())/3
            outer=abs(centre.x)>.70 and centre.z>.35 or centre.z>.77
            if outer!=(region=='outer_panels'): continue
            lengths=[(a-b).length for a,b in zip(verts,verts[1:]+verts[:1])]
            ratio=max(lengths)**2/(2*tri.area) if tri.area else float('inf')
            total+=1; area+=tri.area
            if ratio>20: poor+=1; poor_area+=tri.area
        triangle_stats[region]={'triangles':total,'aspect_over_20':poor,'area_m2':area,
                                'aspect_over_20_area_m2':poor_area,'aspect_over_20_area_fraction':poor_area/area}
    wheels={o.name:len(surface.overlap(tree(o))) for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('Wheel_')}
    assert all(n==0 for n in wheels.values()),('Body intersects tire',wheels)
    stripe_error=0
    meshes=[o for o in bpy.data.collections['CompactHatchback'].objects if o.type=='MESH']
    front_limit=max(p.y for p in points)-.0001
    nose_samples=[]
    for k in range(7):
        y=1.70+(front_limit-1.70)*k/6
        top=surface.ray_cast(Vector((0,y,2)),Vector((0,0,-1)))[0]
        side=surface.ray_cast(Vector((2,y,.640)),Vector((-1,0,0)))[0]
        assert top is not None and side is not None,'Missing nose shell'
        nose_samples.append({'y_m':y,'hood_center_z_m':top.z,'half_width_at_z_640_m':side.x})
    unchanged_prefixes=('Cabin','RoofGutter','Spoiler','Wheel_','Bumper_Rear','RearValance',
                        'Taillight','RearIndicator','ReverseLight','Mirror','FrontWiper',
                        'RearWiper','LicenseRecess','BlankPlate','HatchLock','Exhaust','Underbody','Sill','DoorHandle')
    hashes={o.name:hashlib.sha256(json.dumps({'vertices':[list(v.co) for v in o.data.vertices],
                                            'matrix':[list(row) for row in o.matrix_world]},separators=(',',':')).encode()).hexdigest()
            for o in meshes if o.name.startswith(unchanged_prefixes)}
    for obj in meshes:
        uv=obj.data.uv_layers['StripeProjection']
        for loop in obj.data.loops:
            p=obj.matrix_world@obj.data.vertices[loop.vertex_index].co
            expected=(.5+p.x/2,.5+p.y/4)
            stripe_error=max(stripe_error,*(abs(uv.data[loop.index].uv[i]-expected[i]) for i in range(2)))
    assert stripe_error<1e-6,('Stripe projection error',stripe_error)
    return {'body_vertices':len(points),'body_max_width_m':2*max(p.x for p in points),
            'arch_max_widths_m':arch_widths,'triangle_aspect_metrics':triangle_stats,
            'body_wheel_triangle_intersections':wheels,'max_stripe_projection_error':stripe_error,
            'nose_samples':nose_samples,'unchanged_component_geometry_sha256':hashes,
            'source_mesh_vertices':{o.name:len(o.data.vertices) for o in meshes},
            'wheel_pivots':{o.name:list(o.location) for o in meshes if o.name.startswith('Wheel_')}}

def main():
    root=Path(sys.argv[sys.argv.index('--')+1]).resolve()
    out=root/'Previews/CompactHatchback/StageC'; report={}
    for stage,path in (('stage_b',out/'.baseline/StageB.blend'),('stage_c',root/'Blender/CompactHatchback.blend')):
        bpy.ops.wm.open_mainfile(filepath=str(path)); report[stage]=measure()
    assert report['stage_b']['wheel_pivots']==report['stage_c']['wheel_pivots'],'Axles moved'
    surface=tree(bpy.data.objects['Body'])
    names=('GrilleSurround','Grille','HeadlightHousing','HeadlightHousing.001','Headlight','Headlight.001',
           'TaillightHousing','TaillightHousing.001','LicenseRecess','BlankPlate')
    # Check geometric crossings. Lamp sockets and backings are separate closed
    # meshes seated inside body recesses; their surfaces must not cut the shell.
    sockets={name:len(surface.overlap(tree(bpy.data.objects[name]))) for name in names}
    assert all(n==0 for n in sockets.values()),('Housing intersects shell',sockets)
    report['stage_c']['body_fascia_triangle_intersections']=sockets
    source=report['stage_c']['source_mesh_vertices']
    before=root/'Previews/CompactHatchback/StageC/FrontCorrection/.baseline/Before.blend'
    if before.exists():
        bpy.ops.wm.open_mainfile(filepath=str(before)); previous=measure()
        current=report['stage_c']
        assert previous['unchanged_component_geometry_sha256']==current['unchanged_component_geometry_sha256'],'Unrelated component geometry changed'
        assert all(abs(previous['arch_max_widths_m'][name]-current['arch_max_widths_m'][name])<1e-6 for name in ('front','rear')),'Stage C arch shaping changed'
        for key in ('hood_center_z_m','half_width_at_z_640_m'):
            values=[sample[key] for sample in current['nose_samples']]
            assert all(b<=a+.00001 for a,b in zip(values,values[1:])),('Nose swells or rises toward front',key,values)
        report['front_correction']={'baseline_commit':'c3010572702a251b993c8d9c681287ee176715ce',
                                   'previous_nose_samples':previous['nose_samples'],
                                   'corrected_nose_samples':current['nose_samples'],
                                   'nose_tapers_without_upturn':True,'arch_widths_unchanged':True,
                                   'unrelated_components_identical':list(current['unchanged_component_geometry_sha256'])}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(root/'Assets/Vehicles/CompactHatchback/Models/CompactHatchback.fbx'))
    imported={o.name:len(o.data.vertices) for o in bpy.context.scene.objects if o.type=='MESH'}
    assert source==imported,('FBX source counts differ',source,imported)
    report['fbx_mesh_counts_match_source']=True
    report['arch_roll_addition_m']={name:report['stage_c']['arch_max_widths_m'][name]/2-report['stage_b']['arch_max_widths_m'][name]/2 for name in ('front','rear')}
    report['wheel_pivots_unchanged']=True
    (out/'geometry_validation.json').write_text(json.dumps(report,indent=2)+'\n')
    print('STAGE_C_GEOMETRY_VALIDATION_PASS',json.dumps({k:v for k,v in report.items() if k not in ('stage_b','stage_c')}),flush=True)

if __name__=='__main__': main()
