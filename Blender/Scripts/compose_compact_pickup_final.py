"""Build Checkpoint C reference/B/final boards and geometry drift tables."""
import argparse
import hashlib
import json
from pathlib import Path
import sys
from PIL import Image,ImageDraw

sys.path.insert(0,str(Path(__file__).parent))
import compose_pickup_stage_a as A
import compose_pickup_stage_b as B


def write(path,value):path.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')


def main():
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path); args=parser.parse_args()
    root=args.root.resolve(); out=root/'Previews/CompactPickup/StageC'; old=root/'Previews/CompactPickup/StageB'; refs=root/'Previews/CompactPickup/References'
    for checkpoint in ('a','b'):
        record=json.loads((root/f'Previews/CompactPickup/approved_checkpoint_{checkpoint}.json').read_text())
        for name,sha in record['files_sha256'].items():assert hashlib.sha256((root/name).read_bytes()).hexdigest()==sha,name
    cameras={s:json.loads((p/'cameras.json').read_text()) for s,p in (('b',old),('c',out))}
    matches={v:all(cameras['b'][v][k]==cameras['c'][v][k] for k in ('position','target','projection','scale','lens_mm')) for v in cameras['b']}
    assert all(matches.values())
    def render(stage,view,box=None):
        folder=old if stage=='b' else out; im=Image.open(folder/f'stage_{stage}_{view}.png').convert('RGB')
        box=box or cameras[stage][view]['vehicle_pixel_bounds']
        return im.crop((max(0,int(box[0])-24),max(0,int(box[1])-24),min(im.width,int(box[2])+24),min(im.height,int(box[3])+24)))
    def ref(sheet,view):
        box=A.REF_BOUNDS.get((sheet,view)) or next(r[2] for r in A.CATALOG if r[0]==sheet and r[1]==view)
        return Image.open(refs/f'Photo{sheet}.jpg').convert('RGB').crop(box)
    core=[]; approved=[]
    for sheet,view,label in ((3,'front34','Front three-quarter'),(3,'side','Side proportions'),(3,'rear34','Rear three-quarter'),(2,'top','Directly above')):
        core.append(B.board(out,label+f' - Photo {sheet}',ref(sheet,view),render('c',view),f'comparison_{view}.png',('SUPPLIED REFERENCE','FINAL MODEL - CHECKPOINT C')))
        a=cameras['b'][view]['vehicle_pixel_bounds']; b=cameras['c'][view]['vehicle_pixel_bounds']; box=[min(a[0],b[0]),min(a[1],b[1]),max(a[2],b[2]),max(a[3],b[3])]
        approved.append(B.board(out,label+' - approved B versus final',render('b',view,box),render('c',view,box),f'approved_b_vs_final_{view}.png',
            ('APPROVED STAGE B','FINAL MODEL - CHECKPOINT C'),'Same cameras, framing, lights and samples; approved B images remain unchanged.'))
    B.stack(out,core,'checkpoint_c_overview.png'); B.stack(out,approved,'approved_b_vs_final_overview.png')
    details=[]
    for sheet,reference,view,label in ((2,'hood_fender','hood_detail','Hood stamps / fender shoulder'),(2,'grille','fascia_detail','Recessed grille / lamps / bumper'),
                                      (1,'front_suspension','wheel_detail','Wheels / clearance / suspension'),(1,'bed','bed','Open bed / liner / wheel tubs')):
        details.append(B.board(out,label,ref(sheet,reference),render('c',view),f'comparison_detail_{view}.png',('SUPPLIED REFERENCE','FINAL MODEL - CHECKPOINT C')))
    B.stack(out,details,'checkpoint_c_details.png')
    mb=A.measures(json.loads((old/'geometry_measurements.json').read_text())); mc=A.measures(json.loads((out/'geometry_measurements.json').read_text()))
    rows=[]
    for b,c in zip(mb['rows'],mc['rows']):
        assert b['metric']==c['metric']; delta=(c['model']/b['model']-1)*100
        rows.append(dict(c,approved_b=b['model'],final=c['model'],b_drift_percent=delta,b_drift_flag=abs(delta)>2))
    dimensions={k:{'approved_b':mb[k],'final':mc[k],'drift_percent':(mc[k]/mb[k]-1)*100} for k in mb if k!='rows'}
    write(out/'proportion_comparison.json',{'rows':rows,'dimensions':dimensions,'max_b_ratio_drift_percent':max(abs(r['b_drift_percent']) for r in rows)})
    lines=['# Reference / approved B / final proportions','',
        'Source pixels and uncertainty remain those recorded at intake. Model values are measured from saved geometry.',
        'Track and secondary plan-bed deviations were accepted at A/B and remain unchanged.','',
        '| Metric | Reference | Approved B | Final | Final vs ref | Final vs B |','|---|---:|---:|---:|---:|---:|']
    for r in rows:lines.append(f"| {r['metric']} | {r['reference']:.5f} | {r['approved_b']:.5f} | {r['final']:.5f} | {r['difference_percent']:+.2f}% | {r['b_drift_percent']:+.4f}% |")
    lines+=['','## Absolute measurements (metres)','', '| Measurement | Approved B | Final | Drift |','|---|---:|---:|---:|']
    for key,v in dimensions.items():lines.append(f"| {key} | {v['approved_b']:.6f} | {v['final']:.6f} | {v['drift_percent']:+.4f}% |")
    (out/'proportion_comparison.md').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    unity=out/'Unity'
    def photos(names,filename,labels):
        canvas=Image.new('RGB',(1920,590*len(names)),(24,30,30)); d=ImageDraw.Draw(canvas)
        for i,(name,label) in enumerate(zip(names,labels)):
            d.text((22,590*i+10),label,fill='#eef2ef',font=A.font(28))
            im=A.fit(Image.open(unity/name).convert('RGB'),(1880,520)); canvas.paste(im,((1920-im.width)//2,590*i+58))
        canvas.save(out/filename)
    photos(['70_CompactPickup_00_undamaged_FL.png','70_CompactPickup_01_Front_FL.png','70_CompactPickup_02_cumulative_9_FL.png','chaos_50_bed.png'],
           'unity_destruction_overview.png',['Actual Unity: undamaged','Actual Unity: 15 m/s front impact - remaining lamp separation is visible',
           'Actual Unity: nine cumulative impacts','Actual Unity: seeded 50-impact Chaos Run - bed and rear'])
    photos(['paint_default.png','paint_primary_only.png','paint_secondary_only.png','paint_deformed.png'],
           'unity_paint_comparison.png',['Default green / cream (blue studio reflection cast)','Only Color A changed; Color B retained','Only Color B changed; Color A retained','Default paint on deformed metal - UV0/UV1 unchanged'])
    manifest={'source_blend_sha256':hashlib.sha256((root/'Blender/CompactPickup.blend').read_bytes()).hexdigest(),
        'generator_sha256':hashlib.sha256((root/'Blender/Scripts/build_compact_pickup_final.py').read_bytes()).hexdigest(),
        'approved_b_revision':'156d2673008b63212f53db4b23784e5bc78c617e','matched_b_cameras':matches,
        'image_sha256':{str(p.relative_to(out)).replace('\\','/'):hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(out.rglob('*'))
            if p.is_file() and p.suffix.lower() in ('.png','.jpg') and p.parent in (out,unity,out/'Experiments/planar_lenses')}}
    write(out/'review_manifest.json',manifest)
    print('CHECKPOINT_C_COMPARISONS',json.dumps({'matched_cameras':len(matches),'ratio_drift_flags':[r['metric'] for r in rows if r['b_drift_flag']],'published_package_images':len(manifest['image_sha256'])}))


if __name__=='__main__':main()
