using Olca.Core.Interfaces;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Olca.Unity
{
    /// <summary>OSによる指の取消をUIクリックへ転用しない。指ごとのEventSystemデータだけを取消す。</summary>
    public sealed class TouchUiCancelGuard : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        private static readonly HashSet<TouchUiCancelGuard> active=new HashSet<TouchUiCancelGuard>();
        private static readonly Dictionary<(int Device,int Finger),int> canceled=new Dictionary<(int,int),int>();
        public static void BeginTouch(int deviceId,int touchId){canceled.Remove((deviceId,touchId));}
        private readonly Dictionary<int,UnityEngine.EventSystems.PointerEventData> presses=new Dictionary<int,UnityEngine.EventSystems.PointerEventData>();
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data)
        {
            if(data==null)return;
            if(data is UnityEngine.InputSystem.UI.ExtendedPointerEventData touch && touch.device!=null &&
                canceled.TryGetValue((touch.device.deviceId,touch.touchId),out var frame) && frame>=Time.frameCount-1)
                data.eligibleForClick=false;
            presses[data.pointerId]=data;active.Add(this);
        }
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData data)
        {
            if(data!=null)presses.Remove(data.pointerId);
            if(data is UnityEngine.InputSystem.UI.ExtendedPointerEventData touch && touch.device!=null)canceled.Remove((touch.device.deviceId,touch.touchId));
            if(presses.Count==0)active.Remove(this);
        }
        public static void CancelTouch(int deviceId,int touchId)
        {
            // 同一Input更新内の押下→取消は、EventSystemのPointerDownより先に届くことがある。
            foreach(var key in canceled.Where(v=>v.Value<Time.frameCount-1).Select(v=>v.Key).ToArray())canceled.Remove(key);
            canceled[(deviceId,touchId)]=Time.frameCount;
            foreach(var guard in active.ToArray()) foreach(var data in guard.presses.Values)
                if(data is UnityEngine.InputSystem.UI.ExtendedPointerEventData touch && touch.device?.deviceId==deviceId && touch.touchId==touchId)
                    data.eligibleForClick=false;
        }
        public static void CancelAll()
        {
            foreach(var guard in active.ToArray())foreach(var data in guard.presses.Values)data.eligibleForClick=false;
        }
        private void OnDisable()
        {
            foreach(var data in presses.Values)data.eligibleForClick=false;presses.Clear();active.Remove(this);
        }
    }

    /// <summary>
    /// UnityEngine.Inputを使用した入力プロバイダー
    /// </summary>
    public class UnityInputProvider : IInputProvider, System.IDisposable
    {
        private readonly List<TimestampedLogicalInput> events = new List<TimestampedLogicalInput>();
        private readonly Dictionary<ButtonControl, bool> buttons = new Dictionary<ButtonControl, bool>();
        private readonly Dictionary<InputDevice, UnityEngine.Vector2> sticks = new Dictionary<InputDevice, UnityEngine.Vector2>();
        private Olca.Core.Logic.Integration.LogicalInput eventState = new Olca.Core.Logic.Integration.LogicalInput();
        private readonly Olca.Core.Logic.Integration.TouchMovementState touchMovement = new Olca.Core.Logic.Integration.TouchMovementState();
        private readonly HashSet<long> physicalTouches = new HashSet<long>();
        private readonly HashSet<long> blockedTouches = new HashSet<long>();
        private bool touchMovementEnabled;
        public bool SetTouchMovementEnabled(bool enabled)
        {
            if(touchMovementEnabled==enabled) return false;
            touchMovementEnabled=enabled; QueueSnapshot(); return true;
        }
        private static long TouchKey(InputDevice device,int id) => ((long)device.deviceId<<32)|(uint)id;
        private static bool IsTouchOverUi(UnityEngine.Vector2 position)
        {
            var system=UnityEngine.EventSystems.EventSystem.current;
            if(system==null) return false;
            var hits=new List<UnityEngine.EventSystems.RaycastResult>();
            system.RaycastAll(new UnityEngine.EventSystems.PointerEventData(system){position=position},hits);
            if(hits.Count>0) {
                var button=hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>();
                if(button!=null&&button.GetComponent<TouchUiCancelGuard>()==null)button.gameObject.AddComponent<TouchUiCancelGuard>();
            }
            return hits.Count>0;
        }
        private void SnapshotTouches()
        {
            TouchUiCancelGuard.CancelAll();
            touchMovement.Clear(); physicalTouches.Clear(); blockedTouches.Clear();
            foreach(var screen in InputSystem.devices.OfType<Touchscreen>().Where(screen=>screen.enabled))
                foreach(var touch in screen.touches) if(touch.press.isPressed) {
                    long id=TouchKey(screen,touch.touchId.ReadValue()); physicalTouches.Add(id); blockedTouches.Add(id);
                    TouchUiCancelGuard.CancelTouch(screen.deviceId,touch.touchId.ReadValue());
                }
        }
        private void CaptureTouches(InputEventPtr evt,Touchscreen screen,out float down,out bool canceled)
        {
            down=0;canceled=false;
            var seen=new HashSet<int>();
            // 単指TouchStateイベントはprimaryTouchにだけマップされる。全画面状態も同じ入口で読む。
            foreach(var touch in screen.touches.Prepend(screen.primaryTouch)) {
                if(!touch.touchId.ReadValueFromEvent(evt,out int finger)||finger<=0||!seen.Add(finger)||
                    !touch.phase.ReadValueFromEvent(evt,out var phase)) continue;
                long id=TouchKey(screen,finger); int previous=touchMovement.Direction;
                if(phase==UnityEngine.InputSystem.TouchPhase.Began) {
                    TouchUiCancelGuard.BeginTouch(screen.deviceId,finger);
                    physicalTouches.Add(id);
                    if(!blockedTouches.Contains(id)) {
                        touch.position.ReadValueFromEvent(evt,out var position);
                        bool overUi=IsTouchOverUi(position);
                        if(touchMovementEnabled&&!overUi) touchMovement.Press(id,position.x<Screen.width*.5f?-1:1);
                        else blockedTouches.Add(id);
                    }
                } else if(phase==UnityEngine.InputSystem.TouchPhase.Ended||phase==UnityEngine.InputSystem.TouchPhase.Canceled) {
                    if(phase==UnityEngine.InputSystem.TouchPhase.Canceled)TouchUiCancelGuard.CancelTouch(screen.deviceId,finger);
                    canceled|=phase==UnityEngine.InputSystem.TouchPhase.Canceled && touchMovement.Contains(id);
                    physicalTouches.Remove(id);blockedTouches.Remove(id);touchMovement.Release(id);
                }
                if(touchMovement.Direction!=previous&&touchMovement.Direction!=0) down=touchMovement.Direction;
            }
        }
        private bool capturing;
        private double eventClockOffset;
        private long pointerReleaseTimestamp;
        private int pointerReleaseRenderFrame = -1;
        // UIのクリック通知はUpdate中に届く。物理イベントがないプログラム操作は呼出時刻を使う。
        public long PointerClickTimestamp => pointerReleaseRenderFrame == Time.frameCount
            ? pointerReleaseTimestamp : System.Diagnostics.Stopwatch.GetTimestamp();
        public Olca.Core.Domain.InputBindingProfile KeyboardProfile { get; private set; } = CreateDefaultKeyboardProfile();
        public static Olca.Core.Domain.InputBindingProfile CreateDefaultKeyboardProfile()
        {
            var slots = new Dictionary<(string Device, string Context, string Action), string>();
            void Add(string context, string action, Key key) => slots.Add(("keyboard", context, action), key.ToString());
            Add("exploration", "JumpPrimary", Key.Space);
            Add("exploration", "DashPrimary", Key.LeftShift); Add("exploration", "DashSecondary", Key.RightShift);
            Add("battle", "LeftPrimary", Key.LeftArrow); Add("battle", "LeftSecondary", Key.A);
            Add("battle", "RightPrimary", Key.RightArrow); Add("battle", "RightSecondary", Key.D);
            Add("battle", "UpPrimary", Key.UpArrow); Add("battle", "UpSecondary", Key.W);
            Add("battle", "DownPrimary", Key.DownArrow); Add("battle", "DownSecondary", Key.S);
            Add("battle", "AttackPrimary", Key.Z); Add("battle", "CardPrimary", Key.Space);
            Add("battle", "CustomPrimary", Key.LeftAlt); Add("battle", "CustomSecondary", Key.RightAlt);
            return new Olca.Core.Domain.InputBindingProfile(slots, new (string, string, string)[0]);
        }
        // 設定画面専用の単一キー取得。ゲーム入力や論理時計へは流さない。
        public static (int HeldCount, string Key) CaptureBindingKey()
        {
            int held = 0; string key = null;
            if (Keyboard.current != null)
                foreach (var control in Keyboard.current.allKeys)
                {
                    if (control.isPressed) { held++; key = control.keyCode.ToString(); }
                }
            return (held, key);
        }

        // メニューの必須操作は対象外。既知の個別スロットだけをまとめて検証してから反映する。
        public bool TryApplyKeyboardProfile(Olca.Core.Domain.InputBindingProfile profile, out string error)
        {
            error = null;
            if (!GameplayPause.IsPaused) { error = "ユーザーポーズ中のみ適用できます"; return false; }
            if (!ValidateKeyboardProfile(profile, out error)) return false;
            KeyboardProfile = profile;
            events.Clear(); QueueSnapshot();
            return true;
        }
        public static bool ValidateKeyboardProfile(Olca.Core.Domain.InputBindingProfile profile, out string error)
        {
            error = null;
            var defaults = CreateDefaultKeyboardProfile().Bindings;
            if (profile == null || profile.Bindings.Count != defaults.Count || profile.Bindings.Keys.Any(k => !defaults.ContainsKey(k)))
            { error = "キーボードのゲーム操作スロットが一致しません"; return false; }
            foreach (var value in profile.Bindings.Values)
                if (value != null && (!System.Enum.TryParse<Key>(value, out var key) || key == Key.None ||
                    !System.Enum.IsDefined(typeof(Key), key) || key.ToString() != value))
                { error = "キー識別子が不正です"; return false; }
            return true;
        }
        public bool TryRestoreKeyboardProfile(Olca.Core.Domain.InputBindingProfile profile, out string error)
        {
            if (capturing) { error = "入力開始後は保存・適用を使用してください"; return false; }
            if (!ValidateKeyboardProfile(profile, out error)) return false;
            KeyboardProfile = profile; return true;
        }
        private bool Held(string context, string action)
        {
            var value = KeyboardProfile.Bindings[("keyboard", context, action)];
            return value != null && KeyHeld(System.Enum.Parse<Key>(value));
        }
        public void StartEventCapture()
        {
            if (capturing) return;
            eventClockOffset = System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency - Time.realtimeSinceStartupAsDouble;
            capturing = true; InputSystem.onEvent += CaptureEvent; InputSystem.onDeviceChange += DeviceChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged += EditorPauseChanged;
#endif
            QueueSnapshot();
        }
        public void Dispose()
        {
            if (!capturing) return;
            InputSystem.onEvent -= CaptureEvent; InputSystem.onDeviceChange -= DeviceChanged;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= EditorPauseChanged;
#endif
            capturing = false; events.Clear(); touchMovement.Clear(); physicalTouches.Clear(); blockedTouches.Clear();
        }
#if UNITY_EDITOR
        private void EditorPauseChanged(UnityEditor.PauseState state)
        { if (state == UnityEditor.PauseState.Unpaused) QueueSnapshot(); }
#endif
        public TimestampedLogicalInput[] CaptureTimestamped()
        { var result = events.ToArray(); events.Clear(); return result; }
        // スティック方向の合成ButtonControlは旧Input.anyKeyの物理ボタンに含めない。
        private static IEnumerable<ButtonControl> Buttons(InputDevice device)
            => device is Touchscreen ? System.Array.Empty<ButtonControl>() : device.allControls.OfType<ButtonControl>().Where(button => !(button.parent is StickControl));
        private static StickControl Stick(InputDevice device) => device is Gamepad pad ? pad.leftStick : (device as Joystick)?.stick;
        public void QueueSnapshot()
        {
            SnapshotTouches(); buttons.Clear(); sticks.Clear();
            foreach (var device in InputSystem.devices)
            {
                foreach (var button in Buttons(device)) buttons[button] = button.isPressed;
                var stick = Stick(device); if (stick != null) sticks[device] = stick.ReadUnprocessedValue();
            }
            eventState = BuildState();
            events.Add(new TimestampedLogicalInput(System.Diagnostics.Stopwatch.GetTimestamp(), eventState.Copy()));
        }
        private void DeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected ||
                change == InputDeviceChange.Disabled || change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                QueueSnapshot();
        }
        private bool KeyHeld(Key key) => buttons.Any(pair => pair.Value && pair.Key is KeyControl control && control.keyCode == key);
        private Olca.Core.Logic.Integration.LogicalInput BuildState()
        {
            float x = (Held("battle", "RightPrimary") || Held("battle", "RightSecondary") ? 1 : 0) - (Held("battle", "LeftPrimary") || Held("battle", "LeftSecondary") ? 1 : 0);
            float y = (Held("battle", "UpPrimary") || Held("battle", "UpSecondary") ? 1 : 0) - (Held("battle", "DownPrimary") || Held("battle", "DownSecondary") ? 1 : 0);
            foreach (var stick in sticks.Values)
            {
                // 既存InputManagerの軸0/1・dead=0.19と同じ範囲。キーは変更しない。
                if (Mathf.Abs(stick.x) >= .19f && Mathf.Abs(stick.x) > Mathf.Abs(x)) x = stick.x;
                if (Mathf.Abs(stick.y) >= .19f && Mathf.Abs(stick.y) > Mathf.Abs(y)) y = stick.y;
            }
            return new Olca.Core.Logic.Integration.LogicalInput {
                HasInputSample = true, AnyKey = physicalTouches.Count>0 || buttons.Values.Any(pressed => pressed),
                Horizontal = touchMovement.Direction!=0 ? touchMovement.Direction : x, Vertical = y, BasicAttackButton = Held("battle", "AttackPrimary"),
                DashButton = Held("exploration", "DashPrimary") || Held("exploration", "DashSecondary")
            };
        }
        private void CaptureEvent(InputEventPtr evt, InputDevice device)
        {
            if (!evt.IsA<StateEvent>() && !evt.IsA<DeltaStateEvent>()) return;
            bool enter = KeyHeld(Key.Enter), numpadEnter = KeyHeld(Key.NumpadEnter), space = KeyHeld(Key.Space), escape = KeyHeld(Key.Escape);
            var pointer = device as Pointer;
            bool pointerWasPressed = pointer != null && buttons.TryGetValue(pointer.press, out var pressed) && pressed;
            bool basic = Held("battle", "AttackPrimary");
            bool jump = Held("exploration", "JumpPrimary"), card = Held("battle", "CardPrimary"), leftShift = Held("exploration", "DashPrimary"), rightShift = Held("exploration", "DashSecondary");
            bool leftAlt = Held("battle", "CustomPrimary"), rightAlt = Held("battle", "CustomSecondary");
            foreach (var button in Buttons(device))
                if (button.ReadValueFromEvent(evt, out float value)) buttons[button] = value >= button.pressPointOrDefault;
            var stick = Stick(device);
            if (stick != null)
            {
                sticks.TryGetValue(device, out var position);
                if (stick.x.ReadUnprocessedValueFromEvent(evt, out float x)) position.x = x;
                if (stick.y.ReadUnprocessedValueFromEvent(evt, out float y)) position.y = y;
                sticks[device] = position;
            }
            int previousTouchCount=physicalTouches.Count;
            float touchDown=0; bool touchCanceled=false;
            if(device is Touchscreen screen) CaptureTouches(evt,screen,out touchDown,out touchCanceled);
            var next = BuildState();
            // タッチの短い押下/取消はキーボードの立ち上がりと独立に併合する。
            next.TouchHorizontalDown=touchDown; next.CancelTouchTap=touchCanceled;
            next.HorizontalDown = device is Touchscreen ? 0 : eventState.Horizontal == 0 ? next.Horizontal : 0;
            next.JumpButtonDown = !jump && Held("exploration", "JumpPrimary");
            next.CardUseButtonDown = !card && Held("battle", "CardPrimary");
            next.BasicAttackButtonDown = !basic && Held("battle", "AttackPrimary");
            next.BasicAttackButtonUp = basic && !Held("battle", "AttackPrimary");
            next.DashButtonDown = (!leftShift && Held("exploration", "DashPrimary")) || (!rightShift && Held("exploration", "DashSecondary"));
            next.CustomScreenButtonDown = (!leftAlt && Held("battle", "CustomPrimary")) || (!rightAlt && Held("battle", "CustomSecondary"));
            long timestamp = (long)System.Math.Round((evt.time + eventClockOffset) * System.Diagnostics.Stopwatch.Frequency);
            next.MenuConfirmButtonDown = (!enter && KeyHeld(Key.Enter)) || (!numpadEnter && KeyHeld(Key.NumpadEnter));
            next.MenuDecideButtonDown = !space && KeyHeld(Key.Space);
            next.MenuCancelButtonDown = !escape && KeyHeld(Key.Escape);
            if ((device is Touchscreen && physicalTouches.Count < previousTouchCount) || (pointerWasPressed && buttons.TryGetValue(pointer.press, out var pointerNow) && !pointerNow))
            { pointerReleaseTimestamp = timestamp; pointerReleaseRenderFrame = Time.frameCount; }
            events.Add(new TimestampedLogicalInput(timestamp, next)); eventState = next;
        }
        private float basicAttackButtonPressTime;
        private bool wasBasicAttackButtonPressed;
        private float lastHorizontal;

        public float Horizontal => Input.GetAxisRaw("Horizontal");

        public float HorizontalDown
        {
            get
            {
                float current = Input.GetAxisRaw("Horizontal");
                // 前フレームが0で、今フレームが0でない場合に立ち上がり
                if (lastHorizontal == 0f && current != 0f)
                {
                    return current;
                }
                return 0f;
            }
        }

        public float Vertical => Input.GetAxisRaw("Vertical");

        public bool JumpButtonDown => Input.GetKeyDown(KeyCode.Space);

        public bool DashButton => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        public bool CardUseButtonDown => Input.GetKeyDown(KeyCode.X);

        public bool BasicAttackButton => Input.GetKey(KeyCode.Z);

        public float BasicAttackButtonHoldTime => basicAttackButtonPressTime;

        public bool CustomScreenButtonDown => Input.GetKeyDown(KeyCode.C);

        public Olca.Core.Logic.Integration.LogicalInput CaptureLogical()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            var value = new Olca.Core.Logic.Integration.LogicalInput {
                Horizontal = horizontal, HorizontalDown = lastHorizontal == 0 ? horizontal : 0,
                Vertical = Input.GetAxisRaw("Vertical"), JumpButtonDown = Input.GetKeyDown(KeyCode.Space),
                CardUseButtonDown = Input.GetKeyDown(KeyCode.Space), BasicAttackButton = Input.GetKey(KeyCode.Z),
                DashButton = DashButton,
                DashButtonDown = Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift),
                CustomScreenButtonDown = Input.GetKeyDown(KeyCode.LeftAlt) || Input.GetKeyDown(KeyCode.RightAlt)
            };
            lastHorizontal = horizontal;
            return value;
        }

        /// <summary>
        /// 毎フレーム呼び出して状態を更新
        /// </summary>
        public void Update()
        {
            // 固有攻撃ボタンの長押し時間を計測
            if (BasicAttackButton)
            {
                if (!wasBasicAttackButtonPressed)
                {
                    // ボタンが押された瞬間
                    basicAttackButtonPressTime = 0f;
                    wasBasicAttackButtonPressed = true;
                }
                else
                {
                    // 押され続けている
                    basicAttackButtonPressTime += UnityEngine.Time.deltaTime;
                }
            }
            else
            {
                // ボタンが離された
                basicAttackButtonPressTime = 0f;
                wasBasicAttackButtonPressed = false;
            }

            // 前フレームの横方向入力を記録（HorizontalDownの立ち上がり検出用）
            lastHorizontal = Input.GetAxisRaw("Horizontal");
        }
    }
    /// <summary>表示・非同期演出と入力のポーズ境界。Coreの停止理由とは独立して扱う。</summary>
    public static class GameplayPause
    {
        public static bool IsPaused { get; private set; }
        private static bool awaitRelease;
        private static int resumeFrames;
        public static bool InputBlocked => IsPaused || awaitRelease || resumeFrames > 0;
        public static float DeltaTime => IsPaused ? 0f : Time.deltaTime;
        public static void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            if (!paused) { awaitRelease = true; resumeFrames = 2; }
        }
        public static void UpdateInputBarrier() => UpdateInputBarrier(Input.anyKey);
        public static void UpdateInputBarrier(bool anyKey)
        {
            if (!IsPaused && awaitRelease && !anyKey)
            { awaitRelease = false; resumeFrames = 2; }
        }
        public static void AdvanceLogicalFrame() { if (!IsPaused && !awaitRelease && resumeFrames > 0) resumeFrames--; }
        public static async Cysharp.Threading.Tasks.UniTask WaitAsync(System.Threading.CancellationToken ct = default)
        {
            while (IsPaused) await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, ct);
            ct.ThrowIfCancellationRequested();
        }
        public static async Cysharp.Threading.Tasks.UniTask NextFrame(System.Threading.CancellationToken ct = default)
        {
            await Cysharp.Threading.Tasks.UniTask.Yield(Cysharp.Threading.Tasks.PlayerLoopTiming.Update, ct);
            await WaitAsync(ct);
        }
        public static async Cysharp.Threading.Tasks.UniTask Delay(int milliseconds, System.Threading.CancellationToken cancellationToken = default)
        {
            await WaitAsync(cancellationToken);
            float remaining = milliseconds / 1000f;
            while (remaining > 0f) { await NextFrame(cancellationToken); remaining -= DeltaTime; }
        }
        public static Cysharp.Threading.Tasks.UniTask Delay(System.TimeSpan duration, System.Threading.CancellationToken cancellationToken = default)
            => Delay((int)duration.TotalMilliseconds, cancellationToken);
    }
}

