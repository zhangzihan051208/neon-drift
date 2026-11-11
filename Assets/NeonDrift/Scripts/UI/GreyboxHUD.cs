using UnityEngine;

namespace NeonDrift
{
    /// <summary>灰盒阶段的简易界面（IMGUI）。正式界面之后用 UI Toolkit 重做。</summary>
    public class GreyboxHUD : MonoBehaviour
    {
        static string msg = "";
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
                Color c = i < p.Hp.hp ? new Color(0.89f, 0.23f, 0.27f) : new Color(0.25f, 0.08f, 0.1f);
                Fill(new Rect(x + i * 16 * s, y, 13 * s, 13 * s), c);
            }
            // 能量
            Bar(new Rect(x, y + 20 * s, 120 * s, 6 * s), p.energy / p.maxEnergy, new Color(0.02f, 0.85f, 0.91f));
            // 闪移冷却
            Bar(new Rect(x, y + 30 * s, 60 * s, 4 * s), p.DashReady, new Color(1f, 0.16f, 0.43f));

            // 房间信息
            var room = root.Room;
            if (room != null)
            {
                string info = room.Cleared ? $"街区 {root.RoomIndex} · 已清理" : $"街区 {root.RoomIndex} · 波次 {room.Wave}/{room.WaveCount} · 剩余 {room.Alive}";
                GUI.Label(new Rect(Screen.width - 260 * s, 10 * s, 250 * s, 20 * s), info, new GUIStyle(label) { alignment = TextAnchor.UpperRight });
            }
            GUI.Label(new Rect(Screen.width - 260 * s, 26 * s, 250 * s, 20 * s),
                      $"射速 {p.fireRate:0.0}  伤害 {p.bulletDamage:0.00}  反弹 {p.bulletBounces}",
                      new GUIStyle(label) { alignment = TextAnchor.UpperRight, fontStyle = FontStyle.Normal });

            // 奖励名称
            var cam = Camera.main;
            foreach (var pk in Pickup.All)
            {
                Vector3 sp = cam.WorldToScreenPoint(pk.transform.position + Vector3.up * 0.7f);
                GUI.Label(new Rect(sp.x - 60 * s, Screen.height - sp.y - 10 * s, 120 * s, 20 * s), pk.Label, small);
            }

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
