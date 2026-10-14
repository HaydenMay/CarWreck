"""Deliberately low-detail accessories; all coordinates are accessory-local metres."""
import hashlib
import json
import math
import bpy
import numpy as np
from mathutils import Vector


def material(name, rgb, metallic=0, roughness=.45):
    mat = bpy.data.materials.new(name)
    mat.diffuse_color = (*rgb,1)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type=="BSDF_PRINCIPLED")
    node.inputs["Base Color"].default_value=(*rgb,1)
    node.inputs["Metallic"].default_value=metallic
    node.inputs["Roughness"].default_value=roughness
    return mat


def mesh_object(name, vertices, faces, collection, mat):
    mesh=bpy.data.meshes.new(name)
    mesh.from_pydata(vertices,[],faces)
    mesh.update()
    obj=bpy.data.objects.new(name,mesh)
    collection.objects.link(obj)
    mesh.materials.append(mat)
    return obj


def box(name, centre, size, collection, mat):
    x,y,z=(s/2 for s in size)
    vertices=[(centre[0]+a,centre[1]+b,centre[2]+c)
              for a,b,c in ((-x,-y,-z),(x,-y,-z),(x,y,-z),(-x,y,-z),
                            (-x,-y,z),(x,-y,z),(x,y,z),(-x,y,z))]
    return mesh_object(name,vertices,[(0,3,2,1),(4,5,6,7),(0,1,5,4),
                       (1,2,6,5),(2,3,7,6),(3,0,4,7)],collection,mat)


def tube(name, points, radius, collection, mat, sides=12):
    verts=[]
    for i,p in enumerate(points):
        tangent=Vector(points[min(i+1,len(points)-1)])-Vector(points[max(0,i-1)])
        tangent.normalize()
        a=tangent.cross(Vector((0,1,0)))
        if a.length<1e-5:
            a=tangent.cross(Vector((1,0,0)))
        a.normalize()
        b=tangent.cross(a).normalized()
        for j in range(sides):
            angle=2*math.pi*j/sides
            verts.append(Vector(p)+radius*(a*math.cos(angle)+b*math.sin(angle)))
    faces=[]
    for i in range(len(points)-1):
        for j in range(sides):
            faces.append((i*sides+j,i*sides+(j+1)%sides,(i+1)*sides+(j+1)%sides,(i+1)*sides+j))
    faces.extend((tuple(reversed(range(sides))),tuple((len(points)-1)*sides+j for j in range(sides))))
    obj=mesh_object(name,verts,faces,collection,mat)
    for poly in obj.data.polygons:
        poly.use_smooth=True
    return obj


def geometry_hash(objects):
    pieces=[]
    for ob in objects:
        if ob.type=="MESH":
            payload={"vertices":[list(v.co) for v in ob.data.vertices],
                     "faces":[list(p.vertices) for p in ob.data.polygons]}
            pieces.append(hashlib.sha256(json.dumps(payload,sort_keys=True).encode()).hexdigest())
    return hashlib.sha256("".join(sorted(pieces)).encode()).hexdigest()


