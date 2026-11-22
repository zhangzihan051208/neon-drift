"""
NEON DRIFT 武器像素图：45 把武器全部用代码画（侧视，枪口朝右，握把朝下）。
画法：先用"材质"填出各个零件的形状，再统一上光影——零件顶边提亮、底边压暗、外面描一圈近黑的轮廓、加一点磨损噪点。
颜色压暗、去饱和，和人物（深色风衣）和场景一致；只有能量部件、烧红的刀刃这些发光的地方用亮色（不用蓝/青）。
输出：<out>/<slug>.png + weapons.json（每把的握持点 pivot，0–1）
"""
import json, os, re, sys, math, random
from PIL import Image
OUT = sys.argv[1] if len(sys.argv) > 1 else 'out'
os.makedirs(OUT, exist_ok=True)

MAT = {   # 底色
 'steel':(70,72,78),'dsteel':(46,47,53),'poly':(36,36,40),'grip':(27,26,29),'wood':(88,56,36),'dwood':(62,40,28),
 'rust':(104,58,34),'brass':(140,110,56),'copper':(128,72,44),'tape':(78,70,58),'leather':(64,38,30),'chrome':(132,134,140),
 'blade':(120,122,128),'edge':(178,178,184),'black':(20,19,22),'red':(120,34,32),'sign':(150,118,40),'signw':(170,168,160),
 'cloth':(74,30,34),'rope':(96,80,56),'olive':(62,64,46),'bone':(160,150,130),'orange':(150,76,30),'pink':(110,40,70),
}
GLOW = {'hot':(255,120,50),'hot2':(255,200,90),'arc':(255,90,200),'arc2':(255,190,240),'amber':(255,176,72),'red':(255,60,60),'green':(170,230,90),'white':(240,235,230)}

