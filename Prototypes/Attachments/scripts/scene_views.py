"""Minimal studio and diagnostic markers for the technical demonstration."""
import math
import bpy
import numpy as np
from mathutils import Vector
from bpy_extras.object_utils import world_to_camera_view
from models import box, mesh_object, tube

WIDTH, HEIGHT = 1100,760


def aim(obj, target):
    obj.rotation_euler=(Vector(target)-obj.location).to_track_quat("-Z","Y").to_euler()


def setup(scene, materials, kind):
    scene.unit_settings.system="METRIC"
    scene.unit_settings.scale_length=1
    # Retain Blender 5.2's actual default render engine, rather than guessing its enum.
    scene.render.resolution_x,scene.render.resolution_y=WIDTH,HEIGHT
    scene.render.resolution_percentage=100
    scene.render.image_settings.file_format="PNG"
    if scene.render.engine=="CYCLES":
        scene.cycles.samples=24
        scene.cycles.use_denoising=True
    scene.render.film_transparent=False
    scene.world=bpy.data.worlds.new(scene.name+"_World")
    scene.world.use_nodes=True
    bg=next(n for n in scene.world.node_tree.nodes if n.type=="BACKGROUND")
    bg.inputs["Color"].default_value=(.20,.23,.28,1)
    bg.inputs["Strength"].default_value=.6
    scene.view_settings.view_transform="AgX"
    box(scene.name+"_Ground",(0,0,-.065),(200,200,.10),scene.collection,materials["floor"])
    for name,pos,power,size in [("Key",(-3,4,6),1200,5),("Fill",(4,1,4),900,4),("Rim",(0,-4,5),1300,4)]:
        data=bpy.data.lights.new(scene.name+name,"AREA")
        data.energy,data.size=power,size
        ob=bpy.data.objects.new(data.name,data)
        scene.collection.objects.link(ob)
        ob.location=pos
        aim(ob,(0,0,.7))
    data=bpy.data.cameras.new(scene.name+"_Camera")
    data.type="ORTHO"
    cam=bpy.data.objects.new(data.name,data)
    scene.collection.objects.link(cam)
    scene.camera=cam
    camera_view(cam,kind)
    cam.data.clip_end=300
    return cam


def camera_view(cam,kind):
    views={"overview":((-4.7,6.2,3.1),(0,0,.75),5.65),
           "hood":((-2.6,4.3,2.5),(0,1.32,.81),1.95),
           "rack":((-2.8,2.8,3.1),(0,-.5,1.36),2.5),
           "push":((-2.7,4.6,1.8),(0,2.06,.67),2.5),
           "front":((0,5,1.3),(0,2,.65),2.3),
           "hood_top":((0,1.35,6),(0,1.35,0),1.85)}
    eye,target,scale=views[kind]
    cam.location=eye
    cam.data.ortho_scale=scale
    aim(cam,target)


def ball(name, point, radius, coll, mat):
    verts=[Vector(point)+radius*Vector(p) for p in
           [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)]]
    faces=[(a,b,c) for a,b in ((0,2),(2,1),(1,3),(3,0)) for c in (4,5)]
    return mesh_object(name,verts,faces,coll,mat)


def ring(name, point, normal, coll, mat, radius=.022):
    n=Vector(normal).normalized()
    a=n.cross(Vector((0,1,0)))
    if a.length<1e-5:
        a=n.cross(Vector((1,0,0)))
    a.normalize()
    b=n.cross(a)
    path=[Vector(point)+radius*(a*math.cos(j*math.tau/20)+b*math.sin(j*math.tau/20)) for j in range(21)]
    return tube(name,path,.0035,coll,mat,sides=6)


def diagnostics(scene, key, data, materials, rest_pose=None):
    anchors=np.array(data["surface_points"])
    normals=np.array(data["surface_normals"])
    feet=np.array(data["feet_current"])
    pose=np.array(data["applied_pose"])
    foot_normal=pose[:3,data["contact_axis"]]
    ids=data["mount_ids"]
    notes=[]
    for ident,anchor,normal,foot in zip(ids,anchors,normals,feet):
        # Exact contact locations. Preview overlays expose occluded surface anchors.
        a,f=anchor,foot
        marker=ball(scene.name+"_socket_"+ident,a,.012,scene.collection,materials["yellow"])
        marker.show_in_front=True
        marker=ring(scene.name+"_foot_"+ident,f,foot_normal,scene.collection,materials["magenta"])
        marker.show_in_front=True
        if np.linalg.norm(anchor-foot)>.008:
            tube(scene.name+"_gap_"+ident,[a,f],.003,scene.collection,materials["orange"],sides=6)
        notes.append({"id":ident,"world":a.tolist(),"foot_world":f.tolist()})
    if rest_pose is not None:
        from mount_math import transform
        # White wire box is the root-parented scoop pose, left at its original location.
        corners=[[-.215,-.275,.11],[.215,-.275,.11],[.215,.275,.11],[-.215,.275,.11],[-.215,-.275,.11]]
        tube(scene.name+"_root_only_reference",transform(rest_pose,corners),.002,scene.collection,materials["white"],sides=6)
    return notes


def projected_annotations(scene, notes):
    bpy.context.window.scene=scene
    bpy.context.view_layer.update()
    output=[]
    for note in notes:
        p=world_to_camera_view(scene,scene.camera,Vector(note["world"]))
        foot=world_to_camera_view(scene,scene.camera,Vector(note["foot_world"]))
        output.append({"id":note["id"],"world":note["world"],"pixel":[round(p.x*WIDTH,2),round((1-p.y)*HEIGHT,2)],
                       "foot_world":note["foot_world"],"foot_pixel":[round(foot.x*WIDTH,2),round((1-foot.y)*HEIGHT,2)],
                       "in_frame":bool(0<=p.x<=1 and 0<=p.y<=1 and p.z>0)})
    return output
