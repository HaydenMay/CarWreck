"""Build the isolated Blender-side attachment proof of concept from verified sources.

Run from any directory:
blender -b --factory-startup --python-exit-code 1 --python build_demo.py
Run twice for repeat-build evidence. Rendering is a separate script.
"""
import hashlib
import json
import math
import sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Matrix, Vector
sys.path.insert(0,str(Path(__file__).resolve().parent))
import mount_math as mm
import models
import scene_views as views

BASE=Path(__file__).resolve().parents[1]
ROOT=BASE.parents[1]
LIMITS={"strained_m":.025,"invalid_m":.06,"spacing_fraction":.25,"normal_deg":55}
SOURCES={"hatch":("CompactHatchback","6385a079f750a8762910308c6ea66b4ec0024e13cf7f069199d2d63b318ffef5"),
         "muscle":("MuscleCar","8836620ad6d85cd3d6aae2bd861efd7f060621f9cbb5953a236a88ace51871ad")}


def dump(path, data):
    path.write_text(json.dumps(data,indent=2,sort_keys=True,allow_nan=False)+"\n",encoding="utf-8")


def source_car(key):
    name,expected=SOURCES[key]
    path=ROOT/"Blender"/(name+".blend")
    if hashlib.sha256(path.read_bytes()).hexdigest()!=expected:
        raise ValueError("verified source hash mismatch: "+name)
    with bpy.data.libraries.load(str(path),link=False) as (available,loaded):
        names=list(available.objects)
        loaded.objects=list(names)
    mapping=dict(zip(names,loaded.objects))
    root=mapping[name]
    descendants=set(root.children_recursive)
    objects=[root]+[ob for ob in loaded.objects if ob in descendants]
    roles={ob:role for role,ob in mapping.items()}
    for ob in objects:
        ob["source_role"]=roles[ob]
    return {"root":root,"objects":objects,"roles":roles,"mapping":mapping,
            "all_loaded":list(loaded.objects),"name":name}


def geometry(obj):
    # Binding roles are direct children of an identity car root.
    assert obj.parent is not None and obj.parent.parent is None
    matrix=obj.matrix_basis
    verts=np.array([list(matrix@v.co) for v in obj.data.vertices])
    obj.data.calc_loop_triangles()
    triangles=[list(t.vertices) for t in obj.data.loop_triangles]
    return verts,triangles


def seeds(asset, vehicle="hatch"):
    out=[]
    for ident,p in zip(asset["mount_ids"],asset["feet"]):
        if asset["mode"]=="multi_point_rigid":
            origin=[p[0],2.6,.462+p[2]]
            direction=[0,-1,0]
            bounds=[[-.7,1.78,.38],[.7,2.04,.55]]
            normal=[0,1,0]
        else:
            roof_centre=-.2 if vehicle=="muscle" else -.5
            origin=[p[0],p[1]+(1.35 if asset["mode"]=="panel_follow" else roof_centre),2.]
            direction=[0,0,-1]
            bounds=([[-.4,.9,.7],[.4,1.8,1.0]] if asset["mode"]=="panel_follow"
                    else [[-.65,-1.1,1.34],[.65,.15,1.6]])
            normal=[0,0,1]
        out.append({"id":ident,"origin":origin,"direction":direction,"max_distance":2.,
                    "bounds":bounds,"expected_normal":normal})
    return out


def bind_fit(source, asset, key):
    role={"rack":"Cabin","scoop":"Body","pushbar":"Bumper_Front"}[key]
    vertices,triangles=geometry(source["mapping"][role])
    vehicle="muscle" if source["name"]=="MuscleCar" else "hatch"
    bindings=mm.bind_anchors(vertices,triangles,seeds(asset,vehicle))
    points=np.array([b["rest_point"] for b in bindings])
    normals=np.array([b["rest_normal"] for b in bindings])
    targets=points+.003*normals
    pose=mm.fit_rigid(asset["feet"],targets)
    reconstruction=max(np.linalg.norm(np.array(b["weights"])@vertices[b["vertex_indices"]]-b["rest_point"]) for b in bindings)
    return {"role":role,"bindings":bindings,"rest_pose":pose,"rest_targets":targets,
            "max_reconstruction_error":float(reconstruction),"seeds":seeds(asset,vehicle)}


