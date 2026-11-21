"""
NEON DRIFT 武器音效（第二版，按最初 demo 的路子）：
枪 = 高频爆裂 + 中低频枪身 + 下扫的低音"砰" + 巷子回声；刀 = 带通风声 + 金属余音。
频带保留完整、峰值更高，比其他音效更突出；其他音效仍用 gen_sfx.py。
"""
import numpy as np, sys, os
from scipy.signal import butter, lfilter
from scipy.io import wavfile
SR=44100
OUT=sys.argv[1] if len(sys.argv)>1 else 'out'
os.makedirs(OUT,exist_ok=True)
def T(d): return np.arange(int(SR*d))/SR
def N(d,r): return r.standard_normal(int(SR*d))
def env(n,a=0.002,d=0.2,curve=4):
    x=np.arange(n)/SR; return np.minimum(1,x/max(a,1e-4))*np.exp(-curve*np.maximum(0,x-a)/d)
def _f(x,k,f,o=2):
    b,a=butter(o,[f[0]/(SR/2),f[1]/(SR/2)] if k=='band' else f/(SR/2),k); return lfilter(b,a,x)
def lp(x,f): return _f(x,'low',f)
def hp(x,f): return _f(x,'high',f)
def bp(x,f1,f2): return _f(x,'band',(f1,f2))
def sweep(f0,f1,d,sq=False):
    t=T(d); f=f0*(f1/f0)**(t/d); s=np.sin(2*np.pi*np.cumsum(f)/SR); return np.sign(s) if sq else s
def tone(f,d): return np.sin(2*np.pi*f*T(d))
def echo(x,taps=((0.085,0.28),(0.16,0.14),(0.26,0.06)),cut=1800):
    y=x.copy(); w=lp(x,cut)
    for dl,g in taps:
        s=int(dl*SR)
        if s<len(y): y[s:]+=w[:len(y)-s]*g
    return y
def swoosh_env(n,att,dec): return np.sin(np.pi*np.clip(np.arange(n)/(SR*att),0,1))**2*np.exp(-np.arange(n)/(SR*dec))
def ring(d,fs,dec,amp):
    n=int(SR*d); return sum(tone(f,d)*a for f,a in fs)*env(n,0.004,dec,4)*amp
def save(name,x,peak):
    n=int(SR*0.008); x=x.copy(); x[-n:]*=np.linspace(1,0,n)
    x=x/(np.max(np.abs(x))+1e-9)
    if name.split('_')[0] in DRIVE: k=DRIVE[name.split('_')[0]]; x=np.tanh(x*k)/np.tanh(k)   # 软削波：更响、更有冲劲
    x=x*peak
    wavfile.write(f'{OUT}/{name}.wav',SR,(x*32767).astype(np.int16))
DRIVE={'shoot':1.5,'swing':1.8,'thrust':1.8,'heavy':1.6,'melee':2.0,'blunt':2.0,'bullet':1.6,'crit':2.0}
def var(name,fn,peak,count):
    for i in range(count): save(f'{name}_{i+1}',fn(np.random.default_rng(abs(hash(name))%9999+i*31),i),peak)

# ---------------- 枪
def snap(d,r,amp=1.0,f=3000):     # 开枪最前面 1–3 ms 的尖锐瞬态，决定"脆不脆"
    n=int(SR*d); return hp(N(d,r),f)*env(n,0.0001,0.004,6)*amp
def crackle(d,r,amp=0.6):         # 高频的"啪"，比原来更亮
    n=int(SR*d); return bp(N(d,r),3000,12000)*env(n,0.0003,0.03,5)*amp
# ---- 写实枪声：膛口爆音（Friedlander 冲击波）+ 气流噪声 + 机械声 + 环境混响
from scipy.signal import fftconvolve
def blast(d,r,T,boom=1.0):
    """膛口爆音：Friedlander 波形 —— 一瞬间冲到峰值、再带一个负压回落，真实枪声"砰"的形状。"""
    n=int(SR*d); t=np.arange(n)/SR; x=(1-t/T)*np.exp(-2.2*t/T)
    x[t>T*6]=0
    return lp(x,6000)*boom
