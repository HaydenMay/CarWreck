"""Render the archived Stage B source and current Stage C under matched conditions.

Blender --python-exit-code 1 --python <this script> -- <project root>
       [--baseline <StageB.blend>] [--samples 48]
The sources are opened read-only; only previews and the comparison manifest are written.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys

import bpy

VIEWS=('front34','side','rear34','front_fender','rear_fender','fascia','wireframe')

def rounded(value):
    if isinstance(value,float): return round(value,7)
    if isinstance(value,dict): return {k:rounded(v) for k,v in value.items()}
    if isinstance(value,(tuple,list)): return [rounded(v) for v in value]
    return value

def conditions():
    scene=bpy.context.scene
    bg=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND')
    lights={o.name:{'matrix':list(sum((list(r) for r in o.matrix_world),[])),
                         'type':o.data.type,'shape':o.data.shape,'size':o.data.size,
                         'energy':o.data.energy,'color':list(o.data.color)}
            for o in scene.objects if o.type=='LIGHT'}
    materials={}
    for mat in bpy.data.materials:
        if not mat.name.startswith('Hatch_') or mat.name=='Hatch_Wire': continue
        bs=next(n for n in mat.node_tree.nodes if n.type=='BSDF_PRINCIPLED')
        materials[mat.name]={key:list(bs.inputs[key].default_value) if key.endswith('Color') else bs.inputs[key].default_value
                             for key in ('Base Color','Metallic','Roughness','Coat Weight','Emission Color','Emission Strength')}
        if mat.name=='Hatch_Paint':
            node=mat.node_tree.nodes['Primary and Stripe Colors']
            materials[mat.name]['colors']=[list(node.inputs[i].default_value) for i in (1,2)]
    return rounded({'engine':scene.render.engine,'samples':scene.cycles.samples,
                    'seed':scene.cycles.seed,'denoising':scene.cycles.use_denoising,
                    'resolution':[scene.render.resolution_x,scene.render.resolution_y,scene.render.resolution_percentage],
                    'view_transform':scene.view_settings.view_transform,'look':scene.view_settings.look,
                    'exposure':scene.view_settings.exposure,'gamma':scene.view_settings.gamma,
                    'world':{'color':list(bg.inputs['Color'].default_value),'strength':bg.inputs['Strength'].default_value},
                    'lights':lights,'materials':materials})

def camera_record(camera):
    return rounded({'matrix_world':[list(r) for r in camera.matrix_world],
                    'type':camera.data.type,'lens':camera.data.lens,'ortho_scale':camera.data.ortho_scale,
                    'clip_start':camera.data.clip_start,'clip_end':camera.data.clip_end})

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path)
    parser.add_argument('--baseline',type=Path); parser.add_argument('--samples',type=int,default=48)
    parser.add_argument('--baseline-commit',default='f870cf26e6f52d8d5b7f4b6716d1f05dda562a20')
    parser.add_argument('--output',type=Path,help='Preview directory, relative to project root or absolute')
    parser.add_argument('--views',nargs='+',choices=VIEWS+('front','nose_top'),default=VIEWS)
    parser.add_argument('--before-label',default='Stage B'); parser.add_argument('--after-label',default='Stage C')
    parser.add_argument('--stage',choices=('both','stage_c'),default='both',help='Resume C using an existing matched B manifest')
    args=parser.parse_args(argv); root=args.root.resolve()
    out=root/(args.output or 'Previews/CompactHatchback/StageC'); out.mkdir(parents=True,exist_ok=True)
    # Blender's library loader resolves relative paths differently from Python.
    baseline=(root/args.baseline).resolve() if args.baseline else out/'.baseline/StageB.blend'
    source=root/'Blender/CompactHatchback.blend'
    if not baseline.is_file():
        # Retrieve the committed LFS blob as bytes; PowerShell text redirection
        # would corrupt this binary file. This does not change the checkout.
        blob=subprocess.check_output(['git','cat-file','--filters',
                                      args.baseline_commit+':Blender/CompactHatchback.blend'],cwd=root)
        assert len(blob)>100000,'Stage B LFS content unavailable; run git lfs fetch first'
        baseline.parent.mkdir(parents=True,exist_ok=True); baseline.write_bytes(blob)
    spec=importlib.util.spec_from_file_location('hatch',root/'Blender/Scripts/build_compact_hatchback.py')
    hatch=importlib.util.module_from_spec(spec); spec.loader.exec_module(hatch)
    manifest={'baseline_commit':args.baseline_commit,
              'source_sha256':{'stage_b':sha(baseline),'stage_c':sha(source)},
              'labels':{'stage_b':args.before_label,'stage_c':args.after_label},
              'blender_version':bpy.app.version_string,'conditions':{},'cameras':{},'outputs':{}}
    paint_colors=None
    stages=(('stage_b',baseline),('stage_c',source))
    if args.stage=='stage_c':
        manifest=json.loads((out/'comparison_manifest.json').read_text())
        assert manifest['source_sha256']['stage_b']==sha(baseline),'Baseline changed'
        manifest['source_sha256']['stage_c']=sha(source)
        with bpy.data.libraries.load(str(baseline),link=False) as (available,loaded):
            loaded.materials=['Hatch_Paint']
        baseline_node=loaded.materials[0].node_tree.nodes['Primary and Stripe Colors']
        paint_colors=[tuple(baseline_node.inputs[i].default_value) for i in (1,2)]
        stages=stages[1:]
    for stage,path in stages:
        bpy.ops.wm.open_mainfile(filepath=str(path))
        scene=bpy.context.scene; scene.cycles.samples=args.samples
        scene.cycles.seed=0; scene.cycles.use_animated_seed=False
        hatch.COLL=bpy.data.collections['CompactHatchback']; hatch.CAR=bpy.data.objects['CompactHatchback']
        hatch.MATS={mat.name[6:]:mat for mat in bpy.data.materials if mat.name.startswith('Hatch_')}
        node=hatch.MATS['Paint'].node_tree.nodes['Primary and Stripe Colors']
        if paint_colors is None: paint_colors=[tuple(node.inputs[i].default_value) for i in (1,2)]
        for i,color in zip((1,2),paint_colors): node.inputs[i].default_value=color
        camera=scene.camera
        manifest['conditions'][stage]=conditions()
        if stage=='stage_c':
            assert manifest['conditions']['stage_b']==manifest['conditions']['stage_c'],'Render conditions differ between stages'
        manifest['cameras'][stage]={}; manifest['outputs'][stage]={}
        for view in args.views:
            hatch.render_view(view,camera,out,root)
            bpy.context.view_layer.update()
            manifest['cameras'][stage][view]=camera_record(camera)
            filename=stage+'_'+view+'.png'; os.replace(out/(view+'.png'),out/filename)
            manifest['outputs'][stage][view]=filename
            if stage=='stage_c':
                assert manifest['cameras']['stage_b'][view]==manifest['cameras']['stage_c'][view],('Camera differs',view)
            print('MATCHED_RENDER',stage,view,flush=True)
        (out/'comparison_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    manifest['identical_conditions']=True; manifest['identical_cameras']=True
    (out/'comparison_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print('COMPARISON_CONDITIONS_PASS',flush=True)

if __name__=='__main__': main()
