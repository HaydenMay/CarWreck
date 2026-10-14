"""Stage A compact hatchback, in metres: +X right, +Y forward, +Z up.

Run: blender -b --factory-startup --python build_compact_hatchback.py -- <root>
     [--render] [--view front34|side|rear34|comparison] [--samples 48]

Creates only the new Blender source, four previews and an inspection report.
No FBX, Unity assets or damage-system integration are produced before visual approval.
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


def mix(a, b, t):
    return a + (b - a) * t


def curve(keys, y):
    for (a, va), (b, vb) in zip(keys, keys[1:]):
        if y <= b:
            return mix(va, vb, max(0, min(1, (y - a) / (b - a))))
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
    paint = material('Paint', (0.86, 0.065, 0.035), .18, .30, .5)
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
    color.inputs[1].default_value = (*linear((.86, .065, .035)), 1)
    color.inputs[2].default_value = (*linear((.94, .93, .88)), 1)
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


def bevel(obj, width=.012, segments=3):
    mod = obj.modifiers.new('Soft manufactured edges', 'BEVEL')
    mod.width, mod.segments = width, segments
    mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True
    return obj


def box(name, loc, size, mat, radius=.012, rot=None):
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
    flare = .065 * max(math.exp(-((y - axle) / .47) ** 4) for axle in AXLES)
    return top, width + flare


def arch_z(y):
    z = BOTTOM
    for axle in AXLES:
        d = abs(y - axle)
        if d < ARCH:
            z = max(z, RADIUS + math.sqrt(ARCH * ARCH - d * d))
    return z


def body_half(y):
    top, width = body_params(y)
    shoulder = width - .05
    lower = arch_z(y)
    points = [(shoulder * k / 10, top + .024 * (1 - (k / 10) ** 2)) for k in range(11)]
    for k in range(1,5):
        a = math.pi / 2 * (1 - k / 4)
        points.append((shoulder + .05 * math.cos(a), top - .05 + .05 * math.sin(a)))
    for k in range(1,9):
        t = k / 8
        z = mix(top - .05, lower, t)
        points.append((width - .035 * t ** 2, z))
    # The contour folds into the wheel well, then reaches the inner chassis.
    points.extend([(mix(points[-1][0], .575, k / 3), lower) for k in range(1,4)])
    points.extend([(.575, mix(lower, BOTTOM, k / 3)) for k in range(1,4)])
    points.extend([(.575 * (1-k/5), BOTTOM) for k in range(1,6)])
    return points


def body():
    ys = {-1.87 + 3.74 * k / 64 for k in range(65)}
    for axle in AXLES:
        ys.update(axle + ARCH * math.cos(math.pi * k / 18) for k in range(19))
        ys.update((axle - ARCH - .015, axle + ARCH + .015))
    stations = []
    for y in sorted(ys):
        if not stations or y - stations[-1] > .009:
            stations.append(y)
    rings = []
    for y in stations:
        half = body_half(y)
        contour = half + [(-x,z) for x,z in reversed(half[1:-1])]
        rings.append([(x,y,z) for x,z in contour])
    mb = Mesh()
    ids = mb.grid(rings, closed=True)
    mb.face(ids[0][::-1])
    mb.face(ids[-1])
    ob = mb.object('Body')
    ob['stage'] = 'A: coarse loft, not deformation qualified'
    for poly in ob.data.polygons:
        if poly.normal.z < -.5:
            if MATS['Underbody'].name not in ob.data.materials:
                ob.data.materials.append(MATS['Underbody'])
            poly.material_index = list(ob.data.materials).index(MATS['Underbody'])
    # Red arch lips share the body's contour and expose the tire opening.
    for side in (-1,1):
        for ai,axle in enumerate(AXLES):
            lip = Mesh()
            rows = []
            for k in range(33):
                a = math.pi * k / 32
                row=[]
                for r in (ARCH, ARCH+.025, ARCH+.055):
                    y = axle + r*math.cos(a)
                    _,w = body_params(y)
                    row.append((side*(w-.025+.009*(r-ARCH)/.055), y, RADIUS+r*math.sin(a)))
                rows.append(row)
            lip.grid(rows)
            lip.object('FenderLip_' + ('L' if side<0 else 'R') + str(ai))


def cabin_point(t, u, side):
    z = mix(.863,1.385,u)
    front, rear = mix(.65,.10,u), mix(-1.72,-1.25,u)
    width = mix(.763,.668,u)
    return (side*width, mix(front,rear,t), z)


def cabin():
    mb = Mesh()
    ts = [0,.028,.052,.17,.30,.43,.525,.555,.590,.70,.81,.925,.953,1]
    us = [0,.075,.115,.145,.30,.49,.66,.81,.87,.91,.955,1]
    for side in (-1,1):
        ids = [[mb.vertex(cabin_point(t,u,side)) for t in ts] for u in us]
        for i in range(len(us)-1):
            for j in range(len(ts)-1):
                t, u = (ts[j]+ts[j+1])/2, (us[i]+us[i+1])/2
                glass = .115<u<.91 and (.052<t<.525 or .59<t<.925)
                trim = .075<u<.955 and (.028<t<.555 or .555<t<.953)
                mb.face((ids[i][j],ids[i][j+1],ids[i+1][j+1],ids[i+1][j]), 'Glass' if glass else 'Trim' if trim else 'Paint')
    xs = [-1,-.94,-.90,-.60,-.30,0,.30,.60,.90,.94,1]
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
    for j in range(17):
        y=mix(-1.25,.10,j/16)
        rows.append([(x*.668,y,1.385+.027*(1-x*x)*math.sin(math.pi*j/16)) for x in xs])
    mb.grid(rows)
    bevel(mb.object('Cabin', smooth=False), .009, 3)
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
           'comparison':((-7.4,8.7,4.1),(0,0,.74),8.05)}
    loc,target,scale=views[view]
    reference=None
    if view=='comparison':
        reference,_=compare_muscle(root)
    camera.data.type='PERSP' if view=='side' else 'ORTHO'
    camera.data.lens=100
    camera.location=loc; camera.data.ortho_scale=scale; aim(camera,target)
    scene.render.filepath=str(out/(view+'.png'))
    bpy.ops.render.render(write_still=True)
    if reference:
        for obj in list(reference.objects):
            bpy.data.objects.remove(obj,do_unlink=True)
        bpy.data.collections.remove(reference)
        CAR.location.x=0


def inspection():
    meshes=[o for o in COLL.objects if o.type=='MESH']
    corners=[o.matrix_world@Vector(c) for o in meshes for c in o.bound_box]
    extent=[max(p[i] for p in corners)-min(p[i] for p in corners) for i in range(3)]
    result={'stage':'A','orientation':'Blender +Y forward; +Z up','units':'metres',
            'dimensions_xyz_m':extent,'wheelbase_m':2.32,'body_loft_station_spacing_m':.0584,
            'meshes':len(meshes),'base_vertices':sum(len(o.data.vertices) for o in meshes),
            'body_vertices':len(bpy.data.objects['Body'].data.vertices),
            'wheels':[o.name for o in meshes if o.name.startswith('Wheel_')],
            'wheel_pivots_m':{o.name:list(o.location) for o in meshes if o.name.startswith('Wheel_')},
            'deformation_tested':False,'fbx_exported':False}
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
        uv=obj.data.uv_layers['UVMap']
        for poly in obj.data.polygons:
            points=[uv.data[i].uv for i in poly.loop_indices]
            area=abs(sum(a.x*b.y-b.x*a.y for a,b in zip(points,points[1:]+points[:1])))/2
            if area<1e-12:
                bad_uvs+=1
    result['collapsed_uv_faces']=bad_uvs
    assert bad_uvs==0,'Collapsed primary UVs'
    result['body_quad_fraction']=sum(len(p.vertices)==4 for p in bpy.data.objects['Body'].data.polygons)/len(bpy.data.objects['Body'].data.polygons)
    return result


def main():
    global CAR,COLL
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser()
    parser.add_argument('root',nargs='?',default=str(Path(__file__).resolve().parents[2]))
    parser.add_argument('--render',action='store_true')
    parser.add_argument('--view',choices=['front34','side','rear34','comparison'])
    parser.add_argument('--samples',type=int,default=48)
    args=parser.parse_args(argv)
    root=Path(args.root).resolve(); out=root/'Previews'/'CompactHatchback'; out.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    COLL=bpy.data.collections.new('CompactHatchback'); bpy.context.scene.collection.children.link(COLL)
    CAR=bpy.data.objects.new('CompactHatchback',None); COLL.objects.link(CAR)
    CAR['development_stage']='A — visual approval required before production'
    materials(); body(); cabin(); wheels(); details()
    # Primitives and curves receive the same top-down UV convention as the loft.
    for obj in COLL.objects:
        if obj.type=='MESH' and not obj.data.uv_layers:
            obj.data.uv_layers.new(name='UVMap')
    camera=studio(args.samples)
    camera.location=(-4.8,6.4,3.1); aim(camera,(0,0,.69))
    bpy.context.view_layer.update()
    report=inspection()
    (out/'stage_a_inspection.json').write_text(json.dumps(report,indent=2)+'\n')
    bpy.context.scene['stage_a_report']=json.dumps(report)
    bpy.context.scene.render.filepath=str(out/'front34.png')
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(root/'Blender'/'CompactHatchback.blend'))
    print('STAGE_A_INSPECTION',json.dumps(report),flush=True)
    if args.render:
        for view in ([args.view] if args.view else ['front34','side','rear34','comparison']):
            render_view(view,camera,out,root)
            print('RENDERED',view,flush=True)


if __name__=='__main__':
    main()