namespace Olca.Unity
{
    /// <summary>ゲーム保存と独立したキーボード設定。未知版・破損を上書きしない。</summary>
    public sealed class KeyboardBindingFileStore
    {
        [System.Runtime.Serialization.DataContract]
        private sealed class Document
        {
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public string Device;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public Slot[] Slots;
        }
        [System.Runtime.Serialization.DataContract]
        private sealed class Slot
        {
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public string Context;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public string Action;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public string Key;
        }
        private readonly CheckpointFileIo io;
        public string DirectoryPath { get; }
        public string ActivePath => System.IO.Path.Combine(DirectoryPath,"keyboard-gameplay.json");
        private string BackupPath => System.IO.Path.Combine(DirectoryPath,"keyboard-gameplay.backup.json");
        public KeyboardBindingFileStore(string directory, CheckpointFileIo io=null)
        {
            DirectoryPath=System.IO.Path.GetFullPath(directory ?? throw new System.ArgumentNullException(nameof(directory)));
            this.io=io ?? new CheckpointFileIo();
        }
        private static byte[] Encode(Olca.Core.Domain.InputBindingProfile profile)
        {
            var doc=new Document{Version=1,Device="keyboard",Slots=profile.Bindings.OrderBy(p=>p.Key.Context,System.StringComparer.Ordinal)
                .ThenBy(p=>p.Key.Action,System.StringComparer.Ordinal).Select(p=>new Slot{Context=p.Key.Context,Action=p.Key.Action,Key=p.Value}).ToArray()};
            using(var stream=new System.IO.MemoryStream()){
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).WriteObject(stream,doc);return stream.ToArray();}
        }
        private static Olca.Core.Domain.InputBindingProfile Decode(byte[] bytes)
        {
            if(bytes.Length==0 || bytes.Length>65536)throw new System.IO.InvalidDataException();
            Document doc;using(var stream=new System.IO.MemoryStream(bytes))
                doc=(Document)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
            if(doc==null || doc.Version!=1 || doc.Device!="keyboard" || doc.Slots==null)throw new System.IO.InvalidDataException();
            var slots=new Dictionary<(string Device,string Context,string Action),string>();
            foreach(var slot in doc.Slots){if(slot==null)throw new System.IO.InvalidDataException();slots.Add(("keyboard",slot.Context,slot.Action),slot.Key);}
            var profile=new Olca.Core.Domain.InputBindingProfile(slots,new (string,string,string)[0]);
            if(!UnityInputProvider.ValidateKeyboardProfile(profile,out _))throw new System.IO.InvalidDataException();
            return profile;
        }
        private static bool Failure(System.Exception e) => e is System.IO.IOException || e is System.IO.InvalidDataException || e is System.UnauthorizedAccessException ||
            e is System.Runtime.Serialization.SerializationException || e is System.Xml.XmlException || e is System.ArgumentException || e is System.Security.SecurityException;
        public bool TryRead(out Olca.Core.Domain.InputBindingProfile profile,out string error)
        {
            profile=null;error=null;
            try{
                if(!System.IO.File.Exists(ActivePath)){
                    if(System.IO.Directory.Exists(ActivePath) || System.IO.File.Exists(BackupPath) || System.IO.Directory.Exists(BackupPath))throw new System.IO.InvalidDataException();
                    profile=UnityInputProvider.CreateDefaultKeyboardProfile();return true;}
                profile=Decode(System.IO.File.ReadAllBytes(ActivePath));return true;
            }catch(System.Exception e)when(Failure(e)){error="キー設定を読み込めません。保存済みデータは変更していません。";return false;}
        }
        private bool writing;
        public bool TrySave(Olca.Core.Domain.InputBindingProfile profile,out string error)
        {
            error=null;
            if(writing){error="キー設定を保存中です";return false;}
            if(!UnityInputProvider.ValidateKeyboardProfile(profile,out error))return false;
            if(!TryRead(out _,out error))return false;
            var bytes=Encode(profile);string temporary=null;writing=true;
            try{
                System.IO.Directory.CreateDirectory(DirectoryPath);
                temporary=System.IO.Path.Combine(DirectoryPath,".keyboard-"+System.Guid.NewGuid().ToString("N")+".tmp");
                io.WriteAndFlush(temporary,bytes);Decode(System.IO.File.ReadAllBytes(temporary));
                if(System.IO.File.Exists(ActivePath))io.Replace(temporary,ActivePath,BackupPath);else io.MoveNew(temporary,ActivePath);
                if(!System.IO.File.ReadAllBytes(ActivePath).SequenceEqual(bytes))throw new System.IO.IOException();
                return true;
            }catch(System.Exception e)when(Failure(e)){
                // OSが置換後に失敗を返した場合は、確定済み内容を照合して二重保存を避ける。
                try{if(System.IO.File.Exists(ActivePath) && System.IO.File.ReadAllBytes(ActivePath).SequenceEqual(bytes))return true;}catch(System.Exception read)when(Failure(read)){}
                error="キー設定を保存できません。現在の割当を維持します。";return false;
            }finally{
                writing=false;
                if(temporary!=null)try{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}catch(System.Exception e)when(Failure(e)){}
            }
        }
    }
    /// <summary>変身演出だけの独立設定。攻略の保存・初期化と混ぜない。</summary>
    public sealed class TransformationPresentationFileStore
    {
        [System.Runtime.Serialization.DataContract] private sealed class Document {
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public bool Short;
        }
        private readonly string directory;
        private readonly CheckpointFileIo io;
        private bool writing;
        public string ActivePath => System.IO.Path.Combine(directory,"transformation-presentation.json");
        private string BackupPath => ActivePath+".backup";
        public TransformationPresentationFileStore(string directory,CheckpointFileIo io=null)
        {this.directory=System.IO.Path.GetFullPath(directory);this.io=io??new CheckpointFileIo();}
        private static byte[] Encode(bool shortened){using(var stream=new System.IO.MemoryStream()){
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).WriteObject(stream,new Document{Version=1,Short=shortened});return stream.ToArray();}}
        private static bool Decode(byte[] bytes){if(bytes.Length==0||bytes.Length>4096)throw new System.IO.InvalidDataException();
            using(var stream=new System.IO.MemoryStream(bytes)){var doc=(Document)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
                if(doc==null||doc.Version!=1)throw new System.IO.InvalidDataException();return doc.Short;}}
        private static bool Failure(System.Exception e)=>e is System.IO.IOException||e is System.UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException||e is System.Xml.XmlException||e is System.ArgumentException||e is System.Security.SecurityException;
        public bool TryRead(out bool shortened,out string error){shortened=false;error=null;try{
            if(!System.IO.File.Exists(ActivePath)){if(System.IO.Directory.Exists(ActivePath)||System.IO.File.Exists(BackupPath))throw new System.IO.IOException();return true;}
            shortened=Decode(System.IO.File.ReadAllBytes(ActivePath));return true;
        }catch(System.Exception e)when(Failure(e)){error="変身演出の設定を読めません。保存内容は変更していません。";return false;}}
        public bool TrySave(bool shortened,out string error){error=null;if(writing){error="演出設定を保存中です";return false;}if(!TryRead(out _,out error))return false;
            var bytes=Encode(shortened);string temporary=null;writing=true;try{
                System.IO.Directory.CreateDirectory(directory);temporary=System.IO.Path.Combine(directory,".form-"+System.Guid.NewGuid().ToString("N")+".tmp");
                io.WriteAndFlush(temporary,bytes);Decode(System.IO.File.ReadAllBytes(temporary));
                if(System.IO.File.Exists(ActivePath))io.Replace(temporary,ActivePath,BackupPath);else io.MoveNew(temporary,ActivePath);
                if(!System.IO.File.ReadAllBytes(ActivePath).SequenceEqual(bytes))throw new System.IO.IOException();return true;
            }catch(System.Exception e)when(Failure(e)){
                try{if(System.IO.File.Exists(ActivePath)&&System.IO.File.ReadAllBytes(ActivePath).SequenceEqual(bytes))return true;}catch(System.Exception read)when(Failure(read)){}
                error="演出設定を保存できません。現在の設定を維持します。";return false;
            }finally{writing=false;if(temporary!=null)try{if(System.IO.File.Exists(temporary))System.IO.File.Delete(temporary);}catch(System.Exception e)when(Failure(e)){} }
        }
    }
}