def gas(d,r,dec,f1,f2,amp):          # 火药燃气喷出的宽带噪声，衰减很快
    n=int(SR*d); return bp(N(d,r),f1,f2)*env(n,0.0001,dec,6)*amp
def snapwave(d,r,T=0.0006,amp=1.0):  # 超音速子弹的"啪"：N 形波，很短很亮
    n=int(SR*d); x=np.zeros(n); L=max(4,int(SR*T)); x[:L]=np.linspace(1,-1,L); return hp(x,800)*amp
def mech(d,r,at,amp,f=(2600,4100)):   # 机械声：套筒/枪栓复位的金属咔哒（短，不嗡）
    n=int(SR*d); x=np.zeros(n); s=int(SR*at); L=int(SR*0.025)
    if s+L<n:
        k=bp(r.standard_normal(L),1500,7000)*np.exp(-np.arange(L)/(SR*0.003))
        k+=sum(np.sin(2*np.pi*ff*np.arange(L)/SR) for ff in f)*np.exp(-np.arange(L)/(SR*0.006))*0.15
        x[s:s+L]=k*amp
    return x
def room(r,rt,early=0.6,bright=0.5):
    """环境混响的脉冲响应：密集的早期反射（巷子两边的墙）+ 平滑衰减、高频先消失的尾巴。"""
    n=int(SR*rt*1.6); t=np.arange(n)/SR; ir=np.zeros(n)
    for _ in range(40):
        k=int(SR*r.uniform(0.004,0.07)); ir[k]+=r.uniform(-1,1)*early*np.exp(-k/SR/0.05)
    lo=lp(r.standard_normal(n),900)*np.exp(-t/(rt*0.32)); hi=hp(r.standard_normal(n),900)*np.exp(-t/(rt*0.12))*bright
    tail=(lo+hi)*np.minimum(1,t/0.01); ir+=tail*0.05
    return lp(ir,7000)
REVERB_WET=1.0; REVERB_LEN=1.0          # 回响整体系数（1 = 写实版原样；试过 0.5/0.7，觉得太干）
def place(x,r,rt,wet,early=0.6,bright=0.5):
    rt*=REVERB_LEN; wet*=REVERB_WET
    y=fftconvolve(x,room(r,rt,early,bright))[:len(x)]
    y=y/(np.max(np.abs(y))+1e-9)*np.max(np.abs(x))
    out=x+y*wet; m=int(len(out)*0.15); out[-m:]*=np.linspace(1,0,m)**2
    return out
def pistol(r,i):
    d=0.7; T=0.0028*(1+0.08*(i-1))
    x=blast(d,r,T,1.0)+gas(d,r,0.012,250,9000,0.55)+gas(d,r,0.004,2500,14000,0.35)+mech(d,r,0.045+0.005*i,0.12)
    return place(x,r,0.75,0.55)
def rifle(r,i):
    d=0.42; T=0.0022*(1+0.06*(i-1))
    x=snapwave(d,r,0.0005,0.5)+blast(d,r,T,1.0)+gas(d,r,0.009,300,11000,0.6)+gas(d,r,0.003,3000,15000,0.4)+mech(d,r,0.03,0.08,(3000,4700))
    return place(x,r,0.45,0.42)
def shotgun(r,i):
    d=0.95; T=0.0048*(1+0.06*(i-1))
    x=blast(d,r,T,1.2)+gas(d,r,0.022,150,7000,0.7)+gas(d,r,0.006,2000,12000,0.35)
    return place(x,r,0.95,0.6,0.7,0.4)
def sniper(r,i):
    d=1.5; T=0.004*(1+0.06*(i-1))
    x=snapwave(d,r,0.0007,0.8)+blast(d,r,T,1.2)+gas(d,r,0.016,200,10000,0.7)+gas(d,r,0.004,3000,15000,0.45)+mech(d,r,0.55,0.1,(2200,3500))
    return place(x,r,1.4,0.7,0.7,0.45)
