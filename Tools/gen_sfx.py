"""
NEON DRIFT 音效合成：全部用噪声、正弦、滤波和包络从零合成。
风格要求：不要太吵、朴素、不花哨 —— 高频都削掉（低通 4–7 kHz），尾音短，峰值统一压到 -6 dB 以下，
常用的声音各出 3 个变体（游戏里随机挑一个，再加一点点音高随机）。
输出：16 bit 单声道 44.1 kHz WAV，文件名 名字_序号.wav
"""
import numpy as np, sys, os
from scipy.signal import butter, lfilter
from scipy.io import wavfile
SR=44100
OUT=sys.argv[1] if len(sys.argv)>1 else 'out'
os.makedirs(OUT,exist_ok=True)

def T(d): return np.arange(int(SR*d))/SR
def N(d,r): return r.standard_normal(int(SR*d))
def env(n,a=0.002,d=0.1,curve=4.0):
    x=np.arange(n)/SR; return np.minimum(1,x/max(a,1e-4))*np.exp(-curve*np.maximum(0,x-a)/max(d,1e-4))
def filt(x,kind,f,o=2):
    if kind=='band': b,a=butter(o,[f[0]/(SR/2),f[1]/(SR/2)],'band')
    else: b,a=butter(o,f/(SR/2),kind)
    return lfilter(b,a,x)
def lp(x,f,o=2): return filt(x,'low',f,o)
def hp(x,f,o=2): return filt(x,'high',f,o)
def bp(x,f1,f2,o=2): return filt(x,'band',(f1,f2),o)
def sweep(f0,f1,d,r=None):
    t=T(d); f=f0*(f1/f0)**(t/d); return np.sin(2*np.pi*np.cumsum(f)/SR)
def tone(f,d): return np.sin(2*np.pi*f*T(d))
def pad(x,d): n=int(SR*d); return np.pad(x,(0,max(0,n-len(x))))[:n]
def mix(*xs):
    n=max(len(x) for x in xs); return sum(np.pad(x,(0,n-len(x))) for x in xs)
def fade(x,ms=6):
    n=int(SR*ms/1000); x=x.copy(); x[-n:]*=np.linspace(1,0,n); x[:min(len(x),40)]*=np.linspace(0,1,min(len(x),40)); return x
def save(name,x,peak):
    x=lp(x,7000)                                        # 统一削掉刺耳的高频
    x=fade(x); x=x/(np.max(np.abs(x))+1e-9)*peak
    wavfile.write(f'{OUT}/{name}.wav',SR,(x*32767).astype(np.int16))

def var(name,fn,peak,count=3):
    for i in range(count):
        r=np.random.default_rng(hash(name)%10000+i*17)
        save(f'{name}_{i+1}',fn(r,i),peak)

# ---------------------------------------------------------------- 枪
def pistol(r,i):
    d=0.32; n=int(SR*d); p=1+0.04*(i-1)
    crack=bp(N(d,r),900,4500)*env(n,0.0005,0.025,6)
    body=lp(N(d,r),700)*env(n,0.001,0.07,5)*1.4
    thump=sweep(110*p,45,d)*env(n,0.001,0.08,4)*0.8
    return crack*0.5+body+thump
def rifle(r,i):
    d=0.18; n=int(SR*d)
    return bp(N(d,r),400,3000)*env(n,0.0005,0.03,5)*0.7+sweep(150+10*i,60,d)*env(n,0.001,0.04,5)*0.6
def shotgun(r,i):
    d=0.5; n=int(SR*d)
    return lp(N(d,r),1200)*env(n,0.001,0.12,4)*1.3+sweep(90,35,d)*env(n,0.001,0.15,4)+bp(N(d,r),800,3000)*env(n,0.0005,0.03,6)*0.4
def sniper(r,i):
    d=0.7; n=int(SR*d)
    return bp(N(d,r),600,4000)*env(n,0.0005,0.03,6)*0.7+sweep(80,30,d)*env(n,0.001,0.2,4)*1.1+lp(N(d,r),300)*env(n,0.02,0.4,3)*0.4
