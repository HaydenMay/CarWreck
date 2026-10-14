"""Stage B refinement of the approved hatchback, in metres: +X right, +Y forward, +Z up.

Run: blender -b --factory-startup --python build_compact_hatchback.py -- <root>
     [--render] [--view NAME] [--samples 48] [--primary R G B] [--secondary R G B]

Produces editable Blender source, selected-only FBX, a grayscale stripe mask and previews.
Refines the Stage A profiles and component layout; never writes the original muscle car.
"""
import argparse
import json
import math
import os
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector

MATS = {}
CAR = None
COLL = None
AXLES = (1.16, -1.16)
RADIUS = 0.325
ARCH = 0.375
TRACK = 0.745
BOTTOM = 0.215
PRIMARY = (.86,.065,.035)
SECONDARY = (.94,.93,.88)
SPACING = .035


def mix(a, b, t):
    return a + (b - a) * t


def curve(keys, y):
    def slope(i):
        a,b=keys[max(0,i-1)],keys[min(len(keys)-1,i+1)]
        return (b[1]-a[1])/(b[0]-a[0])
    for i,((a,va),(b,vb)) in enumerate(zip(keys,keys[1:])):
        if y <= b:
            t=max(0,min(1,(y-a)/(b-a))); h=b-a
            return (2*t**3-3*t*t+1)*va+(t**3-2*t*t+t)*slope(i)*h+(-2*t**3+3*t*t)*vb+(t**3-t*t)*slope(i+1)*h
    return keys[-1][1]


def linear(rgb):
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb)


def material(name, rgb, metal=0, rough=0.4, coat=0, glow=0):
    mat = bpy.data.materials.new('Hatch_' + name)
    mat.use_nodes = True
    bs = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    color = (*linear(rgb), 1)
    bs.inputs['Base Color'].default_value = color
    bs.inputs['Metallic'].default_value = metal
    bs.inputs['Roughness'].default_value = rough
    bs.inputs['Coat Weight'].default_value = coat
    if glow:
        bs.inputs['Emission Color'].default_value = color
        bs.inputs['Emission Strength'].default_value = glow
    mat.diffuse_color = color
    MATS[name] = mat
    return mat


def materials():
    paint = material('Paint', PRIMARY, .18, .28, .55)
    material('Rubber', (.075, .083, .094), 0, .75)
    material('Trim', (.065, .074, .084), .12, .36)
    material('Glass', (.11, .15, .18), .28, .16, .65)
    material('Rim', (.12, .145, .17), .8, .29)
    material('Silver', (.72, .75, .77), .85, .24)
    material('Underbody', (.10, .11, .12), .3, .65)
    material('Headlight', (.97, .91, .74), .05, .21, .25, .6)
    material('Indicator', (.98, .35, .015), .05, .26, .25, .35)
    material('Taillight', (.69, .035, .019), .1, .23, .35, .25)
    material('Reverse', (.84, .87, .87), .05, .3)
    # As on the muscle car, stripes are a shader/UV treatment, never floating meshes.
    nodes, links = paint.node_tree.nodes, paint.node_tree.links
    tex = nodes.new('ShaderNodeUVMap')
    tex.uv_map = 'StripeProjection'
    separate = nodes.new('ShaderNodeSeparateXYZ')
    links.new(tex.outputs['UV'], separate.inputs[0])
    def math_node(op, a, b=None):
        node = nodes.new('ShaderNodeMath')
        node.operation = op
        if hasattr(a, 'is_output'):
            links.new(a, node.inputs[0])
        else:
            node.inputs[0].default_value = a
        if b is not None:
            if hasattr(b, 'is_output'):
                links.new(b, node.inputs[1])
            else:
                node.inputs[1].default_value = b
        return node.outputs[0]
    x = math_node('ABSOLUTE', math_node('SUBTRACT', separate.outputs['X'], .5))
    mask = math_node('MULTIPLY', math_node('GREATER_THAN', x, .022), math_node('LESS_THAN', x, .145))
    color = nodes.new('ShaderNodeMixRGB')
    color.name='Primary and Stripe Colors'; color.label='Primary (Color1) / Stripe (Color2)'
    color.inputs[1].default_value = (*linear(PRIMARY), 1)
    color.inputs[2].default_value = (*linear(SECONDARY), 1)
    links.new(mask, color.inputs[0])
    bs = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
    links.new(color.outputs[0], bs.inputs['Base Color'])


def assign_uvs(data, transform=None):
    """Usable face UVs plus a separate, continuous car-space stripe projection.

    This is a blockout unwrap, not a packed production texture atlas. Dominant
    face projection avoids collapsed UVs on vertical panels and wheel faces.
    """
    uv = data.uv_layers.get('UVMap') or data.uv_layers.new(name='UVMap')
    stripe = data.uv_layers.get('StripeProjection') or data.uv_layers.new(name='StripeProjection')
    for poly in data.polygons:
        normal = transform.to_3x3() @ poly.normal if transform is not None else poly.normal
        axis = max(range(3), key=lambda i: abs(normal[i]))
        for li in poly.loop_indices:
            p = data.vertices[data.loops[li].vertex_index].co
            if transform is not None:
                p = transform @ p
            stripe.data[li].uv = (.5 + p.x / 2, .5 + p.y / 4)
            uv.data[li].uv = ((.5 + p.y / 4, p.z / 2) if axis == 0 else
                              (.5 + p.x / 2, p.z / 2) if axis == 1 else
                              (.5 + p.x / 2, .5 + p.y / 4))
    data.uv_layers.active = uv


