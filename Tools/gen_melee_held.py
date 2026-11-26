"""
NEON DRIFT 近战武器「手持版」像素图：挥砍动作里拿在零手上的那一套。
和人物同一个像素密度（人物约 148 像素高 ≈ 1.7 米），按真实比例画：武士刀齐腰长，链锯剑过半身，蝴蝶刀一拳大。
画法和人物统一：冷调的深蓝灰色阶（和风衣一样），整体压暗、偏脏（油污、斑点、划痕、锈），每种材质 5 级明暗，光从左上来——零件顶边提亮、底边压暗，
刃口一道冷白高光；外轮廓用人物描边同一个深蓝黑 (10,13,23)。只有烧红的刃、能量部件这些发光的地方用亮色（不用蓝/青）。
输出：<out>/<slug>.png + held.json（握持点 pivot，0–1，和 weapons.json 同格式）
"""
import json, os, re, sys, math, random
from PIL import Image
OUT = sys.argv[1] if len(sys.argv) > 1 else 'held'
os.makedirs(OUT, exist_ok=True)
OUTLINE = (10, 13, 23)
RAMP = {   # 暗 → 亮，冷调阴影
 'steel':  [(28,32,44),(52,58,74),(84,90,106),(122,126,140),(170,174,184)],
 'dsteel': [(16,19,29),(28,33,46),(46,52,66),(70,76,92),(104,110,124)],
 'black':  [(10,12,20),(17,20,30),(26,30,42),(42,46,60),(74,78,94)],
 'edge':   [(118,124,138),(160,166,178),(196,200,210),(222,225,232),(242,244,248)],
 'grip':   [(12,13,19),(20,22,29),(30,32,41),(44,46,57),(62,64,77)],
 'leather':[(28,20,24),(46,30,32),(66,42,42),(88,58,54),(112,80,72)],
 'wrap':   [(36,16,20),(58,26,28),(80,36,36),(104,50,48),(130,70,64)],
 'paint':  [(46,18,22),(78,28,30),(110,40,38),(140,60,52),(170,94,82)],
 'brass':  [(46,36,26),(76,60,38),(106,86,52),(138,118,74),(176,158,110)],
 'tape':   [(32,30,34),(50,48,50),(70,66,64),(92,88,82),(118,114,106)],
 'rust':   [(44,26,24),(68,38,30),(94,54,38),(118,72,50),(142,94,66)],
 'orange': [(52,28,22),(88,46,28),(124,68,36),(156,94,52),(186,128,78)],
 'white':  [(104,106,116),(144,146,156),(180,182,190),(206,208,216),(228,230,236)],
 'cable':  [(20,20,28),(34,34,44),(52,52,64),(74,74,88),(100,100,116)],
 'blade':  [(34,38,52),(62,68,86),(98,104,122),(146,150,166),(206,210,222)],     # 打磨过的刃钢：色阶拉得更开，反光更亮
 'bblade': [(8,10,17),(16,19,29),(28,32,46),(52,56,74),(110,114,132)],          # 打磨过的黑刃
 'gold':   [(52,40,24),(88,68,36),(126,100,54),(168,140,80),(214,192,132)],     # 镶边、铭牌
}
# 整体压暗、去一点饱和：脏、旧、在夜里
def _dark(c, k=0.78, desat=0.25):
    g = sum(c)/3
    return tuple(int(round((v*(1-desat)+g*desat)*k)) for v in c)
RAMP = {m: [_dark(c, 0.80 if m == 'edge' else 0.86 if m in ('blade','bblade','gold') else 0.74, 0.30 if m in ('paint','orange','brass','leather','wrap','rust') else 0.12) for c in r] for m, r in RAMP.items()}
GRIME = (34, 30, 30)          # 油污、泥灰
POLISH = ('edge', 'blade', 'bblade', 'gold')
GLOW = {'hot':(255,122,52),'hot2':(255,188,96),'hot3':(255,228,170),'amber':(255,176,72),'amber2':(255,214,140),
        'green':(170,230,90),'green2':(214,250,150),'arc':(236,96,200),'arc2':(255,184,236),'red':(236,58,58),'red2':(255,130,120)}