def energy(r,i):       # 电弧枪、智能弹：一声短促的“嗒”，带一点点电味
    d=0.16; n=int(SR*d)
    return sweep(700+60*i,260,d)*env(n,0.001,0.05,5)*0.6+bp(N(d,r),1500,4000)*env(n,0.0005,0.02,6)*0.25
def launcher(r,i):
    d=0.35; n=int(SR*d)
    return sweep(70,40,d)*env(n,0.002,0.12,4)+lp(N(d,r),500)*env(n,0.002,0.08,4)*0.6
def flame(r,i):
    d=0.22; n=int(SR*d)
    return bp(N(d,r),300,1800)*np.sin(np.pi*np.arange(n)/n)**0.8*0.8
def throw(r,i):
    d=0.2; n=int(SR*d)
    return bp(N(d,r),900+200*i,3500)*np.sin(np.pi*np.arange(n)/n)**2*0.7
def dry_fire(r,i):
    d=0.06; n=int(SR*d)
    return bp(N(d,r),1500,5000)*env(n,0.0003,0.006,6)+tone(2200,d)*env(n,0.0003,0.01,6)*0.2
def reload_start(r,i):
    d=0.16; n=int(SR*d)
    return bp(N(d,r),800,3500)*env(n,0.0005,0.015,6)+pad(bp(N(0.05,r),500,2500)*env(int(SR*0.05),0.0005,0.01,6)*0.6,0.0)[:0].sum()*0+0
def reload_end(r,i):
    d=0.12; n=int(SR*d)
    click=bp(N(d,r),1000,4500)*env(n,0.0003,0.01,6)
    return click+tone(1600,d)*env(n,0.0003,0.015,6)*0.25
# ---------------------------------------------------------------- 近战
def swing(r,i):        # 挥刀：一口带走的风声
    d=0.24; n=int(SR*d)
    e=np.sin(np.pi*np.clip(np.arange(n)/(SR*0.11),0,1))**2*np.exp(-np.arange(n)/(SR*0.12))
    return bp(N(d,r),1200+150*i,4200)*e
def thrust(r,i):
    d=0.14; n=int(SR*d)
    e=np.sin(np.pi*np.clip(np.arange(n)/(SR*0.05),0,1))*np.exp(-np.arange(n)/(SR*0.05))
    return bp(N(d,r),1800,4800)*e
def heavy_swing(r,i):
    d=0.35; n=int(SR*d)
    e=np.sin(np.pi*np.clip(np.arange(n)/(SR*0.18),0,1))**2*np.exp(-np.arange(n)/(SR*0.15))
    return bp(N(d,r),300,1600)*e
def melee_hit(r,i):    # 砍中：一下闷响
    d=0.14; n=int(SR*d)
    return lp(N(d,r),1400)*env(n,0.0005,0.03,5)+sweep(220-20*i,80,d)*env(n,0.0005,0.04,5)*0.7
def blunt_hit(r,i):
    d=0.3; n=int(SR*d)
    return lp(N(d,r),600)*env(n,0.001,0.06,5)*1.2+sweep(110,40,d)*env(n,0.001,0.1,4)
def bullet_cut(r,i):   # 劈掉子弹：很短的“叮”，不要太尖
    d=0.18; n=int(SR*d)
    f=1900+180*i
    return (tone(f,d)*0.5+tone(f*1.51,d)*0.2)*env(n,0.0005,0.05,5)+bp(N(d,r),2000,5000)*env(n,0.0003,0.008,6)*0.4
def crit(r,i):         # 暴击：闷一点的重击 + 一声低调的“叮”
    d=0.4; n=int(SR*d)
    return lp(N(d,r),1200)*env(n,0.0005,0.04,5)*1.1+sweep(160,50,d)*env(n,0.001,0.1,4)*0.9+(tone(1100,d)*0.3+tone(1650,d)*0.12)*env(n,0.002,0.18,3)
# ---------------------------------------------------------------- 移动 / 受击
def dash(r,i):
    d=0.26; n=int(SR*d)
    e=np.sin(np.pi*np.arange(n)/n)**1.5
    return bp(N(d,r),500+100*i,2600)*e
