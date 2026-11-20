using UnityEngine;

namespace NeonDrift
{
    /// <summary>灰盒阶段的简易界面（IMGUI）。正式界面之后用 UI Toolkit 重做。</summary>
    public class GreyboxHUD : MonoBehaviour
    {
        static string msg = "";
        /// <summary>全屏黑幕（爬梯子转场用），0–1。</summary>
        public static float Fade;

        // 飘字（暴击、+1 子弹）：在世界坐标上往上飘、淡出
        class Pop { public Vector2 pos; public string text; public Color col; public float t; }
        static readonly System.Collections.Generic.List<Pop> pops = new System.Collections.Generic.List<Pop>();
        public static void Popup(Vector2 world, string text, Color col)
        {
            if (pops.Count > 30) pops.RemoveAt(0);
            pops.Add(new Pop { pos = world + new Vector2(Random.Range(-0.2f, 0.2f), 0f), text = text, col = col });
        }
        static float msgT;

        public static void Message(string text, float seconds)
        {
            msg = text;
            msgT = seconds;
        }

        GUIStyle label, big, small;
        Texture2D white;

        void Update()
        {
            if (msgT > 0f) msgT -= Time.unscaledDeltaTime;
            for (int i = pops.Count - 1; i >= 0; i--) { pops[i].t += Time.unscaledDeltaTime; if (pops[i].t > 0.8f) pops.RemoveAt(i); }
        }

        void OnGUI()
        {
            float s = Screen.height / 540f;
            if (white == null) white = Texture2D.whiteTexture;
            label ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
            small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };
            label.fontSize = Mathf.RoundToInt(12 * s);
            big.fontSize = Mathf.RoundToInt(16 * s);
            small.fontSize = Mathf.RoundToInt(10 * s);

            var root = GameRoot.I;
            var p = root != null ? root.Player : null;
            if (p == null) return;

            // 生命（格子）
            float x = 14 * s, y = 12 * s;
            for (int i = 0; i < Mathf.CeilToInt(p.Hp.max); i++)
            {
                // 有了护甲和抗性，生命会出现小数：最后一格按剩余比例画一部分
                var cell = new Rect(x + i * 16 * s, y, 13 * s, 13 * s);
                Fill(cell, new Color(0.25f, 0.08f, 0.1f));
                float f = Mathf.Clamp01(p.Hp.hp - i);
                if (f > 0f) Fill(new Rect(cell.x, cell.y + cell.height * (1f - f), cell.width, cell.height * f), new Color(0.89f, 0.23f, 0.27f));
            }
            // 护甲（生命右边的灰金色格子）：满的格子实心，正在恢复的那一格从下往上填
            float ax = x + Mathf.CeilToInt(p.Hp.max) * 16 * s + 8 * s;
            var armorCol = new Color(0.86f, 0.80f, 0.62f);
            for (int i = 0; i < p.MaxArmor; i++)
            {
                var cell = new Rect(ax + i * 14 * s, y + 1 * s, 11 * s, 11 * s);
                Fill(cell, new Color(0.18f, 0.17f, 0.14f));
                if (i < p.Armor) Fill(cell, armorCol);
                else if (i == p.Armor && p.ArmorProgress > 0f)
                {
                    float f = p.ArmorProgress;
                    Fill(new Rect(cell.x, cell.y + cell.height * (1f - f), cell.width, cell.height * f), new Color(armorCol.r, armorCol.g, armorCol.b, 0.55f));
                }
            }
            // 电量（黄绿色，和残电、电芯一个颜色）+ 信用点
            var ec = Loot.EnergyCol;
            Bar(new Rect(x, y + 20 * s, 120 * s, 6 * s), p.energy / p.maxEnergy, ec);
            GUI.Label(new Rect(x + 126 * s, y + 14 * s, 120 * s, 18 * s), $"POWER {Mathf.FloorToInt(p.energy)}", new GUIStyle(label) { normal = { textColor = ec } });
            GUI.Label(new Rect(x, y + 36 * s, 160 * s, 18 * s), $"₡ {p.credits}", new GUIStyle(label) { normal = { textColor = Loot.CreditCol } });
            // RAM（骇入用）：洋红色小格，正在恢复的那格按进度填
            if (p.Hacker != null)
            {
                var hk = p.Hacker;
                GUI.Label(new Rect(x, y + 52 * s, 40 * s, 16 * s), "RAM", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, normal = { textColor = Hacker.HackCol } });
                for (int i = 0; i < hk.MaxRam; i++)
                {
                    var cell = new Rect(x + 32 * s + i * 12 * s, y + 55 * s, 9 * s, 9 * s);
                    Fill(cell, new Color(0.2f, 0.06f, 0.14f));
                    float f = Mathf.Clamp01(hk.Ram - i);
                    if (f > 0f) Fill(new Rect(cell.x, cell.y + cell.height * (1f - f), cell.width, cell.height * f), f >= 1f ? Hacker.HackCol : new Color(Hacker.HackCol.r, Hacker.HackCol.g, Hacker.HackCol.b, 0.5f));
                }
            }
            // 闪移冷却
            Bar(new Rect(x, y + 30 * s, 60 * s, 4 * s), p.DashReady, new Color(1f, 0.16f, 0.43f));