def displacement(points, mode):
    x,y,z=points.T
    d=np.zeros_like(points)
    if mode.startswith("hood"):
        # Full weight over all four mounting samples, smoothly fading at patch edges.
        wx=np.clip((.60-np.abs(x))/.24,0,1)
        wy=np.clip((y-.72)/.27,0,1)*np.clip((1.91-y)/.22,0,1)
        w=wx*wy*np.clip((z-.65)/.14,0,1)
        d[:,0]=.025*w
        d[:,1]=-.04*w
        d[:,2]=w*(-.075-.16*(y-1.35)+.055*x)
        if mode=="hood_severe":
            fold=np.exp(-((x-.235)/.20)**2-((y-1.65)/.22)**2)
            d[:,2]-=.38*fold*w
            d[:,1]-=.11*fold*w
    elif mode=="roof_dent":
        w=np.clip((z-1.30)/.07,0,1)
        d[:,2]=-.23*np.exp(-((x+.27)/.60)**2-((y+.5)/.72)**2)*w
    elif mode=="front_crush":
        w=np.clip((y-1.30)/.45,0,1)
        right=np.clip((x+.25)/.71,0,1)
        height=np.clip((z-.422)/.08,0,1.7)
        d[:,1]=-w*(.12+.18*right+.26*right*height)
        d[:,2]=-w*(.015+.055*right)
    return d


def copy_vehicle(source, scene, mode):
    copies={}
    for original in source["objects"]:
        clone=original.copy()
        clone.name=scene.name+"_"+source["roles"][original]
        if clone.type=="MESH" and mode!="rest" and (not mode.startswith("hood") or source["roles"][original]=="Body"):
            clone.data=original.data.copy()
        scene.collection.objects.link(clone)
        copies[original]=clone
    for original,clone in copies.items():
        clone.parent=copies.get(original.parent)
        clone.matrix_basis=original.matrix_basis.copy()
    bpy.context.window.scene=scene
    bpy.context.view_layer.update()
    result={source["roles"][o]:c for o,c in copies.items()}
    for role,obj in result.items():
        if obj.type!="MESH" or mode=="rest":
            continue
        if mode.startswith("hood") and role!="Body":
            continue
        if mode=="front_crush" and ("Wheel" in role or (obj.parent and "Wheel" in obj.parent.name)):
            continue
        matrix=obj.matrix_world.copy()
        points=np.array([list(matrix@v.co) for v in obj.data.vertices])
        changed=points+displacement(points,mode)
        inv=matrix.inverted()
        for vertex,p in zip(obj.data.vertices,changed):
            vertex.co=inv@Vector(p)
        obj.data.update()
    return result


def pose_change(pose,rest):
    distance=float(np.linalg.norm(pose[:3,3]-rest[:3,3]))
    angle=0. if np.array_equal(pose,rest) else math.degrees(math.acos(
        np.clip((np.trace(rest[:3,:3].T@pose[:3,:3])-1)/2,-1,1)))
    return distance,angle


