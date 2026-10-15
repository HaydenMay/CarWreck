"""Modeling diagnostics and socket authoring checks; not Unity or final asset acceptance QA.

blender -b Blender/CompactPickup_StageB.blend --python-exit-code 1 --python <script> -- <root>
"""
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


def main():
    root=Path(sys.argv[sys.argv.index('--')+1]).resolve(); out=root/'Previews/CompactPickup/StageB'
    sys.path.insert(0,str(root/'Blender/Scripts'))
    import build_compact_pickup_stage_b as B
    A=B.A; B.verify_approved(root)
    A.PARTS=[ob for ob in bpy.data.objects if ob.type=='MESH' and 'engine_role_proposal' in ob]
    bpy.context.view_layer.update()
    bounds=lambda names:A.bounds([bpy.data.objects[n] for n in names])
    g={'stage':'B','units':'metres','orientation':'+X right, +Y forward, +Z up',
       'bounds':A.bounds(A.PARTS),'cabin_bounds':A.bounds([o for o in A.PARTS if o['engine_role_proposal']=='Cabin']),
       'roof_bounds':bounds(['Cabin_Roof']),'bed_bounds':bounds(['BedSide_L','BedSide_R']),
       'hood_bounds':bounds(['Body_Hood']),'body_bounds':A.bounds([o for o in A.PARTS if o.name.startswith(('FrontFender','Door_','BedSide_'))]),
       'liner_floor_bounds':bounds(['BedLiner_Floor']),'grille_bounds':bounds(['Grille_Frame']),
       'lower_paint_boundary_m':A.PAINT_LINE,'front_differential_bounds':bounds(['Suspension_Front']),
       'meshes':len(A.PARTS),'vertices':sum(len(o.data.vertices) for o in A.PARTS),
       'deformable_vertices':sum(len(o.data.vertices) for o in A.PARTS if o['engine_role_proposal']!='Wheel'),
       'wheel_bounds':{o.name:A.bounds([o]) for o in A.PARTS if o['engine_role_proposal']=='Wheel'},
       'production_asset_qa':'NOT RUN - modeling diagnostics only','unity_import':'NOT RUN','destruction_tests':'NOT RUN',
       'production_manifest_written':False,'runtime_sockets_written':False}
    g['wheel_centers']={name:[(v['min'][i]+v['max'][i])*.5 for i in range(3)] for name,v in g['wheel_bounds'].items()}
    g['wheel_origins']={name:list(bpy.data.objects[name].location) for name in g['wheel_bounds']}
    g['rocker_bottom_m']=g['body_bounds']['min'][2]
    fender=bpy.data.objects['FrontFender_L']
    arch_points=[(fender.matrix_world@v.co).z for v in fender.data.vertices if abs((fender.matrix_world@v.co).y-A.FRONT_AXLE)<.03]
    g['front_tire_to_arch_gap_m']=min(arch_points)-g['wheel_bounds']['Wheel_FL']['max'][2]
    metrics=[]; total_zero=0; collapsed_uv=0
    for ob in A.PARTS:
        lengths=sorted((ob.data.vertices[e.vertices[0]].co-ob.data.vertices[e.vertices[1]].co).length for e in ob.data.edges)
        p90=lengths[min(len(lengths)-1,int(len(lengths)*.9))] if lengths else 0
        zero=sum(p.area<1e-12 for p in ob.data.polygons); total_zero+=zero
        uv=ob.data.uv_layers.get('UVMap'); bad_uv=0
        for p in ob.data.polygons:
            points=[uv.data[i].uv for i in p.loop_indices]
            area=abs(sum(a.x*b.y-b.x*a.y for a,b in zip(points,points[1:]+points[:1])))*.5
            bad_uv+=area<1e-12
        collapsed_uv+=bad_uv
        metrics.append({'name':ob.name,'proposed_role':ob['engine_role_proposal'],'vertices':len(ob.data.vertices),
                        'polygons':len(ob.data.polygons),'quads':sum(len(p.vertices)==4 for p in ob.data.polygons),
                        'ngons':sum(len(p.vertices)>4 for p in ob.data.polygons),'zero_area_faces':zero,
                        'collapsed_uv_faces':bad_uv,'p90_edge_m':p90})
    g['components']=metrics
    (out/'geometry_measurements.json').write_text(json.dumps(g,indent=2)+'\n')
    crumple={'Body':1,'Cabin':.85,'Bumper':.25,'Wheel':0,'Panel':1,'Fascia':.55,'Light':.45,'Trim':1,'Underbody':1}
    (out/'component_roles.json').write_text(json.dumps({'stage':'B role proposals; not an imported vehicle manifest',
        'components':[{'pattern':m['name'],'match':'Exact','role':m['proposed_role'],
                       'mode':'Rigid' if m['proposed_role']=='Wheel' else 'Deform','crumple':crumple[m['proposed_role']]} for m in metrics]},indent=2)+'\n')
    # Raycasts in the saved scene check the open bed and physical separation.
    deps=bpy.context.evaluated_depsgraph_get()
    def ray(x,y):
        ok,p,n,i,ob,matrix=bpy.context.scene.ray_cast(deps,Vector((x,y,4)),Vector((0,0,-1)))
        return {'hit':ok,'object':ob.name if ok else None,'point':list(p) if ok else None}
    center=ray(0,-1.28); gap=ray(0,-.294)
    # Roof UV1 values above the lower-band mask cutoff must select only Color A.
    roof=bpy.data.objects['Cabin_Roof']; roof_uv=roof.data.uv_layers['StripeProjection']
    roof_selects_a=all(d.uv.y>A.PAINT_LINE/2+.01 for d in roof_uv.data)
    hood=bpy.data.objects['Body_Hood']; by_y={}
    for v in hood.data.vertices:
        p=hood.matrix_world@v.co; by_y.setdefault(round(p.y,5),[]).append(p.x)
    widths=[max(row)-min(row) for y,row in sorted(by_y.items())]
    # Stage A wheel roots are in the protected blend; their authored coordinates are preserved.
    origin_expected={'Wheel_FL':(-.876,1.64,.42),'Wheel_FR':(.876,1.64,.42),'Wheel_RL':(-.876,-1.31,.42),'Wheel_RR':(.876,-1.31,.42)}
    origin_delta=max((Vector(g['wheel_origins'][n])-Vector(p)).length for n,p in origin_expected.items())
    cavity_directions={'BedLiner_Side_L':(1,0,0),'BedLiner_Side_R':(-1,0,0),
                       'BedLiner_FrontWall':(0,-1,0),'BedLiner_Tailgate':(0,1,0),'Wheelhouse_Inner':(0,0,-1)}
    cavity_normals={name:min(p.normal.dot(Vector(direction)) for p in bpy.data.objects[name].data.polygons)
                    for name,direction in cavity_directions.items()}
    bumper_boundaries={}
    for name in ('Bumper_Front','Bumper_Rear'):
        bm=bmesh.new(); bm.from_mesh(bpy.data.objects[name].data)
        bumper_boundaries[name]=sum(e.is_boundary for e in bm.edges); bm.free()
    checks={
        'approved_stage_a_hashes_unchanged':True,
        'deformable_vertices_under_90000':g['deformable_vertices']<=90000,
        'deformable_parts_at_most_80':sum(m['proposed_role']!='Wheel' for m in metrics)<=80,
        'all_coordinates_finite':all(math.isfinite(c) for ob in A.PARTS for v in ob.data.vertices for c in v.co),
        'no_zero_area_faces':total_zero==0,
        'no_collapsed_uv_faces':collapsed_uv==0,
        'body_grid_p90_under_6cm':all(m['p90_edge_m']<=.0601 for m in metrics if m['proposed_role']=='Body'),
        'body_has_no_ngons':all(m['ngons']==0 for m in metrics if m['proposed_role']=='Body'),
        'wheel_origins_preserved_under_0_1mm':origin_delta<.0001,
        'four_wheels_centered_within_3cm':len(g['wheel_centers'])==4 and all((Vector(g['wheel_origins'][n])-Vector(g['wheel_centers'][n])).length<.03 for n in g['wheel_centers']),
        'open_bed_center_hits_liner':center['hit'] and center['object'].startswith('BedLiner') and center['point'][2]<1.0,
        'cab_bed_gap_clear_above_chassis':gap['hit'] and gap['point'][2]<.60,
        'hood_plan_monotone_no_hourglass':all(b<=a+1e-5 for a,b in zip(widths,widths[1:])),
        'roof_uses_primary_green_uv_mask':roof_selects_a,
        'front_arch_gap_at_least_10cm':g['front_tire_to_arch_gap_m']>.10,
        'all_vehicle_object_scales_applied':all(max(abs(c-1) for c in ob.scale)<1e-6 for ob in A.PARTS),
        'two_uv_layers_on_every_part':all('UVMap' in ob.data.uv_layers and 'StripeProjection' in ob.data.uv_layers for ob in A.PARTS),
        'grille_slats_recessed_behind_frame':bounds(['Grille_Inset'])['max'][1]<g['grille_bounds']['max'][1]-.025,
        'open_cavity_panels_face_visible_interior':all(v>0 for v in cavity_normals.values()),
        'stamped_bumper_surfaces_remain_closed':all(v==0 for v in bumper_boundaries.values()),
    }
    sockets,validation=socket_authoring(A)
    (out/'socket_authoring.json').write_text(json.dumps(sockets,indent=2)+'\n')
    (out/'socket_authoring_checks.json').write_text(json.dumps(validation,indent=2)+'\n')
    checks['socket_seeds_hit_support_and_normal']=all(row['pass'] for row in validation)
    report={'stage':'B modeling diagnostics only','checks':checks,'passed':sum(checks.values()),'total':len(checks),
            'source_blend_sha256':hashlib.sha256((root/'Blender/CompactPickup_StageB.blend').read_bytes()).hexdigest(),
            'approved_a_revision':json.loads((root/'Previews/CompactPickup/approved_checkpoint_a.json').read_text())['approved_revision'],
            'vertices_total':g['vertices'],'deformable_vertices':g['deformable_vertices'],'body_edge_p90_m':{m['name']:m['p90_edge_m'] for m in metrics if m['proposed_role']=='Body'},
            'zero_area_faces':total_zero,'collapsed_uv_faces':collapsed_uv,'wheel_origin_max_delta_m':origin_delta,
            'bed_ray':center,'cab_gap_ray':gap,'roof_primary_srgb':list(A.COLOR_A),'roof_mask_selects_a':roof_selects_a,
            'cavity_min_normal_dot':cavity_normals,'bumper_open_boundary_edges':bumper_boundaries,
            'production_asset_qa':'NOT RUN','unity_import':'NOT RUN','destruction_tests':'NOT RUN','live_attachments':'NOT RUN'}
    (out/'modeling_checks.json').write_text(json.dumps(report,indent=2)+'\n')
    print('MODELING_CHECKS',json.dumps({'passed':report['passed'],'total':report['total'],'deformable_vertices':g['deformable_vertices'],
                                      'failed':[k for k,v in checks.items() if not v],
                                      'worst_body_p90_m':max(report['body_edge_p90_m'].values()),
                                      'zero_area':total_zero,'collapsed_uv':collapsed_uv}),flush=True)
    assert all(checks.values()),'Modeling diagnostic failed; inspect modeling_checks.json'


