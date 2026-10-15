"""Detailed pickup, preserving approved Stage A. Blender 5.2, metres, +Y forward.

blender -b --factory-startup --python-exit-code 1 --python <script> -- <root> [--render] [--views ...]
Writes only Blender source and Stage B authoring/review artifacts. No Unity integration.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector

sys.path.insert(0,str(Path(__file__).parent))
import build_compact_pickup as A

SPACING=.032


def lerp(a,b,t): return a+(b-a)*t
def smooth(a,b,x):
    t=max(0,min(1,(x-a)/(b-a)))
    return t*t*(3-2*t)


def stations(a,b,step=SPACING,extra=()):
    n=max(1,math.ceil((b-a)/step))
    values=[a+(b-a)*i/n for i in range(n+1)]+[v for v in extra if a<v<b]
    result=[]
    for v in sorted(values):
        if not result or v-result[-1]>1e-5: result.append(v)
    return result


class Mesh:
    def __init__(self): self.v=[]; self.f=[]; self.m=[]; self.s=[]
    def vertex(self,p): self.v.append(tuple(p)); return len(self.v)-1
    def face(self,ids,mat,smooth_shading=True):
        self.f.append(tuple(ids)); self.m.append(mat); self.s.append(smooth_shading)
    def grid(self,us,vs,fn,mat,material_fn=None,omit=None):
        ids=[[self.vertex(fn(u,v)) for u in us] for v in vs]
        for j in range(len(vs)-1):
            for i in range(len(us)-1):
                u=(us[i]+us[i+1])*.5; v=(vs[j]+vs[j+1])*.5
                if omit and omit(u,v): continue
                self.face((ids[j][i],ids[j][i+1],ids[j+1][i+1],ids[j+1][i]),material_fn(u,v) if material_fn else mat)
        return ids
    def rings(self,rings,mat,closed_section=False):
        ids=[[self.vertex(p) for p in row] for row in rings]
        for i in range(len(ids) if closed_section else len(ids)-1):
            a=ids[i]; b=ids[(i+1)%len(ids)]
            for j in range(len(a)):
                k=(j+1)%len(a); self.face((a[j],a[k],b[k],b[j]),mat)
    def box(self,center,size,mat,radius=.008,spacing=SPACING,xf=None,stamp=None):
        half=[v*.5 for v in size]; radius=min(radius,min(half)*.90)
        for axis in range(3):
            other=[i for i in range(3) if i!=axis]
            for sign in (-1,1):
                us=stations(-half[other[0]],half[other[0]],spacing)
                vs=stations(-half[other[1]],half[other[1]],spacing)
                def point(u,v):
                    p=Vector((0,0,0)); p[axis]=sign*half[axis]; p[other[0]]=u; p[other[1]]=v
                    core=Vector([max(-half[k]+radius,min(half[k]-radius,p[k])) for k in range(3)])
                    d=p-core
                    if radius and d.length: p=core+d.normalized()*radius
                    if stamp: p=Vector(stamp(tuple(p),axis,sign)[0])
                    p=Vector(xf(p) if xf else p)+Vector(center)
                    return p
                def face_mat(u,v):
                    if not stamp: return mat
                    p=[0,0,0]; p[axis]=sign*half[axis]; p[other[0]]=u; p[other[1]]=v
                    return stamp(p,axis,sign)[1] or mat
                self.grid(us,vs,point,mat,face_mat)
    def lathe(self,profile,center,mat,n=64,side=1):
        rows=[]
        for axial,r in profile:
            if r<1e-7: rows.append([self.vertex((center[0]+side*axial,center[1],center[2]))])
            else: rows.append([self.vertex((center[0]+side*axial,center[1]+r*math.cos(i*math.tau/n),center[2]+r*math.sin(i*math.tau/n))) for i in range(n)])
        for a,b in zip(rows,rows[1:]):
            for i in range(n):
                k=(i+1)%n
                if len(a)==1 and len(b)>1: self.face((a[0],b[k],b[i]),mat)
                elif len(b)==1 and len(a)>1: self.face((a[i],a[k],b[0]),mat)
                elif len(a)>1 and len(b)>1: self.face((a[i],a[k],b[k],b[i]),mat)
    def tube(self,path,radius,mat,n=10,cap=True):
        rings=[]
        for i,point in enumerate(path):
            tangent=Vector(path[min(i+1,len(path)-1)])-Vector(path[max(0,i-1)])
            tangent.normalize(); ref=Vector((0,0,1)) if abs(tangent.z)<.9 else Vector((0,1,0))
            u=tangent.cross(ref).normalized(); v=tangent.cross(u).normalized()
            rings.append([Vector(point)+radius*(u*math.cos(j*math.tau/n)+v*math.sin(j*math.tau/n)) for j in range(n)])
        self.rings(rings,mat)
        if cap:
            for row,point in ((rings[0],path[0]),(rings[-1],path[-1])):
                c=self.vertex(point); ids=[self.vertex(p) for p in row]
                for j in range(n): self.face((c,ids[j],ids[(j+1)%n]),mat,False)
    def rod(self,a,b,radius,mat,n=16):
        distance=(Vector(b)-Vector(a)).length; count=max(2,math.ceil(distance/.03))
        self.tube([Vector(a).lerp(Vector(b),i/count) for i in range(count+1)],radius,mat,n)
    def object(self,name,role,origin=(0,0,0)):
        ob=A.mesh(name,self.v,self.f,next(iter(A.MATS)),role)
        ob.data.materials.clear(); names=list(A.MATS)
        for key in names: ob.data.materials.append(A.MATS[key])
        for p,mat,shading in zip(ob.data.polygons,self.m,self.s):
            p.material_index=names.index(mat); p.use_smooth=shading
        if origin!=(0,0,0):
            for v in ob.data.vertices: v.co-=Vector(origin)
            ob.location=origin
        ob['stage']='B - detailed geometry; Unity validation pending'
        return ob


def rr_loop(hw,hh,r,nx,ny,nc=5):
    result=[]
    centers=((hw-r,hh-r),(-hw+r,hh-r),(-hw+r,-hh+r),(hw-r,-hh+r))
    for k,(cx,cz) in enumerate(centers):
        start=k*math.pi/2
        for j in range(nc):
            a=start+j*math.pi/2/nc; result.append((cx+r*math.cos(a),cz+r*math.sin(a)))
        a=start+math.pi/2
        p=(cx+r*math.cos(a),cz+r*math.sin(a))
        qx,qz=centers[(k+1)%4]; q=(qx+r*math.cos(a),qz+r*math.sin(a))
        n=nx if k%2==0 else ny
        for j in range(n): result.append((lerp(p[0],q[0],j/n),lerp(p[1],q[1],j/n)))
    return result


def frame(m,center,hw,hh,mat='Grille'):
    nx=max(4,math.ceil(2*hw/.024)); ny=max(4,math.ceil(2*hh/.024))
    profiles=((hw,hh,-.055),(hw,hh,-.006),(hw-.006,hh-.006,.004),
              (hw-.023,hh-.023,.003),(hw-.027,hh-.027,-.013),(hw-.027,hh-.027,-.060))
    m.rings([[(x+center[0],center[1]+depth,z+center[2]) for x,z in rr_loop(w,h,.018,nx,ny)] for w,h,depth in profiles],mat,True)


def arch(y,axle):
    t=abs(y-axle)/.535
    return max(.505,.42+.565*max(0,1-t**3.4)**(1/3.4)) if t<1 else .505


def side_x(y,z,width,axle=None,start=-10,end=10):
    x=width-.035*max(0,(.90-z)/.395)**2
    # Small stamped character grooves, never floating ribbons or thin double skins.
    x-=.004*math.exp(-((z-1.17)/.015)**2)
    x-=.003*math.exp(-((z-.624)/.025)**2)
    for edge in (start+.018,end-.018): x-=.003*math.exp(-((y-edge)/.008)**2)
    if axle is not None:
        distance=z-arch(y,axle)
        x-=.012*(1-math.exp(-max(0,distance)/.025))*math.exp(-max(0,distance)/.12)
    return x


def side_panel(name,start,end,axle=None,top=1.31,width=.97):
    ys=stations(start,end,.035,(start+.012,start+.02,end-.02,end-.012))
    if axle is not None:
        # Adaptive arc stations prevent sparse vertical arch transitions.
        for i in range(29):
            angle=math.pi*.5*i/28
            d=.535*math.sin(angle)**(2/3.4)
            for y in (axle-d,axle+d):
                if start<y<end: ys.append(y)
        ys=sorted(set(round(y,7) for y in ys))
        ys=[v for i,v in enumerate(ys) if i==0 or v-ys[i-1]>.0005]
    vs=stations(0,1,1/24)
    for side in (-1,1):
        m=Mesh()
        def point(y,t):
            bottom=arch(y,axle) if axle is not None else .505
            h=top(y) if callable(top) else top; z=lerp(bottom,h,t)
            w=width(y) if callable(width) else width
            return (side*side_x(y,z,w,axle,start,end),y,z)
        m.grid(ys,vs,point,'Paint')
        if axle is not None:
            # Roll the cut edge inward; no parallel back skin over the side panel.
            m.grid(ys,stations(0,.035,.014),lambda y,d:(point(y,0)[0]-side*d,y,point(y,0)[2]+d*.12),'Paint')
        m.object(name+('_L' if side<0 else '_R'),'Body')


def hood_height(y,x):
    t=(y-.98)/1.27; w=.97-.035*t; u=x/w
    crown=.028*(1-(x/.96)**2)
    window=smooth(.985,1.105,y)*(1-smooth(2.105,2.235,y))
    ridges=(.010*smooth(.773,.803,abs(u))+.007*smooth(.386,.414,abs(u)))*window
    seam=-.0025*math.exp(-((abs(u)-.88)/.006)**2)
    return 1.285-.108*t+crown+ridges+seam


def body():
    side_panel('FrontFender',.98,2.25,A.FRONT_AXLE,lambda y:hood_height(y,.97-.035*(y-.98)/1.27),lambda y:.97-.035*(y-.98)/1.27)
    side_panel('Door',-.27,.98,top=1.29)
    side_panel('BedSide',-2.26,-.315,A.REAR_AXLE)
    us=stations(-1,1,.028,tuple(s*(u+d) for s in (-1,1) for u in (.40,.79,.88) for d in (-.012,0,.012)))
    m=Mesh(); m.grid(us,stations(.98,2.25,.026),lambda u,y:((.97-.035*(y-.98)/1.27)*u,y,hood_height(y,(.97-.035*(y-.98)/1.27)*u)),
                     'Paint',lambda u,y:'Mirror' if .875<abs(u)<.885 else 'Paint')
    m.object('Body_Hood','Body')
    m=Mesh(); m.box((0,-.247,.925),(1.90,.045,.84),'Paint',.012,.050)
    m.object('Body_CabRear','Body')
    # Dense front sheet with true fascia openings. A deep rear sheet closes the nose.
    m=Mesh(); xs=stations(-.94,.94,.028,(-.904,-.882,-.602,-.595,.595,.602,.882,.904))
    zs=stations(.795,1.175,.025,(.8625,.87,1.1475,1.155))
    def opening(x,z): return (abs(x)<.595 and .8625<z<1.1475) or (.602<abs(x)<.882 and .87<z<1.155)
    m.grid(xs,zs,lambda x,z:(x,2.255,z),'Paint',omit=opening)
    m.grid(xs,zs,lambda x,z:(x,2.185,z),'Paint')
    for z in (.795,1.175): m.grid(xs,stations(2.185,2.255,.025),lambda x,y:(x,y,z),'Paint')
    for x in (-.94,.94): m.grid(stations(2.185,2.255,.025),zs,lambda y,z:(x,y,z),'Paint')
    m.object('Body_Nose','Body')


def front():
    m=Mesh(); frame(m,(0,2.281,1.005),.595,.1425,'BrightEdge'); m.object('Grille_Frame','Fascia')
    m=Mesh(); m.box((0,2.175,1.005),(1.145,.030,.248),'Grille',.008,.025)
    for z in (.925,1.005,1.085): m.box((0,2.224,z),(1.12,.032,.019),'BrightEdge',.006,.022)
    for x in (-.28,.28): m.box((x,2.207,1.005),(.018,.025,.24),'Grille',.003,.022)
    m.object('Grille_Inset','Fascia')
    for side in (-1,1):
        suffix='L' if side<0 else 'R'; x=side*.742
        m=Mesh(); frame(m,(x,2.281,1.013),.14,.1425,'Grille'); m.object('HeadlightHousing_'+suffix,'Light')
        m=Mesh()
        m.grid(stations(-.115,.115,.009),stations(-.108,.108,.009),
               lambda u,v:(x+u,2.235+.035*((u/.115)**2+(v/.108)**2)*.5,1.017+v),'BrightEdge')
        m.box((x,2.300,1.017),(.24,.028,.23),'Headlight',.018,.014)
        m.object('Headlight_'+suffix,'Light')
        m=Mesh(); m.box((side*.934,2.273,1.016),(.060,.057,.267),'Amber',.008,.009)
        m.object('FrontIndicator_'+suffix,'Light')
    def stamp(p,axis,sign):
        p=list(p); mat=None
        # Coordinate-based shaping also moves adjoining faces at shared borders.
        p[1]-=.008*smooth(.65,.80,abs(p[0]))
        if axis==1 and sign>0:
            x,z=p[0],p[2]+.697
            if .23<abs(x)<.59 and .602<z<.660:
                p[1]-=.035; mat='Grille'
        return p,mat
    m=Mesh(); m.box((0,2.26,.697),(1.99,.18,.235),'Bumper',.033,.035,stamp=stamp)
    m.object('Bumper_Front','Bumper')
    for side in (-1,1):
        m=Mesh(); frame(m,(side*.77,2.348,.707),.102,.050,'Grille')
        m.box((side*.77,2.351,.707),(.18,.018,.078),'Amber',.007,.009)
        m.object('BumperMarker_'+('L' if side<0 else 'R'),'Light')
    m=Mesh(); m.box((0,2.356,.66),(.37,.012,.16),'Mirror',.004,.018); m.object('FrontPlate','Trim')


def cabin():
    # One continuous surface per face: paint -> seal -> inset glass, no hidden painted skin.
    faces=[('Cabin_Windshield',[(-.945,.98,1.285),(.945,.98,1.285),(.822,.52,1.79),(-.822,.52,1.79)],(0,1,0)),
           ('Cabin_RearGlass',[(.955,-.27,1.285),(-.955,-.27,1.285),(-.822,-.20,1.79),(.822,-.20,1.79)],(0,-1,0))]
    for side in (-1,1):
        faces.append(('Cabin_SideGlass_'+('L' if side<0 else 'R'),[(side*.955,-.27,1.285),(side*.945,.98,1.285),(side*.822,.52,1.79),(side*.822,-.20,1.79)],(side,0,0)))
    for name,corners,normal in faces:
        m=Mesh(); rear=name=='Cabin_RearGlass'
        us=stations(0,1,.025,(.045,.060,.075,.925,.940,.955))
        vs=stations(0,1,.060,(.095,.120,.145,.865,.89,.915))
        def category(u,v):
            # Rear glazing uses a slightly wider painted C-pillar.
            edge=min(u,1-u); low=.15 if rear else .095; high=.91
            if edge<(.10 if rear else .045) or v<low or v>high: return 'Paint'
            if edge<(.12 if rear else .060) or v<low+.025 or v>high-.025: return 'Seal'
            return 'Glass'
        def point(u,v):
            lo=Vector(corners[0]).lerp(Vector(corners[1]),u)
            hi=Vector(corners[3]).lerp(Vector(corners[2]),u)
            p=lo.lerp(hi,v)
            if category(u,v)=='Glass': p-=Vector(normal)*.007
            return p
        m.grid(us,vs,point,'Paint',category); m.object(name,'Cabin')
    m=Mesh()
    m.grid(stations(-.822,.822,.026),stations(-.20,.52,.026),
           lambda x,y:(x,y,1.79+.03*(1-(x/.822)**2)*math.sin(math.pi*(y+.20)/.72)),'Paint')
    m.object('Cabin_Roof','Cabin')
    # Rounded roof edge/gutter follows the approved roof footprint, without a floating outer strip.
    m=Mesh()
    for side in (-1,1):
        m.tube([(side*(.822-.008),y,1.792) for y in stations(-.18,.50,.025)],.008,'Paint',8)
    m.object('Cabin_RoofEdges','Cabin')
    m=Mesh(); m.box((0,-.242,1.55),(.016,.018,.31),'Mirror',.004,.014); m.object('Cabin_RearDivider','Cabin')
    m=Mesh()
    for side in (-1,1):
        m.box((side*.974,-.083,1.19),(.023,.123,.035),'Bumper',.007,.013)
        m.rod((side*.94,.79,1.35),(side*1.045,.79,1.39),.016,'Mirror',12)
        m.box((side*1.055,.78,1.42),(.145,.145,.16),'Mirror',.018,.018)
        m.box((side*1.055,.702,1.42),(.119,.009,.13),'Glass',.003,.016)
    m.object('CabTrim_MirrorsHandles','Trim')
    m=Mesh()
    for side in (-1,1):
        a=(side*.59,.947,1.33); b=(side*.31,.924,1.36)
        m.rod(a,b,.007,'Mirror',10)
        m.box((side*.36,.932,1.36),(.42,.014,.012),'Mirror',.004,.018,
              xf=lambda p:Vector((p.x,p.y-side*.04*p.x,p.z+side*.045*p.x)))
    m.object('Wipers','Trim')


def rib(x,pitch=.0718):
    t=abs((x/pitch+.5)%1-.5)*pitch
    return 1-smooth(.009,.018,t)


def floor_height(x,y):
    t=abs(y-A.REAR_AXLE)/.48
    tub=.19*max(0,1-t**4)**.6*smooth(.57,.67,abs(x)) if t<1 else 0
    return .910+.010*rib(x)*(1-smooth(.01,.10,tub))+tub


def bed():
    m=Mesh(); m.box((0,-1.285,.867),(1.74,1.91,.055),'Paint',.005,.055); m.object('BedFloor','Panel')
    xs=[i*.0718/6 for i in range(-72,73)]
    ys=stations(-2.2125,-.3575,.035,(-1.79,-1.75,-.87,-.83))
    for name,left,right in (('BedLiner_Floor',-.57,.57),('BedLiner_WheelTub_L',-.865,-.57),('BedLiner_WheelTub_R',.57,.865)):
        us=[left]+[x for x in xs if left+1e-5<x<right-1e-5]+[right]; m=Mesh()
        m.grid(us,ys,lambda x,y:(x,y,floor_height(x,y)),'Liner'); m.object(name,'Trim')
    for side in (-1,1):
        s='L' if side<0 else 'R'; m=Mesh()
        ygrid=stations(-2.215,-.342,.012)
        m.grid(ygrid,stations(.91,1.298,.032),lambda y,z:(side*(.865-.004*rib(y,.048)),y,z),'Liner')
        m.object('BedLiner_Side_'+s,'Trim')
        m=Mesh()
        # A rolled rail section with substantial depth, not a millimetre double skin.
        profile=[(.870,1.292),(.875,1.322),(.895,1.330),(.957,1.330),(.974,1.316),(.974,1.290),(.95,1.281),(.89,1.281)]
        m.rings([[(side*x,y,z) for x,z in profile] for y in stations(-2.26,-.313,.027)],'Paint')
        m.object('BedRail_'+s,'Panel')
    m=Mesh(); m.box((0,-.334,1.09),(1.84,.035,.435),'Paint',.006,.026); m.object('BedFrontWall','Body')
    for name,y in (('BedLiner_FrontWall',-.357),('BedLiner_Tailgate',-2.205)):
        m=Mesh(); xx=stations(-.85,.85,.012)
        sign=-1 if y> -1 else 1
        m.grid(xx,stations(.912,1.29,.032),lambda x,z:(x,y+sign*.005*rib(x,.048),z),'Liner')
        m.object(name,'Trim')
    def stamp(p,axis,sign):
        p=list(p); mat=None
        if axis==1 and sign<0:
            x,z=p[0],p[2]+.93
            amount=.004*smooth(.72,.68,abs(x))*(smooth(.91,.93,z)*(1-smooth(1.14,1.17,z)))
            p[1]+=amount
            if abs(x)<.102 and 1.117<z<1.203: p[1]+=.018; mat='Mirror'
        return p,mat
    m=Mesh(); m.box((0,-2.237,.93),(1.89,.047,.76),'Paint',.012,.048,stamp=stamp)
    m.object('Tailgate','Body')
    m=Mesh(); m.box((0,-2.237,1.31),(1.89,.055,.036),'Paint',.007,.026); m.object('BedRail_Rear','Panel')
    m=Mesh(); m.box((0,-2.250,1.16),(.155,.020,.040),'Bumper',.006,.015); m.object('TailgateHandle','Trim')
    def rear_stamp(p,axis,sign):
        p=list(p); mat=None
        # Apply the same displacement on all incident faces so stamped borders weld.
        p[2]-=.012*smooth(.91,.81,abs(p[0]))*smooth(.025,.075,p[2])
        p[1]+=.025*(1-smooth(.20,.24,abs(p[0])))*(1-smooth(-.065,-.025,p[1]))
        if axis==2 and sign>0: mat='Grille'
        return p,mat
    m=Mesh(); m.box((0,-2.268,.624),(1.99,.164,.20),'Bumper',.025,.035,stamp=rear_stamp); m.object('Bumper_Rear','Bumper')
    m=Mesh(); m.box((0,-2.354,.626),(.39,.012,.143),'Mirror',.004,.018); m.object('RearPlate','Trim')
    for side in (-1,1):
        m=Mesh(); m.box((side*.893,-2.259,1.046),(.12,.044,.385),'Grille',.008,.016)
        for z,h,mat in ((1.18,.10,'Red'),(1.075,.09,'Amber'),(.988,.065,'Headlight'),(.913,.065,'Red')):
            m.box((side*.893,-2.279,z),(.10,.018,h),mat,.005,.009)
        m.object('Taillight_'+('L' if side<0 else 'R'),'Light')


def steel_disc(m,center,side):
    # Ten actual vent openings in a dished plate; no painted black hole disks.
    r0,r1,rh,hole=.080,.215,.151,.027
    half=math.pi/10
    corner_angles=[math.atan2(r*math.sin(a),r*math.cos(a)-rh) for r in (r0,r1) for a in (-half,half)]
    angles=sorted(set([i*math.tau/24 for i in range(24)]+[a%math.tau for a in corner_angles]))
    def ray_distance(phi):
        du,dv=math.cos(phi),math.sin(phi); candidates=[]
        for radius in (r0,r1):
            disc=(rh*du)**2-(rh*rh-radius*radius)
            if disc>=0:
                for t in (-rh*du-math.sqrt(disc),-rh*du+math.sqrt(disc)):
                    if t>1e-6: candidates.append(t)
        for a in (-half,half):
            slope=math.tan(a); denominator=dv-slope*du
            if abs(denominator)>1e-8:
                t=slope*rh/denominator
                if t>1e-6: candidates.append(t)
        return min(candidates)
    for sector in range(10):
        theta=sector*math.tau/10
        rings=[]
        for frac in (0,.33,.66,1):
            row=[]
            for phi in angles:
                distance=lerp(hole,ray_distance(phi),frac)
                u=rh+distance*math.cos(phi); v=distance*math.sin(phi)
                radius=math.hypot(u,v); a=theta+math.atan2(v,u)
                axial=.145-.020*smooth(.10,.205,radius)
                row.append((center[0]+side*axial,center[1]+radius*math.cos(a),center[2]+radius*math.sin(a)))
            rings.append(row)
        m.rings(rings,'Steel')
        # Deep vent walls and return, open through the disc.
        front=rings[0]; back=[(p[0]-side*.030,p[1],p[2]) for p in front]
        m.rings([front,back],'Steel')


def wheels():
    for label,y,side in [('FL',A.FRONT_AXLE,-1),('FR',A.FRONT_AXLE,1),('RL',A.REAR_AXLE,-1),('RR',A.REAR_AXLE,1)]:
        c=(side*.876,y,.42); m=Mesh()
        profile=[(-.132,.235),(-.153,.280),(-.155,.32),(-.150,.355),(-.133,.386),(-.105,.402),
                 (-.07,.403),(-.035,.403),(0,.403),(.035,.403),(.07,.403),(.105,.402),(.133,.386),(.150,.355),(.155,.32),(.153,.28),(.132,.235),(-.132,.235)]
        m.lathe(profile,c,'Rubber',80)
        for s in (-1,1):
            for r in (.285,.353): m.lathe([(s*.150,r-.002),(s*.157,r),(s*.150,r+.002)],c,'Rubber',80)
        m.lathe([(-.158,.235),(-.15,.243),(-.135,.244),(-.129,.233),(.126,.233),(.135,.245),(.158,.245),(.171,.238),(.165,.226),(.138,.218)],c,'Steel',80,side)
        steel_disc(m,c,side)
        m.lathe([(.118,0),(.118,.070),(.155,.071),(.186,.061),(.186,0)],c,'Bumper',48,side)
        m.lathe([(-.08,0),(-.08,.19),(-.06,.19),(-.06,0)],c,'Chassis',48,side)
        for i in range(6):
            a=i*math.tau/6
            m.rod((c[0]+side*.145,y+.091*math.cos(a),.42+.091*math.sin(a)),
                  (c[0]+side*.166,y+.091*math.cos(a),.42+.091*math.sin(a)),.010,'Steel',6)
        # Four interlocking tread rows. Each block is a beveled polygonal prism in tire coordinates.
        for i in range(48):
            for row in range(4):
                theta=(i+(.28 if row%2 else 0))*math.tau/48
                axial=(-1.5+row)*.068; sign=1 if row<2 else -1
                footprint=[(-.030,-.021),(.020,-.021),(.031,-.010),(.028,.013),(.006,.025),(-.031,.017)]
                rings=[]
                for radial,shrink in ((.397,1),(.416,1),(.421106,.88)):
                    pts=[]
                    for ax,along in footprint:
                        ax*=shrink; along*=shrink
                        angle=theta+(along+sign*ax*.32)/.41
                        pts.append((c[0]+axial+ax,y+radial*math.cos(angle),.42+radial*math.sin(angle)))
                    rings.append(pts)
                m.rings(rings,'Rubber')
                for loop in (rings[0],rings[-1]):
                    center=sum((Vector(p) for p in loop),Vector())/len(loop)
                    ci=m.vertex(center); ids=[m.vertex(p) for p in loop]
                    for j in range(len(ids)): m.face((ci,ids[j],ids[(j+1)%len(ids)]),'Rubber',False)
        m.object('Wheel_'+label,'Wheel',c)


def suspension():
    m=Mesh()
    for side in (-1,1): m.box((side*.55,-.07,.455),(.10,4.20,.115),'Chassis',.012,.055)
    m.box((0,.37,.523),(1.64,1.21,.06),'Chassis',.006,.055)
    m.rod((0,-1.2,.42),(0,1.4,.42),.027,'Chassis')
    m.object('Underbody_Frame','Underbody')
    for label,y in (('Front',A.FRONT_AXLE),('Rear',A.REAR_AXLE)):
        m=Mesh(); m.rod((-.84,y,.42),(.84,y,.42),.047,'Chassis')
        m.lathe([(-.15,0),(-.15,.045),(-.10,.105),(0,.12),(.10,.105),(.15,.045),(.15,0)],(0,y,.42),'Chassis',32)
        for side in (-1,1):
            m.rod((side*.68,y,.43),(side*.60,y+.12,.86),.022,'Bumper')
            m.rod((side*.63,y+.08,.69),(side*.60,y+.12,.86),.012,'Steel')
            if label=='Front':
                path=[(side*.66+.055*math.cos(i*math.tau/32),y+.055*math.sin(i*math.tau/32),.57+.29*i/160) for i in range(161)]
                m.tube(path,.010,'Chassis',8)
            else:
                for leaf in range(4):
                    rings=[]
                    for t in stations(-.55,.55,.045):
                        z=.48-leaf*.014+.10*(t/.55)**2
                        rings.append([(side*.59+dx,y+t,z+dz) for dx,dz in ((-.025,-.0045),(.025,-.0045),(.025,.0045),(-.025,.0045))])
                    m.rings(rings,'Chassis')
        m.object('Suspension_'+label,'Underbody')
    # Wheel-house returns block the unnatural see-through arch backdrop.
    m=Mesh()
    for y in (A.FRONT_AXLE,A.REAR_AXLE):
        for side in (-1,1):
            m.grid(stations(-.50,.50,.025),stations(.70,.925,.025),
                   lambda dy,x:(side*x,y+dy,arch(y+dy,y)+.012),'Chassis')
    m.object('Wheelhouse_Inner','Underbody')


def surface_materials():
    A.materials()
    A.material('Seal',(.025,.035,.028),0,.78)
    glass=A.MATS['Glass'].node_tree.nodes.get('Principled BSDF')
    glass.inputs['Base Color'].default_value=(*A.linear((.075,.12,.105)),1)
    glass.inputs['Roughness'].default_value=.19
    lamp=A.MATS['Headlight'].node_tree.nodes.get('Principled BSDF')
    lamp.inputs['Transmission Weight'].default_value=.85; lamp.inputs['IOR'].default_value=1.46
    lamp.inputs['Emission Strength'].default_value=0; lamp.inputs['Roughness'].default_value=.10
    for key,scale,distance in (('Liner',150,.0006),('Rubber',100,.0004),('Headlight',95,.0005),('Amber',120,.0005),('Red',120,.0005)):
        mat=A.MATS[key]; nodes=mat.node_tree.nodes; links=mat.node_tree.links
        uv=nodes.new('ShaderNodeUVMap'); uv.uv_map='UVMap'
        noise=nodes.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=scale
        bump=nodes.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.22; bump.inputs['Distance'].default_value=distance
        links.new(uv.outputs['UV'],noise.inputs['Vector']); links.new(noise.outputs['Fac'],bump.inputs['Height'])
        links.new(bump.outputs['Normal'],nodes.get('Principled BSDF').inputs['Normal'])
    # Optical cross flutes remain attached through UVMap rather than world coordinates.
    mat=A.MATS['Headlight']; nodes=mat.node_tree.nodes; links=mat.node_tree.links
    uv=nodes.new('ShaderNodeUVMap'); uv.uv_map='UVMap'
    split=nodes.new('ShaderNodeSeparateXYZ'); links.new(uv.outputs['UV'],split.inputs[0])
    waves=[]
    for axis,frequency in (('X',785),('Y',630)):
        scale=nodes.new('ShaderNodeMath'); scale.operation='MULTIPLY'; scale.inputs[1].default_value=frequency
        sine=nodes.new('ShaderNodeMath'); sine.operation='SINE'
        links.new(split.outputs[axis],scale.inputs[0]); links.new(scale.outputs[0],sine.inputs[0]); waves.append(sine)
    add=nodes.new('ShaderNodeMath'); add.operation='ADD'
    links.new(waves[0].outputs[0],add.inputs[0]); links.new(waves[1].outputs[0],add.inputs[1])
    bump=nodes.new('ShaderNodeBump'); bump.inputs['Distance'].default_value=.0012; bump.inputs['Strength'].default_value=.45
    links.new(add.outputs[0],bump.inputs['Height']); links.new(bump.outputs[0],lamp.inputs['Normal'])


def finalize():
    bpy.context.view_layer.update()
    for ob in A.PARTS:
        bm=bmesh.new(); bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
        bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.0000001)
        loose=[v for v in bm.verts if not v.link_faces]
        if loose: bmesh.ops.delete(bm,geom=loose,context='VERTS')
        bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces))
        # Open cavity sheets have no enclosed volume from which to infer 'outside'.
        toward={'BedLiner_Side_L':(1,0,0),'BedLiner_Side_R':(-1,0,0),
                'BedLiner_FrontWall':(0,-1,0),'BedLiner_Tailgate':(0,1,0),
                'Wheelhouse_Inner':(0,0,-1)}.get(ob.name)
        if toward:
            bm.normal_update(); pending=set(bm.faces); target=Vector(toward)
            while pending:
                component=[]; stack=[pending.pop()]
                while stack:
                    face=stack.pop(); component.append(face)
                    for edge in face.edges:
                        for neighbour in edge.link_faces:
                            if neighbour in pending: pending.remove(neighbour); stack.append(neighbour)
                if sum(f.normal.dot(target)*f.calc_area() for f in component)<0:
                    bmesh.ops.reverse_faces(bm,faces=component)
        bm.to_mesh(ob.data); bm.free(); ob.data.update()
        uv=ob.data.uv_layers.new(name='UVMap'); band=ob.data.uv_layers.new(name='StripeProjection')
        for p in ob.data.polygons:
            axes=sorted(range(3),key=lambda k:abs(p.normal[k]))[:2]
            for index in p.loop_indices:
                v=ob.data.vertices[ob.data.loops[index].vertex_index].co; world=ob.matrix_world@v
                uv.data[index].uv=(v[axes[0]],v[axes[1]])
                band.data[index].uv=(.5+world.x/2.4,world.z/2)


def palette_and_mask(out):
    palette=[]
    for key,mat in A.MATS.items():
        bs=mat.node_tree.nodes.get('Principled BSDF')
        entry={'name':mat.name,'color_linear':list(bs.inputs['Base Color'].default_value),
               'metallic':bs.inputs['Metallic'].default_value,'smoothness':1-bs.inputs['Roughness'].default_value,
               'emission_linear':list(bs.inputs['Emission Color'].default_value),'emission_strength':bs.inputs['Emission Strength'].default_value}
        if key=='Paint': entry.update(primary_srgb=list(A.COLOR_A),secondary_srgb=list(A.COLOR_B))
        palette.append(entry)
    (out/'palette.json').write_text(json.dumps({'stage':'B authoring only','materials':palette},indent=2)+'\n')
    image=bpy.data.images.new('Pickup_LowerBodyMask',width=8,height=1024)
    image.colorspace_settings.name='Non-Color'; pixels=[]
    for j in range(1024):
        value=1. if (j+.5)/1024<A.PAINT_LINE/2 else 0.
        pixels.extend((value,value,value,1.)*8)
    image.pixels=pixels; image.filepath_raw=str(out/'LowerBodyMask.png'); image.file_format='PNG'; image.save(); image.pack()
    # The same UV1 texture path can drive the existing Unity two-color shader after B approval.
    mat=A.MATS['Paint']; nodes=mat.node_tree.nodes; links=mat.node_tree.links
    uv=next(n for n in nodes if n.type=='UVMAP'); tex=nodes.new('ShaderNodeTexImage')
    tex.name='Lower body mask UV1'; tex.image=image; tex.interpolation='Closest'; tex.extension='EXTEND'
    links.new(uv.outputs['UV'],tex.inputs['Vector'])
    links.new(tex.outputs['Color'],nodes.get('Primary and Lower Body Colors').inputs[0])


EXTRA_VIEWS={
    'hood_detail':((-2.6,3.7,2.65),(-.57,1.64,1.17),1.80),
    'wheel_detail':((-3.4,2.3,1.2),(-.89,1.64,.52),1.45),
    'cabin_detail':((-3.6,4.6,2.5),(0,.39,1.5),2.6),
    'roof_color':((-3.2,3.0,4.5),(0,.15,1.5),2.45),
    'fascia_detail':((-2.3,4.9,1.45),(-.40,2.25,1.00),1.65),
}


def render(camera,out,views):
    from bpy_extras.object_utils import world_to_camera_view
    records={}; all_views=dict(A.VIEWS,**EXTRA_VIEWS)
    for name in views:
        pos,target,scale=all_views[name]; camera.location=pos; A.aim(camera,target)
        camera.data.type='PERSP' if name in ('front34','rear34') else 'ORTHO'
        camera.data.lens=70; camera.data.ortho_scale=scale
        if name=='top': camera.rotation_euler=(0,0,-math.pi/2)
        bs=A.MATS['Paint'].node_tree.nodes.get('Principled BSDF')
        keys=('Metallic','Roughness','Coat Weight','Specular IOR Level')
        original={k:bs.inputs[k].default_value for k in keys}
        if name=='roof_color':
            for k,v in {'Metallic':0,'Roughness':.8,'Coat Weight':0,'Specular IOR Level':0}.items(): bs.inputs[k].default_value=v
        bpy.context.view_layer.update(); bpy.context.scene.render.filepath=str(out/f'stage_b_{name}.png')
        bpy.ops.render.render(write_still=True)
        projected=[world_to_camera_view(bpy.context.scene,camera,ob.matrix_world@v.co) for ob in A.PARTS for v in ob.data.vertices]
        records[name]={'position':pos,'target':target,'projection':camera.data.type,'lens_mm':70,'scale':scale,
                       'vehicle_pixel_bounds':[min(p.x for p in projected)*1440,(1-max(p.y for p in projected))*960,
                                               max(p.x for p in projected)*1440,(1-min(p.y for p in projected))*960],
                       'diagnostic_paint_specular_disabled':name=='roof_color'}
        for k,value in original.items(): bs.inputs[k].default_value=value
        print('STAGE_B_RENDERED',name,flush=True)
    existing=json.loads((out/'cameras.json').read_text()) if (out/'cameras.json').exists() else {}
    existing.update(records); (out/'cameras.json').write_text(json.dumps(existing,indent=2)+'\n')


def verify_approved(root):
    record=json.loads((root/'Previews/CompactPickup/approved_checkpoint_a.json').read_text())
    for path,expected in record['files_sha256'].items():
        assert hashlib.sha256((root/path).read_bytes()).hexdigest()==expected,'Approved Stage A file changed: '+path


def main():
    argv=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    parser=argparse.ArgumentParser(); parser.add_argument('root',type=Path)
    parser.add_argument('--render',action='store_true'); parser.add_argument('--views',nargs='+')
    parser.add_argument('--samples',type=int,default=32); args=parser.parse_args(argv)
    root=args.root.resolve(); verify_approved(root); out=root/'Previews/CompactPickup/StageB'; out.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True); A.PARTS.clear(); A.MATS.clear()
    A.ROOT=bpy.data.objects.new('CompactPickup',None); bpy.context.scene.collection.objects.link(A.ROOT)
    A.ROOT['development_stage']='B - detailed model; approval pending'
    surface_materials(); body(); front(); cabin(); bed(); wheels(); suspension(); finalize()
    palette_and_mask(out)
    camera=A.studio(); bpy.context.scene.cycles.samples=args.samples
    camera.location=A.VIEWS['front34'][0]; A.aim(camera,A.VIEWS['front34'][1]); camera.data.type='PERSP'; camera.data.lens=70
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(root/'Blender/CompactPickup_StageB.blend'))
    print('STAGE_B_GEOMETRY',len(A.PARTS),sum(len(o.data.vertices) for o in A.PARTS),flush=True)
    if args.render: render(camera,out,args.views or list(dict(A.VIEWS,**EXTRA_VIEWS)))
    verify_approved(root)


if __name__=='__main__': main()
