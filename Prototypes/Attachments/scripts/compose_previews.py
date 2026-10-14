"""Add exact projected diagnostic overlays and assemble unchanged raw render panels."""
import json
import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

BASE=Path(__file__).resolve().parents[1]
report=json.loads((BASE/"evidence/results.json").read_text())
annotations=json.loads((BASE/"evidence/render_annotations.json").read_text())
OUT=BASE/"previews"
OUT.mkdir(exist_ok=True)
BG=(15,25,39)
WHITE=(237,242,248)
YELLOW=(255,214,28)
MAGENTA=(255,53,157)
ORANGE=(255,107,48)
FONT_PATH=Path("C:/Windows/Fonts")
def font(size,bold=False):
    return ImageFont.truetype(str(FONT_PATH/("segoeuib.ttf" if bold else "segoeui.ttf")),size)

TITLES={
"01_hatch_installed":"THREE ACCESSORIES / UNDAMAGED HATCHBACK",
"02_scoop_rest":"HOOD SCOOP / UNDAMAGED",
"03_scoop_moderate":"HOOD SCOOP / MODERATE DEFORMATION",
"04_scoop_severe":"HOOD SCOOP / SEVERE FOLD: INVALID SUPPORT",
"05_scoop_repair":"HOOD SCOOP / REPAIRED",
"06_rack_rest":"ROOF RACK / UNDAMAGED HATCHBACK",
"07_rack_roof_dent":"ROOF RACK / FIXED FRAME, FAILED ROOF CONTACT",
"08_pushbar_rest":"PUSH BAR / UNDAMAGED",
"09_pushbar_crush":"PUSH BAR / ASYMMETRIC SUPPORT FAILURE",
"10_muscle_rack":"SAME ROOF RACK / MUSCLE-CAR ADAPTER"}

def dashed(draw,a,b,color,width=2):
    distance=math.dist(a,b)
    if distance<10:
        return
    steps=max(1,int(distance/9))
    for i in range(0,steps,2):
        p=tuple(a[j]+(b[j]-a[j])*i/steps for j in range(2))
        q=tuple(a[j]+(b[j]-a[j])*min(i+1,steps)/steps for j in range(2))
        draw.line([p,q],fill=color,width=width)

def annotate(key):
    raw=Image.open(BASE/"renders"/(key+".png")).convert("RGB")
    header,footer=98,124
    canvas=Image.new("RGB",(raw.width,raw.height+header+footer),BG)
    canvas.paste(raw,(0,header))
    draw=ImageDraw.Draw(canvas)
    source_key=key
    for suffix in ("_top","_front","_detail"):
        if source_key not in report["cases"] and source_key.endswith(suffix):
            source_key=source_key[:-len(suffix)]
    case=report["cases"][source_key]
    title=TITLES[source_key]
    if key!=source_key:
        title+=" / "+key.split("_")[-1].upper()
    draw.text((24,12),title,font=font(25,True),fill=WHITE)
    mounts=case["mounts"]
    mount=next(iter(mounts.values()))
    if source_key=="03_scoop_moderate":
        sub=f'{mount["translation_change_m"]*1000:.1f} mm translation | {mount["rotation_change_deg"]:.2f} deg rotation | {mount["candidate"]["max_residual_m"]*1000:.1f} mm max error'
    elif mount["status"]=="invalid_support":
        sub=f'Best rigid fit: {mount["candidate"]["max_residual_m"]*1000:.1f} mm error | Applied/frozen feet: {mount["applied"]["max_residual_m"]*1000:.1f} mm error'
    elif len(mounts)>1:
        sub="Four rack feet | Four scoop anchors | Two push-bar brackets, four mounting points"
    else:
        sub=f'Max rigid-fit mounting error: {mount["candidate"]["max_residual_m"]*1000:.2f} mm | Scale = 1.0'
    draw.text((24,51),sub,font=font(20),fill=WHITE)
    for note in annotations[key]["points"]:
        if not note["in_frame"]:
            raise ValueError("anchor outside render: "+key+" "+note["id"])
        a=(note["pixel"][0],note["pixel"][1]+header)
        b=(note["foot_pixel"][0],note["foot_pixel"][1]+header)
        dashed(draw,a,b,ORANGE,3)
        draw.ellipse((b[0]-9,b[1]-9,b[0]+9,b[1]+9),outline=MAGENTA,width=3)
        draw.ellipse((a[0]-4,a[1]-4,a[0]+4,a[1]+4),fill=YELLOW,outline=BG,width=1)
        draw.line((a[0]-7,a[1],a[0]+7,a[1]),fill=YELLOW,width=2)
        draw.line((a[0],a[1]-7,a[0],a[1]+7),fill=YELLOW,width=2)
        ident=note["id"]
        left=note["world"][0]<0
        x=a[0]+(-62 if left else 22)
        y=a[1]+(15 if int(ident[1:])<=2 else -46)
        x=max(6,min(raw.width-52,x))
        y=max(header+4,min(header+raw.height-36,y))
        draw.line([a,(x+22,y+16)],fill=YELLOW,width=1)
        draw.rounded_rectangle((x,y,x+44,y+31),radius=4,fill=BG,outline=YELLOW,width=1)
        draw.text((x+7,y+1),ident,font=font(19,True),fill=YELLOW)
    y=header+raw.height+10
    draw.text((24,y),"YELLOW +  surface anchor     MAGENTA O  rigid foot     ORANGE - -  gap     WHITE wire  root-only pose",font=font(17,True),fill=WHITE)
    draw.text((24,y+28),"X-ray overlay shows occluded anchors. Errors include the authored 3 mm clearance target.",font=font(17),fill=WHITE)
    if source_key=="07_rack_roof_dent":
        line="LIMITATION: roof separation needs future mounting response or detachment; unacceptable as final gameplay behavior."
    elif source_key=="04_scoop_severe":
        line="LIMITATION: rigid scoop cannot follow this fold. Last valid pose retained to expose the failed connection."
    elif source_key=="09_pushbar_crush":
        line="LIMITATION: rigid brackets cannot satisfy diverging supports. No strength or detachment physics is simulated."
    else:
        line="Synthetic Blender displacement only. No Unity integration or impact/force physics."
    draw.text((24,y+57),line,font=font(16),fill=ORANGE if mount["status"]=="invalid_support" else WHITE)
    errors=[]
    for part,data in mounts.items():
        errors.extend(ident+f' {error*1000:.1f}' for ident,error in zip(data["mount_ids"],data["applied"]["residuals_m"]))
    draw.text((24,y+83),"Applied foot error, mm:  "+" | ".join(errors),font=font(16),fill=WHITE)
    canvas.save(OUT/(key+".png"))
    return canvas

panels={Path(p).stem:annotate(Path(p).stem) for p in report["expected_renders"]}

def board(name,keys):
    w,h=panels[keys[0]].size
    canvas=Image.new("RGB",(w*2+16,h*2+16),BG)
    for i,key in enumerate(keys):
        canvas.paste(panels[key],((i%2)*(w+16),(i//2)*(h+16)))
    canvas.save(OUT/(name+".png"))

board("hood_scoop_comparison",["02_scoop_rest","03_scoop_moderate","04_scoop_severe","05_scoop_repair"])
board("roof_rack_comparison",["06_rack_rest","07_rack_roof_dent","10_muscle_rack","10_muscle_rack_detail"])
board("push_bar_comparison",["08_pushbar_rest","09_pushbar_crush","08_pushbar_rest_front","01_hatch_installed"])
print("PREVIEWS:",len(panels),"labelled views and 3 comparison boards")
