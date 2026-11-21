using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace NeonDrift
{
    /// <summary>
    /// 音效：从 StreamingAssets/Audio/sfx 读 WAV（代码合成的，见 Tools/gen_sfx.py），同名不同序号的是变体，随机挑一个，
    /// 音高再随机一点点。离镜头远的声音小、往左右偏；同一个声音太密（机枪、火焰）会被限流，不会叠成噪音。
    /// 整体音量 Master 默认 0.7（要求：不要太吵）。
    /// </summary>
    public static class Sfx
    {
        public static float Master = 0.7f;
        /// <summary>武器音量（枪声、刀声）单独一档，不受 Master 压低：武器手感是战斗反馈的核心。</summary>
        public static float WeaponVol = 1f;
        /// <summary>设置界面里的四个音量滑块（0–1）。</summary>
        public static float UserMaster = 1f, UserWeapons = 1f, UserEffects = 1f;
        static float userAmbience = 1f, loopBase = 0f;
        public static void SetAmbience(float v) { userAmbience = v; if (loopSrc != null) loopSrc.volume = loopBase * Master * UserMaster * userAmbience; }
        static bool IsWeapon(string n) => n.StartsWith("shoot_") || n == "swing" || n == "thrust" || n == "heavy_swing"
            || n == "melee_hit" || n == "blunt_hit" || n == "bullet_cut" || n == "crit";
        static readonly Dictionary<string, int> lastVariant = new Dictionary<string, int>();
        static Dictionary<string, List<AudioClip>> clips;
        static readonly Dictionary<string, float> last = new Dictionary<string, float>();
        static AudioSource[] pool;
        static AudioSource loopSrc;
        static int next;

        static void Load()
        {
            if (clips != null) return;
            clips = new Dictionary<string, List<AudioClip>>();
            string dir = Path.Combine(Application.streamingAssetsPath, "Audio", "sfx");
            if (!Directory.Exists(dir)) { Debug.LogWarning("找不到音效目录：" + dir); return; }
            int n = 0;
            foreach (var f in Directory.GetFiles(dir, "*.wav"))
            {
                string nm = Path.GetFileNameWithoutExtension(f);
                int us = nm.LastIndexOf('_');
                string key = us > 0 && int.TryParse(nm.Substring(us + 1), out _) ? nm.Substring(0, us) : nm;
                var clip = LoadWav(File.ReadAllBytes(f), nm);
                if (clip == null) continue;
                if (!clips.TryGetValue(key, out var list)) clips[key] = list = new List<AudioClip>();
                list.Add(clip); n++;
            }
            Debug.Log($"音效：读到 {n} 个");
        }

        /// <summary>读 16 bit PCM 的 WAV。</summary>
        static AudioClip LoadWav(byte[] b, string name)
        {
            if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F') return null;
            int ch = 1, sr = 44100, bits = 16, pos = 12, dataPos = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int len = System.BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ") { ch = System.BitConverter.ToInt16(b, pos + 10); sr = System.BitConverter.ToInt32(b, pos + 12); bits = System.BitConverter.ToInt16(b, pos + 22); }
                else if (id == "data") { dataPos = pos + 8; dataLen = Mathf.Min(len, b.Length - dataPos); break; }
                pos += 8 + len + (len & 1);
            }
            if (dataPos < 0 || bits != 16) return null;
            int count = dataLen / 2;
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = System.BitConverter.ToInt16(b, dataPos + i * 2) / 32768f;
            var clip = AudioClip.Create(name, count / ch, ch, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Ensure()
        {
            if (pool != null && pool[0] != null) return;
            var host = new GameObject("[Sfx]");
            Object.DontDestroyOnLoad(host);
            pool = new AudioSource[24];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = host.AddComponent<AudioSource>();
                s.playOnAwake = false; s.spatialBlend = 0f;
                pool[i] = s;
            }
            loopSrc = host.AddComponent<AudioSource>();
            loopSrc.playOnAwake = false; loopSrc.loop = true;
        }

        /// <summary>
        /// 播一个音效。at：声音在世界里的位置（离镜头远会变小、往左右偏；不给就是界面上的声音）。
        /// minGap：同一个声音两次之间至少隔多久（秒），机枪这类密集的声音靠它限流。
        /// </summary>
        public static void Play(string name, float vol = 1f, Vector2? at = null, float pitchVar = 0.05f, float minGap = 0.03f)
        {
            Load(); Ensure();
            if (!clips.TryGetValue(name, out var list) || list.Count == 0) return;
            float now = Time.unscaledTime;
            if (last.TryGetValue(name, out var t0) && now - t0 < minGap) return;
            last[name] = now;
            float v = vol * UserMaster * (IsWeapon(name) ? WeaponVol * UserWeapons : Master * UserEffects), pan = 0f;
            if (v <= 0.001f) return;
            var cam = Camera.main;
            if (at.HasValue && cam != null)
            {
                Vector2 d = at.Value - (Vector2)cam.transform.position;
                v *= Mathf.Clamp01(1.25f - d.magnitude / 16f);
                if (v <= 0.01f) return;
                pan = Mathf.Clamp(d.x / 14f, -0.45f, 0.45f);
            }
            var src = pool[next]; next = (next + 1) % pool.Length;
            int k = Random.Range(0, list.Count);                    // 不和上一次挑到同一个变体
            if (list.Count > 1 && lastVariant.TryGetValue(name, out var pk) && k == pk) k = (k + 1 + Random.Range(0, list.Count - 1)) % list.Count;
            lastVariant[name] = k;
            src.clip = list[k];
            src.volume = v;
            src.pitch = 1f + Random.Range(-pitchVar, pitchVar);
            src.panStereo = pan;
            src.Play();
        }

        /// <summary>环境循环（雨声）。</summary>
        public static void Loop(string name, float vol)
        {
            Load(); Ensure();
            if (!clips.TryGetValue(name, out var list) || list.Count == 0) return;
            loopBase = vol;
            float v = vol * Master * UserMaster * userAmbience;
            if (loopSrc.isPlaying && loopSrc.clip == list[0]) { loopSrc.volume = v; return; }
            loopSrc.clip = list[0]; loopSrc.volume = v; loopSrc.Play();
        }

        /// <summary>按子弹样式挑开枪声。</summary>
        public static string ShotSound(WeaponDef w) => w.bullet switch
        {
            "pistol" or "slug" => "shoot_pistol",
            "rifle" or "smg" or "mg" => "shoot_rifle",
            "pellet" or "ember" => "shoot_shotgun",
            "sniper" or "rail" => "shoot_sniper",
            "smart" or "stinger" or "bolt" or "orb" or "nail" => "shoot_energy",
            "grenade" or "missile" => "shoot_launcher",
            "flame" => "shoot_flame",
            _ => "throw",
        };
    }
}