def parent_keep_world(child, parent):
    matrix = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = matrix


class Mesh:
    def __init__(self):
        self.verts, self.faces, self.mats = [], [], []

    def vertex(self, p):
        self.verts.append(tuple(p))
        return len(self.verts) - 1

    def face(self, ids, mat='Paint'):
        self.faces.append(tuple(ids))
        self.mats.append(mat)

    def grid(self, rings, mat='Paint', closed=False):
        ids = [[self.vertex(p) for p in r] for r in rings]
        for a, b in zip(ids, ids[1:]):
            for j in range(len(a) if closed else len(a) - 1):
                k = (j + 1) % len(a)
                self.face((a[j], a[k], b[k], b[j]), mat)
        return ids

    def object(self, name, smooth=True):
        data = bpy.data.meshes.new(name)
        data.from_pydata(self.verts, [], self.faces)
        data.update()
        names = list(dict.fromkeys(self.mats))
        for name_mat in names:
            data.materials.append(MATS[name_mat])
        for poly, mat in zip(data.polygons, self.mats):
            poly.material_index = names.index(mat)
            poly.use_smooth = smooth
        bm = bmesh.new()
        bm.from_mesh(data)
        bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(data)
        bm.free()
        assign_uvs(data)
        obj = bpy.data.objects.new(name, data)
        COLL.objects.link(obj)
        obj.parent = CAR
        return obj


