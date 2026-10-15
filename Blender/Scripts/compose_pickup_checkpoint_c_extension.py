"""Matched-view evidence for the focused C extension; approved galleries are read-only."""
import json,hashlib,sys
from pathlib import Path
from PIL import Image,ImageDraw
sys.path.insert(0,str(Path(__file__).parent))
import compose_pickup_stage_a as A
import compose_pickup_stage_b as B
root=Path(sys.argv[1]);base=root/'Previews/CompactPickup';out=base/'StageC/Revision2';old=base/'StageB'
cameras=json.loads((out/'cameras.json').read_text());approved=json.loads((old/'cameras.json').read_text())
assert all(all(cameras[v][k]==approved[v][k] for k in ('position','target','projection','scale','lens_mm')) for v in cameras)
def model(folder,stage,view):
 im=Image.open(folder/f'stage_{stage}_{view}.png').convert('RGB');b=(cameras if stage=='c' else approved)[view]['vehicle_pixel_bounds']
 return im.crop((max(0,b[0]-24),max(0,b[1]-24),min(im.width,b[2]+24),min(im.height,b[3]+24)))
def ref(sheet,view):
 b=A.REF_BOUNDS.get((sheet,view)) or next(r[2] for r in A.CATALOG if r[0]==sheet and r[1]==view)
 return Image.open(base/f'References/Photo{sheet}.jpg').convert('RGB').crop(b)
boards=[]
for sheet,view,label in ((3,'front34','Front three-quarter'),(3,'side','Side proportions'),(3,'rear34','Rear three-quarter'),(2,'top','Directly above')):
 boards.append(B.board(out,label,ref(sheet,view),model(out,'c',view),f'comparison_{view}.png',('SUPPLIED REFERENCE','FOCUSED C REVISION 2')))
B.stack(out,boards,'reference_overview.png')
boards=[]
for view in ('front34','front','top','hood_detail'):
 boards.append(B.board(out,view+' / approved B versus revision 2',model(old,'b',view),model(out,'c',view),f'approved_b_vs_revision2_{view}.png',('APPROVED B','C REVISION 2 - INDICATORS INSET 22 mm'),'Same camera and lighting; B images retained. Revision2 uses 24 render samples.'))
B.stack(out,boards,'approved_b_overview.png')
boards=[]
for sheet,refview,view,label in ((2,'hood_fender','hood_detail','Hood stamps and shoulders'),(2,'grille','fascia_detail','Integrated fascia and lamps'),(1,'front_suspension','wheel_detail','Raised stance and suspension'),(1,'bed','bed','Open bed / liner / wheel tubs')):
 boards.append(B.board(out,label,ref(sheet,refview),model(out,'c',view),f'comparison_detail_{view}.png',('SUPPLIED REFERENCE','C REVISION 2')))
B.stack(out,boards,'detail_overview.png')
before=A.measures(json.loads((old/'geometry_measurements.json').read_text()));after=A.measures(json.loads((out/'geometry_measurements.json').read_text()))
rows=[dict(c,approved_b=b['model'],drift_percent=(c['model']/b['model']-1)*100) for b,c in zip(before['rows'],after['rows'])]
lines=['# Measured proportions','', 'All approved vehicle dimensions, wheel positions, stance and paint boundary are retained. Each indicator is translated inward by 22 mm (1.1% of body width); source surface comparison to B has a 23.63 mm maximum exposed difference including the prior C radius correction.','', '|Metric|Reference|Approved B|Revision2|vs reference|vs B|','|---|---:|---:|---:|---:|---:|']
for r in rows:lines.append(f"|{r['metric']}|{r['reference']:.5f}|{r['approved_b']:.5f}|{r['model']:.5f}|{r['difference_percent']:+.2f}%|{r['drift_percent']:+.5f}%|")
lines+=['','Accepted reference differences remain: estimated track +4.69% and conflicting top-sheet bed estimate -7.23%. Side-sheet bed ratio remains within 0.06%. Roof material is green; the pale strip is studio reflection. Headlight microfluting and lamp color/blue Unity reflections retain the previously documented differences.']
(out/'proportions.md').write_text('\n'.join(lines)+'\n')
print('EXTENSION_COMPARISONS',len(cameras),'matched cameras; max B proportion drift',max(abs(r['drift_percent']) for r in rows))
