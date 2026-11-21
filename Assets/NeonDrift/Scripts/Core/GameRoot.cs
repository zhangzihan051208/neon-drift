using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    /// <summary>
    /// 灰盒原型的入口。点 Play 后自动生成：摄像机设置、玩家、由街区拼成的世界、HUD。
    /// 场景里放一个 NoBootstrap 组件可以关掉它（以后做正式场景时用）。
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        public PlayerController Player { get; private set; }
        public RoomDirector Room { get; private set; }
        public int RoomIndex { get; private set; }
        public int Deaths { get; private set; }
        public WorldBuilder World { get; private set; }
        public bool RunComplete { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Object.FindAnyObjectByType<NoBootstrap>() != null) return;
            if (I != null) return;
            var go = new GameObject("[NeonDrift]");
            go.AddComponent<GameRoot>();
        }

        void Awake()
        {
            I = this;
            CameraRig.Setup();
            Atmosphere.Setup(Camera.main);
            GameSettings.Load();                                  // 语言、音量、震动等设置
            gameObject.AddComponent<GreyboxHUD>();
            gameObject.AddComponent<Menus>();
            StartRun();
            Menus.I.ShowTitle();                                  // 先进开始页面
        }

        public void StartRun()
        {
            if (climbCo != null) { StopCoroutine(climbCo); climbCo = null; }
            GreyboxHUD.Fade = 0f;
            if (World != null) Destroy(World.gameObject);
            if (Player != null) Destroy(Player.gameObject);
            Projectile.ClearAll();
            foreach (var p in Pickup.All.ToArray()) Destroy(p.gameObject);
            RunComplete = false;
            Room = null;
            RoomIndex = 1;
            Player = PlayerController.Spawn(Vector2.zero);
            World = WorldBuilder.Build(Player);
            PlaceInRoom();
            Sfx.Loop("rain_loop", 0.22f);                          // 雨声底噪，很轻
            ShowIntro();
        }

        /// <summary>开局的操作提示（从开始页面进游戏时也再显示一次）。</summary>
        public void ShowIntro()
        {
            GreyboxHUD.Message("WASD move · LMB shoot · RMB melee · Space dash · R reload · Q swap · F hack · E interact\nAmmo is scarce: melee kills give bullets back", 5f);
        }

        /// <summary>玩家走进一个街区时由街区调用。</summary>
        public void EnterRoom(RoomDirector room)
        {
            Room = room;
            RoomIndex = room.Index;
            Player?.OnRoomStart();
            if (room.Kind == RoomKind.Shop) GreyboxHUD.Message("Old Ghost's black market: walk up to an item and press E to buy", 3f);
            else if (room.Kind == RoomKind.Event) GreyboxHUD.Message($"Event: {room.EventName}", 2.5f);
            else if (room.Kind == RoomKind.Signal) { }                                     // 幽灵频道房：入侵台词由房间自己显示
            else if (room.Index > 1) GreyboxHUD.Message($"Room {room.Index}", 1.5f);
        }

        public void OnRunComplete()
        {
            RunComplete = true;
            GreyboxHUD.Message("End of the demo run. Press R for a new route", 999f);
        }

        public void OnPlayerDied()
        {
            Deaths++;
            GreyboxHUD.Message($"You died (death #{Deaths}). Press R to restart at the clinic", 999f);
        }

        void PlaceInRoom()
        {
            var r = World.Current;
            Player.transform.position = r.SpawnPoint;
            CameraRig.I.SetTarget(Player.transform, r.ViewRect);        // 镜头只框住这一间
        }

        // ---------------------------------------------------------------- 爬梯子转场
        Coroutine climbCo;
        public bool Climbing => climbCo != null;

        public void Climb(Ladder l)
        {
            if (climbCo != null || Player == null || Player.IsDead) return;
            if (Room != null && !Room.Cleared) return;
            climbCo = StartCoroutine(DoClimb(l));
        }

        IEnumerator DoClimb(Ladder l)
        {
            var p = Player;
            var rb = p.GetComponent<Rigidbody2D>();
            p.Frozen = true;
            rb.linearVelocity = Vector2.zero;
            rb.simulated = false;
            Sfx.Play("ladder");
            // 走到梯子脚下
            Vector2 start = p.transform.position;
            p.ClimbMove = (l.Foot - start).sqrMagnitude > 0.01f ? (l.Foot - start).normalized : Vector2.up;
            for (float t = 0f; t < 0.2f; t += Time.deltaTime)
            {
                p.transform.position = Vector2.Lerp(start, l.Foot, t / 0.2f);
                yield return null;
            }
            // 往上爬，画面渐黑
            p.ClimbMove = Vector2.up;
            for (float t = 0f; t < 0.6f; t += Time.deltaTime)
            {
                p.transform.position = l.Foot + Vector2.up * (2.4f * t / 0.6f);
                GreyboxHUD.Fade = t / 0.6f;
                yield return null;
            }
            GreyboxHUD.Fade = 1f;
            p.ClimbMove = Vector2.zero;
            if (l.target == RoomKind.Boss)
            {
                RunComplete = true;
                GreyboxHUD.Message("The boss fight is not built yet. End of chapter demo. Press R to start over", 999f);
                yield break;                                    // 停在黑屏上
            }
            World.Next(l.target);
            PlaceInRoom();
            yield return new WaitForSeconds(0.15f);
            p.Frozen = false;
            rb.simulated = true;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                GreyboxHUD.Fade = 1f - t / 0.4f;
                yield return null;
            }
            GreyboxHUD.Fade = 0f;
            climbCo = null;
        }

        void Update()
        {
            if (Menus.Blocking) return;                           // 菜单开着：不交互、不重开
            Interactable.Tick(Player);
            var kb = Keyboard.current;
            var gp = Gamepad.current;
            bool restart = (kb != null && kb.rKey.wasPressedThisFrame) || (gp != null && gp.startButton.wasPressedThisFrame);
            if (restart && Player != null && (Player.IsDead || RunComplete))
            {
                GreyboxHUD.Message("", 0f);
                StartRun();
            }
        }

        // ---------------------------------------------------------------- 顿帧
        Coroutine stopCo;

        public static void Hitstop(float seconds)
        {
            if (I == null) return;
            if (I.stopCo != null) I.StopCoroutine(I.stopCo);
            I.stopCo = I.StartCoroutine(I.DoHitstop(seconds));
        }

        /// <summary>慢动作（完美闪避）：seconds 是真实时间，scale 是时间流速。</summary>
        public static void SlowMo(float seconds, float scale)
        {
            if (I == null) return;
            if (I.stopCo != null) I.StopCoroutine(I.stopCo);
            I.stopCo = I.StartCoroutine(I.DoSlow(seconds, scale));
        }

        IEnumerator DoSlow(float seconds, float scale)
        {
            Time.timeScale = scale;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = 1f;
            stopCo = null;
        }

        IEnumerator DoHitstop(float seconds)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = 1f;
            stopCo = null;
        }
    }
}
