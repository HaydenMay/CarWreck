"""Throwaway geometry candidates for field diagnosis. Never edits approved or production assets."""
import sys, json, math
from pathlib import Path
import bpy, bmesh
from mathutils import Vector
sys.path.insert(0,str(Path(__file__).parent))
import build_compact_pickup_stage_b as B
A=B.A
root=Path(sys.argv[sys.argv.index('--')+1]); out=root/'Previews/CompactPickup/StageC/Revision2/Investigation'
bpy.ops.wm.read_factory_settings(use_empty=True);A.PARTS.clear();A.MATS.clear()
A.ROOT=bpy.data.objects.new('Probe',None);bpy.context.scene.collection.objects.link(A.ROOT);B.surface_materials()
for side in (-1,1):
    for label,radius,spacing,inset in [('baseline',.014,.009,0),('regular_spacing',.014,.014,0),('dense',.014,.006,0),('rounder20',.020,.009,0),('rounder26',.026,.009,0),('inset22',.014,.009,.022),('inset22_rounder20',.020,.009,.022)]:
        mesh=B.Mesh();mesh.box((side*(.934-inset),2.273,1.016),(.060,.057,.267),'Amber',radius,spacing)
        mesh.object(f'Indicator_{side}_{label}','Light')
for n in (8,12,16):
    for along in (160,240,320):
        mesh=B.Mesh()
        for side in (-1,1):
            path=[(side*.66+.055*math.cos(i*math.tau*5/along),A.FRONT_AXLE+.055*math.sin(i*math.tau*5/along),.57+.29*i/along) for i in range(along+1)]
            mesh.tube(path,.010,'Chassis',n)
        mesh.object(f'Coils_{n}_{along}','Underbody')
B.finalize(); rows=[]
for ob in A.PARTS:
    if ob.name.startswith('Indicator'):
        bm=bmesh.new();bm.from_mesh(ob.data);bm.normal_update()
        bmesh.ops.delete(bm,geom=[f for f in bm.faces if f.normal.y<-.5 and f.calc_center_median().y<2.253],context='FACES')
        bm.to_mesh(ob.data);bm.free();ob.data.update()
    ob.data.calc_loop_triangles()
    rows.append({'name':ob.name,'crumple':.45 if ob.name.startswith('Indicator') else 1,
        'vertices':[{'x':v.co.x,'y':v.co.z,'z':v.co.y} for v in ob.data.vertices],
        'normals':[{'x':v.normal.x,'y':v.normal.z,'z':v.normal.y} for v in ob.data.vertices],
        'triangles':[i for t in ob.data.loop_triangles for i in t.vertices]})
(out/'topology_candidates.json').write_text(json.dumps({'candidates':rows}))
print('DIAGNOSTIC_CANDIDATES',len(rows),flush=True)
