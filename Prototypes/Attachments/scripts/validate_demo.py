"""Acceptance checks on generated results and independent FBX reimports."""
import hashlib
import json
import sys
from pathlib import Path
import bpy
from mathutils import Vector

BASE = Path(__file__).resolve().parents[1]
ROOT = BASE.parents[1]
checks = []
def check(name, condition):
    if not condition:
        raise AssertionError(name)
    checks.append(name)

report_path = BASE/"evidence/results.json"
check("generated results exist", report_path.is_file())
report = json.loads(report_path.read_text())
cases = report["cases"]
check("ten saved demonstration cases", len(cases)==10)
rest = cases["02_scoop_rest"]["mounts"]["scoop"]
moderate = cases["03_scoop_moderate"]["mounts"]["scoop"]
severe = cases["04_scoop_severe"]["mounts"]["scoop"]
repair = cases["05_scoop_repair"]["mounts"]["scoop"]
check("scoop rest fit within 10 mm", rest["candidate"]["max_residual_m"]<.01)
check("moderate scoop remains convincing within 25 mm", moderate["candidate"]["max_residual_m"]<.025 and moderate["status"]=="valid")
check("moderate translates and rotates", moderate["translation_change_m"]>.03 and moderate["rotation_change_deg"]>3)
check("root parenting visibly fails", moderate["root_only_max_gap_m"]>.04)
check("severe support failure is flagged", severe["status"]=="invalid_support")
check("severe shows actual foot separation", severe["applied"]["max_residual_m"]>.06)
check("invalid scoop retains last valid moderate pose", severe["frozen_from"]=="03_scoop_moderate" and severe["applied_pose"]==moderate["applied_pose"])
check("repair restores source vertices exactly", cases["05_scoop_repair"]["vertices_equal_rest"])
check("repair restores mount pose", repair["translation_change_m"]<1e-6 and repair["rotation_change_deg"]<1e-4)
rack = cases["07_rack_roof_dent"]["mounts"]["rack"]
check("rack remains chassis fixed", rack["translation_change_m"]<1e-8 and rack["rotation_change_deg"]<1e-6)
check("rack roof gap is marked as limitation", rack["status"]=="invalid_support" and rack["applied"]["max_residual_m"]>.06)
check("push bar asymmetric supports report failure", cases["09_pushbar_crush"]["mounts"]["pushbar"]["status"]=="invalid_support")
check("identical rack geometry used on both vehicles", report["rack_reuse"]["same_geometry_hash"] and report["rack_reuse"]["unit_scale_both"])
check("cross-vehicle rack rest fits within 10 mm", report["rack_reuse"]["max_rest_residual_m"]<.01)
check("anchor rest reconstruction within 1 micrometre", report["max_anchor_rest_reconstruction_error_m"]<1e-6)
check("second build has identical deterministic signature", report["repeat_build_matches_previous"] is True)
for relative, expected in report["production_sha256"].items():
    check("source untouched: "+relative, hashlib.sha256((ROOT/relative).read_bytes()).hexdigest()==expected)

# Reimport each generated FBX independently; compare exported geometry in Blender world,
# including its import-root rotation. No inference about Unity indices is made.
for name, model in report["models"].items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(BASE/"models"/(name+".fbx")), use_anim=False)
    bpy.context.view_layer.update()
    meshes = [o for o in bpy.context.scene.objects if o.type=="MESH"]
    check(name+" FBX mesh count", len(meshes)==model["mesh_objects"])
    points = [o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    bounds = [[f(p[i] for p in points) for i in range(3)] for f in (min,max)]
    error = max(abs(bounds[j][i]-model["bounds"][j][i]) for j in range(2) for i in range(3))
    check(name+" FBX metre-scale bounds", error<1e-5)
    check(name+" standalone .blend exists", (BASE/"models"/(name+".blend")).is_file())
if "--renders" in sys.argv:
    from struct import unpack
    for relative in report["expected_renders"]:
        path = BASE/relative
        data = path.read_bytes()
        check(relative+" is 1100 x 760 PNG", data[:8]==b"\x89PNG\r\n\x1a\n" and unpack(">II",data[16:24])==(1100,760))

(BASE/"evidence/acceptance.json").write_text(json.dumps(
    {"status":"PASS","checks":checks,"count":len(checks),"scope":"Blender only; no Unity integration"},indent=2)+"\n")
print("ACCEPTANCE PASS:",len(checks),"checks")
