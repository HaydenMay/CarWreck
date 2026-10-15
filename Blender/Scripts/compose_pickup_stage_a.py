"""Catalogue the three supplied sheets; compose comparisons without altering the renders.

python compose_pickup_stage_a.py <root> --references <attachment directory>
All pixel landmarks are manual estimates in the original 1280x960 sheets.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil

from PIL import Image, ImageDraw, ImageFont


CATALOG = [
    (1,'front_low',(0,0,640,353),'perspective, low front three-quarter','lift, tire stance, integrated grille'),
    (1,'side',(643,0,1280,353),'near orthographic side','wheelbase, cab/bed, wheel arch gap'),
    (1,'front_suspension',(0,355,472,690),'perspective detail','front tire, suspension and rocker height'),
    (1,'rear_wheel',(474,355,842,690),'perspective detail','rear arch, tire and bed side'),
    (1,'bed',(844,355,1280,690),'perspective detail','ribbed liner, intruding wheel tubs, bed depth'),
    (1,'lower_door',(0,693,637,960),'perspective detail','continuous cream trim and rocker contour'),
    (1,'rear_low',(639,693,1280,960),'perspective, low rear three-quarter','tailgate, clearance and bed length'),
    (2,'top',(0,0,795,486),'near orthographic top','convex hood plan, short roof, open bed, track'),
    (2,'nose_top',(797,0,1280,486),'perspective, high front','hood crown, shoulder lines and grille'),
    (2,'front',(0,488,512,960),'near orthographic front','rectangular lamps, grille width, track'),
    (2,'grille',(514,488,873,960),'perspective detail','grille integration, bumper depth'),
    (2,'hood_fender',(875,488,1280,960),'perspective detail','convex hood and fender contour'),
    (3,'front34',(0,0,748,462),'perspective front three-quarter','primary design and stock stance'),
    (3,'rear34',(750,0,1280,462),'perspective rear three-quarter','tailgate, bed volume and cab rear'),
    (3,'side',(0,465,625,765),'near orthographic side; PRIMARY proportions','wheelbase, overhangs, hood, cab/bed'),
    (3,'front',(627,465,940,765),'near orthographic front','width, roof taper, grille and ride height'),
    (3,'rear',(942,465,1280,765),'near orthographic rear','tailgate, roof width and rear clearance'),
    (3,'primary_palette',(21,786,438,925),'color chips','forest green, warm cream, dark steel'),
    (3,'alternate_palette',(444,786,867,925),'color chips; secondary direction','independent color A/B; not selected'),
    (3,'alternate34',(869,768,1280,960),'perspective; alternate palette','body design cross-check; blue not selected'),
]

# Original-sheet vehicle bounds for same-scale presentation; never anisotropically resized.
REF_BOUNDS = {
    (3,'front34'):(30,34,739,429), (3,'rear34'):(752,37,1256,392),
    (3,'side'):(21,481,616,719), (2,'top'):(11,38,779,425),
    (2,'front'):(17,495,479,903), (3,'front'):(633,476,922,730),
    (3,'rear'):(948,477,1244,728), (2,'nose_top'):(802,0,1278,452),
    (1,'front_low'):(6,6,637,316), (1,'side'):(681,49,1268,278),
    (1,'rear_low'):(717,694,1247,936),
}


def font(size):
    return ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',size)


def fit(image,size):
    scale=min(size[0]/image.width,size[1]/image.height)
    return image.resize((round(image.width*scale),round(image.height*scale)),Image.Resampling.LANCZOS)


def board(out, label, reference, model, filename, note='Reference crop and geometry render; camera angle approximate for perspective views.'):
    canvas=Image.new('RGB',(1800,710),(24,30,30)); draw=ImageDraw.Draw(canvas)
    draw.text((24,12),label,fill='#eef2ef',font=font(29))
    for x,title,img in ((20,'SUPPLIED REFERENCE',reference),(910,'STAGE A BLOCKOUT',model)):
        draw.text((x,59),title,fill='#c2cfc8',font=font(21))
        im=fit(img,(860,550)); canvas.paste(im,(x+(860-im.width)//2,95+(550-im.height)//2))
    draw.text((24,666),note,fill='#b7c0ba',font=font(19))
    canvas.save(out/filename)
    return canvas


def measures(g):
    extent=lambda b,k:b['max'][k]-b['min'][k]
    length=extent(g['bounds'],1)
    ground=g['bounds']['min'][2]
    front=g['wheel_centers']['Wheel_FL'][1]; rear=g['wheel_centers']['Wheel_RL'][1]
    wb=front-rear
    hood=g['hood_bounds']; cab=g['cabin_bounds']; roof=g['roof_bounds']; bed=g['bed_bounds']
    # Each source includes visible pixel landmarks, so estimates can be audited.
    # Ratios use full bumper-to-bumper length, not a mixture of body and bumper length.
    entries=[
      ('Wheelbase / length',(482-111)/(614-22),wb/length,'Photo3 side: x=111,482; bumpers=22,614','~1.5%',3),
      ('Front overhang / length',(111-22)/592,(g['bounds']['max'][1]-front)/length,'Photo3 side: x=22..111','~2%',3),
      ('Rear overhang / length',(614-482)/592,(rear-g['bounds']['min'][1])/length,'Photo3 side: x=482..614','~2%',3),
      ('Height / length',(715-486)/592,(g['bounds']['max'][2]-ground)/length,'Photo3 side: roof=486, ground=715','~2%',5),
      ('Tire diameter / length',108/592,extent(g['wheel_bounds']['Wheel_FL'],2)/length,'Photo3 side: diameter ~108 px','~3%',5),
      ('Wheel center height / length',54/592,(g['wheel_centers']['Wheel_FL'][2]-ground)/length,'Photo3 side: ground715, center661','~3%',5),
      ('Bed rail height / length',(715-550)/592,(bed['max'][2]-ground)/length,'Photo3 side: rail550, ground715','~2%',5),
      ('Hood incl. front bumper / length',(194-22)/592,(g['bounds']['max'][1]-hood['min'][1])/length,'Photo3 side: front22, screen base194','~3%',5),
      ('Greenhouse base length / length',(355-194)/592,extent(cab,1)/length,'Photo3 side: base194..355','~3%',5),
      ('Roof length / length',(343-249)/592,extent(roof,1)/length,'Photo3 side: roof249..343','~4%',5),
      ('Bed side length / length',(604-360)/592,extent(bed,1)/length,'Photo3 side: bed360..604','~3%',5),
      ('Body width / length - plan',(390-83)/(762-21),extent(g['body_bounds'],0)/length,'Photo2 top: body y83..390; x21..762','~3%',5),
      ('Grille frame / body width',268/445,extent(g['grille_bounds'],0)/extent(g['body_bounds'],0),'Photo2 front: grille x116..384 / body width445','~3%',5),
      ('Track / body width',390/445,(g['wheel_centers']['Wheel_FR'][0]-g['wheel_centers']['Wheel_FL'][0])/extent(g['body_bounds'],0),'Photo2 front: tire centers ~57,447 / body width445','~4%',3),
      ('Cream boundary height / length',(715-614)/592,(g['lower_paint_boundary_m']-ground)/length,'Photo3 side: boundary614, ground715','~3%',5),
      ('Rocker height / length',(715-653)/592,(g['rocker_bottom_m']-ground)/length,'Photo3 side: rocker653, ground715','~4%',5),
      ('Front axle location / length',89/592,(g['bounds']['max'][1]-front)/length,'Photo3 side; measured from nose','~2%',3),
      ('Rear axle location / length',460/592,(g['bounds']['max'][1]-rear)/length,'Photo3 side; measured from nose','~2%',3),
      ('Windscreen rake from vertical deg',math.degrees(math.atan(55/56)),math.degrees(math.atan((cab['max'][1]-roof['max'][1])/(roof['min'][2]-cab['min'][2]))),'Photo3 side: approx dx55 / dz56','~5 degrees',5),
      # Secondary sheet differences are intentionally visible, not silently averaged.
      ('Secondary: bed / length - plan',(755-426)/(762-21),extent(bed,1)/length,'Photo2 top: bed426..755; x21..762','~3%; differs from Photo3',5),
    ]
    rows=[]
    for name,ref,model,source,uncertainty,threshold in entries:
        difference=(model/ref-1)*100
        rows.append({'metric':name,'reference':round(ref,5),'model':round(model,5),
                     'difference_percent':round(difference,2),'flag':abs(difference)>threshold,
                     'threshold_percent':threshold,'source_pixels':source,'reference_uncertainty':uncertainty})
    return {'length_m':length,'body_width_m':extent(g['body_bounds'],0),'mirror_width_m':extent(g['bounds'],0),
            'height_m':g['bounds']['max'][2]-ground,'wheelbase_m':wb,'tire_diameter_m':extent(g['wheel_bounds']['Wheel_FL'],2),
            'bed_side_length_m':extent(bed,1),'bed_inside_depth_m':bed['max'][2]-g['liner_floor_bounds']['max'][2],
            'roof_length_m':extent(roof,1),'roof_width_m':extent(roof,0),'rows':rows}


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path)
    parser.add_argument('--references',type=Path,required=True); args=parser.parse_args()
    root=args.root.resolve(); out=root/'Previews/CompactPickup/StageA'; refs=root/'Previews/CompactPickup/References'
    refs.mkdir(parents=True,exist_ok=True)
    sources={}
    for i in (1,2,3):
        source=args.references/f'{i}-Photo-{i}.jpg'; dest=refs/f'Photo{i}.jpg'
        shutil.copy2(source,dest); sources[i]=Image.open(dest).convert('RGB')
    catalog=[]
    for sheet,view,rect,perspective,features in CATALOG:
        name=f'photo{sheet}_{view}.png'; sources[sheet].crop(rect).save(refs/name)
        catalog.append({'sheet':sheet,'view':view,'crop_pixels':rect,'projection':perspective,'features':features,'crop_file':name})
    reference_manifest={'catalog':catalog,'primary_proportion_view':'Photo3 side',
                        'primary_plan_view':'Photo2 top','primary_palette':'Photo3 forest/cream',
                        'sheet_sha256':{str(i):hashlib.sha256((refs/f'Photo{i}.jpg').read_bytes()).hexdigest() for i in sources}}
    (refs/'reference_catalog.json').write_text(json.dumps(reference_manifest,indent=2)+'\n')
    cameras=json.loads((out/'cameras.json').read_text())
    def rendered(view):
        im=Image.open(out/f'stage_a_{view}.png').convert('RGB')
        box=cameras[view]['vehicle_pixel_bounds']; pad=28
        return im.crop((max(0,int(box[0])-pad),max(0,int(box[1])-pad),min(im.width,int(box[2])+pad),min(im.height,int(box[3])+pad)))
    def reference(sheet,view):
        box=REF_BOUNDS.get((sheet,view))
        if box is None:
            box=next(row[2] for row in CATALOG if row[0]==sheet and row[1]==view)
        return sources[sheet].crop(box)
    core=[]
    for sheet,view,label in ((3,'front34','Front three-quarter - Photo 3'),(3,'side','Side proportions - Photo 3'),
                            (3,'rear34','Rear three-quarter - Photo 3'),(2,'top','Directly above - Photo 2')):
        core.append(board(out,label,reference(sheet,view),rendered(view),f'comparison_{view}.png'))
    overview=Image.new('RGB',(1800,2840))
    for i,im in enumerate(core): overview.paste(im,(0,i*710))
    overview.save(out/'checkpoint_a_overview.png')
    details=[]
    for sheet,ref,view,label in ((2,'nose_top','nose_top','Hood / fenders / integrated grille - Photo 2'),
                               (1,'bed','bed','Bed liner / interior wheel tubs - Photo 1'),
                               (1,'front_low','clearance','Low-angle suspension clearance - Photo 1'),
                               (2,'front','front','Front stance / lamp and grille width - Photo 2')):
        details.append(board(out,label,reference(sheet,ref),rendered(view),f'comparison_detail_{view}.png'))
    detail=Image.new('RGB',(1800,2840))
    for i,im in enumerate(details): detail.paste(im,(0,i*710))
    detail.save(out/'checkpoint_a_details.png')
    board(out,'Secondary side reference - Photo 1',reference(1,'side'),rendered('side'),'comparison_photo1_side.png')
    board(out,'Secondary rear reference - Photo 1',reference(1,'rear_low'),rendered('rear34'),'comparison_photo1_rear.png')
    g=json.loads((out/'geometry_measurements.json').read_text()); m=measures(g)
    m['source_blend_sha256']=hashlib.sha256((root/'Blender/CompactPickup_StageA.blend').read_bytes()).hexdigest()
    (out/'proportion_comparison.json').write_text(json.dumps(m,indent=2)+'\n')
    lines=['# Stage A measured proportion comparison','',
           'Reference values are manual pixel estimates; model dimensions come from saved mesh vertices.',
           'Ratios use full bumper-to-bumper length. Absolute metre scale is a design assumption.',
           'Perspective views are approximate angle matches; lighting and materials differ. No exact-match claim.','',
           '| Metric | Reference | Model | Difference | Flag | Reference evidence / uncertainty |',
           '|---|---:|---:|---:|---|---|']
    for row in m['rows']:
        lines.append(f"| {row['metric']} | {row['reference']:.4f} | {row['model']:.4f} | {row['difference_percent']:+.2f}% | {'REVIEW' if row['flag'] else ''} | {row['source_pixels']}; {row['reference_uncertainty']} |")
    (out/'proportion_comparison.md').write_text('\n'.join(lines)+'\n')
    print(json.dumps({k:v for k,v in m.items() if k!='rows'},indent=2))
    print('FLAGS',json.dumps([r for r in m['rows'] if r['flag']]))
    print('COMPARISONS_WRITTEN',len(core)+len(details)+2)


if __name__=='__main__':
    main()
