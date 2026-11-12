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
            gameObject.AddComponent<GreyboxHUD>();
            StartRun();
        }

        public void StartRun()
        {
            if (World != null) Destroy(World.gameObject);
            if (Player != null) Destroy(Player.gameObject);
            Projectile.ClearAll();
            foreach (var p in Pickup.All.ToArray()) Destroy(p.gameObject);
            RunComplete = false;
            Room = null;
            RoomIndex = 1;
            Player = PlayerController.Spawn(Vector2.zero);
            World = WorldBuilder.Build(Player);
            Player.transform.position = World.Blocks[0].SpawnPoint;
            // 整个世界是连续的，镜头只跟随，不再限制在单个房间里
            CameraRig.I.SetTarget(Player.transform, new Rect(-10000f, -10000f, 20000f, 20000f));
            GreyboxHUD.Message("WASD 移动 · 鼠标瞄准 · 左键射击 · 空格闪移\n清完一个街区后出口打开，一路往画面上方走", 4f);
        }

        /// <summary>玩家走进一个街区时由街区调用。</summary>
        public void EnterRoom(RoomDirector room)
        {
            Room = room;
            RoomIndex = room.Index;
            if (room.Index > 1) GreyboxHUD.Message($"街区 {room.Index}", 2f);
        }

        public void OnRunComplete()
        {
            RunComplete = true;
            GreyboxHUD.Message("本局演示结束。按 R 重新生成一条新的路线", 999f);
        }

        public void OnPlayerDied()
        {
            Deaths++;
            GreyboxHUD.Message($"你死了（第 {Deaths} 次）。按 R 在诊所重启", 999f);
        }

        void Update()
        {
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

        IEnumerator DoHitstop(float seconds)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(seconds);
            Time.timeScale = 1f;
            stopCo = null;
        }
    }
}