def fill_caps(obj):
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='DESELECT')
    bpy.context.tool_settings.mesh_select_mode=(False,True,False)
    bm=bmesh.from_edit_mesh(obj.data)
    edges=[e for e in bm.edges if e.is_boundary]
    while edges:
        group={edges[0]}; vertices=set(edges[0].verts); changed=True
        while changed:
            changed=False
            for e in edges:
                if e not in group and any(v in vertices for v in e.verts):
                    group.add(e); vertices.update(e.verts); changed=True
        bpy.ops.mesh.select_all(action='DESELECT')
        for e in group: e.select_set(True)
        print('CAP_LOOP',obj.name,len(group),tuple(sum(v.co[a] for v in vertices)/len(vertices) for a in range(3)),flush=True)
        bmesh.update_edit_mesh(obj.data)
        bpy.ops.mesh.fill_grid(span=max(1,len(group)//4),use_interp_simple=True)
        edges=[e for e in edges if e not in group]
    bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT'); obj.data.update(); assign_uvs(obj.data)
    bm=bmesh.new(); bm.from_mesh(obj.data); print('CAP_REMAINING',obj.name,sum(e.is_boundary for e in bm.edges),flush=True); bm.free()


def bevel(obj, width=.012, segments=3):
    mod = obj.modifiers.new('Soft manufactured edges', 'BEVEL')
    mod.width, mod.segments = min(width,min(obj.dimensions)*.4), segments
    mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True
    return obj


def box(name, loc, size, mat, radius=.012, rot=None):
    deform = name.startswith(('Bumper','FrontValance','RearValance','FrontSplitter','Sill','Spoiler','HeadlightHousing','TaillightHousing'))
    if deform:
        half=Vector(size)/2; r=min(radius,*half)
        counts=[max(2,math.ceil(s/.035)) for s in size]; mb=Mesh(); lookup={}
        def vertex(i,j,k):
            key=(i,j,k)
            if key not in lookup:
                p=Vector(tuple(-half[a]+size[a]*v/counts[a] for a,v in enumerate(key)))
                q=Vector(tuple(max(-half[a]+r,min(half[a]-r,p[a])) for a in range(3)))
                d=p-q
                if d.length>1e-9: p=q+d.normalized()*r
                lookup[key]=mb.vertex(p)
            return lookup[key]
        for axis in range(3):
            a,b=[v for v in range(3) if v!=axis]
            for end in (0,counts[axis]):
                for i in range(counts[a]):
                    for j in range(counts[b]):
                        face=[]
                        for u,v in ((i,j),(i+1,j),(i+1,j+1),(i,j+1)):
                            key=[0,0,0]; key[axis]=end; key[a]=u; key[b]=v; face.append(vertex(*key))
                        mb.face(face,mat)
        obj=mb.object(name); obj.location=loc
        if rot: obj.rotation_euler=rot
        bpy.context.view_layer.update(); assign_uvs(obj.data,obj.matrix_world)
        obj['deformable']=True
        return obj
    bpy.ops.mesh.primitive_cube_add(size=1, location=loc)
    obj = bpy.context.object
    obj.name = name
    obj.dimensions = size
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for coll in list(obj.users_collection):
        coll.objects.unlink(obj)
    COLL.objects.link(obj)
    obj.parent = CAR
    obj.data.materials.append(MATS[mat])
    if rot:
        obj.rotation_euler = rot
    # Cube defaults repeat UVs on every face. Project in car space so only
    # central bodywork receives stripes, while mirrors and sills remain red.
    obj.update_tag()
    bpy.context.view_layer.update()
    assign_uvs(obj.data, obj.matrix_world)
    if radius:
        bevel(obj, radius)
    return obj


def pipe(name, points, radius, mat):
    data = bpy.data.curves.new(name, 'CURVE')
    data.dimensions = '3D'
    data.bevel_depth, data.bevel_resolution = radius, 2
    spline = data.splines.new('POLY')
    spline.points.add(len(points) - 1)
    for point, co in zip(spline.points, points):
        point.co = (*co, 1)
    obj = bpy.data.objects.new(name, data)
    COLL.objects.link(obj)
    obj.parent = CAR
    data.materials.append(MATS[mat])
    return obj


def body_params(y):
    top = curve([(-1.87,.865),(-1.5,.88),(.48,.88),(.78,.87),(1.3,.825),(1.87,.765)],y)
    width = curve([(-1.87,.765),(-1.60,.795),(-.55,.793),(.50,.793),(1.55,.795),(1.87,.757)], y)
    flare = max(.045*math.exp(-((y-axle)/.52)**2)+.016*math.exp(-((y-axle)/.33)**4) for axle in AXLES)
    return top, width + flare


def arch_z(y):
    z = BOTTOM
    for axle in AXLES:
        d = abs(y - axle)
        if d < ARCH:
            z = max(z, RADIUS + math.sqrt(ARCH * ARCH - d * d))
        elif d < ARCH+.045:
            t=(ARCH+.045-d)/.045
            z=max(z,mix(BOTTOM,RADIUS,t*t*(3-2*t)))
    return z


def body_half(y):
    top, width = body_params(y)
    shoulder = width - .105
    lower = arch_z(y)
    points = [(shoulder*k/20,top+.046*(1-(k/20)**2)) for k in range(21)]
    for k in range(1,6):
        a=math.pi/2*(1-k/5)
        points.append((shoulder+.065*math.cos(a),top-.065+.065*math.sin(a)))
    for k in range(1,19):
        z=mix(top-.065,lower,k/18)
        if z>=.575:
            t=max(0,min(1,(top-.065-z)/max(.02,top-.065-.575)))
            x=mix(width-.04,width,1-(1-t)**2)
        else:
            t=max(0,min(1,(.575-z)/(.575-BOTTOM)))
            x=width-.068*t**1.8
        x+=.004*math.exp(-((z-.575)/.026)**2)
        points.append((x,z))
    # The contour folds into the wheel well, then reaches the inner chassis.
    points.extend([(mix(points[-1][0],.575,k/8),lower+.008*math.sin(math.pi*k/8)) for k in range(1,9)])
    points.extend([(.575,mix(lower,BOTTOM,k/14)) for k in range(1,15)])
    points.extend([(.575*(1-k/17),BOTTOM) for k in range(1,18)])
    return points


def body():
    ys = {-1.87+3.74*k/108 for k in range(109)}
    for axle in AXLES:
        ys.update(axle+ARCH*math.cos(math.pi*k/36) for k in range(37))
        ys.update((axle-ARCH-.025,axle+ARCH+.025))
    stations = []
    for y in sorted(ys):
        if not stations or y-stations[-1]>.015:
            stations.append(y)
    # Preserve the established loft, inserting stations only where arch slope
    # would otherwise create a long edge at the vertical ends of an opening.
    refined=[stations[0]]
    def append_interval(a,b,depth=0):
        pa,pb=body_half(a),body_half(b)
        distance=max(Vector((x1-x0,b-a,z1-z0)).length for (x0,z0),(x1,z1) in zip(pa,pb))
        if distance>.048 and depth<12:
            mid=(a+b)/2; append_interval(a,mid,depth+1); append_interval(mid,b,depth+1)
        else: refined.append(b)
    for a,b in zip(stations,stations[1:]): append_interval(a,b)
    stations=refined
    rings = []
    for y in stations:
        half = body_half(y)
        contour = half + [(-x,z) for x,z in reversed(half[1:-1])]
        rings.append([(x,y,z) for x,z in contour])
    mb = Mesh()
    ids = mb.grid(rings, closed=True)
    ob = mb.object('Body')
    fill_caps(ob)
    bevel(ob,.018,3)
    ob['stage'] = 'B: refined continuous loft; destruction integration untested'
    ob['deformable']=True
    for poly in ob.data.polygons:
        if poly.normal.z < -.5:
            if MATS['Underbody'].name not in ob.data.materials:
                ob.data.materials.append(MATS['Underbody'])
            poly.material_index = list(ob.data.materials).index(MATS['Underbody'])
    # Arch lips now belong to the same shell instead of overlapping strips.


def cabin_point(t, u, side):
    z = mix(.863,1.385,u)
    front, rear = mix(.65,.10,u), mix(-1.72,-1.25,u)
    width = mix(.763,.668,u)
    return (side*(width+.013*math.sin(math.pi*t)*math.sin(math.pi*u)),mix(front,rear,t),z)


def cabin():
    mb = Mesh()
    ts = [0,.028,.052,.17,.30,.43,.525,.555,.590,.70,.81,.925,.953,1]
    us = [0,.075,.115,.145,.30,.49,.66,.81,.87,.91,.955,1]
    def dense(keys,spacing):
        result=[keys[0]]
        for a,b in zip(keys,keys[1:]):
            n=max(1,math.ceil((b-a)/spacing))
            result.extend(mix(a,b,k/n) for k in range(1,n+1))
        return result
    ts=dense(ts,.035/2.37); us=dense(us,.035/.522)
    for side in (-1,1):
        ids = [[mb.vertex(cabin_point(t,u,side)) for t in ts] for u in us]
        for i in range(len(us)-1):
            for j in range(len(ts)-1):
                t, u = (ts[j]+ts[j+1])/2, (us[i]+us[i+1])/2
                glass = .115<u<.91 and (.052<t<.525 or .59<t<.925)
                trim = .075<u<.955 and (.028<t<.555 or .555<t<.953)
                mb.face((ids[i][j],ids[i][j+1],ids[i+1][j+1],ids[i+1][j]), 'Glass' if glass else 'Trim' if trim else 'Paint')
    xs=dense([-1,-.94,-.90,-.60,-.30,0,.30,.60,.90,.94,1],.035/.763)
    for front in (True,False):
        rows=[]
        for u in us:
            x,y,z=cabin_point(0 if front else 1,u,1)
            rows.append([mb.vertex((s*x,y,z)) for s in xs])
        for i in range(len(us)-1):
            for j in range(len(xs)-1):
                u=(us[i]+us[i+1])/2; x=(xs[j]+xs[j+1])/2
                glass=.115<u<.91 and abs(x)<.90
                trim=.075<u<.955 and abs(x)<.94
                mb.face((rows[i][j],rows[i][j+1],rows[i+1][j+1],rows[i+1][j]), 'Glass' if glass else 'Trim' if trim else 'Paint')
    # Slight crown, with a joined roof edge rather than a floating roof slab.
    rows=[]
    for t in reversed(ts):
        y=mix(.10,-1.25,t)
        rows.append([(x*.668,y,1.385+.040*(1-x*x)*math.sin(math.pi*t)) for x in xs])
    mb.grid(rows)
    ob=mb.object('Cabin',smooth=True); fill_caps(ob); bevel(ob,.018,3); ob['deformable']=True
    # Thin roof gutters and hatch spoiler are part of the Stage A silhouette.
    for side in (-1,1):
        pipe('RoofGutter_' + str(side),[(side*.671,-1.25,1.388),(side*.671,.10,1.388)],.009,'Paint')
    box('Spoiler', (0,-1.32,1.398),(1.45,.19,.05),'Paint',.022)
    box('Spoiler_Underside',(0,-1.342,1.366),(1.31,.12,.027),'Trim',.008)


def lathe(mb, profile, centre, mat, n=48):
    rows=[]
    for x,r in profile:
        rows.append([(centre[0]+x,centre[1]+r*math.cos(2*math.pi*k/n),centre[2]+r*math.sin(2*math.pi*k/n)) for k in range(n)])
    mb.grid(rows,mat,closed=True)


def wheels():
    for side in (-1,1):
        for ai,y in enumerate(AXLES):
            centre=(side*TRACK,y,RADIUS)
            mb=Mesh()
            profile=[(-.112,.208),(-.132,.235),(-.139,.275),(-.130,.307),(-.106,RADIUS),(-.07,RADIUS),(.07,RADIUS),(.106,RADIUS),(.13,.307),(.139,.275),(.132,.235),(.112,.208),(-.112,.208)]
            lathe(mb,profile,centre,'Rubber',64)
            outer=side*.13
            lathe(mb,[(outer,.192),(outer+side*.012,.211),(outer+side*.011,.229),(outer,.232),(outer-side*.024,.224),(outer,.192)],centre,'Silver')
            # Closed annular shells give normal recalculation an unambiguous
            # outside on both sides of the car, unlike a single open disc.
            lathe(mb,[(outer-side*.046,.05),(outer-side*.046,.205),(outer-side*.034,.205),(outer-side*.034,.05),(outer-side*.046,.05)],centre,'Rim')
            lathe(mb,[(outer-side*.022,0),(outer-side*.022,.045),(outer+side*.007,.045),(outer+side*.01,.025),(outer+side*.01,0)],centre,'Silver',32)
            wheel=mb.object('Wheel_' + ('F' if ai==0 else 'R') + ('L' if side<0 else 'R'))
            for vertex in wheel.data.vertices:
                vertex.co -= Vector(centre)
            wheel.location = centre
            bpy.context.view_layer.update()
            # Low-cost five-spoke performance wheels; distinct from the muscle rims.
            for k in range(5):
                a=2*math.pi*k/5
                spoke=box('Spoke', (side*(TRACK+.132),y+.12*math.cos(a),RADIUS+.12*math.sin(a)),(.027,.145,.038),'Rim',.009,(a,0,0))
                parent_keep_world(spoke,wheel)
            wheel['rigid_component']=True
            # A subtle sidewall ring catches light without expensive tread meshes.
            lathe_mb=Mesh()
            lathe(lathe_mb,[(side*.137,.263),(side*.139,.267),(side*.137,.271),(side*.137,.263)],centre,'Trim',64)
            ring=lathe_mb.object('Sidewall'); parent_keep_world(ring,wheel)
            # One rigid mesh per wheel avoids FBX space-baking inconsistencies
            # on nested rotated objects, and follows the muscle-car hierarchy.
            for child in list(wheel.children):
                bpy.context.view_layer.objects.active=child
                for mod in list(child.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
            bpy.ops.object.select_all(action='DESELECT')
            for obj in [wheel]+list(wheel.children): obj.select_set(True)
            bpy.context.view_layer.objects.active=wheel
            bpy.ops.object.join()


def details():
    box('Bumper_Front',(0,1.897,.462),(1.79,.20,.148),'Trim',.025)
    box('Bumper_Rear',(0,-1.888,.465),(1.79,.20,.148),'Trim',.025)
    box('FrontValance',(0,1.858,.305),(1.67,.14,.17),'Paint',.017)
    box('FrontSplitter',(0,1.923,.211),(1.79,.25,.042),'Trim',.015)
    box('RearValance',(0,-1.857,.296),(1.69,.15,.16),'Paint',.02)
    box('Grille',(0,1.897,.676),(1.16,.055,.222),'Trim',.015)
    for i in range(5):
        box('GrilleSlat',(0,1.929,.587+i*.041),(1.12,.021,.017),'Underbody',.004)
    box('LowerIntake',(0,1.945,.311),(.94,.027,.145),'Trim',.004)
    for i in range(4):
        box('LowerSlat',(0,1.963,.260+i*.03),(.89,.011,.010),'Underbody',.002)
    for side in (-1,1):
        box('HeadlightHousing',(side*.675,1.893,.677),(.303,.082,.241),'Trim',.022)
        box('Headlight',(side*.675,1.940,.682),(.252,.027,.190),'Headlight',.015)
        box('FrontIndicator',(side*.641,2.001,.467),(.245,.015,.054),'Indicator',.006)
        box('FogHousing',(side*.642,1.952,.305),(.256,.044,.133),'Trim',.010)
        box('FogLight',(side*.642,1.978,.312),(.181,.020,.079),'Headlight',.008)
        box('TaillightHousing',(side*.652,-1.899,.695),(.30,.048,.278),'Trim',.012)
        box('Taillight',(side*.652,-1.930,.735),(.260,.025,.141),'Taillight',.007)
        box('RearIndicator',(side*.706,-1.935,.618),(.148,.025,.072),'Indicator',.006)
        box('ReverseLight',(side*.574,-1.935,.618),(.099,.025,.072),'Reverse',.006)
        box('DoorHandle',(side*.803,-.395,.781),(.025,.142,.046),'Trim',.007)
        box('MirrorStem',(side*.797,.45,.937),(.12,.095,.061),'Trim',.011)
        box('Mirror',(side*.866,.48,.987),(.16,.18,.115),'Paint',.022)
        box('MirrorGlass',(side*.869,.381,.987),(.12,.011,.076),'Glass',.010)
        box('Sill',(side*.787,-.035,.251),(.068,1.47,.089),'Paint',.012)
        # Side rubbing strips follow the same flare/arch profile as the metal.
        for start,end in ((-1.81,-1.50),(-.81,.81),(1.50,1.81)):
            pts=[]
            for k in range(16):
                y=mix(start,end,k/15)
                _,w=body_params(y)
                pts.append((side*(w-.008),y,.503))
            pipe('SideMoulding',pts,.020,'Trim')
        pipe('DoorSeam',[(side*.789,.585,.817),(side*.802,.535,.58),(side*.785,.48,.326),(side*.785,-.54,.326),(side*.802,-.58,.58),(side*.790,-.58,.817)],.0023,'Trim')
        box('SideIndicator',(side*.857,.77,.740),(.016,.071,.033),'Indicator',.005)
    box('LicenseRecess',(0,-1.910,.652),(.55,.028,.180),'Trim',.008)
    box('BlankPlate',(0,-1.932,.652),(.43,.014,.114),'Reverse',.005)
    box('HatchLock',(0,-1.946,.807),(.031,.016,.035),'Trim',.008)
    # The hatch outline and single rear wiper are simple silhouette cues.
    pipe('HatchSeam',[(-.475,-1.874,.815),(-.475,-1.90,.568),(.475,-1.90,.568),(.475,-1.874,.815)],.0025,'Trim')
    pipe('RearWiper',[(0,-1.685,.939),(.37,-1.583,1.062)],.009,'Trim')
    pipe('FrontWiper_L',[(-.66,.604,.915),(-.15,.610,.910)],.009,'Trim')
    pipe('FrontWiper_R',[(.02,.610,.910),(.61,.604,.915)],.009,'Trim')
    box('Underbody',(0,0,.228),(1.14,2.85,.059),'Underbody',.025)
    mb=Mesh()
    # Exhaust axis is along Y, unlike the wheel lathe axis.
    rows=[]
    for y,r in ((-1.99,.061),(-1.75,.061),(-1.75,.048),(-1.995,.048)):
        rows.append([(-.48+r*math.cos(2*math.pi*k/32),y,.255+r*math.sin(2*math.pi*k/32)) for k in range(32)])
    mb.grid(rows,'Silver',closed=True)
    mb.object('Exhaust')


def aim(obj, at):
    obj.rotation_euler=(Vector(at)-obj.location).to_track_quat('-Z','Y').to_euler()


def studio(samples):
    global COLL,CAR
    scene=bpy.context.scene
    scene.unit_settings.system='METRIC'
    scene.unit_settings.scale_length=1
    scene.render.engine='CYCLES'
    scene.cycles.samples=samples
    scene.cycles.use_denoising=True
    scene.render.resolution_x,scene.render.resolution_y=1400,900
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format='PNG'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    world=bpy.data.worlds.new('Neutral Studio')
    world.use_nodes=True; scene.world=world
    bg=next(n for n in world.node_tree.nodes if n.type=='BACKGROUND')
    bg.inputs['Color'].default_value=(.24,.26,.29,1)
    bg.inputs['Strength'].default_value=.35
    bpy.ops.mesh.primitive_plane_add(size=200)
    floor=bpy.context.object; floor.name='StudioFloor'
    floor_mat=material('StudioFloor',(.31,.33,.35),0,.55)
    floor.data.materials.append(floor_mat)
    # A curved studio cove removes the hard floor/world horizon in low views.
    vertices,faces=[],[]
    levels=[(16+4*math.sin(math.pi*k/24),4*(1-math.cos(math.pi*k/24))) for k in range(13)]
    levels.append((20,15))
    for r,z in levels:
        vertices.extend((r*math.cos(2*math.pi*k/128),r*math.sin(2*math.pi*k/128),z) for k in range(128))
    for i in range(len(levels)-1):
        for j in range(128):
            k=(j+1)%128
            faces.append((i*128+j,(i+1)*128+j,(i+1)*128+k,i*128+k))
    mesh=bpy.data.meshes.new('StudioCove'); mesh.from_pydata(vertices,[],faces); mesh.update()
    for poly in mesh.polygons:
        poly.use_smooth=True
    cove=bpy.data.objects.new('StudioCove',mesh); scene.collection.objects.link(cove)
    mesh.materials.append(floor_mat)
    for name,loc,power,size in [('Key',(-3,4,6),750,5),('Fill',(4,2,3.5),550,4),('Rim',(1,-4,5),850,3)]:
        light=bpy.data.lights.new(name,'AREA'); light.energy=power; light.shape='DISK'; light.size=size
        obj=bpy.data.objects.new(name,light); scene.collection.objects.link(obj); obj.location=loc; aim(obj,(0,0,.6))
    camera=bpy.data.cameras.new('PreviewCamera'); camera.type='ORTHO'; camera.ortho_scale=5.5
    obj=bpy.data.objects.new('PreviewCamera',camera); scene.collection.objects.link(obj); scene.camera=obj
    camera.clip_end=250
    return obj


def compare_muscle(root):
    path=root/'Blender'/'MuscleCar.blend'
    with bpy.data.libraries.load(str(path),link=False) as (source,target):
        target.objects=[n for n in source.objects if n=='MuscleCar' or n in ('Body','Cabin','Bumper_Front','Bumper_Rear','Grille','Headlights','HoodScoop','Mirrors','SidePipes','Spoiler','Taillights','Trim','Wheel_FL','Wheel_FR','Wheel_RL','Wheel_RR')]
    reference=bpy.data.collections.new('Reference_MuscleCar_READONLY'); bpy.context.scene.collection.children.link(reference)
    for obj in target.objects:
        if obj:
            reference.objects.link(obj)
    parent=next(o for o in reference.objects if o.name.startswith('MuscleCar'))
    parent.location.x=1.37
    CAR.location.x=-1.38
    # Resolve the original car's stripe texture without rewriting its source file.
    for img in bpy.data.images:
        if 'CarPaint_Stripes' in img.name:
            img.filepath=str(root/'Assets'/'DamageLab'/'Art'/'Textures'/'CarPaint_Stripes.png')
            img.reload()
    return reference,parent


def render_view(view,camera,out,root):
    scene=bpy.context.scene
    views={'front34':((-4.8,6.4,3.1),(0,0,.69),5.35),
           'side':((-13,0,1.15),(0,0,.72),4.68),
           'rear34':((4.6,-6.4,2.9),(0,0,.72),5.35),
           'front':((0,8,1.15),(0,0,.72),2.65),
           'rear':((0,-8,1.15),(0,0,.72),2.65),
           'top':((0,0,10),(0,0,.0),4.7),
           'wireframe':((-4.8,6.4,3.1),(0,0,.69),5.35),
           'color_blue':((-4.8,6.4,3.1),(0,0,.69),5.35),
           'color_ivory':((-4.8,6.4,3.1),(0,0,.69),5.35),
           'comparison':((-7.4,8.7,4.1),(0,0,.74),8.05)}
    loc,target,scale=views[view]
    reference=None
    if view=='comparison':
        reference,_=compare_muscle(root)
    camera.data.type='PERSP' if view=='side' else 'ORTHO'
    camera.data.lens=100
    camera.location=loc; camera.data.ortho_scale=scale; aim(camera,target)
    if view=='top': camera.rotation_euler.rotate_axis('Z',math.pi/2)
    paint=MATS['Paint']; color=next(n for n in paint.node_tree.nodes if n.type=='MIX_RGB')
    original=[tuple(color.inputs[i].default_value) for i in (1,2)]
    if view in ('color_blue','color_ivory'):
        palette=(((.055,.24,.62),(.97,.62,.07)) if view=='color_blue' else ((.91,.87,.72),(.09,.13,.19)))
        for i,rgb in zip((1,2),palette): color.inputs[i].default_value=(*linear(rgb),1)
    wire=None
    if view=='wireframe':
        wire_material=material('Wire',(.015,.025,.034),0,.85)
        wire=bpy.data.collections.new('TopologyPreview'); scene.collection.children.link(wire)
        for name in ('Body','Cabin'):
            original_obj=bpy.data.objects[name]
            obj=original_obj.copy(); obj.data=original_obj.data.copy(); wire.objects.link(obj)
            obj.parent=CAR; obj.data.materials.clear(); obj.data.materials.append(wire_material)
            for p in obj.data.polygons: p.material_index=0
            mod=obj.modifiers.new('Topology edges','WIREFRAME'); mod.thickness=.0015; mod.offset=1; mod.use_replace=True
    scene.render.filepath=str(out/(view+'.png'))
    bpy.ops.render.render(write_still=True)
    for i,rgba in zip((1,2),original): color.inputs[i].default_value=rgba
    if wire:
        for obj in list(wire.objects): bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(wire)
    if reference:
        for obj in list(reference.objects):
            bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(reference)
        CAR.location.x=0


def finalize():
    from mathutils.bvhtree import BVHTree
    body=bpy.data.objects['Body']
    surface=BVHTree.FromPolygons([v.co for v in body.data.vertices],[list(p.vertices) for p in body.data.polygons])
    for obj in COLL.objects:
        if obj.type=='CURVE' and obj.name.startswith(('DoorSeam','SideMoulding','HatchSeam')):
            for spline in obj.data.splines:
                for point in spline.points:
                    p=Vector(point.co[:3])
                    if obj.name.startswith('HatchSeam'):
                        hit=surface.ray_cast(Vector((p.x,-2.3,p.z)),Vector((0,1,0)))[0]
                        if hit: p.y=hit.y-obj.data.bevel_depth*.5
                    else:
                        side=-1 if p.x<0 else 1
                        hit=surface.ray_cast(Vector((side*1.5,p.y,p.z)),Vector((-side,0,0)))[0]
                        if hit: p.x=hit.x+side*obj.data.bevel_depth*.5
                    point.co=(*p,1)
    for obj in list(COLL.objects):
        if obj.type not in ('MESH','CURVE'): continue
        bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
        bpy.context.view_layer.objects.active=obj
        if obj.type=='CURVE': bpy.ops.object.convert(target='MESH')
        for mod in list(obj.modifiers): bpy.ops.object.modifier_apply(modifier=mod.name)
        bm=bmesh.new(); bm.from_mesh(obj.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
        bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.000001)
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        bm.to_mesh(obj.data); bm.free(); obj.data.update()
        assign_uvs(obj.data,obj.matrix_world)
        obj.data.update()


def inspection():
    meshes=[o for o in COLL.objects if o.type=='MESH']
    corners=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
    extent=[max(p[i] for p in corners)-min(p[i] for p in corners) for i in range(3)]
    result={'stage':'B','orientation':'Blender +Y forward; +Z up','units':'metres',
            'dimensions_xyz_m':extent,'wheelbase_m':2.32,'body_loft_station_spacing_m':3.74/108,
            'meshes':len(meshes),'base_vertices':sum(len(o.data.vertices) for o in meshes),
            'body_vertices':len(bpy.data.objects['Body'].data.vertices),
            'wheels':[o.name for o in meshes if o.name.startswith('Wheel_')],
            'wheel_pivots_m':{o.name:list(o.location) for o in meshes if o.name.startswith('Wheel_')},
            'deformation_tested':False,'fbx_exported':True}
    assert len(result['wheels'])==4,'Expected four separate wheels'
    assert 3.8<extent[1]<4.1 and 1.35<extent[2]<1.5,'Unexpected vehicle proportions'
    assert all(o.data.uv_layers for o in meshes),'Missing UV layer'
    result['all_meshes_have_uvs']=True
    for obj in meshes:
        if obj.name.startswith('Wheel_'):
            side = -1 if obj.location.x < 0 else 1
            outward = [p for p in obj.data.polygons
                       if obj.data.materials[p.material_index] == MATS['Rim']
                       and abs(p.center.x - side * .096) < .0001]
            assert outward and all(p.normal.x * side > .99 for p in outward), 'Inward wheel backing normals'
    result['wheel_backing_normals_outward']=True
    bad_uvs=0
    for obj in meshes:
        object_bad=0
        uv=obj.data.uv_layers['UVMap']
        for poly in obj.data.polygons:
            points=[uv.data[i].uv for i in poly.loop_indices]
            area=abs(sum(a.x*b.y-b.x*a.y for a,b in zip(points,points[1:]+points[:1])))/2
            if area<1e-12:
                bad_uvs+=1
                object_bad+=1
                if object_bad<3: print('BAD_UV_FACE',obj.name,poly.index,tuple(poly.normal),poly.area,flush=True)
        if object_bad: print('BAD_UV_TOTAL',obj.name,object_bad,flush=True)
    result['collapsed_uv_faces']=bad_uvs
    assert bad_uvs==0,'Collapsed primary UVs'
    result['body_quad_fraction']=sum(len(p.vertices)==4 for p in bpy.data.objects['Body'].data.polygons)/len(bpy.data.objects['Body'].data.polygons)
    result['deformable_vertices']=sum(len(o.data.vertices) for o in meshes if o.get('deformable'))
    result['surface_metrics']={}
    for name in ('Body','Cabin'):
        obj=bpy.data.objects[name]; bm=bmesh.new(); bm.from_mesh(obj.data)
        lengths=sorted(e.calc_length() for e in bm.edges)
        stats={'vertices':len(bm.verts),'faces':len(bm.faces),'boundary_edges':sum(e.is_boundary for e in bm.edges),
               'nonmanifold_edges':sum(not e.is_manifold for e in bm.edges),'zero_area_faces':sum(f.calc_area()<1e-12 for f in bm.faces),
               'ngons':sum(len(f.verts)>4 for f in bm.faces),
               'edge_length_p10_m':lengths[len(lengths)//10], 'edge_length_median_m':lengths[len(lengths)//2],
               'edge_length_p90_m':lengths[int(len(lengths)*.9)], 'max_edge_length_m':lengths[-1]}
        assert stats['nonmanifold_edges']==0 and stats['zero_area_faces']==0,(name,stats)
        result['surface_metrics'][name]=stats; bm.free()
    return result


def export(root):
    folder=root/'Assets'/'Vehicles'/'CompactHatchback'; models=folder/'Models'; textures=folder/'Textures'
    models.mkdir(parents=True,exist_ok=True); textures.mkdir(parents=True,exist_ok=True)
    image=bpy.data.images.new('Hatch_StripeMask',width=1024,height=4)
    image.colorspace_settings.name='Non-Color'
    pixels=[]
    for i in range(1024):
        value=1. if .022<abs((i+.5)/1024-.5)<.145 else 0.
        pixels.extend((value,value,value,1))
    image.pixels=pixels*4; image.filepath_raw=str(textures/'StripeMask.png'); image.file_format='PNG'; image.save()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in COLL.objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=CAR
    bpy.ops.export_scene.fbx(filepath=str(models/'CompactHatchback.fbx'),use_selection=True,object_types={'EMPTY','MESH'},
        apply_unit_scale=True,apply_scale_options='FBX_SCALE_ALL',bake_space_transform=True,axis_forward='-Z',axis_up='Y',
        mesh_smooth_type='FACE',use_mesh_modifiers=True,add_leaf_bones=False,path_mode='AUTO',embed_textures=False,
        bake_anim=False,use_custom_props=True)
    palette={}
    for name,mat in MATS.items():
        if name in ('StudioFloor','Wire'): continue
        bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        palette[mat.name]={'color_linear':list(mat.diffuse_color),'metallic':bs.inputs['Metallic'].default_value,
                           'smoothness':1-bs.inputs['Roughness'].default_value,
                           'emission_linear':list(bs.inputs['Emission Color'].default_value),
                           'emission_strength':bs.inputs['Emission Strength'].default_value}
    palette['Hatch_Paint']['primary_srgb']=list(PRIMARY); palette['Hatch_Paint']['secondary_srgb']=list(SECONDARY)
    (folder/'palette.json').write_text(json.dumps({'materials':[dict(name=k,**v) for k,v in palette.items()]},indent=2)+'\n')


def main():
    global CAR,COLL,PRIMARY,SECONDARY
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser()
    parser.add_argument('root',nargs='?',default=str(Path(__file__).resolve().parents[2]))
    parser.add_argument('--render',action='store_true')
    parser.add_argument('--view',choices=['front34','side','rear34','front','rear','top','wireframe','comparison','color_blue','color_ivory'])
    parser.add_argument('--samples',type=int,default=48)
    parser.add_argument('--primary',type=float,nargs=3)
    parser.add_argument('--secondary',type=float,nargs=3)
    args=parser.parse_args(argv)
    root=Path(args.root).resolve(); out=root/'Previews'/'CompactHatchback'/'StageB'; out.mkdir(parents=True,exist_ok=True)
    # Preserve colors saved by the user when regenerating geometry.
    existing=root/'Blender'/'CompactHatchback.blend'
    if existing.exists():
        with bpy.data.libraries.load(str(existing),link=False) as (src,dst):
            dst.materials=[n for n in src.materials if n=='Hatch_Paint']
        if dst.materials:
            mix_node=next((n for n in dst.materials[0].node_tree.nodes if n.type=='MIX_RGB'),None)
            def srgb(rgb): return tuple(12.92*c if c<=.0031308 else 1.055*c**(1/2.4)-.055 for c in rgb[:3])
            if mix_node: PRIMARY,SECONDARY=srgb(mix_node.inputs[1].default_value),srgb(mix_node.inputs[2].default_value)
    PRIMARY=tuple(args.primary) if args.primary else PRIMARY
    SECONDARY=tuple(args.secondary) if args.secondary else SECONDARY
    bpy.ops.wm.read_factory_settings(use_empty=True)
    COLL=bpy.data.collections.new('CompactHatchback'); bpy.context.scene.collection.children.link(COLL)
    CAR=bpy.data.objects.new('CompactHatchback',None); COLL.objects.link(CAR)
    CAR['development_stage']='B — asset production; destruction integration untested'
    materials(); body(); cabin(); wheels(); details()
    # Primitives and curves receive the same top-down UV convention as the loft.
    for obj in COLL.objects:
        if obj.type=='MESH' and not obj.data.uv_layers:
            obj.data.uv_layers.new(name='UVMap')
    bpy.context.view_layer.update(); finalize()
    camera=studio(args.samples)
    camera.location=(-4.8,6.4,3.1); aim(camera,(0,0,.69))
    bpy.context.view_layer.update()
    report=inspection()
    export(root)
    (out/'asset_inspection.json').write_text(json.dumps(report,indent=2)+'\n')
    bpy.context.scene['stage_b_report']=json.dumps(report)
    bpy.context.scene.render.filepath=str(out/'front34.png')
    bpy.context.preferences.filepaths.save_version=0
    blend_path=root/'Blender'/'CompactHatchback.blend'
    pending=root/'Blender'/'CompactHatchback.pending.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(pending))
    os.replace(pending,blend_path)
    print('STAGE_B_INSPECTION',json.dumps(report),flush=True)
    if args.render:
        for view in ([args.view] if args.view else ['front34','rear34','side','front','rear','top','wireframe','comparison','color_blue','color_ivory']):
            render_view(view,camera,out,root)
            print('RENDERED',view,flush=True)


if __name__=='__main__':
    main()