def perfect_dodge(r,i):
    d=0.5; n=int(SR*d)
    return (tone(660,d)*0.4+tone(990,d)*0.2)*env(n,0.03,0.25,3)+bp(N(d,r),800,2500)*np.sin(np.pi*np.arange(n)/n)*0.3
def hurt(r,i):
    d=0.3; n=int(SR*d)
    return lp(N(d,r),500)*env(n,0.001,0.06,5)+sweep(140,55,d)*env(n,0.001,0.1,4)*0.9
def armor_block(r,i):  # 护甲挡下：金属的“当”，收得快
    d=0.3; n=int(SR*d); f=520+40*i
    return (tone(f,d)*0.5+tone(f*2.7,d)*0.15+tone(f*4.1,d)*0.06)*env(n,0.0005,0.09,4)+lp(N(d,r),2000)*env(n,0.0005,0.02,6)*0.6
def armor_regen(r,i):
    d=0.12; n=int(SR*d)
    return tone(880+60*i,d)*env(n,0.004,0.05,4)*0.5
def death(r,i):
    d=1.0; n=int(SR*d)
    return sweep(120,35,d)*env(n,0.002,0.4,3)+lp(N(d,r),300)*env(n,0.01,0.5,3)*0.6
def swap(r,i):
    d=0.1; n=int(SR*d)
    return bp(N(d,r),900,3500)*env(n,0.0005,0.012,6)+pad(np.zeros(int(SR*0.045)),0.045).tolist().__len__()*0+0
def swap2(r,i):        # 换枪：两下小的咔哒
    d=0.12; n=int(SR*d); x=np.zeros(n)
    for k,off in enumerate((0,0.05)):
        s=int(off*SR); L=int(SR*0.03); x[s:s+L]+=bp(N(0.03,r),900+300*k,3800)*env(L,0.0003,0.006,6)
    return x
# ---------------------------------------------------------------- 拾取 / 道具 / 交互
def coin(r,i):
    d=0.12; n=int(SR*d); f=1500+100*i
    return (tone(f,d)*0.5+tone(f*2.01,d)*0.15)*env(n,0.0005,0.04,4)
def energy_pick(r,i):
    d=0.12; n=int(SR*d)
    return sweep(500,900,d)*env(n,0.002,0.05,4)*0.6
def ammo_pick(r,i):
    d=0.12; n=int(SR*d); f=2300+150*i
    return tone(f,d)*env(n,0.0003,0.02,5)*0.4+bp(N(d,r),1500,5000)*env(n,0.0003,0.006,6)*0.3
def item_use(r,i):
    d=0.3; n=int(SR*d)
    return (tone(523,d)*0.4+tone(784,d)*0.25)*env(n,0.01,0.15,3)
def buy(r,i):
    d=0.25; x=np.zeros(int(SR*d))
    for k,f in enumerate((1200,1600)):
        s=int(k*0.06*SR); seg=tone(f,0.1)*env(int(SR*0.1),0.0005,0.04,4)*0.5; x[s:s+len(seg)]+=seg
    return x
def weapon_pickup(r,i):
    d=0.25; n=int(SR*d); x=np.zeros(n)
    for k,off in enumerate((0,0.07,0.13)):
        s=int(off*SR); L=int(SR*0.04); x[s:s+L]+=bp(N(0.04,r),700+400*k,3500)*env(L,0.0003,0.01,6)*(1-0.2*k)
    return x
def chest_open(r,i):
    d=0.6; n=int(SR*d)
    creak=sweep(180,260,0.35)*np.sin(np.pi*np.arange(int(SR*0.35))/(SR*0.35))*0.25
    thunk=lp(N(0.25,r),500)*env(int(SR*0.25),0.001,0.06,5)
    return mix(creak,np.pad(thunk,(int(SR*0.3),0)))
def buff(r,i):         # 拿到增益 / 协议：低调的两个音
    d=0.6; x=np.zeros(int(SR*d))
    for k,f in enumerate((440,660)):
        s=int(k*0.1*SR); L=int(SR*0.4); x[s:s+L]+=tone(f,0.4)*env(L,0.01,0.2,3)*0.4
    return x
