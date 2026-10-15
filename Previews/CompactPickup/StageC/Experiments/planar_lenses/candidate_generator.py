"""Reproduce approved pickup geometry, export production assets and render Stage C.

blender -b --factory-startup --python-exit-code 1 --python <script> -- <root> [--render]
Approved A/B sources and images are read-only. No shared engine configuration is changed.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0,str(Path(__file__).parent))
import build_compact_pickup_stage_b as B

A=B.A
MASS_KG=1350
CRUMPLE={'Body':1,'Cabin':.85,'Bumper':.25,'Wheel':0,'Panel':1,'Fascia':.55,'Light':.45,'Trim':1,'Underbody':1}


def write(path,value):
    path.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')


def protect(root):
    for stage in ('a','b'):
        record=json.loads((root/f'Previews/CompactPickup/approved_checkpoint_{stage}.json').read_text())
        for path,sha in record['files_sha256'].items():
            assert hashlib.sha256((root/path).read_bytes()).hexdigest()==sha,'Approved file changed: '+path


def signatures(parts):
    records={}
    for ob in parts:
        h=hashlib.sha256()
        for v in ob.data.vertices:h.update(struct.pack('<3f',*v.co))
        for p in ob.data.polygons:
            h.update(struct.pack('<II?',len(p.vertices),p.material_index,p.use_smooth))
            for i in p.vertices:h.update(struct.pack('<I',i))
        for uv in ob.data.uv_layers:
            h.update(uv.name.encode())
            for item in uv.data:h.update(struct.pack('<2f',*item.uv))
        h.update(json.dumps({'origin':list(ob.location),'scale':list(ob.scale),'role':ob['engine_role_proposal'],
                             'materials':[m.name for m in ob.data.materials]},sort_keys=True).encode())
        records[ob.name]=h.hexdigest()
    return records


def measurements():
    bounds=lambda names:A.bounds([bpy.data.objects[n] for n in names])
    g={'stage':'C','units':'metres','orientation':'+X right, +Y forward, +Z up',
       'bounds':A.bounds(A.PARTS),'cabin_bounds':A.bounds([o for o in A.PARTS if o['engine_role_proposal']=='Cabin']),
       'roof_bounds':bounds(['Cabin_Roof']),'bed_bounds':bounds(['BedSide_L','BedSide_R']),
       'hood_bounds':bounds(['Body_Hood']),'body_bounds':A.bounds([o for o in A.PARTS if o.name.startswith(('FrontFender','Door_','BedSide_'))]),
       'liner_floor_bounds':bounds(['BedLiner_Floor']),'grille_bounds':bounds(['Grille_Frame']),
       'lower_paint_boundary_m':A.PAINT_LINE,'front_differential_bounds':bounds(['Suspension_Front']),
       'meshes':len(A.PARTS),'vertices':sum(len(o.data.vertices) for o in A.PARTS),
       'deformable_vertices':sum(len(o.data.vertices) for o in A.PARTS if o['engine_role_proposal']!='Wheel'),
       'wheel_bounds':{o.name:A.bounds([o]) for o in A.PARTS if o['engine_role_proposal']=='Wheel'}}
    g['wheel_centers']={name:[(v['min'][i]+v['max'][i])*.5 for i in range(3)] for name,v in g['wheel_bounds'].items()}
    g['wheel_origins']={name:list(bpy.data.objects[name].location) for name in g['wheel_bounds']}
    g['rocker_bottom_m']=g['body_bounds']['min'][2]
    f=bpy.data.objects['FrontFender_L']
    g['front_tire_to_arch_gap_m']=min((f.matrix_world@v.co).z for v in f.data.vertices if abs((f.matrix_world@v.co).y-A.FRONT_AXLE)<.03)-g['wheel_bounds']['Wheel_FL']['max'][2]
    return g


def remesh_hood():
    """Replace near-duplicate station strips with bounded spacing on the SAME approved contour."""
    old=bpy.data.objects['Body_Hood']; A.PARTS.remove(old); bpy.data.objects.remove(old,do_unlink=True)
    knots=sorted(set([0]+[side*u for side in (-1,1) for u in (.37,.386,.40,.414,.43,.75,.773,.788,.803,.825,.86,.875,.885,.90,1)]))
    us=[]
    for a,b in zip(knots,knots[1:]):us.extend(B.stations(a,b,.027)[:-1])
    us.append(knots[-1]); assert min(b-a for a,b in zip(us,us[1:]))>=.0099
    mesh=B.Mesh()
    mesh.grid(us,B.stations(.98,2.25,.026),
        lambda u,y:((.97-.035*(y-.98)/1.27)*u,y,B.hood_height(y,(.97-.035*(y-.98)/1.27)*u)),
        'Paint',lambda u,y:'Mirror' if .875<abs(u)<.885 else 'Paint')
    mesh.object('Body_Hood','Body')


def refine_small_surfaces():
    # Single exposed sheets avoid a second skin hidden against the glass/tailgate.
    for name in ('Wipers','BedRail_Rear'):
        ob=bpy.data.objects[name]; bm=bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.normal_update()
        if name=='Wipers':
            facing=Vector((0,.74,.67)).normalized()
            remove=[f for f in bm.faces if f.normal.dot(facing)<0]
        else:
            remove=[f for f in bm.faces if f.normal.z<-.5 and f.calc_center_median().z<1.30]
        bmesh.ops.delete(bm,geom=remove,context='FACES'); bm.to_mesh(ob.data); bm.free(); ob.data.update()
    # The visible lens faces remain in place. Remove concealed reflector dishes and
    # extruded return skins. This removes measured lens reversals; separate visual
    # checks must still establish whether lamp alignment and side depth are acceptable.
    # A dense, single exposed sheet is a supported deformation surface, not a rigid part.
    for side in (-1,1):
        suffix='L' if side<0 else 'R'
        for family,center,size,material,radius,spacing in (
            ('FrontIndicator',(side*.934,2.273,1.016),(.060,.057,.267),'Amber',.014,.009),
            ('Headlight',(side*.742,2.300,1.017),(.24,.028,.23),'Headlight',.018,.014)):
            name=family+'_'+suffix; old=bpy.data.objects[name]
            A.PARTS.remove(old); bpy.data.objects.remove(old,do_unlink=True)
            half=Vector(size)*.5; radius=min(radius,min(half)*.90)
            def point(u,v):
                p=Vector((u,half.y,v)); core=Vector([max(-half[k]+radius,min(half[k]-radius,p[k])) for k in range(3)])
                p=core+(p-core).normalized()*radius
                p.y=half.y
                return Vector(center)+p
            mesh=B.Mesh(); mesh.grid(B.stations(-half.x,half.x,spacing),list(reversed(B.stations(-half.z,half.z,spacing))),point,material)
            mesh.object(name,'Light')


def export(root,out,g):
    asset=root/'Assets/Vehicles/CompactPickup'; models=asset/'Models'; textures=asset/'Textures'
    models.mkdir(parents=True,exist_ok=True); textures.mkdir(parents=True,exist_ok=True)
    image=bpy.data.images['Pickup_LowerBodyMask']; image.filepath_raw=str(textures/'LowerBodyMask.png'); image.save()
    palette=json.loads((out/'palette.json').read_text()); palette.pop('stage',None)
    write(out/'palette.json',palette); write(asset/'palette.json',palette)
    bpy.ops.object.select_all(action='DESELECT')
    for ob in [A.ROOT]+A.PARTS:ob.select_set(True)
    bpy.context.view_layer.objects.active=A.ROOT
    bpy.ops.export_scene.fbx(filepath=str(models/'CompactPickup.fbx'),use_selection=True,object_types={'EMPTY','MESH'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,axis_forward='-Z',axis_up='Y',
        mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,path_mode='AUTO',embed_textures=False,
        bake_anim=False,use_custom_props=True)
    sockets,checks=__import__('inspect_pickup_stage_b').socket_authoring(A)
    write(out/'socket_authoring.json',sockets); write(out/'socket_authoring_checks.json',checks)
    assert all(row['pass'] for row in checks),'Socket authoring support failed'
    manifest={'format':'carwreck.vehicle.v1','id':'carwreck.compact_pickup','revision':1,'displayName':'Compact Pickup',
        'model':'Assets/Vehicles/CompactPickup/Models/CompactPickup.fbx','palette':'Assets/Vehicles/CompactPickup/palette.json',
        'paint':{'material':'Pickup_Paint','stripeMask':'Assets/Vehicles/CompactPickup/Textures/LowerBodyMask.png'},
        'massKg':MASS_KG,'unknownComponentCrumple':1,'maxImpactRecords':24,
        'cabinLine':{'start':0,'end':0},'shell':{'frontDepth':0,'rearDepth':0,'sideDepth':0,'impactHeight':0},
        'components':[{'pattern':o.name,'match':'Exact','role':o['engine_role_proposal'],
                       'mode':'Rigid' if o['engine_role_proposal']=='Wheel' else 'Deform','crumple':CRUMPLE[o['engine_role_proposal']]} for o in sorted(A.PARTS,key=lambda o:o.name)],
        'expected':{'dimensionsM':[g['bounds']['max'][i]-g['bounds']['min'][i] for i in range(3)],
                    'wheels':[{'name':n,'position':p} for n,p in g['wheel_origins'].items()]},
        'sockets':{k:sockets[k] for k in ('coordinates','runtime_ready','groups')}}
    manifest['revision']=2
    write(asset/'vehicle.json',manifest)


def main():
    argv=sys.argv[sys.argv.index('--')+1:]; parser=argparse.ArgumentParser()
    parser.add_argument('root',type=Path); parser.add_argument('--render',action='store_true')
    parser.add_argument('--views',nargs='+'); parser.add_argument('--samples',type=int,default=32)
    args=parser.parse_args(argv); root=args.root.resolve(); protect(root)
    out=root/'Previews/CompactPickup/StageC'; out.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(root/'Blender/CompactPickup_StageB.blend'))
    approved=signatures([o for o in bpy.data.objects if o.type=='MESH' and 'engine_role_proposal' in o])
    corrections={'Body_Hood':.005,'Wipers':.018,'BedRail_Rear':.020,'FrontIndicator_L':.005,'FrontIndicator_R':.005,'Headlight_L':.005,'Headlight_R':.005}
    approved_points={o.name:[list(o.matrix_world@v.co) for v in o.data.vertices] for o in bpy.data.objects if o.name in corrections}
    bpy.ops.wm.read_factory_settings(use_empty=True); A.PARTS.clear(); A.MATS.clear()
    A.ROOT=bpy.data.objects.new('CompactPickup',None); bpy.context.scene.collection.objects.link(A.ROOT)
    A.ROOT['development_stage']='C - production validation; approval pending'
    B.surface_materials(); B.body(); B.front(); B.cabin(); B.bed(); B.wheels(); B.suspension(); remesh_hood(); refine_small_surfaces(); B.finalize()
    current=signatures(A.PARTS); changed=[n for n in approved if current.get(n)!=approved[n]]
    assert set(current)==set(approved) and set(changed)==set(corrections),('Unexpected change from approved B components',changed)
    surface_changes={}
    for name in changed:
        ob=bpy.data.objects[name]; tree=BVHTree.FromPolygons([ob.matrix_world@v.co for v in ob.data.vertices],[list(p.vertices) for p in ob.data.polygons])
        distances=sorted(tree.find_nearest(Vector(p))[3] for p in approved_points[name])
        surface_changes[name]={'max_m':max(distances),'p95_m':distances[int(len(distances)*.95)]}
        # Measure exposed lens faces separately from the deliberately removed hidden caps.
        exposed=([p for p in approved_points[name] if p[1]>=2.297] if name.startswith('FrontIndicator') else
                 [p for p in approved_points[name] if p[1]>=2.310] if name.startswith('Headlight_') else approved_points[name])
        visible=max(tree.find_nearest(Vector(p))[3] for p in exposed)
        surface_changes[name]['exposed_max_m']=visible
        assert visible<corrections[name],('Topology correction exceeded its recorded exposed-surface bound',name,visible)
    B.palette_and_mask(out); camera=A.studio(); bpy.context.scene.cycles.samples=args.samples
    camera.location=A.VIEWS['front34'][0]; A.aim(camera,A.VIEWS['front34'][1]); camera.data.type='PERSP'; camera.data.lens=70
    bpy.context.view_layer.update(); g=measurements(); write(out/'geometry_measurements.json',g)
    export(root,out,g)
    bpy.context.preferences.filepaths.save_version=0
    path=root/'Blender/CompactPickup.blend'; bpy.ops.wm.save_as_mainfile(filepath=str(path))
    write(out/'approved_b_preservation.json',{'approved_revision':'156d2673008b63212f53db4b23784e5bc78c617e',
        'same_geometry_uv_material_slots_and_roles':current==approved,'unchanged_components':len(current)-len(changed),'changed_components':changed,
        'topology_correction':'Even hood stations retain the approved contour. Hidden wiper backs and lower tailgate-cap skin removed. Front lenses use single exposed sheets; extruded return skins and concealed reflector dishes removed. Visible lens fronts stay within 5 mm; side depth is deliberately reduced. Paint, roles and vehicle footprint retained.',
        'surface_changes':surface_changes,'components':current,
        'final_blend_sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'mass_kg':MASS_KG,
        'mass_note':'1350 kg is a game-design assumption for a compact 4x4 pickup, not a measured reference specification.'})
    print('PICKUP_FINAL_EXPORT',json.dumps({'meshes':g['meshes'],'vertices':g['vertices'],'deformable_vertices':g['deformable_vertices'],'approved_b_changed_components':changed,'surface_changes':surface_changes}),flush=True)
    if args.render:
        B.render(camera,out,args.views or list(dict(A.VIEWS,**B.EXTRA_VIEWS)))
        for p in out.glob('stage_b_*.png'):p.replace(p.with_name(p.name.replace('stage_b_','stage_c_',1)))
    protect(root)


if __name__=='__main__':main()
