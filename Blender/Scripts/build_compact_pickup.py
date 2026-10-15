"""CompactPickup Stage A only. Blender 5.2, metres, +Y forward, +X right.

blender -b --factory-startup --python-exit-code 1 --python <script> -- <root> [--render]
Does not export FBX, write a production vehicle manifest, or touch Unity assets.
"""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector

MATS = {}
PARTS = []
ROOT = None
COLOR_A = (.08, .22, .145)
COLOR_B = (.78, .72, .60)
FRONT_AXLE, REAR_AXLE = 1.64, -1.31
WHEEL_Z = .42
PAINT_LINE = .805


def linear(rgb):
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb)


def material(name, color, metallic=0., roughness=.4, emission=0.):
    mat = bpy.data.materials.new('Pickup_' + name)
    mat.use_nodes = True
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Base Color'].default_value = (*linear(color), 1)
    bs.inputs['Metallic'].default_value = metallic
    bs.inputs['Roughness'].default_value = roughness
    if emission:
        bs.inputs['Emission Color'].default_value = (*linear(color), 1)
        bs.inputs['Emission Strength'].default_value = emission
    mat.diffuse_color = (*linear(color), 1)
    MATS[name] = mat
    return mat


def materials():
    mat = material('Paint', COLOR_A, .18, .36)
    bs = mat.node_tree.nodes.get('Principled BSDF')
    bs.inputs['Coat Weight'].default_value = .20
    uv = mat.node_tree.nodes.new('ShaderNodeUVMap')
    uv.uv_map = 'StripeProjection'
    split = mat.node_tree.nodes.new('ShaderNodeSeparateXYZ')
    mat.node_tree.links.new(uv.outputs['UV'], split.inputs[0])
    mix = mat.node_tree.nodes.new('ShaderNodeMixRGB')
    mix.name = 'Primary and Lower Body Colors'
    mix.inputs[1].default_value = (*linear(COLOR_A), 1)
    mix.inputs[2].default_value = (*linear(COLOR_B), 1)
    lower = mat.node_tree.nodes.new('ShaderNodeMath')
    lower.operation = 'LESS_THAN'
    lower.inputs[1].default_value = PAINT_LINE / 2
    mat.node_tree.links.new(split.outputs['Y'], lower.inputs[0])
    mat.node_tree.links.new(lower.outputs[0], mix.inputs[0])
    mat.node_tree.links.new(mix.outputs[0], bs.inputs['Base Color'])
    # Stage A preview uses a UV-encoded lower-body mask. Stage B must bake the
    # same mapping into a UV1 mask for the existing Unity two-color shader.
    material('Rubber', (.075, .082, .080), 0, .78)
    material('Liner', (.065, .075, .071), .03, .70)
    material('Bumper', (.18, .19, .18), .50, .38)
    material('Grille', (.095, .115, .105), .2, .50)
    material('Mirror', (.10, .12, .11), .1, .45)
    material('Steel', (.62, .63, .58), .70, .34)
    material('BrightEdge', (.52, .55, .52), .70, .28)
    material('Glass', (.18, .24, .235), .28, .22)
    material('Headlight', (.88, .89, .79), .05, .23, .3)
    material('Amber', (.94, .36, .055), .10, .3)
    material('Red', (.62, .035, .025), .10, .3)
    material('Chassis', (.12, .135, .125), .45, .55)


def mesh(name, verts, faces, mat='Paint', role='Body', smooth=False):
    data = bpy.data.meshes.new(name)
    data.from_pydata(verts, [], faces)
    data.update()
    ob = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = ROOT
    ob.data.materials.append(MATS[mat])
    ob['engine_role_proposal'] = role
    ob['stage'] = 'A - blockout; production topology not validated'
    for p in data.polygons:
        p.use_smooth = smooth
    PARTS.append(ob)
    return ob