def protocol_appear(r,i):
    d=1.0; n=int(SR*d)
    sw=sweep(120,240,d)*np.sin(np.pi*np.arange(n)/n)*0.4
    gl=np.zeros(n)
    for _ in range(10):
        s=r.integers(0,n-3000); L=r.integers(400,1800); gl[s:s+L]+=bp(N(L/SR,r),600,2500)[:L]*0.25
    return sw+gl
def room_clear(r,i):
    d=1.2; x=np.zeros(int(SR*d))
    for k,f in enumerate((392,494,587)):
        s=int(k*0.12*SR); L=int(SR*0.8); x[s:s+L]+=tone(f,0.8)*env(L,0.02,0.4,3)*0.3
    return x
def ladder(r,i):       # 爬梯子：几下脚踩铁横档
    d=0.7; n=int(SR*d); x=np.zeros(n)
    for k in range(4):
        s=int(k*0.15*SR); L=int(SR*0.12)
        x[s:s+L]+=(tone(300+20*k,0.12)*0.4+lp(N(0.12,r),1500)*0.5)*env(L,0.0005,0.03,5)
    return x
# ---------------------------------------------------------------- 骇入
def hack_charge(r,i):
    d=0.3; n=int(SR*d)
    return sweep(300,700,d)*np.minimum(1,np.arange(n)/(SR*0.05))*0.35
def hack_fire(r,i):
    d=0.25; n=int(SR*d); x=np.zeros(n)
    for k in range(5):
        s=int(k*0.035*SR); L=int(SR*0.03); x[s:s+L]+=np.sign(tone(400+120*((k*3+i)%5),0.03))*env(L,0.0005,0.012,5)*0.25
    return lp(x,3000)
def no_ram(r,i):
    d=0.2; n=int(SR*d)
    return lp(np.sign(tone(110,d)),800)*env(n,0.002,0.08,4)*0.4
# ---------------------------------------------------------------- 环境 / 敌人
def explosion(r,i):
    d=1.0; n=int(SR*d)
    return lp(N(d,r),700)*env(n,0.002,0.25,3.5)*1.2+sweep(70,30,d)*env(n,0.002,0.3,3)
def shock_warn(r,i):
    d=0.6; n=int(SR*d); x=np.zeros(n)
    for _ in range(7):
        s=r.integers(0,n-1500); L=r.integers(150,700)
        x[s:s+L]+=hp(r.standard_normal(L),1800)*np.exp(-np.arange(L)/(L*0.3))*r.uniform(0.2,0.6)
    return lp(x,4500)
def shock_live(r,i):
    d=1.0; n=int(SR*d); x=np.zeros(n)
    for _ in range(18):
        s=r.integers(0,n-1500); L=r.integers(150,1000)
        x[s:s+L]+=hp(r.standard_normal(L),1500)*np.exp(-np.arange(L)/(L*0.3))*r.uniform(0.2,0.7)
    hum=lp(np.sign(tone(60,d)),400)*0.12*np.sin(np.pi*np.arange(n)/n)
    return lp(x,4500)+hum
def enemy_shoot(r,i):
    d=0.2; n=int(SR*d)
    return bp(N(d,r),300,2000)*env(n,0.0005,0.03,5)*0.7+sweep(180,70,d)*env(n,0.001,0.05,5)*0.5
def enemy_telegraph(r,i):   # 敌人出招前的预警：一声短的低音，提醒但不刺耳
    d=0.2; n=int(SR*d)
    return (tone(330,d)*0.4+tone(330*1.5,d)*0.15)*env(n,0.005,0.08,3)
def enemy_lunge(r,i):
    d=0.2; n=int(SR*d)
    return bp(N(d,r),600,2800)*np.sin(np.pi*np.arange(n)/n)**2*0.8
def brute_slam(r,i):
    d=0.6; n=int(SR*d)
    return lp(N(d,r),400)*env(n,0.001,0.12,4)*1.3+sweep(80,30,d)*env(n,0.001,0.2,3)
