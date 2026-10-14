"""Render saved scenes, preserving the modelling/demo file and raw image contents."""
import json
import sys
from pathlib import Path
import bpy
BASE=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(Path(__file__).resolve().parent))
import scene_views as views

args=sys.argv[sys.argv.index("--")+1:] if "--" in sys.argv else []
only=args[args.index("--only")+1] if "--only" in args else ""
annotations=json.loads((BASE/"evidence/render_annotations.json").read_text())
report=json.loads((BASE/"evidence/results.json").read_text())
extras={"02_scoop_rest_top":("02_scoop_rest","hood_top"),
        "08_pushbar_rest_front":("08_pushbar_rest","front"),
        "10_muscle_rack_detail":("10_muscle_rack","rack")}
for relative in report["expected_renders"]:
    key=Path(relative).stem
    if only and only not in key:
        continue
    scene_name,kind=extras.get(key,(key,None))
    scene=bpy.data.scenes[scene_name]
    bpy.context.window.scene=scene
    if kind:
        views.camera_view(scene.camera,kind)
        notes=[{"id":p["id"],"world":p["world"],"foot_world":p["foot_world"]} for p in annotations[scene_name]["points"]]
        annotations[key]={"points":views.projected_annotations(scene,notes),"view":kind}
    scene.render.filepath=str(BASE/relative)
    print("RENDER",key,scene.render.engine,flush=True)
    bpy.ops.render.render(write_still=True)
(BASE/"evidence/render_annotations.json").write_text(json.dumps(annotations,indent=2,sort_keys=True)+"\n")
print("RENDERS FINISHED",flush=True)
