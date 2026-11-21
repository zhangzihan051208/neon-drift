# 美术 · 零的 PixelLab 动作清单（基于八向图）

**八个方向的基准图**就是这次用 PixelLab「Create 8-directional sprite (Pro)」生成的那张，已经接进游戏当站姿（`idle_*`，单帧）。
- 原图：`images/角色/PixelLab参考/zero_8dir_pixellab.png`
- 拆好的单张参考：`images/角色/PixelLab参考/八向/zero_<方向>.png`（168×168，原样未改）

| 方向 | 正面 | 右下 | 右 | 右上 | 背面 | 左上 | 左 | 左下 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 缩写 | `s` | `se` | `e` | `ne` | `n` | `nw` | `w` | `sw` |

## 怎么做

### 推荐：在 Characters 里直接给零加动作

八向图是在 PixelLab 的 **Characters**（角色创建器）里做的，就在同一个角色上加动作，一次出八个方向，不用一张张传参考图。

1. 打开 Characters，进入零这个角色（八方向那个）。
2. 点 **Add animation**（添加动作）：
   - 清单里有对应模板的（站立呼吸、走路、跑步），直接选模板；
   - 没有模板的（持枪姿势、近战出手、死亡），选自定义动作，把清单里的英文描述贴进去。
3. 方向选全部 8 个（如果界面只给四个方向，就先做四个，斜方向用下面的备用方法补）。
4. 生成后逐个方向检查：造型没跑（冷白长发、黑长大衣、白内搭、义体左手），脚没出画面，动作连贯。不满意就重新生成这一条。
5. 导出：精灵表或逐帧 PNG 都可以，整个 zip 放进 `Tools/pixellab_in/`。文件名不用改，告诉我每个 zip 是哪条动作就行。

帧数以模板为准，不必和清单完全一样，游戏会按图的实际帧数播放。

### 备用：单张参考图 + Animate with text (New)

Characters 里做不出来的那一条，再用这个方法补：上传对应方向的 `zero_<方向>.png`，Animation Action 抄清单英文，帧数 4–16 的偶数，勾 Remove Background，固定一个 seed。

**不用管**尺寸、对齐、颜色：放进来以后我统一换成 240×180 的格子、对齐脚底、核对身高，再放进游戏。

## 清单

### 第一批：移动（必做，做完就是完整的八方向移动）

| 文件名 | 帧数 | Animation Action（英文照抄） |
| --- | --- | --- |
| `idle_<方向>` ×8 | 4 | `idle, standing still, subtle breathing, coat hem sways slightly` |
| `walk_<方向>` ×8 | 6 | `walking, calm steady pace, long coat swinging` |
| `run_<方向>` ×8 | 6 | `running fast, leaning forward slightly, coat flowing behind` |
| `dash_<方向>` ×8 | 4 | `quick dash forward, low lunge, coat whipping back` |

共 32 条。`idle` 做好后会替换现在的单帧站姿，有呼吸起伏。

### 第二批：持枪（空手摆姿势，武器游戏里另外画一层）

动作描述里一定要有 **empty hands, no weapon**，不然 PixelLab 会把枪画进去，游戏里就会出现两把枪。

| 文件名 | 帧数 | Animation Action |
| --- | --- | --- |
| `idle_pistol_<方向>` ×8 | 4 | `standing, one arm raised forward as if holding a pistol, empty hands, no weapon, subtle breathing` |
| `walk_pistol_<方向>` ×8 | 6 | `walking while aiming forward with one arm extended, empty hands, no weapon` |
| `shoot_pistol_<方向>` ×8 | 8 | `raise one arm, aim, small recoil kick twice, lower arm, empty hands, no weapon` |
| `idle_rifle_<方向>` ×8 | 4 | `standing in a two-handed rifle stance, both hands at waist and chest as if holding a long gun, empty hands, no weapon` |
| `walk_rifle_<方向>` ×8 | 6 | `walking in a two-handed rifle stance, empty hands, no weapon` |
| `shoot_rifle_<方向>` ×8 | 8 | `two-handed rifle stance, aim, recoil kicks shoulder back twice, empty hands, no weapon` |

共 48 条。先做这批，手枪和步枪类（含霰弹、狙击、特殊武器）就都有专门的持握姿势了。

### 第三批：近战（出手动作，武器同样由游戏画）

| 文件名 | 帧数 | Animation Action |
| --- | --- | --- |
| `melee_slash_<方向>` ×8 | 6 | `quick diagonal sword slash from high to low, one hand, empty hand, no weapon` |
| `melee_thrust_<方向>` ×8 | 6 | `step forward and stab straight ahead with a spear thrust, empty hands, no weapon` |
| `melee_blunt_<方向>` ×8 | 6 | `raise both arms overhead and smash down hard, empty hands, no weapon` |

共 24 条。

### 第四批：其余姿势和死亡（可以最后做）

| 文件名 | 帧数 | Animation Action |
| --- | --- | --- |
| `idle_heavy_<方向>` / `walk_heavy_<方向>` ×8 | 4 / 6 | `standing / walking braced, both arms low holding a heavy machine gun at the hip, wide stance, empty hands, no weapon` |
| `idle_thrown_<方向>` / `shoot_thrown_<方向>` ×8 | 4 / 6 | `holding a small throwing knife at chest, then throwing it forward with a quick arm flick, empty hand, no weapon` |
| `death_s` `death_e` `death_n` `death_w` | 8 | `collapsing to the ground, knees buckle, falls and lies still` |

死亡只需要四个方向（游戏里按四方向播）。

## 优先级

1. **第一批的 `idle` 和 `walk`**（16 条）：做完走路就是八方向，不会再斜着走播侧面动作。
2. 第一批其余（`run`、`dash`，16 条）。
3. 第二批（持枪，48 条）。
4. 第三批（近战，24 条）。
5. 第四批。

## 现在游戏里的过渡状态

- 站姿：八方向都换成了 PixelLab 新图（单帧，没有呼吸）。
- 走路、跑步、开枪、闪移、死亡：还是原来的四方向图（造型和新图略有差别：旧侧面会显得驼背）。新图放进来就自动替换。
