"""Checkpoint B gallery: reference/B and approved A/B boards, measured drift and provenance.

python compose_pickup_stage_b.py <root>
"""
import argparse
import hashlib
import json
from pathlib import Path
import sys

from PIL import Image,ImageDraw

sys.path.insert(0,str(Path(__file__).parent))
import compose_pickup_stage_a as A


def board(out,label,left,right,filename,titles=('SUPPLIED REFERENCE','STAGE B DETAILED MODEL'),note='Perspective references have unknown lenses; angle matching is approximate.'):
    canvas=Image.new('RGB',(1800,710),(24,30,30)); draw=ImageDraw.Draw(canvas)
    draw.text((24,12),label,fill='#eef2ef',font=A.font(29))
    for x,title,image in ((20,titles[0],left),(910,titles[1],right)):
        draw.text((x,59),title,fill='#c2cfc8',font=A.font(21))
        im=A.fit(image,(860,550)); canvas.paste(im,(x+(860-im.width)//2,95+(550-im.height)//2))
    draw.text((24,666),note,fill='#b7c0ba',font=A.font(19)); canvas.save(out/filename)
    return canvas


def stack(out,boards,filename):
    canvas=Image.new('RGB',(1800,710*len(boards)))
    for i,im in enumerate(boards):canvas.paste(im,(0,i*710))
    canvas.save(out/filename)


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path); args=parser.parse_args()
    root=args.root.resolve(); out=root/'Previews/CompactPickup/StageB'; old=root/'Previews/CompactPickup/StageA'
    refs=root/'Previews/CompactPickup/References'
    approved=json.loads((root/'Previews/CompactPickup/approved_checkpoint_a.json').read_text())
    for path,value in approved['files_sha256'].items():assert hashlib.sha256((root/path).read_bytes()).hexdigest()==value,path
    cameras={stage:json.loads((folder/'cameras.json').read_text()) for stage,folder in (('a',old),('b',out))}
    common=set(cameras['a'])&set(cameras['b']); camera_checks={}
    for view in common:
        keys=('position','target','projection','scale','lens_mm')
        camera_checks[view]=all(cameras['a'][view][k]==cameras['b'][view][k] for k in keys)
    assert all(camera_checks.values()),'A/B camera mismatch'
    def cropped(stage,view,box=None):
        im=Image.open((old if stage=='a' else out)/f'stage_{stage}_{view}.png').convert('RGB')
        box=box or cameras[stage][view]['vehicle_pixel_bounds']; pad=24
        return im.crop((max(0,int(box[0])-pad),max(0,int(box[1])-pad),min(im.width,int(box[2])+pad),min(im.height,int(box[3])+pad)))
    def reference(sheet,view):
        box=A.REF_BOUNDS.get((sheet,view)) or next(r[2] for r in A.CATALOG if r[0]==sheet and r[1]==view)
        return Image.open(refs/f'Photo{sheet}.jpg').convert('RGB').crop(box)
    core=[]; baseline=[]
    for sheet,view,label in ((3,'front34','Front three-quarter'),(3,'side','Side proportions'),(3,'rear34','Rear three-quarter'),(2,'top','Directly above')):
        core.append(board(out,label+f' - Photo {sheet}',reference(sheet,view),cropped('b',view),f'comparison_{view}.png'))
        a=cameras['a'][view]['vehicle_pixel_bounds']; b=cameras['b'][view]['vehicle_pixel_bounds']
        union=[min(a[0],b[0]),min(a[1],b[1]),max(a[2],b[2]),max(a[3],b[3])]
        baseline.append(board(out,label+' - approved A versus B',cropped('a',view,union),cropped('b',view,union),f'approved_a_vs_b_{view}.png',
                              ('APPROVED STAGE A','STAGE B DETAILED MODEL'),'Identical cameras, framing and studio lights; approved A images remain unchanged.'))
    stack(out,core,'checkpoint_b_overview.png'); stack(out,baseline,'approved_a_vs_b_overview.png')
    details=[]
    for sheet,ref,view,label in ((2,'hood_fender','hood_detail','Hood stamps / shoulder contour'),
                               (2,'grille','fascia_detail','Recessed grille / lamps / bumper'),
                               (1,'front_suspension','wheel_detail','Steel wheel / tread / suspension clearance'),
                               (1,'bed','bed','Corrugated liner / wheel tubs / rails')):
        details.append(board(out,label+f' - Photo {sheet}',reference(sheet,ref),cropped('b',view),f'comparison_detail_{view}.png'))
    stack(out,details,'checkpoint_b_details.png')
    board(out,'Cabin and roof',reference(3,'front34'),cropped('b','cabin_detail'),'comparison_cabin.png')
    g_a=json.loads((old/'geometry_measurements.json').read_text()); g_b=json.loads((out/'geometry_measurements.json').read_text())
    ma=A.measures(g_a); mb=A.measures(g_b); rows=[]
    for a,b in zip(ma['rows'],mb['rows']):
        assert a['metric']==b['metric']
        drift=(b['model']/a['model']-1)*100
        row=dict(b,stage_a_model=a['model'],stage_b_model=b['model'],stage_a_drift_percent=round(drift,4),stage_a_drift_flag=abs(drift)>2)
        row['accepted_reference_difference']=b['metric'] in ('Track / body width','Secondary: bed / length - plan')
        rows.append(row)
    checks=json.loads((out/'modeling_checks.json').read_text()); a_checks=json.loads((old/'blockout_checks.json').read_text())
    # Stage A's old table measured to the base sheet; use the actual rib-top ray for both stages.
    ma['bed_clear_depth_m']=g_a['bed_bounds']['max'][2]-a_checks['bed_center_ray']['height_m']
    mb['bed_clear_depth_m']=g_b['bed_bounds']['max'][2]-checks['bed_ray']['point'][2]
    sha=hashlib.sha256((root/'Blender/CompactPickup_StageB.blend').read_bytes()).hexdigest()
    assert sha==checks['source_blend_sha256'],'Refresh modeling checks for the saved scene'
    values={k:{'approved_a':ma[k],'stage_b':mb[k],'drift_percent':(mb[k]/ma[k]-1)*100} for k in ma if k not in ('rows','bed_inside_depth_m')}
    comparison={'stage':'B','approved_a_revision':approved['approved_revision'],'rows':rows,'dimensions':values,
                'source_blend_sha256':sha,'max_ratio_drift_percent':max(abs(r['stage_a_drift_percent']) for r in rows),
                'bed_depth_note':'Both clear-depth values use actual rib-top ray hits; the A table formerly used the base liner sheet.'}
    (out/'proportion_comparison.json').write_text(json.dumps(comparison,indent=2)+'\n')
    lines=['# Stage B proportion comparison','',
           'Reference values retain the original pixel estimates and uncertainties. Model values come from saved mesh geometry.',
           'The user accepted the earlier bed-length and track deviations; they remain reported, not corrected away.','',
           '| Metric | Reference | Approved A | Stage B | B vs ref | B vs A | Notes |',
           '|---|---:|---:|---:|---:|---:|---|']
    for r in rows:
        note='Accepted reference difference' if r['accepted_reference_difference'] else ''
        if r['stage_a_drift_flag']:note+=' DRIFT >2%'
        lines.append(f"| {r['metric']} | {r['reference']:.4f} | {r['stage_a_model']:.4f} | {r['stage_b_model']:.4f} | {r['difference_percent']:+.2f}% | {r['stage_a_drift_percent']:+.4f}% | {note} |")
    lines+=['','## Absolute geometry','', '| Measurement | Approved A (m) | Stage B (m) | Drift |','|---|---:|---:|---:|']
    for key,v in values.items():lines.append(f"| {key} | {v['approved_a']:.6f} | {v['stage_b']:.6f} | {v['drift_percent']:+.4f}% |")
    lines+=['',comparison['bed_depth_note']]
    (out/'proportion_comparison.md').write_text('\n'.join(lines)+'\n')
    images=sorted(out.glob('*.png'))
    manifest={'source_blend_sha256':sha,'generator_sha256':hashlib.sha256((root/'Blender/Scripts/build_compact_pickup_stage_b.py').read_bytes()).hexdigest(),
              'approved_a_revision':approved['approved_revision'],'matched_a_cameras':camera_checks,
              'studio':'Same studio light positions/power and AgX as A; A 24 samples, B 32 samples; paint specular disabled only in roof_color diagnostic.',
              'image_sha256':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in images}}
    (out/'review_manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    print('CHECKPOINT_B_COMPARISONS',json.dumps({'reference_boards':len(core)+len(details)+1,'approved_a_boards':len(baseline),
                                                'matched_cameras':len(common),'max_ratio_drift_percent':comparison['max_ratio_drift_percent'],
                                                'dimensions':values}),flush=True)


if __name__=='__main__':main()
