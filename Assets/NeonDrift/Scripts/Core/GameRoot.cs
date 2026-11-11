using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NeonDrift
{
    /// <summary>
    /// 灰盒原型的入口。点 Play 后自动生成：摄像机设置、玩家、房间、HUD。
    /// 场景里放一个 NoBootstrap 组件可以关掉它（以后做正式场景时用）。
    /// </summary>
    public class GameRoot : MonoBehaviour
    {
        public static GameRoot I { get; private set; }

        public PlayerController Player { get; private set; }
        public RoomDirector Room { get; private set; }
        public int RoomIndex { get; private set; }
        public int Deaths { get; private set; }

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
            gameObject.AddComponent<GreyboxHUD>();
            StartRun();
        }

        public void StartRun()
        {
            if (Room != null) Destroy(Room.gameObject);
            if (Player != null) Destroy(Player.gameObject);
            Projectile.ClearAll();
            RoomIndex = 1;
            Player = PlayerController.Spawn(Vector2.zero);
            BuildRoom();
        }

        public void NextRoom()
        {
            RoomIndex++;
            BuildRoom();
        }

        void BuildRoom()
        {
            if (Room != null) Destroy(Room.gameObject);
            Projectile.ClearAll();
            Room = RoomDirector.Build(RoomIndex, Player);
            CameraRig.I.SetTarget(Player.transform, Room.Bounds);
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
            if (restart && Player != null && Player.IsDead)
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