def mount_record(objects, asset, fit, key, case, previous):
    obj=objects[fit["role"]]
    vertices=np.array([list(obj.matrix_world@v.co) for v in obj.data.vertices])
    samples=[mm.sample_anchor(vertices,b,b["revision"]) for b in fit["bindings"]]
    surface=np.array([p for p,n in samples])
    normals=np.array([n for p,n in samples])
    targets=surface+.003*normals
    rest=fit["rest_pose"]
    candidate=mm.fit_rigid(asset["feet"],targets)
    orientation={"rest_normals":[b["rest_normal"] for b in fit["bindings"]],"normals":normals,"rest_pose":rest}
    quality=mm.assess(asset["feet"],targets,candidate,fit["rest_targets"],LIMITS,**orientation)
    spread=max(math.degrees(math.acos(np.clip(a@b,-1,1))) for a in normals for b in normals)
    quality["normal_spread_deg"]=spread
    if spread>LIMITS["normal_deg"]:
        quality["status"]="invalid_support"
    frozen=None
    if key=="rack":
        applied=rest.copy()
        actual=mm.assess(asset["feet"],targets,applied,fit["rest_targets"],LIMITS,**orientation)
        status="invalid_support" if quality["status"]=="invalid_support" else actual["status"]
        if status=="invalid_support":
            frozen="chassis_reference_fixed"
    elif quality["status"]=="invalid_support":
        applied=previous[key][0].copy()
        frozen=previous[key][1]
        status="invalid_support"
    else:
        applied=candidate.copy()
        status=quality["status"]
    previous[key]=(applied,case)
    actual=mm.assess(asset["feet"],targets,applied,fit["rest_targets"],LIMITS,**orientation)
    translation,rotation=pose_change(applied,rest)
    root_gap=float(np.linalg.norm(mm.transform(rest,asset["feet"])-targets,axis=1).max())
    return {"mount_ids":asset["mount_ids"],"contact_axis":1 if key=="pushbar" else 2,
       "status":status,"candidate":quality,"applied":actual,"frozen_from":frozen,
       "applied_pose":applied.tolist(),"candidate_pose":candidate.tolist(),
       "surface_points":surface.tolist(),"surface_normals":normals.tolist(),
       "feet_current":mm.transform(applied,asset["feet"]).tolist(),
       "translation_change_m":translation,"rotation_change_deg":rotation,
       "root_only_max_gap_m":root_gap}


