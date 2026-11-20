import math, random, json, sys
from PIL import Image
P=32
A,B,HH=0.85,0.55,1.45         # 占地（格）和高度（单位），游戏里再 ×1.2
ax,bx,hp=round(A*P),round(B*P),round(HH*P)
def H(x,y,s):
    h=(x*374761393 + y*668265263 + s*982451653) & 0xffffffff
    h=((h ^ (h>>13))*1274126177) & 0xffffffff
    return ((h ^ (h>>16)) & 0xffff)/65535
def lerp(a,b,t): t=max(0,min(1,t)); return tuple(a[i]+(b[i]-a[i])*t for i in range(3))
def mul(c,k): return tuple(v*k for v in c)
def vnoise(x,y,s):
    x0,y0=math.floor(x),math.floor(y); fx,fy=x-x0,y-y0
    def r(i,j): return H(i,j,s)
    sx,sy=fx*fx*(3-2*fx),fy*fy*(3-2*fy)
    a=r(x0,y0)+(r(x0+1,y0)-r(x0,y0))*sx; b=r(x0,y0+1)+(r(x0+1,y0+1)-r(x0,y0+1))*sx
    return a+(b-a)*sy
def seg(px,py,ax_,ay_,bx_,by_):
    vx,vy=bx_-ax_,by_-ay_; t=max(0,min(1,((px-ax_)*vx+(py-ay_)*vy)/(vx*vx+vy*vy+1e-9))); return math.hypot(px-ax_-vx*t,py-ay_-vy*t)