def socket_authoring(A):
    groups=[]; validation=[]
    def add(group,support,ids,origins,direction,normal,footprint=None):
        ob=bpy.data.objects[support]
        verts=[ob.matrix_world@v.co for v in ob.data.vertices]
        tree=BVHTree.FromPolygons(verts,[list(p.vertices) for p in ob.data.polygons])
        records=[]
        for identity,origin in zip(ids,origins):
            p,n,index,distance=tree.ray_cast(Vector(origin),Vector(direction),.8)
            alignment=n.dot(Vector(normal)) if n is not None else None
            good=p is not None and alignment>=math.cos(math.radians(55))
            validation.append({'socket_group':group,'socket_id':identity,'support':support,'pass':good,
                               'resolved_rest_point':list(p) if p is not None else None,'normal_dot_expected':alignment,
                               'validation':'Blender seed/support only; no runtime binding/strength/attachment test'})
            q=Vector((0,0,1)).rotation_difference(Vector(normal))
            target=p if p is not None else Vector(origin)+Vector(direction)*.4
            records.append({'id':identity,'origin':origin,'direction':direction,'expected_normal':normal,
                            'rotation_xyzw':[q.x,q.y,q.z,q.w],
                            'bounds':[[target[i]-.045 for i in range(3)],[target[i]+.045 for i in range(3)]],
                            'max_distance':.8})
        g={'socket_group':group,'mount_mode':'multi_point_rigid','mesh_role':support,'sockets':records}
        if footprint:g['maximum_candidate_footprint_m']=footprint
        groups.append(g)
    add('roof.compact_mount','Cabin_Roof',['R1','R2','R3','R4'],[(x,y,2.2) for y in (-.06,.36) for x in (-.48,.48)],(0,0,-1),(0,0,1),[1.30,.50])
    add('hood.scoop','Body_Hood',['H1','H2','H3','H4'],[(x,y,1.7) for y in (1.28,1.75) for x in (-.26,.26)],(0,0,-1),(0,0,1),[.70,.70])
    add('front.push_bar','Bumper_Front',['F1','F2'],[(x,2.7,.75) for x in (-.55,.55)],(0,-1,0),(0,1,0))
    add('rear.utility','Bumper_Rear',['B1','B2'],[(x,-2.7,.64) for x in (-.55,.55)],(0,1,0),(0,-1,0))
    for side,name,identity in ((-1,'L','L'),(1,'R','BR')):
        add('bed.rail_'+name.lower(),'BedRail_'+name,[identity+'1',identity+'2'],[(side*.922,y,1.8) for y in (-.65,-1.93)],(0,0,-1),(0,0,1),[.08,1.5])
    add('bed.floor','BedFloor',['C1','C2','C3','C4'],[(x,y,1.4) for y in (-.65,-1.96) for x in (-.4,.4)],(0,0,-1),(0,0,1),[1.0,1.45])
    return {'vehicle_id':'carwreck.compact_pickup','coordinates':'Blender vehicle-local metres; +X right +Y forward +Z up',
            'runtime_ready':False,'stage':'B authoring candidates; not a production manifest or accessory fit approval',
            'unsupported':'Existing full-size roof racks have not been fitted; roof.rack omitted. No permanent accessories.',
            'groups':groups},validation


if __name__=='__main__': main()
