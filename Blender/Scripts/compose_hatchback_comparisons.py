"""Package actual matched Blender renders into labeled comparison boards (Pillow).

Run with regular Python: python <this script> <project root>
Full comparison pairs preserve each 1400 x 900 render without cropping or filtering.
The two overview sheets use identical proportional resizing for their thumbnails.
"""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

LABELS={'front34':'Front three-quarter','side':'Full side','rear34':'Rear three-quarter',
        'front_fender':'Front fender','rear_fender':'Rear fender','fascia':'Front fascia',
        'wireframe':'Body and cabin wireframe','front':'Full front','nose_top':'Near top-down nose',
        'nose_plan':'True orthographic hood / nose'}

def font(size):
    for path in ('C:/Windows/Fonts/segoeuib.ttf','/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf'):
        if Path(path).exists(): return ImageFont.truetype(path,size)
    return ImageFont.load_default(size=size)

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path)
    parser.add_argument('--output',type=Path); args=parser.parse_args()
    root=args.root.resolve(); out=root/(args.output or 'Previews/CompactHatchback/StageC')
    manifest=json.loads((out/'comparison_manifest.json').read_text())
    assert manifest['identical_conditions'] and manifest['identical_cameras']
    assert manifest['source_sha256']['stage_c']==hashlib.sha256((root/'Blender/CompactHatchback.blend').read_bytes()).hexdigest(),'Renders use an older C source'
    pairs={}
    for view,label in LABELS.items():
        if view not in manifest['outputs']['stage_c']: continue
        images=[Image.open(out/manifest['outputs'][stage][view]).convert('RGB') for stage in ('stage_b','stage_c')]
        assert all(i.size==(1400,900) for i in images)
        canvas=Image.new('RGB',(2848,1012),(24,29,34)); draw=ImageDraw.Draw(canvas)
        draw.text((24,14),label,fill=(240,241,242),font=font(29))
        labels=manifest.get('labels',{'stage_b':'Stage B','stage_c':'Stage C'})
        draw.text((24,57),labels['stage_b'].upper(),fill=(196,202,210),font=font(24))
        draw.text((1448,57),labels['stage_c'].upper(),fill=(196,202,210),font=font(24))
        for x,img in zip((24,1448),images): canvas.paste(img,(x,88))
        filename='comparison_'+view+'.png'; canvas.save(out/filename); pairs[view]=filename
    overviews=[]
    for filename,views in (('comparison_overview.png',('front34','side','rear34')),
                           ('detail_overview.png',('front_fender','rear_fender','fascia')),
                           ('nose_overview.png',('nose_top','front','front34')),
                           ('final_outline_overview.png',('nose_plan','front34','front'))):
        if not all(view in pairs for view in views): continue
        width=1800; row_height=650
        canvas=Image.new('RGB',(width,row_height*3),(24,29,34))
        for row,view in enumerate(views):
            image=Image.open(out/pairs[view]); image.thumbnail((width,row_height),Image.Resampling.LANCZOS)
            canvas.paste(image,((width-image.width)//2,row*row_height))
        canvas.save(out/filename)
        overviews.append(filename)
    manifest['comparison_boards']=pairs
    manifest['overview_boards']=overviews
    (out/'comparison_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print('COMPARISON_BOARDS_PASS',json.dumps(pairs))

if __name__=='__main__': main()
