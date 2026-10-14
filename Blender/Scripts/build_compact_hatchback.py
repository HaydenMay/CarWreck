"""Stage C sculpting of the approved hatchback, in metres: +X right, +Y forward, +Z up.

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
FRONT_FACE = 1.925
FRONT_RECESS = .054
REAR_FACE = -1.90
FRONT_SOCKETS=[(-.491,.491,.568,.741),(-.727,-.501,.557,.744),(.501,.727,.557,.744),
               (-.505,.505,.228,.382),(-.690,-.523,.244,.366),(.523,.690,.244,.366)]
REAR_SOCKETS=[(-.791,-.493,.548,.836),(.493,.791,.548,.836),(-.27,.27,.570,.724)]


def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)))
    return t*t*(3-2*t)


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


def recess_cap(obj,plane,sign,rectangles,depth):
    """Fit a constrained cap patch around closed, gridded component sockets.

    Retains every outer boundary vertex of the loft. Constraint triangulation
    avoids cutting slivers through an unrelated cap grid; socket backs are quads.
    """
    from mathutils.geometry import delaunay_2d_cdt
    bm=bmesh.new(); bm.from_mesh(obj.data)
    faces=[f for f in bm.faces if all(abs(v.co.y-plane)<.00005 for v in f.verts)]
    assert faces,('Missing cap',obj.name,plane)
    bmesh.ops.delete(bm,geom=faces,context='FACES_ONLY')
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    # FACES_ONLY can leave old diagonals between surviving boundary vertices.
    # They are wire edges, not part of the replacement cap or deforming shell.
    bmesh.ops.delete(bm,geom=[e for e in bm.edges if e.is_wire],context='EDGES')
    pending={e for e in bm.edges if e.is_boundary and all(abs(v.co.y-plane)<.00005 for v in e.verts)}
    loops=[]
    while pending:
        edge=next(iter(pending)); first=edge.verts[0]; current=first; loop=[]
        while True:
            loop.append(current)
            edge=next(e for e in current.link_edges if e in pending)
            pending.remove(edge); current=edge.other_vert(current)
            if current==first: break
        loops.append(loop)
    def signed_area(loop):
        return sum(a.co.x*b.co.z-b.co.x*a.co.z for a,b in zip(loop,loop[1:]+loop[:1]))/2
    loops.sort(key=lambda loop:abs(signed_area(loop)),reverse=True)
    outer=[Vector((v.co.x,v.co.z)) for v in loops[0]]
    existing_holes=[[Vector((v.co.x,v.co.z)) for v in loop] for loop in loops[1:]]
    def contains(p,polygon):
        inside=False
        for a,b in zip(polygon,polygon[1:]+polygon[:1]):
            if (a.y>p.y)!=(b.y>p.y) and p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x: inside=not inside
        return inside
    def in_domain(p): return contains(p,outer) and not any(contains(p,h) for h in existing_holes)
    def in_socket(p): return any(a<p.x<b and c<p.y<d for a,b,c,d in rectangles)
    inputs=[]; owners=[]; constraints=[]
    def add_loop(verts):
        start=len(inputs)
        for v in verts: inputs.append(Vector((v.co.x,v.co.z))); owners.append(v)
        constraints.extend((start+k,start+(k+1)%len(verts)) for k in range(len(verts)))
    for loop in loops: add_loop(loop)
    socket_loops=[]
    for xmin,xmax,zmin,zmax in rectangles:
        nx=max(2,math.ceil((xmax-xmin)/SPACING)); nz=max(2,math.ceil((zmax-zmin)/SPACING))
        keys=[(k,0) for k in range(nx)]+[(nx,k) for k in range(nz)]+[(nx-k,nz) for k in range(nx)]+[(0,nz-k) for k in range(nz)]
        front=[]
        for i,j in keys:
            x=mix(xmin,xmax,i/nx); z=mix(zmin,zmax,j/nz)
            assert in_domain(Vector((x,z))),('Socket outside body',obj.name,x,z)
            front.append(bm.verts.new((x,plane,z)))
        add_loop(front); socket_loops.append((front,keys,nx,nz,(xmin,xmax,zmin,zmax)))
    segments=[(inputs[a],inputs[b]) for a,b in constraints]
    def separation(p):
        def distance(a,b):
            direction=b-a; t=max(0,min(1,(p-a).dot(direction)/direction.length_squared))
            return (p-(a+t*direction)).length
        return min(distance(a,b) for a,b in segments)
    xmin,xmax=min(p.x for p in outer),max(p.x for p in outer)
    zmin,zmax=min(p.y for p in outer),max(p.y for p in outer)
    nx,nz=math.ceil((xmax-xmin)/SPACING),math.ceil((zmax-zmin)/SPACING)
    for i in range(1,nx):
        for j in range(1,nz):
            p=Vector((mix(xmin,xmax,i/nx),mix(zmin,zmax,j/nz)))
            if in_domain(p) and not in_socket(p) and separation(p)>.012:
                inputs.append(p); owners.append(bm.verts.new((p.x,plane,p.y)))
    coords,_,triangles,originals,_,_=delaunay_2d_cdt(inputs,constraints,[],0,.0000001,True)
    mapped=[owners[ids[0]] if ids else bm.verts.new((p.x,plane,p.y)) for p,ids in zip(coords,originals)]
    for triangle in triangles:
        centre=sum((coords[i] for i in triangle),Vector((0,0)))/len(triangle)
        if in_domain(centre) and not in_socket(centre): bm.faces.new([mapped[i] for i in triangle])
    for front,keys,nx,nz,bounds in socket_loops:
        xmin,xmax,zmin,zmax=bounds
        # Model the lip explicitly. A global bevel on the triangulated cap can
        # clamp to microscopic corner faces; this 3 mm chamfer stays uniform.
        lip=.003
        back=[[bm.verts.new((mix(xmin+lip,xmax-lip,i/nx),plane-sign*depth,mix(zmin+lip,zmax-lip,j/nz))) for j in range(nz+1)] for i in range(nx+1)]
        middle=[bm.verts.new((mix(xmin+lip,xmax-lip,i/nx),plane-sign*lip,mix(zmin+lip,zmax-lip,j/nz))) for i,j in keys]
        rear=[back[i][j] for i,j in keys]
        for a,b in ((front,middle),(middle,rear)):
            for k in range(len(a)):
                q=(k+1)%len(a); bm.faces.new((a[k],a[q],b[q],b[k]))
        for i in range(nx):
            for j in range(nz): bm.faces.new((back[i][j],back[i+1][j],back[i+1][j+1],back[i][j+1]))
    bmesh.ops.delete(bm,geom=[v for v in bm.verts if not v.link_faces],context='VERTS')
    bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces)); bm.to_mesh(obj.data); bm.free(); obj.data.update()
    assign_uvs(obj.data)


def body_bevel_weights(obj):
    bm=bmesh.new(); bm.from_mesh(obj.data)
    weights=bm.edges.layers.float.new('bevel_weight_edge')
    for edge in bm.edges:
        sharp=edge.is_manifold and edge.calc_face_angle()>.5236
        weight=1.0 if sharp else 0
        if sharp:
            for plane,sign,rectangles,depth in ((FRONT_FACE,1,FRONT_SOCKETS,FRONT_RECESS),(REAR_FACE,-1,REAR_SOCKETS,.045)):
                for a,b,c,d in rectangles:
                    points=[v.co for v in edge.verts]
                    in_box=all(a-.0001<=p.x<=b+.0001 and c-.0001<=p.z<=d+.0001 and -.0001<=sign*(plane-p.y)<=depth+.0001 for p in points)
                    if in_box: weight=0
        edge[weights]=weight
    bm.to_mesh(obj.data); bm.free()
    mod=next(m for m in obj.modifiers if m.type=='BEVEL')
    options=[i.identifier for i in mod.bl_rna.properties['limit_method'].enum_items]
    mod.limit_method=next(i for i in options if i=='WEIGHT')


def housing(name,x,z,outer,inner,front,back,mat='Trim'):
    """A closed rectangular bezel with an open centre, not a solid cover slab."""
    hw,hh=outer[0]/2,outer[1]/2; iw,ih=inner[0]/2,inner[1]/2
    nx,nz=max(2,math.ceil(outer[0]/SPACING)),max(2,math.ceil(outer[1]/SPACING))
    def loop(w,h,y):
        coords=[(-w+2*w*k/nx,-h) for k in range(nx)]
        coords += [(w,-h+2*h*k/nz) for k in range(nz)]
        coords += [(w-2*w*k/nx,h) for k in range(nx)]
        coords += [(-w,h-2*h*k/nz) for k in range(nz)]
        return [(x+a,y,z+b) for a,b in coords]
    mb=Mesh(); rows=[loop(hw,hh,back),loop(hw,hh,front),loop(iw,ih,front),loop(iw,ih,back),loop(hw,hh,back)]
    mb.grid(rows,mat,closed=True)
    obj=mb.object(name); obj['deformable']=True
    bevel(obj,.0035,2)
    return obj


def sculpt_bumper(obj,sign):
    # Curved end returns, a shallow horizontal relief and a bevelled lower face.
    for v in obj.data.vertices:
        side=abs(v.co.x)/.895; height=v.co.z/.074
        front=max(0,sign*v.co.y/.1)
        relief=.005*math.exp(-((height+.20)/.13)**2)
        lower=.018*smooth(.15,.95,-height)
        end=.048*smooth(.78,1,side)
        v.co.y+=sign*front*(.015*(1-height*height)-relief-lower-end)


def bevel(obj, width=.012, segments=3):
    mod = obj.modifiers.new('Soft manufactured edges', 'BEVEL')
    mod.width, mod.segments = min(width,min(obj.dimensions)*.4), segments
    mod = obj.modifiers.new('Weighted corner normals', 'WEIGHTED_NORMAL')
    mod.keep_sharp = True
    return obj


def box(name, loc, size, mat, radius=.012, rot=None):
    deform = name.startswith(('Bumper','FrontValance','RearValance','FrontSplitter','Sill','Spoiler','HeadlightHousing','TaillightHousing','FrontIndicator'))
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
    tail=smooth(1.65,-REAR_FACE,-y)
    # Retain the original taper and descending hood line. Fascia dimensions
    # must fit this section; never widen or raise the nose to fit the lamps.
    return top, width+flare+.045*tail


def stamped_side(x,y,z):
    # A radial roll forms the arch in the metal itself. The same loft vertices
    # continue over its shoulder and into the inner wheelwell, without overlays.
    bulge=0
    for axle,amount in zip(AXLES,(.067,.073)):
        distance=math.hypot(y-axle,z-RADIUS)
        roll=math.exp(-((distance-(ARCH+.044))/.093)**2)
        roll*=1-smooth(ARCH+.15,ARCH+.24,distance)
        roll*=smooth(BOTTOM,BOTTOM+.12,z)
        bulge=max(bulge,amount*roll)
    door=(1-smooth(.34,.83,abs(y)))*.014*math.exp(-((z-.63)/.18)**2)
    quarter=.012*math.exp(-((y-AXLES[1])/.62)**2)*math.exp(-((z-.77)/.18)**2)
    return x+bulge+door+quarter


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
    end=max(smooth(1.66,FRONT_FACE,y),smooth(1.65,-REAR_FACE,-y))
    radius=mix(.065,.030,end)
    tumble=mix(.040,.010,end)
    shoulder=width-tumble-radius
    lower = arch_z(y)
    hood=math.sin(math.pi*max(0,min(1,(y-.63)/(FRONT_FACE-.63)))) if y>.63 else 0
    crown=.046+.018*hood
    points=[]
    for k in range(21):
        u=k/20; x=shoulder*u
        pressed=.008*hood*math.exp(-((u-.68)/.13)**2)*(1-u)
        points.append((x,top+crown*(1-u*u)+pressed))
    for k in range(1,6):
        a=math.pi/2*(1-k/5)
        x=shoulder+radius*math.cos(a); z=top-radius+radius*math.sin(a)
        points.append((stamped_side(x,y,z),z))
    for k in range(1,19):
        z=mix(top-radius,lower,k/18)
        if z>=.575:
            t=max(0,min(1,(top-radius-z)/max(.02,top-radius-.575)))
            x=mix(width-tumble,width,1-(1-t)**2)
        else:
            t=max(0,min(1,(.575-z)/(.575-BOTTOM)))
            x=width-.068*t**1.8
        x+=.004*math.exp(-((z-.575)/.026)**2)
        points.append((stamped_side(x,y,z),z))
    # The contour folds into the wheel well, then reaches the inner chassis.
    points.extend([(mix(points[-1][0],.575,k/8),lower+.008*math.sin(math.pi*k/8)) for k in range(1,9)])
    points.extend([(.575,mix(lower,BOTTOM,k/14)) for k in range(1,15)])
    points.extend([(.575*(1-k/17),BOTTOM) for k in range(1,18)])
    return points


def body():
    ys = {-1.87+3.74*k/108 for k in range(109)}
    ys.update((REAR_FACE,1.90,FRONT_FACE))
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
    # Choose the inner well / underside by its loft band, not by the normal.
    # Sculpted lower fenders can face down while still being exterior paint.
    ring_size=len(rings[0])
    for i in range(len(mb.faces)):
        if 43<=i%ring_size<=ring_size-44: mb.mats[i]='Underbody'
    ob = mb.object('Body')
    fill_caps(ob)
    recess_cap(ob,FRONT_FACE,1,FRONT_SOCKETS,FRONT_RECESS)
    recess_cap(ob,REAR_FACE,-1,REAR_SOCKETS,.045)
    bevel(ob,.018,3)
    body_bevel_weights(ob)
    ob['stage'] = 'C: integrated sculpted arches and recessed fascia; destruction integration untested'
    ob['deformable']=True
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
    spoiler=box('Spoiler', (0,-1.32,1.398),(1.45,.19,.05),'Paint',.022)
    for v in spoiler.data.vertices: v.co.z+=.009*(-v.co.y/.095)-.008
    box('Spoiler_Underside',(0,-1.30,1.366),(1.31,.10,.027),'Trim',.008)
    for side in (-1,1): box('SpoilerMount',(side*.54,-1.28,1.352),(.065,.105,.065),'Trim',.010)


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
    sculpt_bumper(box('Bumper_Front',(0,1.897,.462),(1.79,.20,.148),'Trim',.025),1)
    sculpt_bumper(box('Bumper_Rear',(0,-1.888,.465),(1.79,.20,.148),'Trim',.025),-1)
    valance=housing('FrontValance',0,.305,(1.45,.18),(1.01,.154),1.940,1.910,'Paint')
    recess_cap(valance,1.940,1,[(-.690,-.523,.244,.366),(.523,.690,.244,.366)],.022)
    splitter=box('FrontSplitter',(0,1.923,.211),(1.79,.25,.042),'Trim',.015)
    for v in splitter.data.vertices:
        v.co.y-=.055*smooth(.72,1,abs(v.co.x)/.895)
    box('RearValance',(0,-1.857,.296),(1.69,.15,.16),'Paint',.02)
    housing('GrilleSurround',0,.650,(.960,.152),(.932,.130),1.904,1.883)
    box('Grille',(0,1.883,.650),(.949,.012,.145),'Trim',.004)
    for i in range(5):
        box('GrilleSlat',(0,1.896,.592+i*.029),(.928,.009,.012),'Underbody',.003)
    box('LowerIntake',(0,1.882,.305),(.975,.016,.143),'Trim',.004)
    for i in range(4):
        box('LowerSlat',(0,1.905,.254+i*.033),(.96,.011,.010),'Underbody',.002)
    for side in (-1,1):
        housing('HeadlightHousing',side*.614,.651,(.216,.165),(.191,.139),1.904,1.882)
        box('Headlight',(side*.614,1.894,.651),(.187,.012,.135),'Headlight',.006)
        box('FrontIndicator',(side*.641,2.001,.467),(.245,.015,.054),'Indicator',.006)
        housing('FogHousing',side*.6065,.305,(.148,.105),(.125,.080),1.936,1.922)
        box('FogLight',(side*.6065,1.928,.305),(.122,.012,.078),'Headlight',.006)
        housing('TaillightHousing',side*.642,.692,(.282,.272),(.252,.240),-1.892,-1.860)
        box('Taillight',(side*.642,-1.883,.746),(.249,.015,.127),'Taillight',.007)
        box('RearIndicator',(side*.696,-1.883,.618),(.139,.015,.073),'Indicator',.005)
        box('ReverseLight',(side*.571,-1.883,.618),(.099,.015,.073),'Reverse',.005)
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
    housing('LicenseRecess',0,.647,(.523,.146),(.450,.116),-1.896,-1.875)
    box('BlankPlate',(0,-1.884,.647),(.434,.014,.113),'Reverse',.005)
    box('HatchLock',(0,-1.910,.807),(.031,.016,.035),'Trim',.008)
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
           'front_fender':((-2.2,2.85,1.05),(-.72,1.13,.55),1.50),
           'rear_fender':((-2.9,-2.7,1.02),(-.72,-1.16,.60),1.50),
           'fascia':((-.42,4,1.05),(0,1.89,.59),2.05),
           'nose_top':((0,4.6,7.5),(0,1.42,.55),2.35),
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
    bumper=bpy.data.objects['Bumper_Front']
    bumper_surface=BVHTree.FromPolygons([bumper.matrix_world@v.co for v in bumper.data.vertices],[list(p.vertices) for p in bumper.data.polygons])
    for obj in COLL.objects:
        if obj.type=='MESH' and obj.name.startswith('FrontIndicator'):
            inverse=obj.matrix_world.inverted()
            for vertex in obj.data.vertices:
                p=obj.matrix_world@vertex.co
                hit=bumper_surface.ray_cast(Vector((p.x,2.3,p.z)),Vector((0,-1,0)))[0]
                assert hit is not None,'Indicator must fit bumper'
                p.y=hit.y+vertex.co.y-.0035
                vertex.co=inverse@p
        if obj.type=='MESH' and obj.name.startswith(('DoorHandle','SideIndicator')):
            side=-1 if obj.location.x<0 else 1
            hit=surface.ray_cast(Vector((side*1.5,obj.location.y,obj.location.z)),Vector((-side,0,0)))[0]
            if hit: obj.location.x=hit.x+side*obj.dimensions.x*.48
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
    result={'stage':'C','orientation':'Blender +Y forward; +Z up','units':'metres',
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
    for obj in meshes:
        if not obj.get('deformable'): continue
        name=obj.name; bm=bmesh.new(); bm.from_mesh(obj.data)
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
    parser.add_argument('--view',choices=['front34','side','rear34','front','rear','top','wireframe','comparison','color_blue','color_ivory','front_fender','rear_fender','fascia','nose_top'])
    parser.add_argument('--samples',type=int,default=48)
    parser.add_argument('--primary',type=float,nargs=3)
    parser.add_argument('--secondary',type=float,nargs=3)
    args=parser.parse_args(argv)
    root=Path(args.root).resolve(); out=root/'Previews'/'CompactHatchback'/'StageC'; out.mkdir(parents=True,exist_ok=True)
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
    CAR['development_stage']='C — sculpted bodywork; destruction integration untested'
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
    bpy.context.scene['stage_c_report']=json.dumps(report)
    bpy.context.scene.render.filepath=str(out/'front34.png')
    bpy.context.preferences.filepaths.save_version=0
    blend_path=root/'Blender'/'CompactHatchback.blend'
    pending=root/'Blender'/'CompactHatchback.pending.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(pending))
    os.replace(pending,blend_path)
    print('STAGE_C_INSPECTION',json.dumps(report),flush=True)
    if args.render:
        for view in ([args.view] if args.view else ['front34','side','rear34','front_fender','rear_fender','fascia','wireframe']):
            render_view(view,camera,out,root)
            print('RENDERED',view,flush=True)


if __name__=='__main__':
    main()
