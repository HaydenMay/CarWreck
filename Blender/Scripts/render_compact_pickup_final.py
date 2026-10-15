"""Render the saved final source without rebuilding or rewriting approved evidence."""
import argparse
from pathlib import Path
import sys
import bpy

sys.path.insert(0,str(Path(__file__).parent))
import build_compact_pickup_final as C


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path); parser.add_argument('--views',nargs='+')
    parser.add_argument('--samples',type=int,default=32); args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    root=args.root.resolve(); C.protect(root)
    bpy.ops.wm.open_mainfile(filepath=str(root/'Blender/CompactPickup.blend'))
    C.A.PARTS=[o for o in bpy.data.objects if o.type=='MESH' and 'engine_role_proposal' in o]
    C.A.MATS={m.name[7:]:m for m in bpy.data.materials if m.name.startswith('Pickup_')}
    bpy.context.scene.cycles.samples=args.samples; out=root/'Previews/CompactPickup/StageC'
    C.B.render(bpy.context.scene.camera,out,args.views or list(dict(C.A.VIEWS,**C.B.EXTRA_VIEWS)))
    for path in out.glob('stage_b_*.png'):path.replace(path.with_name(path.name.replace('stage_b_','stage_c_',1)))
    C.protect(root)


if __name__=='__main__':main()