            // 两个武器槽（左下角），亮的是手里这把
            for (int i = 0; i < 2; i++)
            {
                var w = p.Slots[i];
                bool on = w != null && w == p.Weapon;
                var r = new Rect(x + i * 150 * s, Screen.height - 50 * s, 144 * s, 38 * s);
                Fill(r, on ? new Color(0.16f, 0.10f, 0.06f, 0.85f) : new Color(0.05f, 0.05f, 0.06f, 0.6f));
                if (w != null) Fill(new Rect(r.x, r.y, r.width, (on ? 3 : 1) * s), Weapons.RarityColor[(int)w.rarity]);
                string txt = w == null ? "(empty gun slot)" : $"{w.name}   {(w.thrown ? $"{p.Ammo(w)}/{p.MagSize(w)} left" : $"{p.Ammo(w)}/{p.MagSize(w)}  +{p.Reserve(w)}")}\n{Weapons.Tag(w)} · {(w.energy > 0f ? $"{w.energy:0.##} pwr" : "no power")} · Wt {w.weight}";
                if (on && p.Reloading) Fill(new Rect(r.x, r.yMax - 3 * s, r.width * p.ReloadProgress, 3 * s), Sprites.Hex("d8a040"));
                var st = new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, normal = { textColor = on ? Color.white : new Color(0.6f, 0.6f, 0.6f) } };
                GUI.Label(new Rect(r.x + 6 * s, r.y, r.width - 8 * s, r.height), txt, st);
            }
            // 近战位
            if (p.MeleeWeapon != null)
            {
                var mw = p.MeleeWeapon;
                var mr = new Rect(x + 300 * s, Screen.height - 50 * s, 144 * s, 38 * s);
                Fill(mr, mw.signature ? new Color(0.05f, 0.03f, 0.04f, 0.92f) : new Color(0.16f, 0.07f, 0.06f, 0.85f));
                Fill(new Rect(mr.x, mr.y, mr.width, 2 * s), mw.signature ? Weapons.SignatureCol : Weapons.RarityColor[(int)mw.rarity]);
                if (mw.signature) Fill(new Rect(mr.x, mr.yMax - 1 * s, mr.width, 1 * s), Weapons.SignatureCol);
                GUI.Label(new Rect(mr.x + 6 * s, mr.y, mr.width - 8 * s, mr.height), $"{mw.name}\nRMB · {Weapons.Tag(mw)} · {Weapons.ReachName(mw.reach)} · Wt {mw.weight}",
                          new GUIStyle(small) { alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } });
            }
            GUI.Label(new Rect(x + 450 * s, Screen.height - 50 * s, 200 * s, 40 * s), "Q swap · R reload\nG drop gun · X drop melee", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
            // 负重：三个武器加起来的重量
            {
                var lr = new Rect(x, Screen.height - 64 * s, 290 * s, 5 * s);
                float lf = p.Load / (float)p.Capacity;
                Fill(lr, new Color(0.1f, 0.09f, 0.08f, 0.85f));
                Fill(new Rect(lr.x, lr.y, lr.width * Mathf.Clamp01(lf), lr.height), lf >= 1f ? Sprites.Hex("e0703a") : Sprites.Hex("b0a080"));
                GUI.Label(new Rect(lr.xMax + 6 * s, lr.y - 6 * s, 120 * s, 16 * s), $"LOAD {p.Load}/{p.Capacity}", new GUIStyle(small) { alignment = TextAnchor.MiddleLeft });
            }
            // 换弹进度：头顶一条小进度条
            if (p.Reloading && Camera.main != null)
            {
                Vector3 hp2 = Camera.main.WorldToScreenPoint(p.transform.position + Vector3.up * 3.2f);
                var bar = new Rect(hp2.x - 22 * s, Screen.height - hp2.y, 44 * s, 4 * s);
                Fill(bar, new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(bar.x, bar.y, bar.width * p.ReloadProgress, bar.height), Sprites.Hex("d8a040"));
                GUI.Label(new Rect(bar.x - 20 * s, bar.y - 16 * s, bar.width + 40 * s, 14 * s), "RELOAD", new GUIStyle(small) { normal = { textColor = Sprites.Hex("d8a040") } });
            }

            // 房间信息
            var room = root.Room;
            if (room != null)
            {
                string kind = room.Kind == RoomKind.Shop ? "Black Market" : room.Kind == RoomKind.Event ? "Event" : room.Kind == RoomKind.Signal ? "Ghost Channel" : "Combat";
                string head = $"FLOODED DISTRICT · ROOM {room.Index}/{WorldBuilder.Part1Rooms} · {kind}";
                string info = room.Cleared ? $"{head} · CLEARED" : $"{head} · WAVE {room.Wave}/{room.WaveCount} · {room.Alive} LEFT";
                GUI.Label(new Rect(Screen.width - 260 * s, 10 * s, 250 * s, 20 * s), info, new GUIStyle(label) { alignment = TextAnchor.UpperRight });
            }
            GUI.Label(new Rect(Screen.width - 360 * s, 26 * s, 350 * s, 20 * s),
                      $"{p.Weapon.name}  rate {p.fireRate:0.0}  dmg {p.bulletDamage:0.00}   (debug: 1-9, 0, - swap weapon)",
                      new GUIStyle(label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Normal });

            // 奖励名称
            var cam = Camera.main;
            foreach (var pk in Pickup.All)
            {
                Vector3 sp = cam.WorldToScreenPoint(pk.transform.position + Vector3.up * 1.1f);
                var bst = new GUIStyle(small) { wordWrap = true, normal = { textColor = pk.LabelColor } };
                Fill(new Rect(sp.x - 75 * s, Screen.height - sp.y - 14 * s, 150 * s, 32 * s), new Color(0f, 0f, 0f, 0.5f));
                GUI.Label(new Rect(sp.x - 75 * s, Screen.height - sp.y - 13 * s, 150 * s, 32 * s), pk.Label, bst);
            }

            // 地上能互动的东西：名字常驻，离得最近的那个显示操作提示
            var cur = Interactable.Current;
            foreach (var it in Interactable.All)
            {
                string lb = it == cur ? it.Prompt : it.Label;
                if (string.IsNullOrEmpty(lb)) continue;
                Vector3 sp = cam.WorldToScreenPoint(it.LabelPoint);
                Color tc = it is WeaponPickup wpk ? wpk.LabelColor : new Color(0.8f, 0.75f, 0.68f);
                var st = new GUIStyle(small) { normal = { textColor = it == cur ? Color.white : tc } };
                if (it == cur) { st.fontStyle = FontStyle.Bold; Fill(new Rect(sp.x - 120 * s, Screen.height - sp.y - 14 * s, 240 * s, 30 * s), new Color(0f, 0f, 0f, 0.55f)); }
                GUI.Label(new Rect(sp.x - 120 * s, Screen.height - sp.y - 12 * s, 240 * s, 30 * s), lb, st);
            }

            // 已经拿到的增益（右上角，房间信息下面）
            if (p.BuffStacks.Count > 0)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in p.BuffStacks)
                {
                    var b = Buffs.Get(kv.Key);
                    if (b == null) continue;
                    sb.Append(b.name).Append(kv.Value > 1 ? $"×{kv.Value}" : "").Append("  ");
                }
                GUI.Label(new Rect(Screen.width - 360 * s, 42 * s, 350 * s, 40 * s), sb.ToString(),
                          new GUIStyle(small) { alignment = TextAnchor.UpperRight, wordWrap = true });
            }

            // 骇入目标：四个角的选框，蓄力时往里收；RAM 不够时变灰
            if (p.Hacker != null && p.Hacker.Target is Object tobj && tobj != null && Fade < 0.01f)
            {
                var tg = p.Hacker.Target;
                bool enough = p.Hacker.Ram >= tg.HackCost;
                Color bc = enough ? Hacker.HackCol : new Color(0.5f, 0.5f, 0.5f, 0.8f);
                Vector3 sp = cam.WorldToScreenPoint(tg.HackPoint);
                float cx = sp.x, cy = Screen.height - sp.y;
                float r = (22f - 8f * p.Hacker.Charge + Mathf.Sin(Time.unscaledTime * 6f) * 1.5f) * s, l = 6 * s, th = 2 * s;
                for (int sx = -1; sx <= 1; sx += 2) for (int sy = -1; sy <= 1; sy += 2)
                {
                    float px = cx + sx * r, py = cy + sy * r;
                    Fill(new Rect(sx < 0 ? px : px - l, py - th / 2f, l, th), bc);
                    Fill(new Rect(px - th / 2f, sy < 0 ? py : py - l, th, l), bc);
                }
                GUI.Label(new Rect(cx - 80 * s, cy + r + 2 * s, 160 * s, 16 * s), $"F {tg.HackName} ({tg.HackCost} RAM)",
                          new GUIStyle(small) { normal = { textColor = bc } });
            }

            // 飘字
            if (cam != null)
                foreach (var pp in pops)
                {
                    float k = pp.t / 0.8f;
                    Vector3 sp2 = cam.WorldToScreenPoint(pp.pos + Vector2.up * (0.3f + k * 0.8f));
                    var c = pp.col; c.a = 1f - k * k;
                    var st2 = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt((pp.text == "CRIT" ? 15 : 11) * s * (k < 0.15f ? 1.3f : 1f)), normal = { textColor = c } };
                    var sh = new GUIStyle(st2) { normal = { textColor = new Color(0f, 0f, 0f, c.a * 0.8f) } };
                    var rr = new Rect(sp2.x - 60 * s, Screen.height - sp2.y - 12 * s, 120 * s, 24 * s);
                    GUI.Label(new Rect(rr.x + 1 * s, rr.y + 1 * s, rr.width, rr.height), pp.text, sh);
                    GUI.Label(rr, pp.text, st2);
                }

            // 转场黑幕
            if (Fade > 0.001f) Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, Mathf.Clamp01(Fade)));

            // 中央提示
            if (msgT > 0f && !string.IsNullOrEmpty(msg))
            {
                var r = new Rect(Screen.width / 2f - 260 * s, Screen.height - 110 * s, 520 * s, 60 * s);
                Fill(r, new Color(0.03f, 0.02f, 0.06f, 0.75f));
                GUI.Label(r, msg, big);
            }
        }

        void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, white);
            GUI.color = old;
        }

        void Bar(Rect r, float t, Color c)
        {
            Fill(r, new Color(c.r * 0.2f, c.g * 0.2f, c.b * 0.25f, 0.9f));
            Fill(new Rect(r.x, r.y, r.width * Mathf.Clamp01(t), r.height), c);
        }
    }
}