def main():
    for path in ("models","evidence","renders","previews"):
        (BASE/path).mkdir(exist_ok=True)
    previous_path=BASE/"evidence/results.json"
    old_signature=json.loads(previous_path.read_text())["deterministic_signature"] if previous_path.exists() else None
    protected=["Blender/"+name+".blend" for name,hash_ in SOURCES.values()]
    protected+=["Blender/Scripts/build_compact_hatchback.py","Blender/Scripts/build_muscle_car.py",
       "Assets/Vehicles/CompactHatchback/Models/CompactHatchback.fbx",
       "Assets/DamageLab/Art/Models/MuscleCar.fbx",
       "Assets/DamageLab/Scenes/VehicleCollisionLab.unity","Assets/DamageLab/Scenes/VehicleDamageLab.unity"]
    production={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in protected}
    bpy.ops.wm.read_factory_settings(use_empty=True)
    materials={key:models.material("Demo_"+key,rgb,metal) for key,rgb,metal in
        [("black",(.018,.023,.028),.65),("cyan",(.02,.60,.74),.25),
         ("steel",(.20,.27,.32),.75),("yellow",(1,.72,.005),0),
         ("magenta",(.9,.005,.38),0),("orange",(1,.075,.006),0),
         ("white",(.65,.80,.9),0),("floor",(.16,.18,.21),0)]}
    assets=models.build_models(materials)
    sources={key:source_car(key) for key in SOURCES}
    for image in bpy.data.images:
        if image.source=="FILE" and "CarPaint_Stripes" in image.name:
            image.filepath=str(ROOT/"Assets/DamageLab/Art/Textures/CarPaint_Stripes.png")
            image.reload()
            image.pack()
    fits={("hatch",key):bind_fit(sources["hatch"],asset,key) for key,asset in assets.items()}
    fits[("muscle","rack")]=bind_fit(sources["muscle"],assets["rack"],"rack")
    fit_adapters=[{"vehicle":v,"attachment":k,"sockets":{b["id"]:b for b in f["bindings"]},"fit":f}
                  for (v,k),f in fits.items()]
    reports={}
    annotations={}
    previous={k:(fits[("hatch",k)]["rest_pose"],"rest") for k in assets}
    cases=[("01_hatch_installed","hatch","rest",list(assets),"overview"),
           ("02_scoop_rest","hatch","rest",["scoop"],"hood"),
           ("03_scoop_moderate","hatch","hood_moderate",["scoop"],"hood"),
           ("04_scoop_severe","hatch","hood_severe",["scoop"],"hood"),
           ("05_scoop_repair","hatch","rest",["scoop"],"hood"),
           ("06_rack_rest","hatch","rest",["rack"],"rack"),
           ("07_rack_roof_dent","hatch","roof_dent",["rack"],"rack"),
           ("08_pushbar_rest","hatch","rest",["pushbar"],"push"),
           ("09_pushbar_crush","hatch","front_crush",["pushbar"],"push"),
           ("10_muscle_rack","muscle","rest",["rack"],"overview")]
    for case,vehicle,mode,mounts,view in cases:
        scene=bpy.data.scenes.new(case)
        objects=copy_vehicle(sources[vehicle],scene,mode)
        record={"vehicle":vehicle,"deformation":mode,"mounts":{}}
        notes=[]
        for key in mounts:
            fit=mm.select_adapter(fit_adapters,vehicle,key,assets[key]["mount_ids"])["fit"]
            data=mount_record(objects,assets[key],fit,key,case,previous)
            record["mounts"][key]=data
            mount_root=models.instantiate(assets[key],scene,np.array(data["applied_pose"]),case)
            data["accessory_geometry_hash"]=models.geometry_hash(mount_root.children_recursive)
            data["instance_unit_scale"]=bool(np.allclose(np.linalg.norm(np.array(mount_root.matrix_world)[:3,:3],axis=0),1,atol=1e-7))
            ghost=fit["rest_pose"] if key=="scoop" and mode.startswith("hood") else None
            notes+=views.diagnostics(scene,key,data,materials,ghost)
        if case=="05_scoop_repair":
            record["vertices_equal_rest"]=all(
                np.array_equal(np.array([list(v.co) for v in objects[role].data.vertices]),
                               np.array([list(v.co) for v in src.data.vertices]))
                for role,src in sources[vehicle]["mapping"].items()
                if role in objects and src.type=="MESH")
        views.setup(scene,materials,view)
        scene["case_id"]=case
        scene["mode"]="SYNTHETIC BLENDER DEFORMATION - NO UNITY PHYSICS"
        scene["mount_status"]=json.dumps({k:v["status"] for k,v in record["mounts"].items()})
        annotations[case]={"points":views.projected_annotations(scene,notes),"view":view}
        record["vehicle_geometry_hash"]=hashlib.sha256("".join(
            mm.mesh_revision([list(v.co) for v in ob.data.vertices],[list(e.vertices) for e in ob.data.edges])
            for ob in objects.values() if ob.type=="MESH").encode()).hexdigest()
        reports[case]=record
        print("CASE",case,{k:(d["status"],round(d["candidate"]["max_residual_m"],5),round(d["applied"]["max_residual_m"],5)) for k,d in record["mounts"].items()},flush=True)
    model_reports={}
    for key,asset in assets.items():
        bpy.context.window.scene=asset["scene"]
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        for obj in asset["collection"].objects:
            obj.select_set(True)
        bpy.context.view_layer.objects.active=asset["root"]
        bpy.ops.export_scene.fbx(filepath=str(BASE/"models"/(key+".fbx")),
           use_selection=True,object_types={"EMPTY","MESH"},apply_unit_scale=True,
           apply_scale_options="FBX_SCALE_ALL",bake_space_transform=True,
           axis_forward="-Z",axis_up="Y",mesh_smooth_type="FACE",use_mesh_modifiers=True,
           add_leaf_bones=False,bake_anim=False,use_custom_props=True)
        bpy.data.libraries.write(str(BASE/"models"/(key+".blend")),{asset["scene"]},fake_user=True,compress=True)
        model_reports[key]={k:asset[k] for k in ("id","category","mode","failure_capability","mount_ids","feet","geometry_hash","mesh_objects","vertices","bounds")}
        model_reports[key]["asset_frame"]={"position_m":[0,0,0],"rotation_xyzw":[0,0,0,1]}
    metadata={"format":"carwreck.attachment-poc.v1","units":"metres",
      "coordinates":{"right":"+X","forward":"+Y","up":"+Z","space":"vehicle_local_Blender"},
      "runtime_ready":False,"models":model_reports,"limits":LIMITS,
      "adapters":[{"vehicle":vehicle,"vehicle_id":"carwreck."+("compact_hatchback" if vehicle=="hatch" else "muscle_car"),"attachment":key,"mesh_role":fit["role"],
         "socket_group":{"rack":"roof.rack","scoop":"hood.scoop","pushbar":"front.push_bar"}[key],
         "source_sha256":SOURCES[vehicle][1],"sockets":{b["id"]:b for b in fit["bindings"]},
         "rest_pose":fit["rest_pose"].tolist(),"normal_clearance_m":.003,
         "projection_seeds":fit["seeds"],"body_scale":[1,1,1],
         "alignment_offset":{"translation_m":[0,0,0],"rotation_xyzw":[0,0,0,1]}}
          for (vehicle,key),fit in fits.items()],
      "failure_metadata":{"force_limit_N":None,"torque_limit_Nm":None,
         "policy":"diagnostic_only","roof_gap":"limitation: requires future mounting response or detachment"},
      "unity_binding":"not generated; rebind on imported mesh; never reuse Blender triangle indices"}
    signature=hashlib.sha256(json.dumps({"metadata":metadata,"cases":reports},sort_keys=True).encode()).hexdigest()
    rack_residual=max(reports[k]["mounts"]["rack"]["candidate"]["max_residual_m"] for k in ("06_rack_rest","10_muscle_rack"))
    result={"scope":"Blender only; synthetic displacement, not destruction physics",
      "blender_version":bpy.app.version_string,"render_engine":bpy.data.scenes["01_hatch_installed"].render.engine,
      "production_sha256":production,"cases":reports,"models":model_reports,
      "rack_reuse":{"same_geometry_hash":reports["06_rack_rest"]["mounts"]["rack"]["accessory_geometry_hash"]==reports["10_muscle_rack"]["mounts"]["rack"]["accessory_geometry_hash"]==assets["rack"]["geometry_hash"],"geometry_hash":assets["rack"]["geometry_hash"],
                    "unit_scale_both":all(reports[k]["mounts"]["rack"]["instance_unit_scale"] for k in ("06_rack_rest","10_muscle_rack")),"max_rest_residual_m":rack_residual},
      "max_anchor_rest_reconstruction_error_m":max(f["max_reconstruction_error"] for f in fits.values()),
      "deterministic_signature":signature,"repeat_build_matches_previous":None if old_signature is None else signature==old_signature,
      "expected_renders":["renders/"+case+".png" for case,*_ in cases]+["renders/02_scoop_rest_top.png","renders/08_pushbar_rest_front.png","renders/10_muscle_rack_detail.png"],
      "not_tested":["Unity imported anchor bindings","runtime damage/repair integration","force transfer",
                    "breakage physics","8-12 vehicle performance","player builds"]}
    dump(BASE/"metadata.json",metadata)
    dump(BASE/"evidence/results.json",result)
    dump(BASE/"evidence/render_annotations.json",annotations)
    # Drop unlinked source objects and the empty startup scene; copies retain source mesh data.
    for source in sources.values():
        for obj in source["all_loaded"]:
            if obj and obj.name in bpy.data.objects:
                bpy.data.objects.remove(obj,do_unlink=True)
    empty=bpy.data.scenes.get("Scene")
    if empty:
        bpy.data.scenes.remove(empty)
    bpy.context.window.scene=bpy.data.scenes["01_hatch_installed"]
    bpy.context.view_layer.update()
    bpy.context.scene.render.filepath=str(BASE/"renders/01_hatch_installed.png")
    bpy.ops.wm.save_as_mainfile(filepath=str(BASE/"demonstration.blend"),compress=True)
    print("BUILD_SIGNATURE",signature,"repeat:",result["repeat_build_matches_previous"],flush=True)


if __name__=="__main__":
    main()