def build_models(materials):
    assets={}
    definitions={
      "rack":{"id":"carwreck.roof_rack.simple.v1","mode":"chassis_rigid","category":"roof_equipment","failure_capability":"detachable_future",
              "feet":[[-.53,-.3,0],[.53,-.3,0],[-.53,.3,0],[.53,.3,0]],
              "mount_ids":["R1","R2","R3","R4"]},
      "scoop":{"id":"carwreck.hood_scoop.low.v1","mode":"panel_follow","category":"hood_accessory","failure_capability":"none",
               "feet":[[-.235,-.30,0],[.235,-.30,0],[-.235,.30,0],[.235,.30,0]],
               "mount_ids":["H1","H2","H3","H4"]},
      "pushbar":{"id":"carwreck.push_bar.tubular.v1","mode":"multi_point_rigid","category":"front_reinforcement","failure_capability":"detachable_future",
                 "feet":[[-.46,0,-.04],[.46,0,-.04],[-.46,0,.04],[.46,0,.04]],
                 "mount_ids":["B1","B2","B3","B4"]}}
    for key,definition in definitions.items():
        scene=bpy.data.scenes.new("Asset_"+key)
        coll=bpy.data.collections.new("Accessory_"+key)
        scene.collection.children.link(coll)
        root=bpy.data.objects.new(definition["id"],None)
        coll.objects.link(root)
        root["attachment_id"]=definition["id"]
        root["mounting_mode"]=definition["mode"]
        mat=materials[{"rack":"black","scoop":"cyan","pushbar":"steel"}[key]]
        if key=="rack":
            for side in (-1,1):
                box("Rack_side_rail",(side*.53,0,.16),(.035,.96,.035),coll,mat)
                box("Rack_end_rail",(0,side*.46,.16),(1.095,.035,.035),coll,mat)
                box("Rack_crossbar_"+str(side),(0,side*.3,.125),(1.095,.04,.035),coll,mat)
            for p in definition["feet"]:
                box("Rack_foot",np.array(p)+[0,0,.015],(.08,.10,.03),coll,mat)
                box("Rack_leg",np.array(p)+[0,0,.075],(.035,.04,.12),coll,mat)
        elif key=="scoop":
            verts=[(-.215,-.275,.01),(.215,-.275,.01),(.215,.275,.01),(-.215,.275,.01),
                   (-.215,-.275,.035),(.215,-.275,.035),(.215,.275,.095),(-.215,.275,.095)]
            mesh_object("Low_rectangular_scoop",verts,[(0,4,5,1),(0,3,7,4),(1,5,6,2),(4,7,6,5)],
                        coll,mat)
            box("Recessed_dark_intake",(0,.248,.048),(.39,.008,.052),coll,materials["black"])
            for p in definition["feet"]:
                box("Scoop_mount_tab",np.array(p)+[0,0,.007],(.07,.075,.014),coll,mat)
        else:
            tube("Heavy_tubular_push_bar",[(-.65,.24,-.10),(-.65,.24,.42),(-.56,.24,.50),
                  (.56,.24,.50),(.65,.24,.42),(.65,.24,-.10)],.035,coll,mat)
            tube("Push_bar_cross_tube",[(-.65,.24,.065),(.65,.24,.065)],.035,coll,mat)
            for x in (-.46,.46):
                box("Solid_mounting_bracket",(x,.12,0),(.075,.24,.115),coll,mat)
        for ob in list(coll.objects):
            if ob!=root:
                ob.parent=root
        for ident,p in zip(definition["mount_ids"],definition["feet"]):
            empty=bpy.data.objects.new("mount."+ident,None)
            coll.objects.link(empty)
            empty.parent=root
            empty.location=p
            empty.empty_display_size=.035
            empty["mount_id"]=ident
        meshes=[o for o in coll.objects if o.type=="MESH"]
        points=np.array([list(v.co) for o in meshes for v in o.data.vertices])
        definition.update({"scene":scene,"collection":coll,"root":root,
          "geometry_hash":geometry_hash(meshes),
          "mesh_objects":len(meshes),"vertices":len(points),
          "bounds":[points.min(axis=0).tolist(),points.max(axis=0).tolist()]})
        assets[key]=definition
    return assets


def instantiate(asset, scene, pose, prefix):
    root=asset["root"].copy()
    root.name=prefix+"_"+asset["id"]
    scene.collection.objects.link(root)
    from mathutils import Matrix
    root.matrix_world=Matrix(pose.tolist())
    for original in asset["collection"].objects:
        if original==asset["root"]:
            continue
        clone=original.copy()
        scene.collection.objects.link(clone)
        clone.parent=root
        clone.matrix_basis=original.matrix_basis.copy()
    return root