class Art:
    def __init__(s, w, h):
        s.w, s.h = w, h; s.m = [[None]*w for _ in range(h)]; s.g = {}; s.pivot = (0, 0)
    def px(s, x, y, mat):
        x, y = int(round(x)), int(round(y))
        if 0 <= x < s.w and 0 <= y < s.h: s.m[y][x] = mat
    def rect(s, x, y, w, h, mat):
        for j in range(int(h)):
            for i in range(int(w)): s.px(x+i, y+j, mat)
    def poly(s, pts, mat):
        ys = [p[1] for p in pts]
        for y in range(int(min(ys)), int(max(ys))+1):
            xs = []
            for i in range(len(pts)):
                (x0,y0),(x1,y1) = pts[i], pts[(i+1)%len(pts)]
                if (y0 <= y+0.5 < y1) or (y1 <= y+0.5 < y0):
                    xs.append(x0 + (y+0.5-y0)*(x1-x0)/(y1-y0))
            xs.sort()
            for a, b in zip(xs[::2], xs[1::2]):
                for x in range(int(math.ceil(a-0.5)), int(math.floor(b-0.5))+1): s.px(x, y, mat)
    def line(s, x0, y0, x1, y1, mat, wd=1):
        n = int(max(abs(x1-x0), abs(y1-y0))) + 1
        for k in range(n+1):
            t = k/max(1,n); x = x0+(x1-x0)*t; y = y0+(y1-y0)*t
            for i in range(wd):
                for j in range(wd): s.px(x+i-(wd-1)//2, y+j-(wd-1)//2, mat)
    def circle(s, cx, cy, r, mat, ring=None):
        for y in range(int(cy-r-1), int(cy+r+2)):
            for x in range(int(cx-r-1), int(cx+r+2)):
                d = math.hypot(x-cx, y-cy)
                if d <= r and (ring is None or d >= r-ring): s.px(x, y, mat)
    def glow(s, x, y, col, w=1, h=1):
        for j in range(h):
            for i in range(w): s.g[(int(x)+i, int(y)+j)] = GLOW[col]
    def render(s, seed, wear=0.5):
        rnd = random.Random(seed)
        W, H = s.w+2, s.h+2
        img = Image.new('RGBA', (W, H), (0,0,0,0)); P = img.load()
        def at(x, y): return s.m[y][x] if 0 <= x < s.w and 0 <= y < s.h else None
        for y in range(s.h):
            for x in range(s.w):
                mt = at(x, y)
                if mt is None: continue
                r, g, b = MAT[mt]
                up, dn = at(x, y-1), at(x, y+1)
                k = 0
                if up != mt: k += 26 if up is None else 14       # 零件顶边受光
                if dn != mt: k -= 22 if dn is None else 12       # 底边背光
                if mt in ('edge','chrome'): k += 10
                n = rnd.randint(-5, 5)
                if mt in ('steel','dsteel','blade') and rnd.random() < 0.05*wear: r, g, b = MAT['rust']; n -= 6   # 锈点
                if mt in ('steel','poly','dsteel') and rnd.random() < 0.04*wear: k += 18                          # 磨亮的划痕
                P[x+1, y+1] = (max(0,min(255,r+k+n)), max(0,min(255,g+k+n)), max(0,min(255,b+k+n)), 255)
        for (x, y), c in s.g.items():
            if 0 <= x < s.w and 0 <= y < s.h: P[x+1, y+1] = c+(255,)
        # 外轮廓：近黑，一圈
        out = img.copy(); O = out.load()
        for y in range(H):
            for x in range(W):
                if P[x, y][3]: continue
                if any(0 <= x+dx < W and 0 <= y+dy < H and P[x+dx, y+dy][3] for dx, dy in ((1,0),(-1,0),(0,1),(0,-1))):
                    O[x, y] = (12, 11, 14, 255)
        return out

def slug(n): return re.sub(r'[^a-z0-9]+', '_', n.lower()).strip('_')

# ======================================================================== 枪的零件
def grip(a, x, y, h=6, mat='grip', slant=2):
    a.poly([(x,y),(x+4,y),(x+4-slant,y+h),(x-slant,y+h)], mat)
    a.line(x+3, y+1, x+3-slant*0.8, y+h-1, 'poly' if mat != 'poly' else 'dsteel')                            # 握把前沿一道分色
def trigger(a, x, y):
    a.rect(x, y, 4, 2, 'dsteel'); a.px(x+1, y, 'steel')                                                       # 扳机护圈（实心小块）+ 扳机
def mag(a, x, y, h, mat='dsteel', w=4, curve=0):
    a.poly([(x,y),(x+w,y),(x+w+curve,y+h),(x+curve,y+h)], mat)
    a.line(x+curve, y+h-1, x+w+curve-1, y+h-1, 'steel')

def W_alleyway():
    a = Art(22, 13)
    a.rect(3, 2, 17, 4, 'dsteel'); a.rect(3, 2, 17, 1, 'steel'); a.rect(19, 3, 2, 2, 'steel')          # 套筒
    for i in range(4): a.px(5+i*2, 3, 'black')                                                          # 防滑槽
    a.rect(4, 6, 12, 2, 'poly'); grip(a, 4, 7, 6); trigger(a, 9, 7); a.px(18, 1, 'steel'); a.px(4, 1, 'steel')
    a.pivot = (6, 10); return a
def W_harmonic():
    a = Art(24, 13)
    a.rect(3, 2, 19, 4, 'poly'); a.rect(3, 2, 19, 1, 'dsteel'); a.rect(21, 3, 2, 2, 'steel')
    a.rect(10, 1, 6, 1, 'dsteel'); a.glow(11, 1, 'arc', 3); a.glow(14, 4, 'arc')                       # 智能瞄准模块
    a.rect(4, 6, 13, 2, 'poly'); grip(a, 4, 7, 6); trigger(a, 9, 7); a.line(16, 6, 19, 6, 'dsteel')
    a.pivot = (6, 10); return a
def W_verdict():
    a = Art(28, 14)
    a.rect(13, 2, 14, 3, 'steel'); a.rect(13, 2, 14, 1, 'chrome'); a.px(26, 1, 'steel')                 # 长枪管
    a.rect(7, 2, 7, 6, 'dsteel'); a.rect(8, 3, 5, 4, 'steel'); a.line(8, 5, 12, 5, 'dsteel')              # 转轮
    a.poly([(3,3),(8,2),(8,8),(5,8)], 'dsteel'); a.px(3, 2, 'steel')                                      # 击锤
    a.poly([(3,7),(8,7),(6,13),(1,13)], 'wood'); a.line(3, 9, 2, 12, 'dwood'); trigger(a, 9, 8)
    a.pivot = (5, 10); return a
def W_veteran():
    a = Art(44, 15)
    a.poly([(0,4),(9,3),(9,8),(2,9)], 'olive'); a.line(1, 8, 8, 7, 'poly')                               # 枪托
    a.rect(9, 3, 16, 5, 'dsteel'); a.rect(9, 3, 16, 1, 'steel'); a.rect(14, 1, 6, 2, 'poly')             # 机匣 + 提把
    a.rect(25, 4, 10, 3, 'olive'); for_ = [a.px(26+i*2, 5, 'black') for i in range(4)]                   # 护木
    a.rect(35, 4, 7, 1, 'steel'); a.rect(41, 3, 2, 3, 'dsteel'); a.px(33, 2, 'dsteel'); a.px(33, 3, 'dsteel')
    mag(a, 20, 8, 6, curve=1); grip(a, 12, 8, 5, 'poly'); trigger(a, 16, 8)
    a.pivot = (14, 11); return a
def W_torrent():
    a = Art(30, 15)
    a.line(0, 4, 0, 7, 'dsteel'); a.line(0, 4, 6, 4, 'dsteel'); a.line(0, 7, 6, 6, 'dsteel')             # 折叠托
    a.rect(6, 3, 14, 4, 'poly'); a.rect(6, 3, 14, 1, 'dsteel'); a.rect(20, 4, 6, 2, 'dsteel'); a.rect(26, 3, 3, 4, 'black')   # 消音器
    mag(a, 15, 7, 7, 'dsteel', 3); grip(a, 8, 7, 5, 'poly'); trigger(a, 12, 7); a.glow(9, 4, 'amber')
    a.pivot = (10, 10); return a
def W_hornet():
    a = Art(32, 15)
    a.poly([(0,4),(6,3),(6,7),(1,8)], 'poly')
    a.rect(6, 3, 16, 4, 'orange'); a.rect(6, 3, 16, 1, 'sign'); a.rect(6, 6, 16, 1, 'poly')              # 黄黑涂装的机匣
    for i in range(3): a.line(8+i*4, 3, 10+i*4, 6, 'black')
    a.rect(22, 4, 8, 2, 'dsteel'); a.rect(12, 1, 7, 2, 'poly'); a.glow(13, 1, 'arc', 2); a.glow(17, 1, 'red')   # 追踪模块
    mag(a, 16, 7, 6, 'poly', 3); grip(a, 8, 7, 5, 'poly'); trigger(a, 12, 7)
    a.pivot = (10, 10); return a
def W_neonhime():
    a = Art(44, 16)
    a.poly([(0,5),(9,4),(9,9),(2,10)], 'pink'); a.line(1, 9, 8, 8, 'poly')
    a.rect(9, 4, 18, 5, 'poly'); a.rect(9, 4, 18, 1, 'pink'); a.rect(13, 2, 8, 2, 'dsteel')
    for i in range(4): a.px(14+i*2, 2, 'black')
    a.rect(27, 5, 9, 3, 'dsteel')
    for i in range(4): a.rect(28+i*2, 4, 1, 5, 'copper')                                                   # 线圈
    a.rect(36, 5, 6, 3, 'poly'); a.rect(41, 4, 2, 5, 'copper'); a.glow(42, 6, 'arc2')
    a.glow(29, 6, 'arc'); a.glow(31, 6, 'arc'); a.glow(33, 6, 'arc'); a.glow(15, 6, 'arc', 6)
    a.rect(20, 9, 5, 5, 'pink'); a.glow(21, 11, 'arc', 3); grip(a, 12, 9, 5, 'poly'); trigger(a, 16, 9)    # 电池弹匣
    a.pivot = (14, 12); return a
def W_skull():
    a = Art(44, 14)
    a.poly([(0,4),(10,3),(10,8),(2,10)], 'wood'); a.line(2, 9, 9, 7, 'dwood'); a.rect(0, 4, 1, 5, 'grip')
    a.rect(10, 3, 9, 5, 'dsteel'); a.rect(10, 3, 9, 1, 'steel')
    a.rect(19, 3, 23, 2, 'steel'); a.rect(19, 5, 20, 2, 'dsteel'); a.px(41, 2, 'steel')                  # 枪管 + 弹仓管
    a.rect(23, 5, 9, 3, 'wood'); [a.px(24+i*2, 6, 'dwood') for i in range(4)]                             # 泵动护木
    grip(a, 11, 8, 4, 'wood', 3); trigger(a, 15, 8)
    a.pivot = (13, 10); return a
def W_sawnoff():
    a = Art(30, 13)
    a.poly([(0,6),(5,4),(8,4),(8,8),(3,12),(0,11)], 'wood'); a.line(2, 10, 6, 6, 'dwood')                # 锯短的手枪握把式托
    a.rect(8, 3, 6, 5, 'dsteel'); a.rect(8, 3, 6, 1, 'steel')
    a.rect(14, 3, 14, 2, 'steel'); a.rect(14, 5, 14, 2, 'steel'); a.line(14, 5, 27, 5, 'dsteel')          # 双管
    a.rect(27, 3, 1, 4, 'black'); a.rect(15, 7, 6, 2, 'wood'); trigger(a, 10, 8); a.px(12, 2, 'steel')
    a.pivot = (5, 9); return a
def W_dragon():
    a = Art(44, 15)
    a.poly([(0,4),(10,3),(10,8),(2,10)], 'dwood'); a.rect(0, 4, 1, 5, 'grip')
    a.rect(10, 3, 9, 5, 'black'); a.rect(10, 3, 9, 1, 'dsteel'); a.glow(12, 5, 'hot')
    a.rect(19, 3, 21, 2, 'dsteel'); a.rect(19, 5, 18, 2, 'black'); a.rect(40, 2, 3, 4, 'copper')          # 龙口枪口
    a.px(43, 3, 'copper'); a.glow(41, 3, 'hot'); a.glow(41, 4, 'hot2')
    for i in range(5): a.px(22+i*3, 2, 'copper')                                                           # 散热片
    a.rect(23, 5, 9, 3, 'leather'); a.line(23, 6, 31, 6, 'rope')
    a.rect(4, 9, 6, 2, 'red'); a.glow(5, 9, 'hot', 4)                                                      # 托上的燃烧弹
    grip(a, 11, 8, 4, 'dwood', 3); trigger(a, 15, 8)
    a.pivot = (13, 10); return a
def W_spike():
    a = Art(54, 14)
    a.poly([(0,4),(14,4),(16,7),(14,9),(3,10),(0,9)], 'wood'); a.line(3, 8, 12, 7, 'dwood')               # 木托
    a.rect(16, 4, 10, 4, 'dsteel'); a.rect(16, 4, 10, 1, 'steel')
    a.rect(26, 5, 26, 2, 'steel'); a.rect(51, 4, 2, 4, 'dsteel')
    a.rect(16, 1, 12, 2, 'black'); a.rect(16, 1, 1, 3, 'dsteel'); a.rect(27, 1, 1, 3, 'dsteel'); a.px(17, 1, 'chrome')   # 瞄准镜
    a.line(22, 8, 24, 9, 'chrome'); a.px(24, 9, 'chrome')                                                  # 拉栓
    a.rect(26, 7, 14, 2, 'wood'); trigger(a, 18, 8); a.poly([(14,8),(17,8),(16,12),(13,12)], 'wood')
    a.pivot = (15, 10); return a
def W_ghost():
    a = Art(56, 16)
    a.poly([(0,5),(12,4),(12,10),(5,11),(0,10)], 'poly'); a.rect(2, 7, 6, 2, 'black')
    a.rect(12, 4, 16, 6, 'signw'); a.rect(12, 4, 16, 1, 'bone'); a.rect(12, 9, 16, 1, 'dsteel')           # 白色机匣（独角兽）
    a.rect(28, 5, 22, 3, 'dsteel'); a.rect(28, 5, 22, 1, 'steel'); a.rect(49, 3, 2, 7, 'dsteel'); a.rect(51, 6, 4, 1, 'chrome')   # 电磁轨 + 长角
    for i in range(5): a.glow(30+i*4, 7, 'arc')
    a.rect(15, 1, 12, 3, 'black'); a.glow(26, 2, 'arc2')
    a.rect(20, 10, 5, 4, 'poly'); a.glow(21, 11, 'arc', 3); grip(a, 13, 10, 5, 'poly'); trigger(a, 18, 10)
    a.pivot = (15, 13); return a
def W_wrath():
    a = Art(60, 18)
    a.poly([(0,5),(12,4),(12,12),(4,13),(0,11)], 'black'); a.rect(2, 7, 7, 3, 'dsteel')
    a.rect(12, 3, 18, 9, 'dsteel'); a.rect(12, 3, 18, 1, 'steel'); a.rect(14, 5, 14, 2, 'black'); a.glow(15, 5, 'red', 12)   # 能量槽
    a.rect(30, 4, 26, 2, 'steel'); a.rect(30, 9, 26, 2, 'steel'); a.rect(30, 6, 26, 3, 'black')            # 上下两条导轨
    for i in range(6): a.glow(32+i*4, 7, 'hot2'); a.glow(33+i*4, 7, 'red')
    a.rect(55, 3, 3, 9, 'dsteel'); a.rect(18, 0, 8, 3, 'black'); a.glow(24, 1, 'red')
    a.rect(22, 12, 6, 5, 'dsteel'); grip(a, 14, 12, 5, 'poly'); trigger(a, 19, 12)
    a.pivot = (16, 15); return a
def W_meat():
    a = Art(52, 18)
    a.poly([(0,5),(10,4),(10,10),(1,11)], 'olive')
    a.rect(10, 4, 18, 7, 'dsteel'); a.rect(10, 4, 18, 1, 'steel'); a.rect(13, 2, 10, 2, 'olive')
    a.rect(28, 5, 18, 3, 'steel'); a.rect(30, 4, 14, 1, 'dsteel'); [a.px(31+i*2, 6, 'black') for i in range(7)]   # 带散热孔的枪管罩
    a.rect(46, 4, 4, 5, 'black'); a.rect(34, 8, 8, 1, 'dsteel')                                             # 收起来的两脚架
    a.rect(18, 11, 8, 6, 'olive'); a.rect(18, 11, 8, 1, 'tape')                                             # 弹药箱
    for i in range(4): a.px(26+i, 10+i % 2, 'brass')                                                        # 弹链
    grip(a, 12, 11, 5, 'poly'); trigger(a, 16, 11)
    a.pivot = (14, 14); return a
def W_downpour():
    a = Art(50, 20)
    a.rect(4, 4, 16, 10, 'dsteel'); a.rect(4, 4, 16, 1, 'steel'); a.rect(6, 6, 6, 4, 'black'); a.glow(7, 7, 'amber', 2)
    for i in range(5): a.rect(20, 5+i*2, 26, 1, 'steel' if i % 2 == 0 else 'dsteel')                      # 转管
    a.rect(20, 4, 2, 11, 'dsteel'); a.rect(32, 4, 2, 11, 'dsteel'); a.rect(45, 4, 3, 11, 'dsteel')
    a.rect(8, 0, 10, 4, 'poly'); a.line(9, 0, 16, 0, 'dsteel')                                               # 提把
    a.rect(6, 14, 9, 5, 'olive'); a.rect(6, 14, 9, 1, 'tape'); grip(a, 15, 14, 5, 'poly')
    a.pivot = (17, 16); return a
def W_launcher():
    a = Art(44, 18)
    a.poly([(0,6),(8,5),(8,11),(1,12)], 'poly')
    a.rect(8, 5, 8, 7, 'dsteel'); a.circle(20, 8, 6, 'dsteel'); a.circle(20, 8, 6, 'steel', 1)           # 转轮弹巢
    for k in range(6):
        ang = k*math.pi/3; a.circle(20+math.cos(ang)*3.4, 8+math.sin(ang)*3.4, 1, 'black')
    a.rect(26, 5, 15, 6, 'olive'); a.rect(26, 5, 15, 1, 'tape'); a.rect(40, 4, 3, 8, 'dsteel'); a.rect(41, 6, 2, 4, 'black')
    a.rect(26, 11, 6, 2, 'poly'); a.rect(12, 2, 4, 3, 'dsteel'); a.glow(13, 3, 'amber')
    a.rect(30, 8, 6, 1, 'orange')                                                                           # 烟花管上的警示条
    grip(a, 9, 12, 5, 'poly'); trigger(a, 13, 12)
    a.pivot = (11, 15); return a
def W_swarm():
    a = Art(42, 18)
    a.rect(6, 3, 30, 11, 'olive'); a.rect(6, 3, 30, 1, 'tape'); a.rect(36, 3, 3, 11, 'dsteel')
    for r in range(2):
        for c in range(3): a.circle(37, 6+r*0, 0, 'black')
    for i in range(3):
        a.rect(37, 4+i*3, 2, 2, 'black'); a.glow(38, 4+i*3, 'red')                                            # 三个发射口
    a.rect(10, 6, 20, 1, 'dsteel'); a.rect(10, 10, 20, 1, 'dsteel'); a.rect(14, 1, 8, 2, 'black'); a.glow(20, 1, 'red')
    a.rect(0, 5, 6, 6, 'poly'); grip(a, 14, 14, 4, 'poly'); trigger(a, 18, 14)
    a.pivot = (16, 16); return a
def W_nailgun():
    a = Art(30, 16)
    a.rect(3, 3, 18, 7, 'orange'); a.rect(3, 3, 18, 1, 'sign'); a.rect(3, 9, 18, 1, 'poly')               # 工地黄的电动工具
    a.rect(21, 4, 6, 4, 'dsteel'); a.rect(27, 5, 2, 2, 'steel')
    a.circle(14, 11, 3, 'dsteel'); a.circle(14, 11, 3, 'steel', 1); a.px(14, 11, 'black')                  # 钉盘
    a.glow(6, 5, 'arc'); a.glow(8, 5, 'arc'); grip(a, 6, 10, 5, 'poly'); trigger(a, 10, 10)
    a.pivot = (8, 13); return a
def W_furnace():
    a = Art(44, 20)
    a.circle(8, 8, 6, 'red'); a.rect(2, 2, 12, 2, 'dsteel'); a.circle(8, 8, 6, 'dsteel', 1)              # 燃料罐
    a.rect(6, 4, 4, 1, 'sign'); a.rect(14, 6, 16, 5, 'dsteel'); a.rect(14, 6, 16, 1, 'steel')
    a.rect(30, 7, 9, 3, 'steel'); a.rect(39, 6, 3, 5, 'copper'); a.glow(41, 8, 'hot'); a.glow(40, 7, 'hot2')
    a.line(5, 14, 16, 12, 'grip', 2); a.rect(32, 10, 2, 2, 'copper'); a.glow(33, 11, 'hot')                 # 软管 + 引燃火
    grip(a, 18, 11, 5, 'poly'); trigger(a, 22, 11); a.rect(26, 11, 4, 3, 'poly')
    a.pivot = (20, 14); return a
def W_tether():
    a = Art(50, 20)
    a.poly([(0,6),(10,5),(10,13),(2,14)], 'poly')
    a.rect(10, 5, 14, 9, 'dsteel'); a.rect(10, 5, 14, 1, 'steel')
    a.circle(17, 9, 3, 'black'); a.glow(17, 9, 'arc2'); a.glow(16, 9, 'arc'); a.glow(18, 9, 'arc'); a.glow(17, 8, 'arc'); a.glow(17, 10, 'arc')
    for i in range(5): a.rect(24+i*4, 4, 2, 11, 'copper'); a.rect(26+i*4, 7, 2, 5, 'dsteel')             # 线圈节
    a.rect(44, 6, 4, 7, 'dsteel'); a.px(48, 7, 'copper'); a.px(48, 11, 'copper'); a.glow(47, 9, 'arc2')
    for i in range(5): a.glow(25+i*4, 9, 'arc')
    a.line(3, 15, 12, 14, 'black', 2); grip(a, 13, 14, 5, 'poly'); trigger(a, 17, 14)                     # 背后的电缆
    a.pivot = (15, 17); return a

# ======================================================================== 近战
def hilt(a, x, y, length, mat='leather', guard='dsteel', gh=5):
    a.rect(x, y, length, 3, mat)
    for i in range(0, length, 2): a.px(x+i, y+1, 'black')                                                    # 缠绳
    a.rect(x+length, y-(gh-3)//2, 2, gh, guard)
def W_nameless():
    # 直刃黑刀：刀身完全水平，刀尖斜切（切先），刃口一道冷光
    a = Art(54, 9)
    a.rect(0, 3, 2, 3, 'dsteel')                                                                              # 柄头
    a.rect(2, 3, 11, 3, 'black'); [a.px(3+i, 4, 'red') for i in range(0, 10, 3)]; [a.px(4+i, 3, 'dsteel') for i in range(0, 9, 3)]   # 黑柄缠暗红
    a.rect(13, 1, 2, 7, 'dsteel'); a.px(13, 1, 'steel'); a.px(13, 7, 'steel')                                  # 方形刀镡
    a.rect(15, 3, 2, 3, 'brass')                                                                               # 鎺
    a.rect(17, 3, 31, 3, 'black'); a.line(17, 3, 47, 3, 'dsteel')                                              # 刀身（水平）
    a.poly([(48,3),(53,5.5),(48,6)], 'black'); a.line(48, 3, 52, 5, 'dsteel')                                  # 切先
    a.line(17, 5, 47, 5, 'edge'); a.px(48, 5, 'edge'); a.px(51, 5, 'edge')                                     # 刃口
    a.pivot = (7, 4); return a
def W_machete():
    a = Art(36, 11)
    a.rect(0, 4, 9, 3, 'steel'); a.rect(0, 4, 9, 1, 'chrome'); [a.px(1+i, 5, 'tape') for i in range(0, 8)]; a.px(0, 5, 'dsteel')   # 钢管柄缠胶布
    a.rect(9, 3, 2, 5, 'dsteel')
    a.poly([(11,3),(30,3),(35,5),(33,8),(11,8)], 'blade')                                                     # 宽刃，刀尖斜切
    a.line(11, 3, 30, 3, 'steel'); a.line(12, 8, 32, 8, 'edge'); a.line(32, 7, 34, 5, 'edge')
    for p in ((15,5),(16,5),(22,4),(26,6),(27,6),(19,7)): a.px(*p, 'rust')
    a.pivot = (4, 5); return a
def W_katana():
    a = Art(52, 9)
    a.rect(0, 3, 2, 3, 'brass'); hilt(a, 2, 3, 10, 'leather', 'brass', 5)
    a.rect(14, 3, 32, 3, 'dsteel'); a.line(14, 3, 45, 3, 'steel')
    a.poly([(46,3),(51,5.5),(46,6)], 'dsteel')
    for x in range(15, 47): a.glow(x, 5, 'hot' if x % 5 else 'hot2')                                           # 烧红的刃口
    a.glow(47, 5, 'hot'); a.glow(49, 5, 'hot2'); a.glow(15, 4, 'hot'); a.glow(16, 4, 'hot')
    a.pivot = (6, 4); return a
def W_mantis():
    a = Art(30, 14)
    a.rect(0, 5, 10, 5, 'dsteel'); a.rect(0, 5, 10, 1, 'steel'); a.rect(2, 7, 6, 1, 'black'); a.glow(3, 7, 'red')   # 前臂套
    a.poly([(10,4),(16,1),(29,0),(18,4),(12,7)], 'blade'); a.line(16, 1, 29, 0, 'edge')
    a.poly([(10,8),(18,10),(28,13),(16,12),(10,10)], 'blade'); a.line(18, 12, 28, 13, 'edge')
    a.pivot = (4, 7); return a
def W_wire():
    a = Art(40, 14)
    a.rect(0, 4, 7, 6, 'dsteel'); a.rect(0, 4, 7, 1, 'steel'); a.circle(3, 7, 2, 'black'); a.glow(3, 7, 'arc')   # 指套发射器
    for x in range(7, 39):
        y = 7 + math.sin(x*0.25)*3*(x-7)/32
        a.glow(x, y, 'arc2' if x % 3 == 0 else 'arc')                                                        # 单分子线（只有发光的细线）
    a.pivot = (3, 7); return a
def W_crowbar():
    a = Art(38, 12)
    a.rect(4, 5, 28, 3, 'red'); a.rect(4, 5, 28, 1, 'orange'); a.rect(6, 5, 7, 3, 'tape')                     # 直杆（红漆），握的地方缠胶布
    a.poly([(32,5),(35,3),(37,4),(37,6),(35,8),(32,8)], 'red'); a.px(37, 3, 'dsteel'); a.px(36, 2, 'dsteel')   # 前端弯钩
    a.poly([(4,5),(1,8),(0,11),(2,11),(4,8)], 'red'); a.px(0, 11, 'dsteel'); a.px(2, 11, 'dsteel')             # 尾端翘起的撬爪
    for p in ((15,6),(16,6),(23,7),(28,6)): a.px(*p, 'dsteel')                                                 # 掉漆
    a.pivot = (9, 6); return a
def W_hammer():
    a = Art(46, 18)
    a.rect(0, 8, 32, 3, 'dsteel'); a.rect(0, 8, 32, 1, 'steel'); a.rect(2, 8, 8, 3, 'grip')
    a.rect(32, 2, 12, 15, 'dsteel'); a.rect(32, 2, 12, 1, 'steel'); a.rect(33, 4, 10, 2, 'orange')           # 锤头
    a.rect(34, 8, 8, 4, 'black'); a.glow(35, 9, 'amber', 6); a.glow(35, 10, 'hot', 6)                        # 动力舱
    a.rect(44, 4, 2, 11, 'steel'); a.rect(30, 6, 2, 7, 'dsteel')
    a.pivot = (5, 9); return a
def W_fists():
    a = Art(26, 16)
    a.rect(0, 3, 12, 10, 'dsteel'); a.rect(0, 3, 12, 1, 'steel'); a.rect(2, 6, 8, 1, 'black')
    for i in range(3): a.rect(2+i*3, 5, 2, 6, 'orange')                                                       # 液压缸
    a.rect(12, 2, 10, 12, 'steel'); a.rect(12, 2, 10, 1, 'chrome')
    for j in range(4): a.rect(22, 3+j*3, 3, 2, 'dsteel')                                                      # 指节
    a.glow(14, 7, 'amber', 2)
    a.pivot = (5, 8); return a
def W_butterfly():
    a = Art(20, 8)
    a.rect(0, 3, 9, 3, 'dsteel'); a.rect(0, 3, 9, 1, 'chrome'); for_ = [a.px(1+i*2, 4, 'black') for i in range(4)]
    a.poly([(9,3),(16,2),(19,3),(16,5),(9,5)], 'blade'); a.line(10, 5, 16, 5, 'edge')
    a.pivot = (4, 4); return a
def W_chain():
    a = Art(50, 14)
    hilt(a, 0, 5, 8, 'grip', 'dsteel', 6)
    pts = [(10 + k*4, 6 + int(round(math.sin(k*0.8)*3))) for k in range(10)]
    for (x0,y0),(x1,y1) in zip(pts, pts[1:]): a.line(x0, y0+1, x1, y1+1, 'dsteel')                            # 链条
    for k,(x,y) in enumerate(pts[1:]):
        a.poly([(x-1,y),(x+2,y-1),(x+3,y+1),(x,y+2)], 'blade'); a.px(x+2, y-1, 'edge')                         # 一节节刀片
    a.pivot = (4, 6); return a
def W_saw():
    a = Art(56, 14)
    hilt(a, 0, 5, 10, 'grip', 'dsteel', 3); a.rect(12, 3, 9, 8, 'orange'); a.rect(12, 3, 9, 1, 'sign'); a.rect(14, 5, 4, 3, 'black')   # 发动机
    a.rect(21, 4, 31, 6, 'dsteel'); a.rect(21, 4, 31, 1, 'steel'); a.circle(51, 7, 3, 'dsteel')
    for x in range(21, 53, 2): a.px(x, 3, 'chrome'); a.px(x+1, 10, 'chrome')                                 # 链齿
    a.glow(15, 6, 'amber')
    a.pivot = (5, 6); return a
def W_knuckles():
    a = Art(22, 13)
    for i in range(4): a.circle(3+i*5, 4, 3.2, 'brass', 1.3)                                                    # 四个指环（中间是空的）
    a.poly([(1,7),(21,7),(18,12),(4,12)], 'brass'); a.line(3, 11, 18, 11, 'copper'); a.line(1, 7, 20, 7, 'bone')   # 握在掌心的弧形底座
    a.pivot = (9, 8); return a
def W_baton():
    a = Art(34, 9)
    a.rect(0, 3, 10, 3, 'grip'); for_ = [a.px(1+i*2, 4, 'black') for i in range(5)]; a.rect(10, 2, 2, 5, 'dsteel')
    a.rect(12, 3, 18, 3, 'poly'); a.rect(12, 3, 18, 1, 'dsteel'); a.rect(30, 2, 3, 5, 'steel')
    a.glow(31, 3, 'arc'); a.glow(32, 5, 'arc2'); a.glow(33, 4, 'arc'); a.glow(5, 3, 'amber')
    a.pivot = (4, 4); return a
def W_sign():
    a = Art(42, 22)
    a.rect(0, 10, 26, 2, 'steel'); a.rect(0, 11, 26, 1, 'dsteel'); a.rect(1, 10, 5, 2, 'tape')
    a.poly([(28,1),(38,1),(41,4),(41,17),(38,20),(28,20),(25,17),(25,4)], 'red')                                # 八角停车牌
    a.poly([(29,3),(37,3),(39,5),(39,16),(37,18),(29,18),(27,16),(27,5)], 'red'); a.rect(28, 9, 11, 3, 'signw')
    a.px(31, 6, 'dsteel'); a.px(35, 14, 'dsteel'); a.rect(26, 9, 2, 4, 'dsteel')                               # 弹孔
    a.pivot = (3, 11); return a
def W_icepick():
    a = Art(24, 8)
    a.rect(0, 3, 8, 3, 'wood'); a.rect(0, 3, 8, 1, 'dwood'); a.rect(8, 2, 1, 5, 'steel')
    a.line(9, 4, 23, 4, 'chrome'); a.line(9, 5, 20, 5, 'dsteel')
    a.pivot = (3, 4); return a
def W_bayonet():
    a = Art(28, 9)
    a.rect(0, 3, 8, 3, 'olive'); a.rect(8, 2, 2, 5, 'dsteel'); a.rect(10, 3, 2, 2, 'steel')
    a.poly([(10,3),(24,3),(27,4),(24,5),(10,5)], 'blade'); a.line(11, 3, 24, 3, 'edge'); a.line(12, 4, 22, 4, 'dsteel')   # 血槽
    a.pivot = (4, 4); return a
def W_rebar():
    a = Art(62, 8)
    for x in range(0, 58):
        a.px(x, 3, 'rust'); a.px(x, 4, 'rust' if x % 3 else 'copper')
        if x % 4 == 0: a.px(x, 2, 'rust'); a.px(x+1, 5, 'rust')                                               # 钢筋的螺纹
    a.poly([(56,2),(61,3.5),(56,5)], 'steel'); a.rect(8, 2, 8, 4, 'tape')
    a.pivot = (12, 4); return a
def W_fang():
    a = Art(22, 14)
    for oy in (0, 7):
        a.rect(0, 2+oy, 7, 3, 'grip'); a.rect(7, 1+oy, 1, 5, 'brass')
        a.poly([(8,2+oy),(18,3+oy),(21,3.5+oy),(18,4+oy),(8,5+oy)], 'blade'); a.line(9, 3+oy, 19, 3+oy, 'edge')
        a.glow(3, 3+oy, 'green')                                                                                # 毒囊
    a.pivot = (3, 7); return a
def W_lance():
    a = Art(64, 12)
    a.rect(0, 5, 30, 2, 'dsteel'); a.rect(0, 5, 30, 1, 'steel'); a.rect(6, 4, 8, 4, 'grip')
    a.rect(30, 3, 10, 6, 'dsteel'); a.rect(30, 3, 10, 1, 'steel')
    for i in range(3): a.rect(31+i*3, 2, 1, 8, 'copper')
    a.poly([(40,3),(56,5),(63,6),(56,7),(40,9)], 'steel'); a.line(41, 6, 60, 6, 'black')
    for x in range(42, 60, 2): a.glow(x, 6, 'arc')
    a.glow(32, 5, 'arc2'); a.glow(35, 6, 'arc2')
    a.pivot = (10, 6); return a

# ======================================================================== 投掷
def W_shuriken():
    a = Art(13, 13)
    for k in range(4):
        ang = k*math.pi/2 + 0.3
        tip = (6+math.cos(ang)*6, 6+math.sin(ang)*6); l = (6+math.cos(ang+1.2)*2, 6+math.sin(ang+1.2)*2); r = (6+math.cos(ang-1.2)*2, 6+math.sin(ang-1.2)*2)
        a.poly([tip, l, (6,6), r], 'blade')
    a.circle(6, 6, 1.2, 'dsteel'); a.px(6, 6, 'black')
    a.pivot = (6, 6); return a
def W_willow():
    a = Art(18, 12)
    for oy, ox in ((1,0),(5,2),(9,1)):
        a.rect(ox, oy, 5, 2, 'cloth'); a.poly([(ox+5,oy),(ox+13,oy+0.5),(ox+16,oy+1),(ox+13,oy+2),(ox+5,oy+2)], 'blade'); a.line(ox+6, oy, ox+13, oy, 'edge')
    a.pivot = (3, 6); return a
def W_caltrops():
    # 铁蒺藜：四根尖刺从中心往四面支开，怎么落地都有一根朝上
    a = Art(22, 12)
    for cx, cy in ((6,7),(15,6)):
        for dx, dy in ((-4,3),(4,3),(0,-5),(1,2)):
            a.line(cx, cy, cx+dx, cy+dy, 'steel' if dy < 0 else 'dsteel')
        a.rect(cx-1, cy-1, 2, 2, 'dsteel')
        a.px(cx, cy-5, 'chrome'); a.px(cx-4, cy+3, 'chrome'); a.px(cx+4, cy+3, 'chrome')
    a.pivot = (11, 7); return a
def W_javelin():
    a = Art(60, 7)
    a.rect(0, 3, 48, 1, 'dsteel'); a.rect(0, 2, 48, 1, 'steel'); a.rect(18, 2, 8, 2, 'tape')
    a.poly([(48,1),(56,2.5),(59,3),(56,3.5),(48,5)], 'blade'); a.line(49, 2, 56, 2, 'edge')
    a.poly([(0,0),(5,2),(0,2)], 'cloth'); a.poly([(0,4),(5,3),(0,6)], 'cloth')                                 # 尾翼
    a.pivot = (22, 3); return a
def W_chakram():
    a = Art(17, 17)
    a.circle(8, 8, 8, 'blade', 3); a.circle(8, 8, 8, 'edge', 1); a.rect(3, 7, 11, 3, 'leather')
    a.circle(8, 8, 5, 'dsteel', 1)
    a.pivot = (8, 8); return a
def W_senbon():
    a = Art(20, 12)
    for k in range(7):
        y = 1 + k*1.5; a.line(0+(k % 2), y, 17+(k % 3), y-1+(k % 2), 'chrome'); a.px(0+(k % 2), y, 'brass')
    a.rect(6, 1, 3, 10, 'cloth')                                                                                 # 扎起来的布条
    a.pivot = (7, 6); return a

ALL = {
 'Nameless': W_nameless, 'Alleyway Pistol': W_alleyway, 'Harmonic Smart Pistol': W_harmonic, 'Verdict Revolver': W_verdict,
 'Veteran Assault Rifle': W_veteran, 'Torrent SMG': W_torrent, 'Hornet Seeker SMG': W_hornet, 'Neon Hime Arc Rifle': W_neonhime,
 'Skullsplitter Shotgun': W_skull, 'Sawn-Off Double Barrel': W_sawnoff, "Dragon's Breath Shotgun": W_dragon,
 'Spike Bolt-Action': W_spike, 'Ghost Unicorn Railsniper': W_ghost, 'Wrath Railgun': W_wrath,
 'Meatgrinder HMG': W_meat, 'Downpour Minigun': W_downpour,
 'Pipe Machete': W_machete, 'Daybreak Thermal Katana': W_katana, 'Mantis Arm Blades': W_mantis, 'Monomolecular Wire': W_wire,
 'Crowbar': W_crowbar, 'Ironbreak Power Hammer': W_hammer, 'Demolisher Hydraulic Fists': W_fists, 'Butterfly Knife': W_butterfly,
 'Whirl Chain Blade': W_chain, 'Headsman Chainsaw Sword': W_saw, 'Brass Knuckles': W_knuckles, 'Stun Baton': W_baton, 'Road Sign': W_sign,
 'Ice Pick': W_icepick, 'Bayonet': W_bayonet, 'Rebar Spear': W_rebar, 'Fang Twin Daggers': W_fang, 'Piercer Mag-Lance': W_lance,
 'Firework Grenade Launcher': W_launcher, 'Swarm Micro-Missiles': W_swarm,
 'Iron Star Shuriken': W_shuriken, 'Willow Throwing Knives': W_willow, 'Caltrops': W_caltrops, 'Armorbreak Javelin': W_javelin,
 'Moon Chakram': W_chakram, 'Senbon Needles': W_senbon,
 'Mag Nailgun': W_nailgun, 'Furnace Flamethrower': W_furnace, 'Tether Arc Cannon': W_tether,
}
WEAR = {'Alleyway Pistol':0.8,'Pipe Machete':1.0,'Crowbar':1.0,'Road Sign':0.9,'Rebar Spear':1.0,'Skullsplitter Shotgun':0.8,'Veteran Assault Rifle':0.7}
meta = {}
for i, (name, fn) in enumerate(ALL.items()):
    a = fn(); img = a.render(i*7+3, WEAR.get(name, 0.35))
    s = slug(name); img.save(f'{OUT}/{s}.png')
    meta[s] = {'name': name, 'w': img.width, 'h': img.height,
               'pivotX': round((a.pivot[0]+1.5)/img.width, 4), 'pivotY': round(1-(a.pivot[1]+1.5)/img.height, 4)}
json.dump(meta, open(f'{OUT}/weapons.json', 'w'), indent=1)
print(len(meta), 'weapons')