def energy(r,i):
    d=0.32; n=int(SR*d)
    zap=sweep(1400+120*i,700,d,sq=True)*env(n,0.0005,0.025,6)*0.35
    fizz=hp(N(d,r),3000)*env(n,0.0005,0.04,6)*1.4
    body=lp(N(d,r),800)*env(n,0.001,0.03,5)*0.45
    x=lp(zap,9000)*1.6+fizz+body; return place(x,r,0.35,0.3)
def launcher(r,i):
    d=0.8; n=int(SR*d)
    x=blast(d,r,0.009,1.3)+gas(d,r,0.03,100,1500,0.6)+bp(N(d,r),1500,7000)*swoosh_env(n,0.04,0.15)*0.18+mech(d,r,0.02,0.08,(1700,2600))
    return place(x,r,0.7,0.45)
def flame(r,i):
    d=0.32; n=int(SR*d)
    roar=bp(N(d,r),180,2200)*np.sin(np.pi*np.arange(n)/n)**0.7
    crackle=np.zeros(n)
    for _ in range(10):
        s=r.integers(0,n-600); L=r.integers(80,500); crackle[s:s+L]+=hp(r.standard_normal(L),2500)*np.exp(-np.arange(L)/(L*0.3))*r.uniform(0.3,0.9)
    return roar+crackle*0.5+lp(N(d,r),150)*np.sin(np.pi*np.arange(n)/n)*0.6
def throw(r,i):
    d=0.3; n=int(SR*d); w=N(d,r); y=np.zeros(n); cut=np.linspace(800+200*i,5000,n)
    for k in range(0,n,512):
        y[k:k+512]=bp(w[k:k+512],max(300,cut[k]*0.6),min(SR/2-200,cut[k]*1.4))
    return y*np.sin(np.pi*np.arange(n)/n)**1.5
def dry_fire(r,i):
    d=0.12; n=int(SR*d)
    click=hp(N(d,r),2500)*env(n,0.0002,0.006,6)
    return click+ring(d,((2600+100*i,0.5),(4100,0.25)),0.025,0.5)
def reload_start(r,i):     # 弹匣退出：咔 + 金属滑动
    d=0.28; n=int(SR*d); x=hp(N(d,r),1500)*env(n,0.0002,0.008,6)
    s=int(0.06*SR); L=int(0.12*SR); x[s:s+L]+=bp(N(0.12,r),1500,6000)[:L]*swoosh_env(L,0.04,0.05)*0.4
    return x+ring(d,((1900+80*i,0.4),(3100,0.2)),0.03,0.5)
def reload_end(r,i):       # 拉栓上膛：两下清脆的金属咔哒
    d=0.3; n=int(SR*d); x=np.zeros(n)
    for k,off in enumerate((0.0,0.11)):
        s=int(off*SR); L=int(0.1*SR)
        x[s:s+L]+=hp(N(0.1,r),1800)[:L]*env(L,0.0002,0.007,6)+ring(0.1,((2300+400*k,0.5),(3700+300*k,0.25)),0.03,0.6)[:L]
    return x+lp(N(d,r),600)*env(n,0.11,0.04,5)*0.5
# ---------------- 近战
def katana(r,i,d=0.32,f1=1800,f2=7000,att=0.14,dec=0.25,rf=(3150,4730),ra=0.12):
    n=int(SR*d)
    sw=bp(N(d,r),f1+120*(i-1),f2)*swoosh_env(n,att,dec)
    rg=tone(rf[0]*(1+0.03*(i-1)),d)*env(n,0.06,0.18,4)*ra+tone(rf[1]*(1+0.03*(i-1)),d)*env(n,0.06,0.12,4)*ra*0.5
    return sw+rg