def make(seed):
    rnd=random.Random(seed)
    top_extra=22                                  # 顶上伸出来的电缆
    w=ax+bx+8; h=ax//2+bx//2+hp+top_extra+6
    fx,fy=ax+4,3
    img={}
    paintL,paintR,paintT=(50,58,52),(34,40,38),(62,68,60)
    rust,rustD,rustH=(104,56,32),(58,34,22),(140,78,40)
    metal,outline,rim=(92,92,86),(14,15,14),(170,108,62)
    hole=(10,10,10)
    # 每个变体随机的细节位置
    dents=[(rnd.uniform(0.2,0.8),rnd.uniform(0.25,0.85),rnd.uniform(0.07,0.11)) for _ in range(2)]
    bullets=[(rnd.uniform(0.2,0.85),rnd.uniform(0.3,0.9)) for _ in range(rnd.randint(2,4))]
    flyer=(rnd.uniform(0.15,0.35),rnd.uniform(0.2,0.35))       # 右面的破海报（u, v 起点）
    ajar=rnd.random()<0.7
    for y in range(h):
      for x in range(w):
        dx,dy=x-fx,y-fy; c=None
        ss=((dy-hp)/16-dx/32)/2; tt=((dy-hp)/16+dx/32)/2
        if 0<=ss<=A and 0<=tt<=B:
            # ---------- 顶面：锈、积水、烟头、垃圾、被灯打亮的前沿
            n=H(x,y,seed)
            c=mul(paintT,0.85+n*0.18)
            if vnoise(x*0.25,y*0.25,seed)>0.55: c=lerp(c,rust,0.7)
            if vnoise(x*0.3+9,y*0.3,seed)>0.72: c=(26,28,30)                       # 积水
            if ss<0.05 or tt<0.05: c=lerp(c,rim,0.4)
            if (ss<0.05 or tt<0.05) and H(x,y,seed+5)<0.25: c=metal                 # 磨掉漆的棱
            if H(x,y,seed+3)>0.988: c=(150,150,140)
            if abs(ss-0.55)<0.04 and abs(tt-0.2)<0.12: c=(200,190,170) if abs(tt-0.3)>0.03 else (170,90,40)   # 烟头
            if abs(ss-0.25)<0.08 and abs(tt-0.38)<0.06: c=(70,62,46)                  # 一团废纸
        elif dx<=0:
            s1=-dx/32; v=dy-16*s1
            if s1<=A and 0<=v<hp:
                u=s1/A; vv=v/hp
                c=mul(paintL,0.88+H(x,y,seed)*0.16)
                # 大块潮斑和掉漆
                if vnoise(u*6,vv*8,seed)>0.66: c=mul(c,0.82)
                if vnoise(u*9+3,vv*9,seed)>0.74: c=lerp(c,metal,0.6)
                # 柜门
                inDoor = 0.1<u<0.9 and 0.12<vv<0.9
                seam=((abs(u-0.1)<0.03 or abs(u-0.9)<0.03) and 0.12<vv<0.9) or ((abs(vv-0.12)<0.02 or abs(vv-0.9)<0.02) and 0.1<u<0.9)
                if seam: c=mul(c,0.5)
                if ajar and 0.84<u<0.92 and 0.14<vv<0.88:                             # 门没关严：一条黑缝，里面露出电线
                    c=(8,8,8)
                    if H(int(vv*hp),0,seed+8)<0.35: c=[(150,40,30),(170,140,40),(60,110,50),(140,140,130)][int(H(int(vv*hp),1,seed)*4)]
                if abs(u-0.14)<0.03 and (abs(vv-0.28)<0.025 or abs(vv-0.76)<0.025): c=metal   # 合页
                if abs(u-0.8)<0.035 and abs(vv-0.55)<0.04: c=(18,18,18) if not ajar else (40,30,24)  # 锁被撬掉的洞
                # 凹痕：一边亮一边暗
                for (du,dv,r) in dents:
                    d=math.hypot((u-du)*1.0,(vv-dv)*0.7)
                    if d<r: c=mul(c,1.18 if (u-du)+(vv-dv)>0 else 0.7)
                # 警示贴（掉了一半）
                tu=(u-0.3)/0.36; tv=(vv-0.44)/0.3
                if 0<=tv<=1 and abs(tu-0.5)<=(1-tv)*0.5+0.02 and not (tu>0.62 and tv<0.5):   # 右下角撕掉了
                    edge = abs(tu-0.5)>(1-tv)*0.5-0.09 or tv<0.09
                    c=(30,26,18) if edge else ((150,120,40) if H(x,y,seed+7)<0.15 else (168,132,40))
                    d=min(seg(tu,tv*0.9,0.56,0.62,0.44,0.36),seg(tu,tv*0.9,0.44,0.36,0.58,0.36),seg(tu,tv*0.9,0.58,0.36,0.46,0.12))
                    if d<0.045 and not edge: c=(24,22,18)
                # 模板喷字的编号：TR-07（褪色白）
                # 弹孔
                for (bu,bv) in bullets:
                    d=math.hypot((u-bu)*ax,(vv-bv)*hp)
                    if d<0.7: c=hole
                    elif d<1.6: c=lerp(c,metal,0.6)
                # 锈：从合页、门缝往下流
                if vnoise(u*14,vv*2.5,seed+2)>0.68 and H(int(u*ax),0,seed)>0.45: c=lerp(c,rust,0.6)
                if vv<0.14: c=lerp(c,rustD,0.55+(0.14-vv)*3)
                if vv<0.05 and H(x,y,seed+13)<0.4: c=(72,62,50)                          # 泥点
                if s1<0.045: c=lerp(c,rim,0.3) if H(x,y,seed+4)>0.3 else metal           # 前棱：磨亮、露铁
                # 门下面垂出来的几根线
        else:
            t1=dx/32; v=dy-16*t1
            if t1<=B and 0<=v<hp:
                u=t1/B; vv=v/hp
                c=mul(paintR,0.88+H(x,y,seed)*0.16)
                if vnoise(u*6,vv*8,seed+1)>0.66: c=mul(c,0.8)
                # 百叶散热口，有一片掉了、一片弯了
                if 0.6<vv<0.88 and 0.18<u<0.82:
                    row=int((vv-0.6)*hp/3)
                    if int(v)%3==0:
                        c=(16,18,18)
                        if row==2 and u>0.5: c=mul(paintR,0.9)                           # 弯掉的那片
                    if row==4: c=(10,10,10)                                              # 掉了的那片：一个黑洞
                    if int(v)%3==1 and H(x,int(v),seed)<0.2: c=rust
                # 电缆出口 + 一圈烧焦
                cu,cv=0.62,0.13
                d=math.hypot((u-cu)*bx,(vv-cv)*hp)
                if d<2.2: c=(8,8,8)
                elif d<9: c=lerp(c,(20,16,14),1-(d-2.2)/7)
                # 破海报：发白的纸，边缘撕得参差，有一行行模糊的字
                pu,pv=flyer
                if pu<u<pu+0.4 and pv<vv<pv+0.28:
                    jag=H(int(u*bx),int(vv*hp),seed+9)
                    if not ((u>pu+0.33 and jag<0.6) or (vv>pv+0.24 and jag<0.5)):
                        c=(150,144,128) if H(x,y,seed+10)>0.1 else (120,114,100)
                        if int((vv-pv)*hp)%3==1 and 0.03<(u-pu)<0.3: c=(90,84,76)
                        if vv<pv+0.06: c=(130,60,50)                                           # 海报上半截红色标题
                # 洋红色乱涂
                if 0.12<u<0.5 and 0.45<vv<0.58 and abs(math.sin(u*28)*0.035+0.51-vv)<0.02: c=(112,46,94)
                if vv<0.14: c=lerp(c,rustD,0.55+(0.14-vv)*3)
                if vnoise(u*10,vv*3,seed+6)>0.7: c=lerp(c,rustD,0.5)
        if c: img[(x,y)]=tuple(int(max(0,min(255,round(v)))) for v in c)
    # ---------- 地上的水泥底座（比柜子大一圈，缺了一角）
    base={}
    for y in range(h):
      for x in range(w):
        dx,dy=x-fx,y-fy+2
        ss=(dy/16-dx/32)/2; tt=(dy/16+dx/32)/2
        if -0.08<=ss<=A+0.08 and -0.08<=tt<=B+0.08 and (x,y) not in img:
            if ss>A-0.02 and tt<0.12: continue                                          # 缺角
            base[(x,y)]=mul((58,56,52),0.85+H(x,y,seed+20)*0.2)
    for k,v in base.items(): img.setdefault(k,tuple(int(round(t)) for t in v))
    # ---------- 顶上伸出来的电缆：从顶面后半部冒出来，拱起来再垂到柜子后面，缠着黄胶布
    topc=(fx + int((B-A)*16), fy+hp+int((A+B)*8))
    for k in range(3):
        sx=topc[0]-5+k*4; sy=topc[1]-3+k
        L=12+k*3; dirx=rnd.choice([-1,1])*(0.6+0.2*k)
        prev=None
        for i in range(L+1):
            t=i/L
            px=round(sx+dirx*i); py=round(sy+6*math.sin(math.pi*min(1,t*1.4))-max(0,(t-0.7))*14)
            col=(22,22,24) if i%6!=3 else (160,132,36)
            for (qx,qy) in ([(px,py)] if prev is None else [(round(prev[0]+(px-prev[0])*f/3),round(prev[1]+(py-prev[1])*f/3)) for f in range(1,4)]):
                img[(qx,qy)]=col; img[(qx,qy-1)]=tuple(int(v*0.7) for v in col)
            prev=(px,py)
    # ---------- 描边
    out=dict(img)
    for (x,y) in list(img.keys()):
        for a,b in((1,0),(-1,0),(0,1),(0,-1)):
            q=(x+a,y+b)
            if q not in img and 0<=q[0]<w and 0<=q[1]<h: out[q]=(14,15,14)
    im=Image.new('RGBA',(w,h),(0,0,0,0))
    for (x,y),c in out.items():
        if 0<=x<w and 0<=y<h: im.putpixel((x,h-1-y),c+(255,))
    return im,fx,fy,w,h

if __name__=='__main__':
    outdir=sys.argv[1]
    previews=[]
    for i,seed in enumerate([11,27,53]):
        im,fx,fy,w,h=make(seed)
        im.save(f'{outdir}/transformer{i+1}.png')
        json.dump({"pivotX":round((fx+0.5)/w,4),"pivotY":0.0},open(f'{outdir}/transformer{i+1}.json','w'))
        previews.append(im)
    W=sum(p.width for p in previews)+40; Hm=max(p.height for p in previews)+20
    bg=Image.new('RGBA',(W,Hm),(22,26,36,255)); x=10
    for p in previews: bg.alpha_composite(p,(x,10)); x+=p.width+10
    bg.resize((W*6,Hm*6),Image.NEAREST).save(f'{outdir}/preview.png')