class Art:
    def __init__(s, w, h):
        s.w, s.h = w, h; s.m = [[None]*w for _ in range(h)]; s.lv = {}; s.g = {}; s.pivot = (0, 0)
        s.sheens = []; s.glints = []
    def px(s, x, y, mat, lv=None):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < s.w and 0 <= y < s.h:
            s.m[y][x] = mat
            if lv is None: s.lv.pop((x, y), None)
            else: s.lv[(x, y)] = lv
    def rect(s, x, y, w, h, mat, lv=None):
        for j in range(int(h)):
            for i in range(int(w)): s.px(x+i, y+j, mat, lv)
    def poly(s, pts, mat, lv=None):
        ys = [p[1] for p in pts]
        for y in range(int(math.floor(min(ys))), int(math.ceil(max(ys)))+1):
            xs = []
            for i in range(len(pts)):
                (x0,y0),(x1,y1) = pts[i], pts[(i+1)%len(pts)]
                if (y0 <= y+0.5 < y1) or (y1 <= y+0.5 < y0): xs.append(x0 + (y+0.5-y0)*(x1-x0)/(y1-y0))
            xs.sort()
            for a, b in zip(xs[::2], xs[1::2]):
                for x in range(int(math.ceil(a-0.5)), int(math.floor(b-0.5))+1): s.px(x, y, mat, lv)
    def line(s, x0, y0, x1, y1, mat, lv=None):
        n = int(max(abs(x1-x0), abs(y1-y0)))
        for k in range(n+1):
            t = k/max(1,n); s.px(x0+(x1-x0)*t, y0+(y1-y0)*t, mat, lv)
    def circle(s, cx, cy, r, mat, lv=None, ring=None):
        for y in range(int(cy-r-1), int(cy+r+2)):
            for x in range(int(cx-r-1), int(cx+r+2)):
                d = math.hypot(x-cx, y-cy)
                if d <= r and (ring is None or d >= r-ring): s.px(x, y, mat, lv)
    def hole(s, x, y): s.m[int(y)][int(x)] = None
    def sheen(s, x0, x1, period=14, width=3):
        """刃面上斜着的一道道反光带（只作用在 blade / bblade 上）。"""
        s.sheens.append((x0, x1, period, width))
    def glint(s, x, y, big=False):
        """高光点：刀尖、镡角这些地方的一点亮光（big 带十字）。"""
        s.glints.append((int(x), int(y), big))
    def glow(s, x, y, col):
        if 0 <= int(x) < s.w and 0 <= int(y) < s.h: s.g[(int(x), int(y))] = GLOW[col]
    def render(s, seed, wear=0.6):
        rnd = random.Random(seed); W, H = s.w+2, s.h+2
        img = Image.new('RGBA', (W, H), (0,0,0,0)); P = img.load()
        at = lambda x, y: s.m[y][x] if 0 <= x < s.w and 0 <= y < s.h else None
        for y in range(s.h):
            for x in range(s.w):
                mt = at(x, y)
                if mt is None: continue
                if (x, y) in s.lv: k = s.lv[(x, y)]
                else:
                    k = 2
                    if at(x, y-1) != mt: k += 1                   # 顶边受光
                    if at(x, y+1) != mt: k -= 1                   # 底边背光
                    if at(x-1, y) is None and at(x, y-1) is None: k += 1
                    if mt in ('steel','dsteel','black') and rnd.random() < 0.05*wear: k += rnd.choice((-1, 1))   # 磨损
                    if mt in ('steel','paint') and rnd.random() < 0.04*wear: mt2 = 'rust'; P[x+1, y+1] = RAMP['rust'][max(0,min(4,k))]+(255,); continue
                P[x+1, y+1] = RAMP[mt][max(0, min(4, k))] + (255,)
        # 脏污：成片的油污泥灰（低频噪声）+ 零星的深色斑点 + 浅色划痕
        def noise(x, y, sc):
            xi, yi = int(x//sc), int(y//sc); fx, fy = x/sc-xi, y/sc-yi
            h = lambda i, j: random.Random((i*73856093) ^ (j*19349663) ^ seed).random()
            a0 = h(xi, yi)*(1-fx)+h(xi+1, yi)*fx; a1 = h(xi, yi+1)*(1-fx)+h(xi+1, yi+1)*fx
            return a0*(1-fy)+a1*fy
        for y in range(s.h):
            for x in range(s.w):
                c = P[x+1, y+1]
                if not c[3] or (x, y) in s.g: continue
                n = noise(x, y, 5)*0.65 + noise(x, y, 2)*0.35
                t = max(0.0, (n-0.42)*1.4) * (0.55+0.45*wear)                                    # 污渍浓度
                if s.m[y][x] in POLISH: t *= 0.25                                              # 刃面、镶金保养得好，脏污少
                r, g, b = [int(c[i]*(1-t)+GRIME[i]*t) for i in range(3)]
                if rnd.random() < 0.06*(0.5+wear): r, g, b = int(r*0.72), int(g*0.70), int(b*0.70)      # 深色斑点
                elif rnd.random() < 0.025 and s.m[y][x] in ('steel','dsteel','black','paint'):          # 划痕
                    r, g, b = min(255, r+22), min(255, g+22), min(255, b+24)
                P[x+1, y+1] = (r, g, b, 255)
        for (x, y), c in s.g.items(): P[x+1, y+1] = tuple(int(v*0.86) for v in c) + (255,)
        # 刃面反光带：斜向一道亮一道暗，像打磨过的金属在反光
        for (x0, x1, period, width) in s.sheens:
            for y in range(s.h):
                for x in range(x0, min(x1, s.w)):
                    if s.m[y][x] not in ('blade', 'bblade') or (x, y) in s.g: continue
                    ph = (x + y*2) % period
                    c = P[x+1, y+1]
                    if ph < width: P[x+1, y+1] = tuple(min(255, int(v*1.22+6)) for v in c[:3]) + (255,)
                    elif ph < width+2: P[x+1, y+1] = tuple(int(v*0.86) for v in c[:3]) + (255,)
        for (x, y, big) in s.glints:
            if 0 <= x < s.w and 0 <= y < s.h:
                P[x+1, y+1] = (236, 238, 244, 255)
                if big:
                    for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                        if 0 <= x+dx < s.w and 0 <= y+dy < s.h and P[x+1+dx, y+1+dy][3]:
                            c = P[x+1+dx, y+1+dy]; P[x+1+dx, y+1+dy] = tuple(min(255, (v+236)//2) for v in c[:3]) + (255,)
        out = img.copy(); O = out.load()
        for y in range(H):
            for x in range(W):
                if P[x, y][3]: continue
                nb = [(dx, dy) for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)) if 0 <= x+dx < W and 0 <= y+dy < H and P[x+dx, y+dy][3]]
                if not nb: continue
                if (0, 1) in nb and len(nb) == 1:                       # 顶边外侧（受光面）：用材质本身的深色，轮廓不发死
                    c = P[x, y+1]; O[x, y] = tuple(int(c[i]*0.38 + OUTLINE[i]*0.62) for i in range(3)) + (255,)
                else: O[x, y] = OUTLINE + (255,)
        return out

def slug(n): return re.sub(r'[^a-z0-9]+', '_', n.lower()).strip('_')
def wrap_grip(a, x, y, L, h, base='grip', cord='wrap'):
    """缠绳握柄：菱形交叉的缠绳，露出的柄底色。"""
    a.rect(x, y, L, h, base)
    for i in range(0, L, 4):
        a.line(x+i, y, x+i+min(3,h-1), y+h-1, cord); a.line(x+i+min(3,h-1), y, x+i, y+h-1, cord)
        a.px(x+i+1, y+h//2, base, 0)
def rivets(a, pts, mat='steel'):
    for p in pts: a.px(*p, mat, 4)

# ---------------------------------------------------------------- 刀
def W_nameless():          # 无铭：直刃黑刀，68
    a = Art(68, 11)
    a.rect(0, 4, 2, 4, 'dsteel'); a.line(0, 4, 0, 7, 'gold')                                                     # 柄头
    wrap_grip(a, 2, 4, 14, 4, 'black', 'wrap')                                                                # 黑柄缠暗红
    a.rect(16, 1, 2, 10, 'dsteel'); a.line(16, 1, 16, 10, 'gold'); a.px(17, 1, 'steel', 4); a.px(17, 10, 'dsteel', 0)   # 方镡
    a.rect(18, 4, 3, 4, 'gold'); a.line(18, 4, 20, 4, 'gold', 4)                                            # 鎺
    a.rect(21, 4, 39, 4, 'bblade')                                                                             # 刀身
    a.line(21, 5, 59, 5, 'bblade', 3)                                                                          # 鎬筋
    a.line(21, 4, 59, 4, 'bblade', 2)
    a.line(21, 7, 59, 7, 'edge', 2); a.line(24, 6, 57, 6, 'bblade', 1)                                         # 刃口冷光
    for x in range(26, 58, 7): a.px(x, 7, 'edge', 4)
    a.poly([(60,4),(67,6.5),(60,8)], 'bblade'); a.line(60, 7, 66, 7, 'edge', 3); a.px(66, 6, 'edge', 4)       # 切先
    a.line(60, 4, 65, 5.5, 'bblade', 3)
    a.glow(40, 7, 'red'); a.glow(52, 7, 'red')                                                                # 刃口一点暗红（无铭的红边）
    a.sheen(21, 67, 16, 3); a.glint(66, 6, True); a.glint(17, 1); a.glint(20, 4)
    for p in ((23, 5), (24, 5), (23, 6)): a.px(*p, 'gold', 2)                                                  # 刀身上的金色铭
    a.pivot = (9, 6); return a
def W_katana():            # 破晓热能武士刀，68，微弯
    a = Art(68, 12)
    a.rect(0, 5, 2, 4, 'gold'); wrap_grip(a, 2, 5, 14, 4, 'leather', 'tape')
    a.circle(17, 7, 3.2, 'gold'); a.circle(17, 7, 1.2, 'dsteel', 1)                                         # 圆镡
    a.rect(20, 5, 3, 4, 'gold')
    pts_top = [(23 + i, 5 - (i/44.0)**2*3) for i in range(45)]
    for x, yt in pts_top:
        a.line(x, yt, x, yt+3, 'bblade'); a.px(x, yt, 'blade', 3); a.px(x, yt+1, 'bblade', 3)
    a.poly([(67,2),(62,1.6),(62,5)], 'bblade'); a.px(66, 2, 'blade', 4)
    for x, yt in pts_top:
        a.glow(x, yt+3, 'hot2' if x % 6 == 0 else 'hot')                                                     # 烧红的刃口
        if x % 3 == 0: a.glow(x, yt+2, 'hot')
    a.glow(63, 4, 'hot2'); a.glow(65, 3, 'hot3'); a.glow(21, 6, 'amber')
    a.sheen(23, 67, 15, 3); a.glint(67, 2, True); a.glint(16, 5)
    a.pivot = (9, 7); return a
def W_machete():           # 水管砍刀，44
    a = Art(44, 14)
    a.rect(0, 5, 14, 4, 'steel'); a.line(0, 5, 13, 5, 'steel', 4)                                              # 钢管
    for i in range(1, 13, 3): a.rect(i, 5, 2, 4, 'tape')                                                       # 胶布
    a.circle(0, 7, 1.5, 'dsteel'); a.px(0, 7, 'black', 0)                                                      # 管口
    a.rect(14, 3, 2, 8, 'dsteel'); rivets(a, [(14, 4), (14, 9)])
    a.poly([(16,3),(36,3),(43,6),(40,11),(16,11)], 'blade')                                                    # 宽刃
    a.line(16, 3, 36, 3, 'blade', 4); a.line(17, 10, 40, 10, 'edge', 2); a.line(18, 11, 39, 11, 'edge', 1)
    a.line(40, 9, 42, 7, 'edge', 3)
    for p in ((20,6),(21,6),(21,7),(27,5),(31,8),(32,8),(24,9)): a.px(*p, 'rust', 2)                           # 锈斑
    a.line(18, 6, 34, 6, 'blade', 3)
    a.sheen(16, 43, 12, 2); a.glint(42, 7, True)
    a.pivot = (7, 7); return a
def W_mantis():            # 螳螂臂刃，42
    a = Art(42, 18)
    a.poly([(0,6),(14,5),(16,8),(14,12),(0,12)], 'dsteel'); a.line(0, 6, 14, 5, 'steel', 3)                  # 前臂套
    a.rect(3, 8, 8, 2, 'black'); a.glow(4, 8, 'red'); a.glow(7, 8, 'red2'); rivets(a, [(1, 7), (12, 7), (1, 11)])
    a.poly([(14,6),(22,2),(41,0),(26,4),(17,8)], 'blade'); a.line(22, 2, 41, 0, 'edge', 3); a.line(26, 3, 38, 1, 'edge', 2)   # 上刃
    a.poly([(14,11),(24,13),(39,17),(23,15),(15,13)], 'blade'); a.line(24, 14, 39, 17, 'edge', 2)            # 下刃
    a.line(1, 10, 13, 10, 'gold', 3); a.sheen(14, 42, 10, 2); a.glint(40, 0, True); a.glint(38, 17)
    a.pivot = (6, 9); return a
def W_wire():              # 单分子线，40
    a = Art(40, 16)
    a.poly([(0,4),(9,4),(11,6),(11,10),(9,12),(0,12)], 'dsteel'); a.line(0, 4, 9, 4, 'steel', 3)              # 腕部发射器
    a.circle(6, 8, 2.4, 'black'); a.glow(6, 8, 'arc2'); a.glow(5, 8, 'arc'); a.glow(7, 8, 'arc')
    rivets(a, [(1, 6), (1, 10)])
    for x in range(11, 40):
        y = 8 + math.sin((x-11)*0.22)*3*(x-11)/28
        a.glow(x, y, 'arc2' if x % 4 == 0 else 'arc')
    a.circle(6, 8, 3.4, 'gold', ring=1)
    a.glow(6, 8, 'arc2'); a.glint(10, 5)
    a.pivot = (5, 8); return a
def W_butterfly():         # 蝴蝶刀，20
    a = Art(20, 8)
    a.rect(0, 2, 9, 2, 'steel'); a.rect(0, 4, 9, 2, 'dsteel'); a.line(0, 2, 8, 2, 'steel', 4)                  # 两片柄
    for i in (2, 5): a.px(i, 3, 'black', 0)                                                                    # 镂空
    a.px(0, 6, 'steel', 3); a.px(1, 6, 'steel', 2)                                                             # 锁扣
    a.poly([(9,2),(15,1.5),(19,3),(15,5.5),(9,5.5)], 'blade'); a.line(10, 5, 16, 5, 'edge', 3); a.px(18, 3, 'edge', 4)
    a.line(10, 3, 15, 3, 'blade', 3)
    a.px(8, 3, 'gold', 3); a.px(8, 4, 'gold', 2); a.glint(19, 3)
    a.pivot = (4, 4); return a
def W_chain():             # 回旋链刃，60（收拢成一把剑）
    a = Art(60, 14)
    a.rect(0, 5, 2, 4, 'dsteel'); wrap_grip(a, 2, 5, 10, 4, 'grip', 'cable'); a.rect(12, 3, 2, 8, 'dsteel'); a.line(12, 3, 12, 10, 'gold')
    a.line(14, 7, 58, 7, 'cable', 2)                                                                          # 中间的钢缆
    for k in range(7):
        x = 15 + k*6
        a.poly([(x,4),(x+5,3.5),(x+6,7),(x+5,10),(x,9.5)], 'blade'); a.line(x+1, 4, x+5, 3.5, 'blade', 4); a.line(x+1, 9, x+5, 10, 'edge', 2)
        a.px(x, 7, 'cable', 1)
    a.poly([(57,4),(59,7),(57,10)], 'blade'); a.px(59, 7, 'edge', 4)
    a.sheen(15, 60, 9, 2); a.glint(59, 7, True)
    a.pivot = (7, 7); return a
def W_saw():               # 首斩链锯剑，88
    a = Art(88, 22)
    a.rect(0, 9, 3, 5, 'dsteel'); wrap_grip(a, 3, 9, 12, 5, 'grip', 'tape')                                    # 后握把
    a.poly([(15,4),(30,4),(32,6),(32,17),(15,17)], 'orange'); a.line(15, 4, 30, 4, 'orange', 4)               # 发动机外壳
    a.rect(17, 7, 9, 6, 'black'); [a.px(18+i, 8+j, 'dsteel', 2) for i in range(0, 8, 2) for j in range(0, 5, 2)]   # 散热格栅
    a.rect(27, 8, 3, 3, 'dsteel'); a.glow(28, 9, 'amber'); a.glow(28, 10, 'amber2')
    a.line(16, 1, 28, 1, 'grip'); a.line(16, 1, 16, 4, 'grip'); a.line(28, 1, 28, 4, 'grip')                  # 上方提把
    rivets(a, [(15, 15), (31, 15), (15, 6)])
    a.poly([(32,6),(84,7),(87,11),(84,15),(32,16)], 'blade')                                                  # 导板
    a.line(32, 6, 84, 7, 'blade', 4); a.rect(36, 9, 44, 4, 'dsteel'); a.line(36, 11, 79, 11, 'black', 1)
    for x in range(33, 85, 3):                                                                                # 链齿
        a.px(x, 5, 'steel', 3); a.px(x+1, 5, 'dsteel', 1); a.px(x, 17, 'steel', 2); a.px(x+1, 17, 'dsteel', 1)
    a.circle(83, 11, 3.5, 'dsteel', ring=1.2); a.px(83, 11, 'steel', 4)
    a.line(15, 4, 30, 4, 'gold', 3); a.rect(19, 14, 6, 2, 'gold'); a.px(20, 14, 'black', 1); a.px(23, 14, 'black', 1)   # 金色镶边 + 铭牌
    a.sheen(32, 87, 18, 3); a.glint(86, 11, True)
    a.pivot = (9, 11); return a

# ---------------------------------------------------------------- 钝器
def W_crowbar():           # 撬棍，54
    a = Art(54, 17)
    a.rect(6, 7, 40, 4, 'paint'); a.line(6, 7, 45, 7, 'paint', 4); a.line(6, 10, 45, 10, 'paint', 1)       # 直杆（红漆，六角断面）
    a.line(8, 8, 44, 8, 'paint', 3)
    for i in range(8, 20, 3): a.rect(i, 7, 2, 4, 'tape')                                                       # 胶布
    for p in ((24,8),(25,8),(25,9),(31,9),(37,8),(41,8),(42,9)): a.px(*p, 'dsteel', 2)                         # 掉漆露铁
    a.poly([(46,7),(50,3.5),(53,4.5),(53,7),(51,10),(46,11)], 'paint'); a.line(50, 3.5, 53, 4.5, 'paint', 4)  # 弯钩
    a.px(53, 4, 'dsteel', 2); a.px(52, 3, 'dsteel', 2); a.line(51, 9, 53, 7, 'dsteel', 1)
    a.poly([(6,7),(1.5,11.5),(0,16),(3.5,16),(6,11)], 'paint'); a.px(0, 16, 'dsteel', 2); a.px(3, 16, 'dsteel', 2)   # 撬爪
    a.line(1, 13, 4, 10, 'dsteel', 1)
    a.glint(53, 5); a.glint(46, 7)
    for p in ((30, 9), (31, 9), (32, 9)): a.px(*p, 'gold', 1)                                                   # 钢印
    a.pivot = (13, 9); return a
def W_hammer():            # 破岩动力锤，76
    a = Art(76, 26)
    a.rect(0, 11, 54, 4, 'dsteel'); a.line(0, 11, 53, 11, 'steel', 3)                                         # 长杆
    wrap_grip(a, 2, 11, 14, 4, 'grip', 'tape'); a.rect(0, 10, 2, 6, 'dsteel')
    for x in (24, 38): a.rect(x, 10, 2, 6, 'gold'); a.px(x, 10, 'gold', 4)                                    # 加强箍
    a.poly([(52,9),(56,7),(56,19),(52,17)], 'dsteel')                                                         # 颈部
    a.rect(56, 2, 18, 22, 'dsteel'); a.line(56, 2, 73, 2, 'steel', 4); a.rect(56, 2, 1, 22, 'steel')          # 锤头
    a.rect(58, 5, 14, 3, 'orange'); a.rect(58, 18, 14, 3, 'orange')                                            # 警示漆
    for x in range(58, 72, 4): a.line(x, 5, x+2, 7, 'black', 1); a.line(x, 18, x+2, 20, 'black', 1)
    a.rect(59, 10, 12, 6, 'black'); [a.glow(60+i, 12, 'amber') for i in range(10)]; [a.glow(60+i, 13, 'hot') for i in range(10)]   # 动力舱
    a.glow(61, 11, 'amber2'); a.glow(68, 14, 'amber2')
    a.rect(74, 4, 2, 18, 'blade'); a.line(75, 4, 75, 21, 'blade', 1)                                         # 打击面
    rivets(a, [(57, 4), (72, 4), (57, 22), (72, 22)], 'gold')
    a.line(56, 2, 73, 2, 'gold', 3); a.glint(75, 5, True); a.glint(56, 2)
    a.pivot = (9, 13); return a
def W_sign():              # 路牌，80
    a = Art(80, 36)
    a.rect(0, 16, 54, 3, 'steel'); a.line(0, 16, 53, 16, 'steel', 4); a.line(0, 18, 53, 18, 'dsteel', 1)      # 杆
    for i in range(1, 12, 3): a.rect(i, 16, 2, 3, 'tape')
    for x in range(14, 52, 6): a.px(x, 17, 'steel', 1)                                                         # 杆上的孔
    cx, cy, R = 64, 17, 15.5
    oct_ = [(cx + R*math.cos(math.radians(22.5+45*i)), cy + R*math.sin(math.radians(22.5+45*i))) for i in range(8)]
    a.poly(oct_, 'white')                                                                                      # 白边
    oct2 = [(cx + (R-2)*math.cos(math.radians(22.5+45*i)), cy + (R-2)*math.sin(math.radians(22.5+45*i))) for i in range(8)]
    a.poly(oct2, 'paint')
    a.rect(52, 14, 24, 6, 'white')                                                                             # 白字条（看不清字的抽象笔画）
    for x in range(54, 74, 3): a.rect(x, 15, 2, 4, 'paint', 1)
    for p in ((58, 7), (70, 25), (66, 9)): a.px(*p, 'black', 0); a.px(p[0]+1, p[1], 'white', 4)                # 弹孔
    a.line(54, 26, 60, 29, 'paint', 1)                                                                         # 凹痕
    a.rect(50, 15, 3, 5, 'dsteel'); rivets(a, [(51, 15), (51, 19)])
    a.glint(64, 2); a.glint(50, 15)
    a.pivot = (6, 17); return a
def W_baton():             # 电棍，38
    a = Art(38, 10)
    a.rect(0, 3, 12, 4, 'grip'); [a.line(1+i, 3, 1+i, 6, 'grip', 3) for i in range(0, 11, 3)]                  # 防滑纹
    a.rect(12, 2, 2, 6, 'gold'); a.glow(5, 3, 'amber')
    a.rect(14, 3, 18, 4, 'black'); a.line(14, 3, 31, 3, 'black', 3)
    a.rect(32, 2, 3, 6, 'blade'); a.px(35, 2, 'steel', 4); a.px(35, 7, 'steel', 3)                             # 电极
    a.glow(36, 3, 'arc'); a.glow(37, 5, 'arc2'); a.glow(36, 6, 'arc'); a.glow(37, 4, 'arc')
    a.glint(34, 2)
    a.pivot = (5, 5); return a
def W_fists():             # 液压拳套，30（戴在手上）
    a = Art(30, 22)
    a.poly([(0,5),(13,4),(14,18),(0,17)], 'dsteel'); a.line(0, 5, 13, 4, 'steel', 3)                        # 前臂护甲
    for i in range(3): a.rect(2+i*4, 7, 2, 8, 'orange'); a.px(2+i*4, 7, 'orange', 4)                            # 液压缸
    a.rect(14, 2, 10, 18, 'steel'); a.line(14, 2, 23, 2, 'steel', 4)                                          # 拳头
    for j in range(4): a.rect(24, 3+j*4, 5, 3, 'blade'); a.px(28, 3+j*4, 'blade', 4)                          # 指节
    a.rect(16, 9, 5, 3, 'black'); a.glow(17, 10, 'amber'); a.glow(18, 10, 'amber2')
    rivets(a, [(15, 4), (15, 18), (22, 4), (22, 18)], 'gold')
    a.line(14, 2, 23, 2, 'gold', 3); a.glint(28, 3)
    a.pivot = (8, 11); return a
def W_knuckles():          # 指虎，16
    a = Art(16, 11)
    for i in range(4): a.circle(2.2+i*3.8, 3, 2.3, 'gold', ring=1.1)                                         # 指环（中间空）
    a.poly([(0,5),(15,5),(13,10),(2,10)], 'gold'); a.line(0, 5, 15, 5, 'gold', 4); a.line(2, 9, 13, 9, 'gold', 1)
    a.line(4, 7, 11, 7, 'gold', 3)
    a.glint(14, 5); a.glint(2, 1)
    a.pivot = (7, 6); return a

# ---------------------------------------------------------------- 突刺类（挥砍动作里一样拿在手上）
def W_icepick():           # 冰锥，26
    a = Art(26, 9)
    a.rect(0, 3, 10, 4, 'grip'); [a.line(1+i, 3, 1+i, 6, 'grip', 3) for i in range(0, 9, 3)]; a.line(0, 3, 0, 6, 'gold')
    a.rect(10, 2, 2, 6, 'gold'); a.px(10, 2, 'gold', 4)
    a.rect(12, 4, 11, 2, 'blade'); a.line(12, 4, 22, 4, 'blade', 4)
    a.poly([(23,4),(25.5,5),(23,6)], 'blade'); a.glint(25, 5, True)
    a.pivot = (5, 5); return a
def W_bayonet():           # 刺刀，34
    a = Art(34, 11)
    a.rect(0, 4, 11, 4, 'dsteel'); [a.px(1+i, 5, 'black', 1) for i in range(0, 10, 2)]; a.line(0, 4, 10, 4, 'steel', 3)   # 带防滑槽的柄
    a.circle(1, 6, 1.6, 'steel', ring=0.8)                                                                       # 卡笋
    a.rect(11, 1, 2, 9, 'dsteel'); a.circle(12, 2, 1.6, 'gold', ring=0.9)                                        # 护手 + 枪口环
    a.poly([(13,4),(29,4),(33,6),(29,8),(13,8)], 'blade'); a.line(13, 4, 29, 4, 'blade', 4)
    a.line(14, 6, 27, 6, 'blade', 1); a.line(14, 8, 31, 7, 'edge', 2)                                            # 血槽 + 刃
    a.sheen(13, 34, 11, 2); a.glint(33, 6, True)
    a.pivot = (5, 6); return a
def W_rebar():             # 钢筋长矛，96
    a = Art(96, 12)
    a.rect(0, 5, 80, 3, 'dsteel'); a.line(0, 5, 79, 5, 'steel', 3)
    for x in range(1, 80, 3): a.px(x, 6, 'dsteel', 0); a.px(x, 7, 'steel', 2)                                     # 钢筋螺纹
    for x0 in (4, 30): a.rect(x0, 5, 10, 3, 'tape')                                                              # 两处缠胶布
    a.rect(76, 4, 5, 5, 'cable'); [a.line(76+i, 4, 76+i, 8, 'steel', 3) for i in range(0, 5, 2)]                  # 铁丝绑扎
    a.poly([(80,3),(90,3.5),(95,6),(90,8.5),(80,9)], 'blade'); a.line(80, 3, 90, 3.5, 'blade', 4)                 # 磨尖的废钢矛头
    a.line(82, 8, 92, 7, 'edge', 2); [a.px(*p, 'rust', 2) for p in ((83, 5), (84, 6), (87, 7))]
    a.sheen(80, 96, 9, 2); a.glint(95, 6, True)
    a.pivot = (9, 6); return a
def W_fang():              # 獠牙双匕（一只手一把），24
    a = Art(24, 10)
    a.rect(0, 4, 2, 3, 'gold'); a.rect(2, 4, 7, 3, 'wrap'); [a.px(3+i, 5, 'black', 1) for i in range(0, 6, 2)]
    a.rect(9, 2, 2, 7, 'dsteel'); a.px(9, 2, 'gold', 4); a.px(9, 8, 'gold', 2)
    a.poly([(11,3),(17,3),(23,1),(20,5),(15,7),(11,7)], 'blade'); a.line(15, 6, 21, 3.5, 'edge', 3)              # 弯成獠牙的刃
    a.glow(12, 5, 'green'); a.glow(13, 5, 'green2'); a.glint(23, 1, True)
    a.pivot = (5, 5); return a
def W_lance():             # 穿刺磁轨枪矛，104
    a = Art(104, 16)
    a.rect(0, 6, 74, 4, 'dsteel'); a.line(0, 6, 73, 6, 'steel', 3); wrap_grip(a, 3, 6, 16, 4, 'grip', 'cable')
    a.rect(0, 5, 3, 6, 'gold')
    for x in range(34, 74, 8):                                                                                   # 磁轨线圈
        a.rect(x, 4, 4, 8, 'dsteel'); a.line(x, 4, x+3, 4, 'gold', 3); a.glow(x+1, 8, 'amber'); a.glow(x+2, 8, 'amber2')
    a.rect(74, 3, 6, 10, 'dsteel'); a.line(74, 3, 79, 3, 'gold', 3)
    a.poly([(80,4),(96,6.5),(103,8),(96,9.5),(80,12)], 'blade'); a.line(80, 4, 96, 6.5, 'blade', 4); a.line(82, 11, 100, 8.6, 'edge', 2)   # 锥形枪头
    a.line(82, 8, 98, 8, 'blade', 1); a.glow(81, 8, 'amber2')
    a.sheen(80, 104, 10, 2); a.glint(103, 8, True)
    a.pivot = (11, 8); return a

ALL = {
 'Nameless': W_nameless, 'Daybreak Thermal Katana': W_katana, 'Pipe Machete': W_machete, 'Mantis Arm Blades': W_mantis,
 'Monomolecular Wire': W_wire, 'Butterfly Knife': W_butterfly, 'Whirl Chain Blade': W_chain, 'Headsman Chainsaw Sword': W_saw,
 'Crowbar': W_crowbar, 'Ironbreak Power Hammer': W_hammer, 'Road Sign': W_sign, 'Stun Baton': W_baton,
 'Demolisher Hydraulic Fists': W_fists, 'Brass Knuckles': W_knuckles,
 'Ice Pick': W_icepick, 'Bayonet': W_bayonet, 'Rebar Spear': W_rebar, 'Fang Twin Daggers': W_fang, 'Piercer Mag-Lance': W_lance,
}
WEAR = {'Rebar Spear': 1.0, 'Pipe Machete': 1.0, 'Crowbar': 1.0, 'Road Sign': 1.0, 'Headsman Chainsaw Sword': 0.9, 'Ironbreak Power Hammer': 0.9, 'Brass Knuckles': 0.8}
if __name__ == '__main__':
    meta = {}
    for i, (name, fn) in enumerate(ALL.items()):
        a = fn(); img = a.render(i*11+5, WEAR.get(name, 0.6))
        s = slug(name); img.save(f'{OUT}/{s}.png')
        meta[s] = {'name': name, 'w': img.width, 'h': img.height,
                   'pivotX': round((a.pivot[0]+1.5)/img.width, 4), 'pivotY': round(1-(a.pivot[1]+1.5)/img.height, 4)}
    json.dump(meta, open(f'{OUT}/held.json', 'w'), indent=1)
    print(len(meta), 'held weapons')