def shing(d,r,i,f0=5200,f1=7400,amp=0.25,dec=0.12):   # 刀刃划过空气的"铮"：高频滑音 + 噪声颤动
    n=int(SR*d); tone_=sweep(f0*(1+0.03*(i-1)),f1,d)*(1+0.4*lp(N(d,r),60))
    return tone_*env(n,0.01,dec,4)*amp
def swing(r,i):
    d=0.32; n=int(SR*d)
    sharp=bp(N(d,r),4000,12000)*swoosh_env(n,0.07,0.1)*0.9
    return katana(r,i,att=0.09,dec=0.16,f1=2600,f2=11000,ra=0.2)+sharp+shing(d,r,i)
def thrust(r,i):
    d=0.24; n=int(SR*d)
    sharp=bp(N(d,r),5000,13000)*swoosh_env(n,0.03,0.05)*1.0
    return katana(r,i,d=d,f1=3200,f2=12000,att=0.03,dec=0.06,rf=(4300,6200),ra=0.18)+sharp+shing(d,r,i,6000,8200,0.22,0.08)
def heavy_swing(r,i):
    d=0.45; n=int(SR*d)
    return bp(N(d,r),250,2400)*swoosh_env(n,0.2,0.22)*1.2+lp(N(d,r),180)*swoosh_env(n,0.2,0.2)*0.6
def melee_hit(r,i):        # 砍中：刀刃入肉的"嚓" + 闷响
    d=0.3; n=int(SR*d)
    slice_=bp(N(d,r),3000,12000)*env(n,0.0003,0.035,5)*1.4+snap(d,r,0.8,4000)
    body=lp(N(d,r),1200)*env(n,0.0005,0.05,5)*1.2
    thump=sweep(200-15*i,60,d)*env(n,0.001,0.06,5)*0.8
    return slice_+body+thump+ring(d,((2900+150*i,0.4),),0.06,0.15)
def blunt_hit(r,i):
    d=0.45; n=int(SR*d)
    crunch=bp(N(d,r),900,4000)*env(n,0.0005,0.02,6)*0.7
    body=lp(N(d,r),700)*env(n,0.001,0.09,5)*1.6
    thump=sweep(130,38,d)*env(n,0.001,0.13,4)*1.3
    return echo(crunch+body+thump,taps=((0.07,0.15),))
def bullet_cut(r,i):       # 刀劈子弹：清脆的金属"锵"
    d=0.5; n=int(SR*d); f=2300+200*i
    hit=hp(N(d,r),3000)*env(n,0.0003,0.01,6)*1.3
    return hit+ring(d,((f*1.3,0.5),(f*1.95,0.35),(f*2.8,0.2),(f*0.65,0.2)),0.25,0.9)
def crit(r,i):
    d=0.6; n=int(SR*d)
    hit=lp(N(d,r),2500)*env(n,0.001,0.05,5)*1.5
    low=sweep(90+10*i,40,d)*env(n,0.001,0.18,4)
    bell=sum(tone(f*(1+0.04*i),d)*a for f,a in((1320,0.5),(1980,0.3),(2640,0.2)))*env(n,0.002,0.35,3)*0.5
    return hit+low+bell

SPECS=[('shoot_pistol',pistol,0.95,3),('shoot_rifle',rifle,0.85,3),('shoot_shotgun',shotgun,0.97,3),('shoot_sniper',sniper,0.97,2),
 ('shoot_energy',energy,0.80,3),('shoot_launcher',launcher,0.95,2),('shoot_flame',flame,0.42,3),('throw',throw,0.6,3),
 ('dry_fire',dry_fire,0.45,2),('reload_start',reload_start,0.5,2),('reload_end',reload_end,0.55,2),
 ('swing',swing,0.9,3),('thrust',thrust,0.88,3),('heavy_swing',heavy_swing,0.9,2),('melee_hit',melee_hit,0.92,3),
 ('blunt_hit',blunt_hit,0.95,2),('bullet_cut',bullet_cut,0.8,3),('crit',crit,0.95,2)]
for name,fn,peak,c in SPECS: var(name,fn,peak,c)
print(len(os.listdir(OUT)),'files')
