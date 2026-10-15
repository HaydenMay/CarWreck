"""Resume review renders from the saved Stage B source without rebuilding geometry.

blender -b --factory-startup --python-exit-code 1 --python <script> -- <root> --views ...
"""
import argparse
from pathlib import Path
import sys

import bpy


def main():
    argv=sys.argv[sys.argv.index('--')+1:]
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path)
    parser.add_argument('--views',nargs='+'); parser.add_argument('--samples',type=int,default=32)
    parser.add_argument('--pack-mask',action='store_true',help='Pack the existing mask without changing geometry or shader values')
    args=parser.parse_args(argv); root=args.root.resolve()
    sys.path.insert(0,str(root/'Blender/Scripts'))
    import build_compact_pickup_stage_b as B
    B.verify_approved(root)
    path=root/'Blender/CompactPickup_StageB.blend'; bpy.ops.wm.open_mainfile(filepath=str(path))
    B.A.PARTS=[o for o in bpy.data.objects if o.type=='MESH' and 'engine_role_proposal' in o]
    B.A.MATS={m.name[7:]:m for m in bpy.data.materials if m.name.startswith('Pickup_')}
    if args.pack_mask:
        for image in bpy.data.images:
            if image.name=='Pickup_LowerBodyMask' and not image.packed_file: image.pack()
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(path))
    bpy.context.scene.cycles.samples=args.samples
    if args.views:B.render(bpy.context.scene.camera,root/'Previews/CompactPickup/StageB',args.views)
    B.verify_approved(root)


if __name__=='__main__': main()
