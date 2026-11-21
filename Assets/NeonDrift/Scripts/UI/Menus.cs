using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    /// <summary>
    /// 开始页面、暂停菜单、设置、操作说明（IMGUI，和灰盒 HUD 一套画法）。
    /// 菜单打开时游戏时间停住（timeScale = 0），玩家输入被挡掉。
    /// 键盘：↑↓/WS 选择，←→/AD 调数值，Enter/空格/E 确认，Esc 返回；手柄：十字键/左摇杆、A 确认、B 返回、Start 暂停。鼠标可以直接点、拖滑块。
    /// </summary>
    public class Menus : MonoBehaviour
    {
        public enum Page { Title, Pause, Settings, Controls }
        public static Menus I { get; private set; }
        readonly List<Page> stack = new List<Page>();
        static float unblockAt;
        /// <summary>菜单开着（或刚关上那一下）：玩家不响应输入。</summary>
        public static bool Blocking => I != null && (I.stack.Count > 0 || Time.unscaledTime < unblockAt);
        public static bool OnTitle => I != null && I.stack.Count > 0 && I.stack[0] == Page.Title;
        Page Top => stack[stack.Count - 1];

        int sel;
        float titleT;                 // 开始页面出现了多久（淡入用）
        bool wasBlocking;

        class Item
        {
            public Func<string> label; public Func<string> value;
            public Action ok; public Action<int> lr; public Func<float> slider; public Action<float> setSlider;
        }

        void Awake() { I = this; }

        public void ShowTitle() { stack.Clear(); stack.Add(Page.Title); sel = 0; titleT = 0f; }
        void Push(Page p) { stack.Add(p); sel = 0; }
        void Pop() { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); sel = 0; if (stack.Count == 0) Close(); }
        void Close() { stack.Clear(); unblockAt = Time.unscaledTime + 0.15f; Time.timeScale = 1f; }

        // ---------------------------------------------------------------- 菜单内容
        List<Item> Items()
        {
            var g = GameRoot.I;
            var list = new List<Item>();
            void Btn(string text, Action ok) => list.Add(new Item { label = () => text, ok = ok });
            void Slider(string text, Func<float> get, Action<float> set) => list.Add(new Item
            {
                label = () => text, slider = get, value = () => Mathf.RoundToInt(get() * 100f) + "%",
                setSlider = v => { set(Mathf.Clamp01(v)); Changed(); },
                lr = d => { set(Mathf.Clamp01(Mathf.Round((get() + d * 0.1f) * 10f) / 10f)); Changed(); },
            });
            void Toggle(string text, Func<bool> get, Action<bool> set) => list.Add(new Item
            {
                label = () => text, value = () => get() ? "On" : "Off",
                ok = () => { set(!get()); Changed(); }, lr = d => { set(!get()); Changed(); },
            });
            switch (Top)
            {
                case Page.Title:
                    Btn("Start Run", () => { Close(); g.ShowIntro(); });
                    Btn("Settings", () => Push(Page.Settings));
                    Btn("Controls", () => Push(Page.Controls));
                    Btn("Quit", Quit);
                    break;
                case Page.Pause:
                    Btn("Resume", Close);
                    Btn("Settings", () => Push(Page.Settings));
                    Btn("Controls", () => Push(Page.Controls));
                    Btn("Restart Run", () => { Close(); g.StartRun(); });
                    Btn("Quit to Title", () => { g.StartRun(); ShowTitle(); });
                    Btn("Quit Game", Quit);
                    break;
                case Page.Settings:
                    list.Add(new Item
                    {
                        label = () => "Language",
                        value = () => Loc.LangNames[Mathf.Max(0, Array.IndexOf(Loc.Langs, GameSettings.Language))],
                        ok = () => CycleLang(1), lr = CycleLang,
                    });
                    Slider("Master Volume", () => GameSettings.Master, v => GameSettings.Master = v);
                    Slider("Weapon Volume", () => GameSettings.Weapons, v => GameSettings.Weapons = v);
                    Slider("Effects Volume", () => GameSettings.Effects, v => GameSettings.Effects = v);
                    Slider("Ambience (Rain)", () => GameSettings.Ambience, v => GameSettings.Ambience = v);
                    Slider("Screen Shake", () => GameSettings.Shake, v => GameSettings.Shake = v);
                    Toggle("Fullscreen", () => GameSettings.Fullscreen, v => GameSettings.Fullscreen = v);
                    Toggle("VSync", () => GameSettings.VSync, v => GameSettings.VSync = v);
                    Btn("Back", Pop);
                    break;
                case Page.Controls:
                    Btn("Back", Pop);
                    break;
            }
            return list;
        }

        void CycleLang(int d)
        {
            int i = Array.IndexOf(Loc.Langs, GameSettings.Language);
            GameSettings.Language = Loc.Langs[((i + d) % Loc.Langs.Length + Loc.Langs.Length) % Loc.Langs.Length];
            Changed();
        }

        static void Changed() { GameSettings.Apply(); GameSettings.Save(); }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        static readonly string[][] ControlRows =
        {
            new[] { "Move", "WASD", "Left Stick" },
            new[] { "Aim", "Mouse", "Right Stick" },
            new[] { "Shoot", "Left Mouse", "RT" },
            new[] { "Melee", "Right Mouse", "B" },
            new[] { "Dash", "Space", "A" },
            new[] { "Reload", "R", "LT" },
            new[] { "Swap Gun", "Q / Wheel", "Y" },
            new[] { "Hack", "F", "LB" },
            new[] { "Interact", "E", "X" },
            new[] { "Drop Gun / Melee", "G / X", "—" },
            new[] { "Pause", "Esc", "Start" },
        };

        // ---------------------------------------------------------------- 输入
        bool stickUp, stickDown, stickLeft, stickRight;

        void Update()
        {
            var kb = Keyboard.current; var gp = Gamepad.current;
            bool esc = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);
            var g = GameRoot.I;
            if (stack.Count == 0)
            {
                // 暂停：活着、没在爬梯子、这一局没结束
                bool canPause = g != null && g.Player != null && !g.Player.IsDead && !g.RunComplete && !g.Climbing;
                if (esc && canPause) Push(Page.Pause);
                return;
            }
            titleT += Time.unscaledDeltaTime;
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (gp != null && gp.buttonEast.wasPressedThisFrame);
            if (gp != null && gp.startButton.wasPressedThisFrame && Top == Page.Pause) { Close(); return; }
            if (back && Top != Page.Title) { Pop(); return; }

            var items = Items();
            Vector2 st = gp != null ? gp.leftStick.ReadValue() : Vector2.zero;
            bool sU = st.y > 0.6f, sD = st.y < -0.6f, sL = st.x < -0.6f, sR = st.x > 0.6f;
            bool up = (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) || (gp != null && gp.dpad.up.wasPressedThisFrame) || (sU && !stickUp);
            bool down = (kb != null && (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) || (gp != null && gp.dpad.down.wasPressedThisFrame) || (sD && !stickDown);
            bool left = (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame)) || (gp != null && gp.dpad.left.wasPressedThisFrame) || (sL && !stickLeft);
            bool right = (kb != null && (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame)) || (gp != null && gp.dpad.right.wasPressedThisFrame) || (sR && !stickRight);
            stickUp = sU; stickDown = sD; stickLeft = sL; stickRight = sR;
            bool ok = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                   || (gp != null && gp.buttonSouth.wasPressedThisFrame);
            if (items.Count == 0) return;
            if (up) { sel = (sel + items.Count - 1) % items.Count; Sfx.Play("swap", 0.5f); }
            if (down) { sel = (sel + 1) % items.Count; Sfx.Play("swap", 0.5f); }
            sel = Mathf.Clamp(sel, 0, items.Count - 1);
            var it = items[sel];
            if ((left || right) && it.lr != null) { it.lr(right ? 1 : -1); Sfx.Play("swap", 0.5f); }
            if (ok && it.ok != null) { Sfx.Play("armor_regen", 0.8f); it.ok(); }
        }

        void LateUpdate()
        {
            bool b = stack.Count > 0;
            if (b) Time.timeScale = 0f;                     // 顿帧、慢动作的协程结束时会把时间调回 1，这里每帧压回去
            wasBlocking = b;
        }

        // ---------------------------------------------------------------- 画面
        GUIStyle txt, head, small, val;
        Texture2D logoNeon, logoDrift;
        static readonly Color Pink = new Color(1f, 0.24f, 0.55f), Amber = new Color(1f, 0.70f, 0.28f), Ink = new Color(0.86f, 0.83f, 0.80f);
        static readonly Color Panel = new Color(0.04f, 0.03f, 0.05f, 0.88f), Dim = new Color(0.55f, 0.52f, 0.52f);

        void OnGUI()
        {
            if (stack.Count == 0) return;
            GUI.depth = -100;
            float s = Screen.height / 540f;
            txt ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            head ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            val ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            txt.fontSize = Mathf.RoundToInt(14 * s); head.fontSize = Mathf.RoundToInt(20 * s);
            small.fontSize = Mathf.RoundToInt(10 * s); val.fontSize = Mathf.RoundToInt(13 * s);

            if (OnTitle) DrawTitleBackdrop(s);
            else Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.03f, 0.62f));

            var items = Items();
            switch (Top)
            {
                case Page.Title: DrawList(items, Screen.width * 0.5f - 110 * s, Screen.height * 0.58f, 220 * s, s, centered: true); break;
                case Page.Pause: DrawPanel("PAUSED", items, 240 * s, s); break;
                case Page.Settings: DrawPanel("SETTINGS", items, 380 * s, s); break;
                case Page.Controls: DrawControls(items, s); break;
            }
            // 底部操作提示
            Text(new Rect(0, Screen.height - 22 * s, Screen.width - 14 * s, 18 * s),
                 Loc.T("↑↓ Select · ←→ Adjust · Enter Confirm · Esc Back"), new GUIStyle(small) { alignment = TextAnchor.MiddleRight }, Dim);
        }

        void DrawPanel(string title, List<Item> items, float w, float s)
        {
            float rowH = 26 * s, h = 60 * s + items.Count * rowH + 12 * s;
            var r = new Rect(Screen.width / 2f - w / 2f, Screen.height / 2f - h / 2f, w, h);
            Fill(r, Panel);
            Frame(r, Amber * new Color(1, 1, 1, 0.5f), s);
            Text(new Rect(r.x, r.y + 10 * s, r.width, 30 * s), Loc.T(title), head, Amber);
            Fill(new Rect(r.x + 20 * s, r.y + 44 * s, r.width - 40 * s, 1 * s), new Color(1f, 0.7f, 0.28f, 0.35f));
            DrawList(items, r.x + 20 * s, r.y + 52 * s, r.width - 40 * s, s, centered: false);
        }

        void DrawList(List<Item> items, float x, float y, float w, float s, bool centered)
        {
            float rowH = 26 * s;
            var e = Event.current;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var row = new Rect(x, y + i * rowH, w, rowH - 3 * s);
                bool hot = i == sel;
                if (row.Contains(e.mousePosition) && (e.type == EventType.MouseMove || e.type == EventType.MouseDown)) { if (sel != i) sel = i; hot = true; }
                if (hot)
                {
                    Fill(row, new Color(1f, 0.24f, 0.55f, 0.16f));
                    Fill(new Rect(row.x, row.y, 3 * s, row.height), Pink);
                }
                string label = Loc.T(it.label());
                var ls = new GUIStyle(txt) { alignment = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft };
                Text(new Rect(row.x + 12 * s, row.y, row.width - 24 * s, row.height), hot && centered ? $"›  {label}  ‹" : label, ls, hot ? Color.white : Ink);
                if (it.slider != null)
                {
                    var bar = new Rect(row.x + row.width * 0.52f, row.y + row.height / 2f - 3 * s, row.width * 0.3f, 6 * s);
                    Fill(bar, new Color(0.2f, 0.17f, 0.2f));
                    Fill(new Rect(bar.x, bar.y, bar.width * it.slider(), bar.height), hot ? Pink : Amber);
                    Fill(new Rect(bar.x + bar.width * it.slider() - 2 * s, bar.y - 3 * s, 4 * s, bar.height + 6 * s), Color.white);
                    Text(new Rect(bar.xMax, row.y, row.xMax - bar.xMax - 8 * s, row.height), it.value(), val, Ink);
                    var hit = new Rect(bar.x - 6 * s, row.y, bar.width + 12 * s, row.height);
                    if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && hit.Contains(e.mousePosition))
                    { it.setSlider((e.mousePosition.x - bar.x) / bar.width); e.Use(); }
                }
                else if (it.value != null)
                    Text(new Rect(row.x, row.y, row.width - 10 * s, row.height), "‹ " + Loc.T(it.value()) + " ›", val, hot ? Pink : Amber);
                if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                {
                    sel = i;
                    if (it.ok != null) { Sfx.Play("armor_regen", 0.8f); it.ok(); }
                    else if (it.lr != null) it.lr(1);
                    e.Use();
                    return;                                  // 菜单可能已经换了一页
                }
            }
        }

        void DrawControls(List<Item> items, float s)
        {
            float w = 460 * s, rowH = 20 * s, h = 70 * s + ControlRows.Length * rowH + 40 * s;
            var r = new Rect(Screen.width / 2f - w / 2f, Screen.height / 2f - h / 2f, w, h);
            Fill(r, Panel); Frame(r, Amber * new Color(1, 1, 1, 0.5f), s);
            Text(new Rect(r.x, r.y + 10 * s, r.width, 30 * s), Loc.T("CONTROLS"), head, Amber);
            var cs = new GUIStyle(txt) { fontSize = Mathf.RoundToInt(12 * s) };
            float y = r.y + 48 * s;
            Text(new Rect(r.x + 24 * s, y, 180 * s, rowH), "", cs, Dim);
            Text(new Rect(r.x + 200 * s, y, 130 * s, rowH), Loc.T("Keyboard & Mouse"), cs, Dim);
            Text(new Rect(r.x + 330 * s, y, 120 * s, rowH), Loc.T("Gamepad"), cs, Dim);
            y += rowH;
            foreach (var row in ControlRows)
            {
                Text(new Rect(r.x + 24 * s, y, 180 * s, rowH), Loc.T(row[0]), cs, Ink);
                Text(new Rect(r.x + 200 * s, y, 130 * s, rowH), Loc.T(row[1]), cs, Amber);
                Text(new Rect(r.x + 330 * s, y, 120 * s, rowH), row[2], cs, Amber);
                y += rowH;
            }
            DrawList(items, r.x + 20 * s, r.yMax - 34 * s, r.width - 40 * s, s, centered: true);
        }

        // ---------------------------------------------------------------- 开始页面
        void DrawTitleBackdrop(float s)
        {
            float a = Mathf.Clamp01(titleT / 0.8f);
            float W = Screen.width, H = Screen.height;
            // 压暗后面冻住的街区：上面更黑，下面透一点霓虹
            for (int i = 0; i < 16; i++)
            {
                float k = i / 15f;
                Fill(new Rect(0, H * i / 16f, W, H / 16f + 1), new Color(0.03f, 0.015f, 0.05f, Mathf.Lerp(0.94f, 0.66f, k)));
            }
            // 雨：斜着落的细线
            float t = Time.unscaledTime;
            var rr = new System.Random(7);
            for (int i = 0; i < 140; i++)
            {
                float sx = (float)rr.NextDouble(), sp = 0.6f + (float)rr.NextDouble() * 0.6f, ph = (float)rr.NextDouble();
                float yy = ((ph + t * sp * 0.9f) % 1f) * (H + 60 * s) - 30 * s;
                float xx = (sx * (W + 80 * s) - yy * 0.18f) % (W + 80 * s) - 40 * s;
                float len = (10 + (float)rr.NextDouble() * 16) * s;
                Fill(new Rect(xx, yy, Mathf.Max(1f, 1 * s), len), new Color(0.75f, 0.68f, 0.82f, 0.10f + 0.08f * (float)rr.NextDouble()));
            }
            // 扫描线
            for (float y = 0; y < H; y += 3 * s) Fill(new Rect(0, y, W, 1), new Color(0, 0, 0, 0.12f));

            // 标题：NEON（洋红）DRIFT（琥珀），5×7 像素字，像霓虹灯管；NEON 偶尔闪一下
            if (logoNeon == null) { logoNeon = Logo("NEON"); logoDrift = Logo("DRIFT"); }
            float px = Mathf.Round(9 * s);
            float wN = logoNeon.width * px, wD = logoDrift.width * px, gap = 4 * px;
            float lx = W / 2f - (wN + gap + wD) / 2f, ly = H * 0.2f;
            float flick = Mathf.PerlinNoise(t * 7f, 0.3f) > 0.78f && Mathf.Repeat(t, 5.3f) < 1.2f ? 0.35f : 1f;
            Neon(logoNeon, new Rect(lx, ly, wN, logoNeon.height * px), Pink, a * flick, px);
            Neon(logoDrift, new Rect(lx + wN + gap, ly, wD, logoDrift.height * px), Amber, a, px);
            // 中文名 + 副标题
            var cn = new GUIStyle(head) { fontSize = Mathf.RoundToInt(22 * s) };
            Text(new Rect(0, ly + logoNeon.height * px + 14 * s, W, 30 * s), "霓  虹  漂  流", cn, new Color(1f, 0.86f, 0.9f, 0.85f * a));
            Text(new Rect(0, ly + logoNeon.height * px + 44 * s, W, 20 * s), Loc.T("A cyberpunk roguelike. It never stops raining in the Flooded District."), small, new Color(0.8f, 0.74f, 0.78f, 0.8f * a));
            Text(new Rect(14 * s, H - 22 * s, 300 * s, 18 * s), "Demo build · v0.1", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft }, Dim);
        }

        /// <summary>把一个词画成 5×7 像素字的贴图（每个笔画格 1 像素，用 Point 采样放大）。</summary>
        static Texture2D Logo(string word)
        {
            int w = word.Length * 6 - 1, h = 7;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[w * h];
            for (int i = 0; i < word.Length; i++)
                if (LadderArt.Glyphs.TryGetValue(word[i], out var g))
                    for (int r = 0; r < 7; r++)
                        for (int c = 0; c < 5; c++)
                            if (g[r][c] == '#') px[(6 - r) * w + i * 6 + c] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        void Neon(Texture2D tex, Rect r, Color col, float a, float px)
        {
            var old = GUI.color;
            // 光晕：往四周错开画几层淡的
            for (int k = 3; k >= 1; k--)
            {
                GUI.color = new Color(col.r, col.g, col.b, 0.07f * a);
                float o = k * px * 0.45f;
                GUI.DrawTexture(new Rect(r.x - o, r.y, r.width, r.height), tex); GUI.DrawTexture(new Rect(r.x + o, r.y, r.width, r.height), tex);
                GUI.DrawTexture(new Rect(r.x, r.y - o, r.width, r.height), tex); GUI.DrawTexture(new Rect(r.x, r.y + o, r.width, r.height), tex);
            }
            GUI.color = new Color(col.r * 0.35f, col.g * 0.3f, col.b * 0.35f, a);       // 灯管背后的暗影
            GUI.DrawTexture(new Rect(r.x + px * 0.5f, r.y + px * 0.5f, r.width, r.height), tex);
            GUI.color = new Color(col.r, col.g, col.b, a);
            GUI.DrawTexture(r, tex);
            GUI.color = new Color(1f, 1f, 1f, 0.35f * a);                                // 灯管中心发白
            GUI.DrawTexture(new Rect(r.x + px * 0.3f, r.y + px * 0.3f, r.width - px * 0.6f, r.height - px * 0.6f), tex);
            GUI.color = old;
        }

        // ---------------------------------------------------------------- 小工具
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        static void Frame(Rect r, Color c, float s)
        {
            float t = Mathf.Max(1f, s);
            Fill(new Rect(r.x, r.y, r.width, t), c); Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c); Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
        static void Text(Rect r, string text, GUIStyle st, Color c)
        {
            var o = st.normal.textColor; st.normal.textColor = c; GUI.Label(r, text, st); st.normal.textColor = o;
        }
    }
}