def enemy_hit(r,i):
    d=0.1; n=int(SR*d)
    return lp(N(d,r),1500)*env(n,0.0005,0.02,5)+sweep(260,120,d)*env(n,0.0005,0.03,5)*0.4
def enemy_death(r,i):
    d=0.4; n=int(SR*d)
    return lp(N(d,r),800)*env(n,0.001,0.1,4)+sweep(150,50,d)*env(n,0.001,0.12,4)*0.6
def turret_shoot(r,i):
    d=0.18; n=int(SR*d)
    return bp(N(d,r),500,2500)*env(n,0.0005,0.02,6)*0.6+tone(240,d)*env(n,0.001,0.04,5)*0.4
# ---------------------------------------------------------------- 雨（环境循环）
def rain_loop():
    d=8.0; r=np.random.default_rng(99); n=int(SR*d)
    x=lp(hp(r.standard_normal(n),400),3500)*0.25                        # 细密的雨
    drops=np.zeros(n)
    for _ in range(600):
        s=r.integers(0,n-800); L=r.integers(100,500)
        drops[s:s+L]+=bp(r.standard_normal(L),1500,4000)*np.exp(-np.arange(L)/(L*0.2))*r.uniform(0.05,0.25)
    x=x+drops
    # 首尾交叉淡化，循环时听不出接缝
    f=int(SR*0.5); x[:f]=x[:f]*np.linspace(0,1,f)+x[-f:]*np.linspace(1,0,f); x=x[:-f]
    return x

# 名字、生成函数、峰值（越常听到的越小声）、变体数
SPECS=[
 ('shoot_pistol',pistol,0.45,3),('shoot_rifle',rifle,0.28,3),('shoot_shotgun',shotgun,0.5,3),('shoot_sniper',sniper,0.5,2),
 ('shoot_energy',energy,0.3,3),('shoot_launcher',launcher,0.45,2),('shoot_flame',flame,0.18,3),('throw',throw,0.3,3),
 ('dry_fire',dry_fire,0.3,2),('reload_start',reload_start,0.3,2),('reload_end',reload_end,0.32,2),
 ('swing',swing,0.32,3),('thrust',thrust,0.3,3),('heavy_swing',heavy_swing,0.38,2),('melee_hit',melee_hit,0.4,3),('blunt_hit',blunt_hit,0.5,2),
 ('bullet_cut',bullet_cut,0.22,3),('crit',crit,0.45,2),
 ('dash',dash,0.25,3),('perfect_dodge',perfect_dodge,0.3,1),('hurt',hurt,0.5,2),('armor_block',armor_block,0.32,2),('armor_regen',armor_regen,0.15,1),('death',death,0.5,1),
 ('swap',swap2,0.28,2),('pickup_credit',coin,0.12,3),('pickup_energy',energy_pick,0.12,2),('pickup_ammo',ammo_pick,0.18,2),
 ('item_use',item_use,0.3,1),('buy',buy,0.3,1),('weapon_pickup',weapon_pickup,0.32,1),('chest_open',chest_open,0.38,1),
 ('buff',buff,0.3,1),('protocol_appear',protocol_appear,0.3,1),('room_clear',room_clear,0.28,1),('ladder',ladder,0.35,1),
 ('hack_charge',hack_charge,0.2,1),('hack_fire',hack_fire,0.28,2),('no_ram',no_ram,0.25,1),
 ('explosion',explosion,0.55,2),('shock_warn',shock_warn,0.2,2),('shock_live',shock_live,0.26,2),
 ('enemy_shoot',enemy_shoot,0.3,3),('enemy_telegraph',enemy_telegraph,0.18,2),('enemy_lunge',enemy_lunge,0.28,2),('brute_slam',brute_slam,0.5,2),
 ('enemy_hit',enemy_hit,0.25,3),('enemy_death',enemy_death,0.35,2),('turret_shoot',turret_shoot,0.28,2),
]
for name,fn,peak,cnt in SPECS: var(name,fn,peak,cnt)
save('rain_loop_1',rain_loop(),0.35)
print(len(os.listdir(OUT)),'files')