def cube(name, center, size, mat='Paint', role='Body', bevel=.008):
    x, y, z = (v / 2 for v in size)
    ob = mesh(name, [(-x,-y,-z),(-x,-y,z),(-x,y,-z),(-x,y,z),
                     (x,-y,-z),(x,-y,z),(x,y,-z),(x,y,z)],
              [(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)], mat, role)
    ob.location = center
    if bevel:
        mod = ob.modifiers.new('Blockout edge softness', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
    return ob


def lathe(name, profile, center, mat, role='Wheel', segments=48):
    verts = [(a, r * math.cos(t * 2 * math.pi / segments), r * math.sin(t * 2 * math.pi / segments))
             for a, r in profile for t in range(segments)]
    faces = []
    for row in range(len(profile) - 1):
        for j in range(segments):
            k = (j + 1) % segments
            faces.append((row*segments+j,row*segments+k,(row+1)*segments+k,(row+1)*segments+j))
    ob = mesh(name, verts, faces, mat, role, True)
    ob.location = center
    return ob


def rod(name, a, b, radius, mat='Chassis', role='Underbody', vertices=16):
    d = Vector(b) - Vector(a)
    bpy.ops.mesh.primitive_cylinder_add(vertices=vertices, radius=radius, depth=d.length,
                                      location=(Vector(a)+Vector(b))*.5)
    ob = bpy.context.object
    ob.name = name
    ob.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()
    ob.parent = ROOT
    ob.data.materials.append(MATS[mat])
    ob['engine_role_proposal'] = role
    PARTS.append(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    return ob


def join(parts, name, origin=None):
    bpy.ops.object.select_all(action='DESELECT')
    for ob in parts:
        ob.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    ob = bpy.context.object
    ob.name = name
    for other in parts[1:]:
        if other in PARTS:
            PARTS.remove(other)
    if origin:
        bpy.context.scene.cursor.location = origin
        bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    return ob


def side_panel(name, start, end, axle=None, role='Body', half_width=.97, top=1.31):
    steps = max(3, math.ceil(abs(end-start)/.055))
    verts, faces = [], []
    for i in range(steps + 1):
        y = start + (end-start)*i/steps
        bottom = .505
        arch = 0.
        if axle is not None and abs(y-axle) < .535:
            # Squarish, generous wheel openings: continuous side grid, no boolean.
            t = abs(y-axle)/.535
            arch = .42 + .565 * (1-t**3.4)**(1/3.4)
            bottom = max(bottom, arch)
        h = top(y) if callable(top) else top
        for j in range(7):
            f = j/6
            z = bottom+(h-bottom)*f
            w = half_width(y) if callable(half_width) else half_width
            x = w - .035*max(0,(.90-z)/.395)**2
            verts.append((x,y,z))
    for i in range(steps):
        for j in range(6):
            a = i*7+j
            faces.append((a,a+7,a+8,a+1))
    parts=[]
    for side in (-1,1):
        suffix = 'L' if side < 0 else 'R'
        vv=[(side*x,y,z) for x,y,z in verts]
        ff=faces if side>0 else [tuple(reversed(f)) for f in faces]
        ob=mesh(name+'_'+suffix,vv,ff,'Paint',role,True)
        sol=ob.modifiers.new('Blockout panel depth', 'SOLIDIFY'); sol.thickness=.025
        parts.append(ob)
    return parts


def hood_height(y, x=0.):
    t=(y-.98)/(2.25-.98)
    crown=.028*(1-(x/.96)**2)
    # A shallow convex hood with two pressed shoulders, no concave plan outline.
    ribs=.012*math.exp(-((abs(x)-.55)/.07)**2)*math.sin(math.pi*max(0,min(1,t)))
    return 1.285-.108*t+crown+ribs


def front_body():
    side_panel('FrontFender',.98,2.25,FRONT_AXLE,
               half_width=lambda y: .97-.035*(y-.98)/1.27,
               top=lambda y: hood_height(y,.94))
    nx, ny = 32, 28
    verts=[]
    for j in range(ny+1):
        y=.98+1.27*j/ny
        w=.97-.035*(y-.98)/1.27
        for i in range(nx+1):
            x=w*(-1+2*i/nx)
            verts.append((x,y,hood_height(y,x)))
    faces=[]
    for j in range(ny):
        for i in range(nx):
            a=j*(nx+1)+i
            faces.append((a,a+1,a+nx+2,a+nx+1))
    mesh('Body_Hood',verts,faces,'Paint','Body',True)
    cube('Body_Nose',(0,2.22,.985),(1.88,.07,.38),'Paint','Body',.022)
    # Fascia sits inside the nose silhouette, with a thin continuous dark frame.
    cube('Grille_Frame',(0,2.263,1.005),(1.19,.048,.285),'BrightEdge','Fascia',.016)
    cube('Grille_Recess',(0,2.289,1.005),(1.145,.018,.248),'Grille','Fascia',.004)
    for z in (.925,1.005,1.085):
        cube('Grille_Slat',(0,2.305,z),(1.12,.019,.018),'BrightEdge','Fascia',.003)
    for x in (-.28,.28):
        cube('Grille_Support',(x,2.303,1.005),(.015,.018,.25),'Grille','Fascia',.002)
    for side in (-1,1):
        s='L' if side<0 else 'R'
        cube('Headlight_Housing_'+s,(side*.742,2.274,1.013),(.28,.052,.285),'Grille','Light',.015)
        cube('Headlight_'+s,(side*.742,2.307,1.017),(.24,.028,.23),'Headlight','Light',.023)
        cube('FrontIndicator_'+s,(side*.934,2.273,1.016),(.060,.057,.267),'Amber','Light',.008)
    cube('Bumper_Front',(0,2.26,.697),(1.99,.18,.235),'Bumper','Bumper',.035)
    for side in (-1,1):
        cube('BumperMarker_'+str(side),(side*.77,2.354,.707),(.18,.012,.08),'Amber','Light',.009)
    cube('FrontPlate',(0,2.356,.66),(.37,.012,.16),'Mirror','Trim',.006)


def glazing(name, points, direction):
    center=sum((Vector(p) for p in points),Vector())/len(points)
    edge=mesh(name+'_Seal',points,[tuple(range(len(points)))],'Mirror','Cabin')
    inner=[tuple(center+(Vector(p)-center)*.925+Vector(direction)*.006) for p in points]
    mesh(name,inner,[tuple(range(len(points)))],'Glass','Cabin')
    return edge


def cab():
    side_panel('Door',-.27,.98,top=1.29)
    # Closed lower cab and distinct rear wall; the bed starts behind a 45 mm gap.
    cube('Body_CabRear',(0,-.247,.925),(1.90,.045,.84),'Paint','Body',.012)
    verts=[(-.955,-.27,1.285),(.955,-.27,1.285),(-.945,.98,1.285),(.945,.98,1.285),
           (-.822,-.20,1.79),(.822,-.20,1.79),(-.822,.52,1.79),(.822,.52,1.79)]
    mesh('Cabin',verts,[(0,2,6,4),(3,1,5,7),(2,3,7,6),(1,0,4,5)],'Paint','Cabin')
    # Slight crown, rather than a slab sitting on a trapezoid.
    roof=[]; faces=[]
    for j in range(9):
        y=-.20+.72*j/8
        for i in range(25):
            x=-.822+1.644*i/24
            roof.append((x,y,1.79+.03*(1-(x/.822)**2)*math.sin(math.pi*(.08+.84*j/8))))
    for j in range(8):
        for i in range(24):
            a=j*25+i; faces.append((a,a+1,a+26,a+25))
    mesh('Cabin_Roof',roof,faces,'Paint','Cabin',True)
    glazing('Cabin_Windshield',[(-.862,.944,1.337),(.862,.944,1.337),(.769,.566,1.739),(-.769,.566,1.739)],(0,1,0))
    for side in (-1,1):
        suffix='L' if side<0 else 'R'
        def at(y,z):
            return (side*(.955-(z-1.285)/.505*.133),y,z)
        glazing('Cabin_SideGlass_'+suffix,[at(.866,1.34),at(-.211,1.34),at(-.15,1.735),at(.481,1.735)],(side,0,0))
        cube('DoorHandle_'+suffix,(side*.974,-.083,1.19),(.023,.123,.035),'Bumper','Trim',.005)
        rod('MirrorStem_'+suffix,(side*.94,.79,1.35),(side*1.045,.79,1.39),.018,'Mirror','Trim')
        cube('Mirror_'+suffix,(side*1.055,.78,1.42),(.145,.145,.16),'Mirror','Trim',.018)
        cube('MirrorGlass_'+suffix,(side*1.055,.702,1.42),(.119,.009,.13),'Glass','Trim',.009)
    glazing('Cabin_RearGlass',[(-.735,-.259,1.37),(.735,-.259,1.37),(.716,-.209,1.73),(-.716,-.209,1.73)],(0,-1,0))
    cube('Cabin_RearGlassDivider',(0,-.251,1.55),(.015,.012,.33),'Mirror','Cabin',.003)


def bed():
    side_panel('BedSide',-2.26,-.315,REAR_AXLE,top=1.31)
    cube('BedFloor',(0,-1.285,.867),(1.74,1.91,.055),'Paint','Panel',.005)
    cube('BedLiner_Floor',(0,-1.285,.901),(1.68,1.855,.018),'Liner','Trim',.006)
    for side in (-1,1):
        s='L' if side<0 else 'R'
        cube('BedLiner_Side_'+s,(side*.865,-1.287,1.098),(.027,1.89,.40),'Liner','Trim',.005)
        cube('BedRail_'+s,(side*.922,-1.287,1.309),(.105,1.947,.042),'Paint','Panel',.012)
        # Intruding wheel tubs are visible from above; not a flat empty box.
        verts=[]; faces=[]
        for i in range(19):
            y=REAR_AXLE-.48+.96*i/18
            t=(y-REAR_AXLE)/.48
            h=.90+.19*max(0,1-t*t)**.65
            for x,z in ((side*.865,.904),(side*.59,.904),(side*.59,h-.035),(side*.65,h),(side*.865,h)):
                verts.append((x,y,z))
        for i in range(18):
            for j in range(4):
                a=i*5+j; faces.append((a,a+5,a+6,a+1))
        faces.extend([(0,1,2,3,4),(94,93,92,91,90)])
        mesh('BedLiner_WheelTub_'+s,verts,faces,'Liner','Trim',True)
    cube('BedFrontWall',(0,-.334,1.09),(1.84,.035,.435),'Paint','Body',.006)
    cube('BedLiner_FrontWall',(0,-.357,1.09),(1.70,.018,.397),'Liner','Trim',.005)
    cube('Tailgate',(0,-2.237,.93),(1.89,.047,.76),'Paint','Body',.012)
    cube('BedLiner_Tailgate',(0,-2.205,1.094),(1.70,.014,.391),'Liner','Trim',.005)
    cube('BedRail_Rear',(0,-2.237,1.31),(1.89,.055,.036),'Paint','Panel',.007)
    ribs=[]
    for i in range(23):
        x=-.79+1.58*i/22
        ribs.append(cube('BedLiner_Rib',(x,-1.28,.915),(.014,1.805,.010),'Liner','Trim',.004))
    join(ribs,'BedLiner_Ribs')
    cube('TailgateHandle',(0,-2.265,1.16),(.19,.019,.067),'Mirror','Trim',.009)
    cube('Bumper_Rear',(0,-2.268,.624),(1.99,.164,.20),'Bumper','Bumper',.025)
    cube('RearPlate',(0,-2.354,.626),(.39,.012,.143),'Mirror','Trim',.005)
    for side in (-1,1):
        for z,h,mat in ((1.18,.10,'Red'),(1.075,.09,'Amber'),(.988,.065,'Headlight'),(.913,.065,'Red')):
            cube('Taillight_'+str(side),(side*.893,-2.271,z),(.10,.022,h),mat,'Light',.005)


def wheels():
    for label,y,side in [('FL',FRONT_AXLE,-1),('FR',FRONT_AXLE,1),('RL',REAR_AXLE,-1),('RR',REAR_AXLE,1)]:
        c=(side*.876,y,WHEEL_Z)
        parts=[]
        profile=[(-.132,.235),(-.155,.30),(-.148,.367),(-.116,.400),(-.080,.408),
                 (.080,.408),(.116,.400),(.148,.367),(.155,.30),(.132,.235),(-.132,.235)]
        parts.append(lathe('Wheel_'+label,profile,c,'Rubber'))
        for sign in (-1,1):
            px=c[0]+sign*.146
            parts.append(lathe('WheelSteel',[(0,.0),(0,.145),(-sign*.025,.205),(-sign*.008,.237),
                                           (sign*.012,.239),(sign*.012,.219),(sign*.002,.213)],
                               (px,y,WHEEL_Z),'Steel'))
        outer=c[0]+side*.161
        parts.append(lathe('WheelHub',[(0,.0),(0,.074),(side*.018,.072),(side*.025,.0)],
                           (outer,y,WHEEL_Z),'Bumper'))
        for i in range(10):
            a=i*math.tau/10
            p=(c[0]+side*.145,y+.157*math.cos(a),WHEEL_Z+.157*math.sin(a))
            vv=[(p[0],p[1]+.027*math.cos(k*math.tau/16),p[2]+.027*math.sin(k*math.tau/16)) for k in range(16)]
            parts.append(mesh('WheelSteelHole',vv,[tuple(range(16))],'Rubber','Wheel'))
        for i in range(36):
            a=i*math.tau/36
            for row in (-1,1):
                aa=a+row*.025
                ob=cube('WheelTread',(c[0]+row*.069,y+.410*math.cos(aa),WHEEL_Z+.410*math.sin(aa)),
                        (.117,.059,.021),'Rubber','Wheel',0)
                ob.rotation_euler.x=aa-math.pi/2
                parts.append(ob)
        ob=join(parts,'Wheel_'+label,c)
        ob['engine_role_proposal']='Wheel'


def underbody():
    for side in (-1,1):
        cube('Underbody_Frame_'+str(side),(side*.55,-.07,.455),(.10,4.20,.115),'Chassis','Underbody',.012)
    for label,y in (('Front',FRONT_AXLE),('Rear',REAR_AXLE)):
        rod('Suspension_Axle_'+label,(-.84,y,.42),(.84,y,.42),.047)
        bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=1, location=(0,y,.42))
        ob=bpy.context.object; ob.name='Suspension_Differential_'+label
        ob.scale=(.15,.115,.12); ob.parent=ROOT; ob.data.materials.append(MATS['Chassis'])
        ob['engine_role_proposal']='Underbody'; PARTS.append(ob)
        for side in (-1,1):
            rod('Suspension_Shock_'+label+'_'+str(side),(side*.68,y,.43),(side*.60,y+.12,.86),.025)
    cube('Underbody_CabFloor',(0,.37,.523),(1.64,1.21,.06),'Chassis','Underbody',.006)


def finalize():
    for ob in list(PARTS):
        bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
        bpy.context.view_layer.objects.active=ob
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        for mod in list(ob.modifiers):
            bpy.ops.object.modifier_apply(modifier=mod.name)
        bm=bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(ob.data); bm.free()
        uv=ob.data.uv_layers.new(name='UVMap')
        band=ob.data.uv_layers.new(name='StripeProjection')
        for poly in ob.data.polygons:
            axes=sorted(range(3),key=lambda k:abs(poly.normal[k]))[:2]
            for idx in poly.loop_indices:
                v=ob.data.vertices[ob.data.loops[idx].vertex_index].co
                world=ob.matrix_world@v
                uv.data[idx].uv=(v[axes[0]],v[axes[1]])
                # UV mask attached to rest-space vertices, continuous across cab/bed.
                band.data[idx].uv=(.5+world.x/2.4,world.z/2)


def bounds(objects):
    pts=[ob.matrix_world@v.co for ob in objects for v in ob.data.vertices]
    return {'min':[min(p[k] for p in pts) for k in range(3)],'max':[max(p[k] for p in pts) for k in range(3)]}


def inspection():
    bpy.context.view_layer.update()
    all_bounds=bounds(PARTS)
    wheel_bounds={o.name:bounds([o]) for o in PARTS if o.name in ('Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR')}
    centers={n:[(b['min'][i]+b['max'][i])*.5 for i in range(3)] for n,b in wheel_bounds.items()}
    cab_bounds=bounds([o for o in PARTS if o.name.startswith('Cabin')])
    roof=bounds([bpy.data.objects['Cabin_Roof']])
    bed_bounds=bounds([bpy.data.objects['BedSide_L'],bpy.data.objects['BedSide_R']])
    hood=bounds([bpy.data.objects['Body_Hood']])
    liner_floor=bounds([bpy.data.objects['BedLiner_Floor']])
    grille=bounds([bpy.data.objects['Grille_Frame']])
    body=bounds([o for o in PARTS if o.name.startswith(('FrontFender','Door_','BedSide_'))])
    result={'stage':'A','units':'metres','orientation':'+X right, +Y forward, +Z up',
            'bounds':all_bounds,'wheel_bounds':wheel_bounds,'wheel_centers':centers,
            'cabin_bounds':cab_bounds,'roof_bounds':roof,'bed_bounds':bed_bounds,'hood_bounds':hood,'body_bounds':body,
            'liner_floor_bounds':liner_floor,'grille_bounds':grille,
            'rocker_bottom_m':body['min'][2],'lower_paint_boundary_m':PAINT_LINE,
            'front_differential_bounds':bounds([bpy.data.objects['Suspension_Differential_Front']]),
            'meshes':len(PARTS),'vertices':sum(len(o.data.vertices) for o in PARTS),
            'components':[{'name':o.name,'proposed_role':o['engine_role_proposal'],'vertices':len(o.data.vertices)} for o in PARTS],
            'production_asset_qa':'NOT RUN - Stage A topology only','unity_import':'NOT RUN',
            'destruction_tests':'NOT RUN','production_manifest_written':False,'runtime_sockets_written':False}
    return result


def aim(ob, target):
    ob.rotation_euler=(Vector(target)-ob.location).to_track_quat('-Z','Y').to_euler()


def studio():
    scene=bpy.context.scene
    scene.render.engine='CYCLES'
    scene.cycles.samples=24
    scene.cycles.use_denoising=True
    scene.cycles.seed=0
    scene.render.resolution_x=1440; scene.render.resolution_y=960; scene.render.resolution_percentage=100
    scene.view_settings.view_transform='AgX'
    world=bpy.data.worlds.new('Studio'); world.use_nodes=True; scene.world=world
    world.node_tree.nodes.get('Background').inputs[0].default_value=(.42,.45,.44,1)
    world.node_tree.nodes.get('Background').inputs[1].default_value=.55
    floor_mat=material('StudioFloor',(.43,.45,.44),0,.70)
    bpy.ops.mesh.primitive_plane_add(size=200, location=(0,0,-.005))
    bpy.context.object.name='StudioFloor'; bpy.context.object.data.materials.append(floor_mat)
    for name,pos,power,size in [('Key',(-3,4,7),1450,5),('Fill',(5,2,4),1050,4),('Rim',(2,-5,6),1750,4)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
        ob=bpy.data.objects.new(name,data); scene.collection.objects.link(ob); ob.location=pos; aim(ob,(0,0,.7))
    data=bpy.data.cameras.new('Camera'); ob=bpy.data.objects.new('Camera',data)
    scene.collection.objects.link(ob); scene.camera=ob
    return ob


VIEWS={
    'front34':((-5.5,8.5,2.4),(0,0,.92),5.9),
    'side':((-10,0,1.14),(0,0,.94),5.45),
    'rear34':((5.7,-8.2,2.6),(0,-.10,.92),5.9),
    'top':((0,0,12),(0,0,0),5.5),
    'front':((0,10,1.03),(0,0,1.03),3.2),
    'rear':((0,-10,1.03),(0,0,1.03),3.2),
    'nose_top':((0,5.6,5.3),(0,1.45,1.02),3.4),
    'bed':((3.8,-5.5,5.8),(0,-1.25,1.0),3.3),
    'clearance':((-5.8,5.5,1.5),(0,0,.78),5.8),
}


def render(camera,out,views):
    from bpy_extras.object_utils import world_to_camera_view
    records={}
    for name in views:
        pos,target,scale=VIEWS[name]
        camera.location=pos; aim(camera,target)
        camera.data.type='ORTHO'; camera.data.ortho_scale=scale
        if name in ('front34','rear34'):
            camera.data.type='PERSP'; camera.data.lens=70
        if name=='top':
            camera.rotation_euler=(0,0,-math.pi/2)
        bpy.context.view_layer.update()
        bpy.context.scene.render.filepath=str(out/('stage_a_'+name+'.png'))
        bpy.ops.render.render(write_still=True)
        records[name]={'position':pos,'target':target,'projection':camera.data.type,'scale':scale,'lens_mm':camera.data.lens}
        projected=[world_to_camera_view(bpy.context.scene,camera,ob.matrix_world@v.co)
                   for ob in PARTS for v in ob.data.vertices]
        records[name]['vehicle_pixel_bounds']=[min(p.x for p in projected)*1440,
             (1-max(p.y for p in projected))*960,max(p.x for p in projected)*1440,
             (1-min(p.y for p in projected))*960]
        print('STAGE_A_RENDERED',name,flush=True)
    (out/'cameras.json').write_text(json.dumps(records,indent=2)+'\n')


def main():
    global ROOT
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path)
    parser.add_argument('--render',action='store_true'); parser.add_argument('--views',nargs='+',choices=VIEWS)
    args=parser.parse_args(argv); root=args.root.resolve(); out=root/'Previews/CompactPickup/StageA'
    out.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    ROOT=bpy.data.objects.new('CompactPickup',None); bpy.context.scene.collection.objects.link(ROOT)
    ROOT['development_stage']='A - initial blockout; approval pending'
    ROOT['design']='1988-1992 compact regular-cab 4x4, standard open bed'
    materials(); front_body(); cab(); bed(); wheels(); underbody()
    finalize(); result=inspection()
    (out/'geometry_measurements.json').write_text(json.dumps(result,indent=2)+'\n')
    camera=studio(); camera.location=VIEWS['front34'][0]; aim(camera,VIEWS['front34'][1])
    camera.data.type='PERSP'; camera.data.lens=70
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(root/'Blender/CompactPickup_StageA.blend'))
    print('STAGE_A_GEOMETRY',result['meshes'],result['vertices'],flush=True)
    if args.render:
        render(camera,out,args.views or list(VIEWS))


if __name__=='__main__':
    main()
