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
        public static bool IsTouchPointer(UnityEngine.EventSystems.PointerEventData data)=>
            data is UnityEngine.InputSystem.UI.ExtendedPointerEventData touch && touch.device is Touchscreen;
        public static int PressedTouchCount()
        {
            int count=0;
            foreach(var device in InputSystem.devices)if(device is Touchscreen screen)
                foreach(var touch in screen.touches)if(touch.press.isPressed)count++;
            return count;
        }
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
        private readonly HashSet<InputDevice> blockedGamepads=new HashSet<InputDevice>();
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
        public static readonly GamepadButton[] BindingButtons = { GamepadButton.DpadUp,GamepadButton.DpadDown,GamepadButton.DpadLeft,GamepadButton.DpadRight,GamepadButton.North,GamepadButton.East,GamepadButton.South,GamepadButton.West,GamepadButton.LeftStick,GamepadButton.RightStick,GamepadButton.LeftShoulder,GamepadButton.RightShoulder,GamepadButton.Start,GamepadButton.Select,GamepadButton.LeftTrigger,GamepadButton.RightTrigger };
        // 列挙型の同値別名（A/X等）は実行系依存なので、保存IDは位置名に固定する。
        public static string GamepadButtonId(GamepadButton key){switch(key){case GamepadButton.South:return "South";case GamepadButton.North:return "North";case GamepadButton.East:return "East";case GamepadButton.West:return "West";default:return key.ToString();}}
        public static Olca.Core.Domain.InputBindingProfile ActiveGamepadProfile { get; private set; } = CreateDefaultGamepadProfile();
        public static int GamepadProfileVersion { get; private set; }
        public Olca.Core.Domain.InputBindingProfile GamepadProfile { get; private set; } = CreateDefaultGamepadProfile();
        public static IEnumerable<(string,string,string)> RequiredGamepadSlots() => CreateDefaultGamepadSlots().Keys.Where(k=>k.Item3=="Pause"||((k.Item2=="menu"||k.Item2=="custom")&&(new[]{"Left","Right","Up","Down","Decide","Cancel","Confirm"}).Contains(k.Item3)));
        private static Dictionary<(string,string,string),string> CreateDefaultGamepadSlots()
        {
            var slots=new Dictionary<(string,string,string),string>();
            void Add(string context,string action,GamepadButton key)=>slots.Add(("gamepad",context,action),GamepadButtonId(key));
            Add("exploration","JumpPrimary",GamepadButton.South);Add("exploration","DashPrimary",GamepadButton.LeftStick);Add("exploration","Pause",GamepadButton.Start);
            Add("battle","LeftPrimary",GamepadButton.DpadLeft);Add("battle","RightPrimary",GamepadButton.DpadRight);Add("battle","UpPrimary",GamepadButton.DpadUp);Add("battle","DownPrimary",GamepadButton.DpadDown);
            Add("battle","AttackPrimary",GamepadButton.West);Add("battle","CardPrimary",GamepadButton.RightShoulder);Add("battle","CustomPrimary",GamepadButton.North);Add("battle","Pause",GamepadButton.Start);
            foreach(string context in new[]{"menu","custom"}) {
                Add(context,"Left",GamepadButton.DpadLeft);Add(context,"Right",GamepadButton.DpadRight);Add(context,"Up",GamepadButton.DpadUp);Add(context,"Down",GamepadButton.DpadDown);
                Add(context,"Decide",GamepadButton.South);Add(context,"Cancel",GamepadButton.East);Add(context,"Confirm",GamepadButton.Start);Add(context,"Customize",GamepadButton.North);Add(context,"Undo",GamepadButton.West);Add(context,"Details",GamepadButton.RightStick);Add(context,"Transform",GamepadButton.Select);Add(context,"PageUp",GamepadButton.LeftShoulder);Add(context,"PageDown",GamepadButton.RightShoulder);
            }
            return slots;
        }
        public static Olca.Core.Domain.InputBindingProfile CreateDefaultGamepadProfile()=>new Olca.Core.Domain.InputBindingProfile(CreateDefaultGamepadSlots(),RequiredGamepadSlots());
        public static bool ValidateGamepadProfile(Olca.Core.Domain.InputBindingProfile profile,out string error)
        {
            error=null;var defaults=CreateDefaultGamepadSlots();
            if(profile==null||profile.Bindings.Count!=defaults.Count||profile.Bindings.Keys.Any(k=>!defaults.ContainsKey(k))||profile.Bindings.Values.Any(v=>v!=null&&!BindingButtons.Any(b=>GamepadButtonId(b)==v))) {error="パッド操作スロットまたはボタンが不正です";return false;}
            try {new Olca.Core.Domain.InputBindingProfile(profile.Bindings,RequiredGamepadSlots());return true;}catch(System.ArgumentException e){error=e.Message;return false;}
        }
        private void ApplyGamepad(Olca.Core.Domain.InputBindingProfile profile){GamepadProfile=profile;ActiveGamepadProfile=profile;GamepadProfileVersion++;gamepadPausePending=false;}
        public bool TryRestoreGamepadProfile(Olca.Core.Domain.InputBindingProfile profile,out string error){if(capturing){error="入力開始後は保存・適用を使用してください";return false;}if(!ValidateGamepadProfile(profile,out error))return false;ApplyGamepad(profile);return true;}
        public bool TryApplyGamepadProfile(Olca.Core.Domain.InputBindingProfile profile,out string error){if(!GameplayPause.IsPaused){error="ユーザーポーズ中のみ適用できます";return false;}if(!ValidateGamepadProfile(profile,out error))return false;ApplyGamepad(profile);events.Clear();QueueSnapshot();return true;}
        public static string GamepadBindingLabel(string value){if(value==null)return "未割当";switch(value){case "LeftShoulder":return "LB";case "RightShoulder":return "RB";case "LeftTrigger":return "LT";case "RightTrigger":return "RT";case "LeftStick":return "L3";case "RightStick":return "R3";case "DpadLeft":return "十字左";case "DpadRight":return "十字右";case "DpadUp":return "十字上";case "DpadDown":return "十字下";default:return value;}}
        public static string GamepadTopology()=>string.Join(",",InputSystem.devices.OfType<Gamepad>().Where(d=>d.enabled).Select(d=>d.deviceId));
        public static (int HeldCount,string Key) CaptureGamepadBinding(){int count=0;string key=null;foreach(var pad in InputSystem.devices.OfType<Gamepad>().Where(p=>p.enabled)){foreach(var b in BindingButtons)if(pad[b].isPressed){count++;key=GamepadButtonId(b);}if(pad.leftStick.ReadValue().sqrMagnitude>.04f||pad.rightStick.ReadValue().sqrMagnitude>.04f)count+=2;}return(count,key);}
        public System.Func<string> GamepadContext {get;set;}
        public System.Func<bool> GamepadPauseAvailable {get;set;}
        private bool gamepadPausePending;
        public bool ConsumeGamepadPause(){bool value=gamepadPausePending;gamepadPausePending=false;return value;}
        private bool PadActionHeld(string context,string action)=>GamepadProfile.Bindings.TryGetValue(("gamepad",context,action),out var key)&&key!=null&&PadHeld(System.Enum.Parse<GamepadButton>(key));
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
        // 既存Input ActionsのWest攻撃・Southジャンプ・L押込Sprintを優先した仮割当。
        public System.Func<bool> GamepadGameplayActive { get; set; }
        private bool PadHeld(GamepadButton key)=>buttons.Any(pair=>pair.Value&&pair.Key.device is Gamepad pad&&pad.enabled&&!blockedGamepads.Contains(pad)&&ReferenceEquals(pair.Key,pad[key]));
        private bool ActionHeld(string context,string action)
        {
            if(Held(context,action))return true;
            return PadActionHeld(context,action);
        }
        private bool PadNeutral(InputDevice device)=>!buttons.Any(p=>p.Key.device==device&&p.Value)&&(!sticks.TryGetValue(device,out var stick)||stick.sqrMagnitude<.19f*.19f);
        public void StartEventCapture()
        {
            InputGuideActivity.EnsureInitialized();
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
            capturing = false; blockedGamepads.Clear(); events.Clear(); touchMovement.Clear(); physicalTouches.Clear(); blockedTouches.Clear();
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
            gamepadPausePending=false;
            SnapshotTouches(); blockedGamepads.RemoveWhere(d=>!InputSystem.devices.Contains(d));buttons.Clear(); sticks.Clear();
            foreach (var device in InputSystem.devices.Where(d=>d.enabled))
            {
                foreach (var button in Buttons(device)) buttons[button] = button.isPressed;
                var stick = Stick(device); if (stick != null) sticks[device] = stick.ReadUnprocessedValue();
            }
            foreach(var pad in InputSystem.devices.OfType<Gamepad>()) {if(!pad.enabled||!PadNeutral(pad))blockedGamepads.Add(pad);else blockedGamepads.Remove(pad);}
            eventState = BuildState();
            events.Add(new TimestampedLogicalInput(System.Diagnostics.Stopwatch.GetTimestamp(), eventState.Copy()));
        }
        public event System.Action OnGamepadDisconnected;
        private void DeviceChanged(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected ||
                change == InputDeviceChange.Disabled || change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                QueueSnapshot();
            // Disabledはフォーカス喪失でも届くため、物理切断の通知と区別する。
            if (device is Gamepad && (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected))
                OnGamepadDisconnected?.Invoke();
        }
        private bool KeyHeld(Key key) => buttons.Any(pair => pair.Value && pair.Key is KeyControl control && control.keyCode == key);
        private Olca.Core.Logic.Integration.LogicalInput BuildState()
        {
            float x = (Held("battle", "RightPrimary") || Held("battle", "RightSecondary") ? 1 : 0) - (Held("battle", "LeftPrimary") || Held("battle", "LeftSecondary") ? 1 : 0);
            float y = (Held("battle", "UpPrimary") || Held("battle", "UpSecondary") ? 1 : 0) - (Held("battle", "DownPrimary") || Held("battle", "DownSecondary") ? 1 : 0);
            x += (PadActionHeld("battle","RightPrimary")?1:0)-(PadActionHeld("battle","LeftPrimary")?1:0);
            y += (PadActionHeld("battle","UpPrimary")?1:0)-(PadActionHeld("battle","DownPrimary")?1:0);
            x=Mathf.Clamp(x,-1,1);y=Mathf.Clamp(y,-1,1);
            foreach (var sample in sticks.Where(p=>p.Key.enabled&&!blockedGamepads.Contains(p.Key)))
            {
                var stick=sample.Value;
                // 既存InputManagerの軸0/1・dead=0.19と同じ範囲。キーは変更しない。
                if (Mathf.Abs(stick.x) >= .19f && Mathf.Abs(stick.x) > Mathf.Abs(x)) x = stick.x;
                if (Mathf.Abs(stick.y) >= .19f && Mathf.Abs(stick.y) > Mathf.Abs(y)) y = stick.y;
            }
            return new Olca.Core.Logic.Integration.LogicalInput {
                HasInputSample = true, AnyKey = physicalTouches.Count>0 || buttons.Values.Any(pressed => pressed),
                Horizontal = touchMovement.Direction!=0 ? touchMovement.Direction : x, Vertical = y, BasicAttackButton = ActionHeld("battle", "AttackPrimary"),
                DashButton = ActionHeld("exploration", "DashPrimary") || ActionHeld("exploration", "DashSecondary")
            };
        }
        private void CaptureEvent(InputEventPtr evt, InputDevice device)
        {
            if (!evt.IsA<StateEvent>() && !evt.IsA<DeltaStateEvent>()) return;
            // 実ゲーム中のSouthを、以前フォーカスしたUIのSubmitへ重ねて送らない。
            if(device is Gamepad&&GamepadGameplayActive?.Invoke()==true)
                UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            string padContext=GamepadContext?.Invoke()??"battle";bool pauseBefore=PadActionHeld(padContext,"Pause");
            bool enter = KeyHeld(Key.Enter), numpadEnter = KeyHeld(Key.NumpadEnter), space = KeyHeld(Key.Space), escape = KeyHeld(Key.Escape);
            var pointer = device as Pointer;
            bool pointerWasPressed = pointer != null && buttons.TryGetValue(pointer.press, out var pressed) && pressed;
            bool basic = ActionHeld("battle", "AttackPrimary");
            bool jump = ActionHeld("exploration", "JumpPrimary"), card = ActionHeld("battle", "CardPrimary"), leftShift = ActionHeld("exploration", "DashPrimary"), rightShift = ActionHeld("exploration", "DashSecondary");
            bool leftAlt = ActionHeld("battle", "CustomPrimary"), rightAlt = ActionHeld("battle", "CustomSecondary");
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
            if(device is Gamepad&&blockedGamepads.Contains(device)&&PadNeutral(device))blockedGamepads.Remove(device);
            int previousTouchCount=physicalTouches.Count;
            float touchDown=0; bool touchCanceled=false;
            if(device is Touchscreen screen) CaptureTouches(evt,screen,out touchDown,out touchCanceled);
            if(device is Gamepad&&!pauseBefore&&PadActionHeld(padContext,"Pause")&&GamepadPauseAvailable?.Invoke()==true)gamepadPausePending=true;
            var next = BuildState();
            // タッチの短い押下/取消はキーボードの立ち上がりと独立に併合する。
            next.TouchHorizontalDown=touchDown; next.CancelTouchTap=touchCanceled;
            next.HorizontalDown = device is Touchscreen ? 0 : eventState.Horizontal == 0 ? next.Horizontal : 0;
            next.JumpButtonDown = !jump && ActionHeld("exploration", "JumpPrimary");
            next.CardUseButtonDown = !card && ActionHeld("battle", "CardPrimary");
            next.BasicAttackButtonDown = !basic && ActionHeld("battle", "AttackPrimary");
            next.BasicAttackButtonUp = basic && !ActionHeld("battle", "AttackPrimary");
            next.DashButtonDown = (!leftShift && ActionHeld("exploration", "DashPrimary")) || (!rightShift && ActionHeld("exploration", "DashSecondary"));
            next.CustomScreenButtonDown = (!leftAlt && ActionHeld("battle", "CustomPrimary")) || (!rightAlt && ActionHeld("battle", "CustomSecondary"));
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
    public sealed class KeyboardBindingFileStore : GameplayBindingFileStore { public KeyboardBindingFileStore(string directory,CheckpointFileIo io=null):base(directory,false,io){} }
    public sealed class GamepadBindingFileStore : GameplayBindingFileStore { public GamepadBindingFileStore(string directory,CheckpointFileIo io=null):base(directory,true,io){} }
    public class GameplayBindingFileStore
    {
        private readonly bool gamepad;
        private string Device=>gamepad?"gamepad":"keyboard";
        private bool ValidateProfile(Olca.Core.Domain.InputBindingProfile value,out string error)=>gamepad?UnityInputProvider.ValidateGamepadProfile(value,out error):UnityInputProvider.ValidateKeyboardProfile(value,out error);
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
        public string ActivePath => System.IO.Path.Combine(DirectoryPath,Device+"-gameplay.json");
        private string BackupPath => System.IO.Path.Combine(DirectoryPath,Device+"-gameplay.backup.json");
        protected GameplayBindingFileStore(string directory, bool gamepad, CheckpointFileIo io=null)
        {
            this.gamepad=gamepad;
            DirectoryPath=System.IO.Path.GetFullPath(directory ?? throw new System.ArgumentNullException(nameof(directory)));
            this.io=io ?? new CheckpointFileIo();
        }
        private byte[] Encode(Olca.Core.Domain.InputBindingProfile profile)
        {
            var doc=new Document{Version=1,Device=Device,Slots=profile.Bindings.OrderBy(p=>p.Key.Context,System.StringComparer.Ordinal)
                .ThenBy(p=>p.Key.Action,System.StringComparer.Ordinal).Select(p=>new Slot{Context=p.Key.Context,Action=p.Key.Action,Key=p.Value}).ToArray()};
            using(var stream=new System.IO.MemoryStream()){
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).WriteObject(stream,doc);return stream.ToArray();}
        }
        private Olca.Core.Domain.InputBindingProfile Decode(byte[] bytes)
        {
            if(bytes.Length==0 || bytes.Length>65536)throw new System.IO.InvalidDataException();
            Document doc;using(var stream=new System.IO.MemoryStream(bytes))
                doc=(Document)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
            if(doc==null || doc.Version!=1 || doc.Device!=Device || doc.Slots==null)throw new System.IO.InvalidDataException();
            var slots=new Dictionary<(string Device,string Context,string Action),string>();
            foreach(var slot in doc.Slots){if(slot==null)throw new System.IO.InvalidDataException();slots.Add((Device,slot.Context,slot.Action),slot.Key);}
            var profile=new Olca.Core.Domain.InputBindingProfile(slots,gamepad?UnityInputProvider.RequiredGamepadSlots():new (string,string,string)[0]);
            if(!ValidateProfile(profile,out _))throw new System.IO.InvalidDataException();
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
                    profile=gamepad?UnityInputProvider.CreateDefaultGamepadProfile():UnityInputProvider.CreateDefaultKeyboardProfile();return true;}
                profile=Decode(System.IO.File.ReadAllBytes(ActivePath));return true;
            }catch(System.Exception e)when(Failure(e)){error="操作設定を読み込めません。保存済みデータは変更していません。";return false;}
        }
        private bool writing;
        public bool TrySave(Olca.Core.Domain.InputBindingProfile profile,out string error)
        {
            error=null;
            if(!io.TryRequireWritable(ActivePath,BackupPath,out error))return false;
            if(writing){error="操作設定を保存中です";return false;}
            if(!ValidateProfile(profile,out error))return false;
            if(!TryRead(out _,out error))return false;
            var bytes=Encode(profile);string temporary=null;writing=true;
            try{
                if(AtomicSettingsWrite.TryCommit(io,DirectoryPath,ActivePath,BackupPath,"."+Device+"-",bytes,b=>{Decode(b);},Failure,out temporary))return true;
                error="操作設定を保存できません。現在の割当を維持します。";return false;
            }finally{writing=false;AtomicSettingsWrite.Cleanup(temporary,Failure);}

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
        public bool TrySave(bool shortened,out string error){error=null;if(!io.TryRequireWritable(ActivePath,BackupPath,out error))return false;if(writing){error="演出設定を保存中です";return false;}if(!TryRead(out _,out error))return false;
            var bytes=Encode(shortened);string temporary=null;writing=true;try{
                if(AtomicSettingsWrite.TryCommit(io,directory,ActivePath,BackupPath,".form-",bytes,b=>{Decode(b);},Failure,out temporary))return true;
                error="演出設定を保存できません。現在の設定を維持します。";return false;
            }finally{writing=false;AtomicSettingsWrite.Cleanup(temporary,Failure);}

        }
    }

    /// <summary>百分率。既定100は既存のソース音量を増幅せず維持する。</summary>
    public sealed class AudioVolumeSettings
    {
        public int Master { get; } public int Bgm { get; } public int Effects { get; } public int Voice { get; }
        public AudioVolumeSettings(int master=100,int bgm=100,int effects=100,int voice=100)
        {if(new[]{master,bgm,effects,voice}.Any(v=>v<0||v>100))throw new System.ArgumentOutOfRangeException();Master=master;Bgm=bgm;Effects=effects;Voice=voice;}
        public float Gain(GameplayAudioChannel channel)=>Master*.01f*(channel==GameplayAudioChannel.Bgm?Bgm:channel==GameplayAudioChannel.Effects?Effects:Voice)*.01f;
    }
    public enum GameplayAudioChannel { Bgm,Effects,Voice }
    /// <summary>既存の全体Listenerには触れず、登録した実ソースへ全体×系統×元音量を反映する。</summary>
    public sealed class GameplayAudioRouter : System.IDisposable
    {
        private sealed class Route { public AudioSource Source;public GameplayAudioChannel Channel;public float BaseVolume; }
        private readonly List<Route> routes=new List<Route>();
        private readonly List<GameObject> owned=new List<GameObject>();
        private AudioVolumeSettings settings=new AudioVolumeSettings();private bool disposed;
        public bool IsPaused { get; private set; }
        public AudioSource Bgm { get; } public AudioSource Effects { get; } public AudioSource Voice { get; }
        public GameplayAudioRouter(Transform parent)
        {
            AudioSource Create(string name,GameplayAudioChannel channel){var obj=new GameObject(name);obj.transform.SetParent(parent,false);owned.Add(obj);
                var source=obj.AddComponent<AudioSource>();source.playOnAwake=false;source.spatialBlend=0;source.loop=channel==GameplayAudioChannel.Bgm;Register(source,channel);return source;}
            Bgm=Create("GameplayBgmAudio",GameplayAudioChannel.Bgm);Effects=Create("GameplayEffectsAudio",GameplayAudioChannel.Effects);Voice=Create("GameplayVoiceAudio",GameplayAudioChannel.Voice);
        }
        public void Register(AudioSource source,GameplayAudioChannel channel)
        {
            if(disposed||source==null||!System.Enum.IsDefined(typeof(GameplayAudioChannel),channel))throw new System.ArgumentException();
            var prior=routes.FirstOrDefault(r=>r.Source==source);if(prior!=null){if(prior.Channel!=channel)throw new System.ArgumentException("音声系統は重複登録できません");return;}
            var route=new Route{Source=source,Channel=channel,BaseVolume=source.volume};routes.Add(route);source.volume=route.BaseVolume*settings.Gain(channel);if(IsPaused)source.Pause();
        }
        public void Unregister(AudioSource source)
        {
            var route = routes.FirstOrDefault(r => ReferenceEquals(r.Source, source));
            if (route == null) return;
            if (source != null) source.volume = route.BaseVolume;
            routes.Remove(route);
        }
        public void Apply(AudioVolumeSettings value){if(disposed)throw new System.ObjectDisposedException(nameof(GameplayAudioRouter));settings=value??throw new System.ArgumentNullException(nameof(value));
            foreach(var route in routes)if(route.Source!=null)route.Source.volume=route.BaseVolume*settings.Gain(route.Channel);}
        public void SetPaused(bool paused){if(disposed||IsPaused==paused)return;IsPaused=paused;foreach(var route in routes)if(route.Source!=null){if(paused)route.Source.Pause();else route.Source.UnPause();}}
        public void PlayBgm(AudioClip clip){if(disposed)return;if(Bgm.clip==clip)return;Bgm.Stop();Bgm.clip=clip;if(clip!=null){Bgm.Play();if(IsPaused)Bgm.Pause();}}
        public void PlayEffect(AudioClip clip){if(!disposed&&!IsPaused&&clip!=null)Effects.PlayOneShot(clip);}
        public void PlayVoice(AudioClip clip){if(disposed||clip==null||IsPaused)return;Voice.Stop();Voice.clip=clip;Voice.Play();}
        public void Dispose(){if(disposed)return;disposed=true;foreach(var route in routes)if(route.Source!=null){if(owned.Contains(route.Source.gameObject))route.Source.Stop();route.Source.volume=route.BaseVolume;}
            foreach(var obj in owned)if(obj!=null){if(UnityEngine.Application.isPlaying)Object.Destroy(obj);else Object.DestroyImmediate(obj);}routes.Clear();owned.Clear();}
    }
    public sealed class AudioVolumeFileStore
    {
        [System.Runtime.Serialization.DataContract] private sealed class Document {
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Master;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Bgm;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Effects;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Voice;
        }
        private readonly string directory;private readonly CheckpointFileIo io;private bool writing;
        public string ActivePath=>System.IO.Path.Combine(directory,"audio-volumes.json");private string BackupPath=>ActivePath+".backup";
        public AudioVolumeFileStore(string directory,CheckpointFileIo io=null){this.directory=System.IO.Path.GetFullPath(directory);this.io=io??new CheckpointFileIo();}
        private static byte[] Encode(AudioVolumeSettings value){using(var stream=new System.IO.MemoryStream()){
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).WriteObject(stream,new Document{Version=1,Master=value.Master,Bgm=value.Bgm,Effects=value.Effects,Voice=value.Voice});return stream.ToArray();}}
        private static AudioVolumeSettings Decode(byte[] bytes){if(bytes.Length==0||bytes.Length>4096)throw new System.IO.InvalidDataException();using(var stream=new System.IO.MemoryStream(bytes)){
            var doc=(Document)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).ReadObject(stream);if(doc==null||doc.Version!=1)throw new System.IO.InvalidDataException();return new AudioVolumeSettings(doc.Master,doc.Bgm,doc.Effects,doc.Voice);}}
        private static bool Failure(System.Exception e)=>e is System.IO.IOException||e is System.IO.InvalidDataException||e is System.UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException||e is System.Xml.XmlException||e is System.ArgumentException||e is System.Security.SecurityException;
        public bool TryRead(out AudioVolumeSettings value,out string error){value=new AudioVolumeSettings();error=null;try{
            if(!System.IO.File.Exists(ActivePath)){if(System.IO.Directory.Exists(ActivePath)||System.IO.File.Exists(BackupPath)||System.IO.Directory.Exists(BackupPath))throw new System.IO.IOException();return true;}
            value=Decode(System.IO.File.ReadAllBytes(ActivePath));return true;
        }catch(System.Exception e)when(Failure(e)){value=new AudioVolumeSettings(0);error="音量設定を読み込めません。安全のため消音し、既存ファイルは変更していません。";return false;}}
        public bool TrySave(AudioVolumeSettings value,out string error){error=null;if(!io.TryRequireWritable(ActivePath,BackupPath,out error))return false;if(value==null){error="音量設定が必要です";return false;}if(writing){error="音量設定を保存中です";return false;}if(!TryRead(out _,out error))return false;
            var bytes=Encode(value);string temporary=null;writing=true;try{
                if(AtomicSettingsWrite.TryCommit(io,directory,ActivePath,BackupPath,".audio-",bytes,b=>{Decode(b);},Failure,out temporary))return true;
                error="音量設定を保存できません。適用済みの音量を維持します。";return false;
            }finally{writing=false;AtomicSettingsWrite.Cleanup(temporary,Failure);}

        }
    }
    public sealed class DialogueTextSettingsStore
    {
        [System.Runtime.Serialization.DataContract] private sealed class Document {
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Version;
            [System.Runtime.Serialization.DataMember(IsRequired=true)] public int Speed;
        }
        private readonly string directory;
        private readonly CheckpointFileIo io;
        private bool writing;
        public string ActivePath => System.IO.Path.Combine(directory,"dialogue-text.json");
        private string BackupPath => ActivePath+".backup";
        public DialogueTextSettingsStore(string directory,CheckpointFileIo io=null)
        {this.directory=System.IO.Path.GetFullPath(directory);this.io=io??new CheckpointFileIo();}
        private static byte[] Encode(Olca.Core.Domain.DialogueTextSpeed speed){using(var stream=new System.IO.MemoryStream()){
            new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).WriteObject(stream,new Document{Version=1,Speed=(int)speed});return stream.ToArray();}}
        private static Olca.Core.Domain.DialogueTextSpeed Decode(byte[] bytes){if(bytes.Length==0||bytes.Length>4096)throw new System.IO.InvalidDataException();
            using(var stream=new System.IO.MemoryStream(bytes)){var doc=(Document)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
                if(doc==null||doc.Version!=1)throw new System.IO.InvalidDataException();if(!System.Enum.IsDefined(typeof(Olca.Core.Domain.DialogueTextSpeed),doc.Speed))throw new System.IO.InvalidDataException();return (Olca.Core.Domain.DialogueTextSpeed)doc.Speed;}}
        private static bool Failure(System.Exception e)=>e is System.IO.IOException||e is System.IO.InvalidDataException||e is System.UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException||e is System.Xml.XmlException||e is System.ArgumentException||e is System.Security.SecurityException;
        public bool TryRead(out Olca.Core.Domain.DialogueTextSpeed speed,out string error){speed=Olca.Core.Domain.DialogueTextSpeed.Normal;error=null;try{
            if(!System.IO.File.Exists(ActivePath)){if(System.IO.Directory.Exists(ActivePath)||System.IO.File.Exists(BackupPath)||System.IO.Directory.Exists(BackupPath))throw new System.IO.IOException();return true;}
            speed=Decode(System.IO.File.ReadAllBytes(ActivePath));return true;
        }catch(System.Exception e)when(Failure(e)){error="文字表示速度の設定を読めません。保存内容は変更していません。";return false;}}
        public bool TrySave(Olca.Core.Domain.DialogueTextSpeed speed,out string error){error=null;if(!io.TryRequireWritable(ActivePath,BackupPath,out error))return false;if(!System.Enum.IsDefined(typeof(Olca.Core.Domain.DialogueTextSpeed),speed)){error="文字表示速度が不正です";return false;}if(writing){error="文字表示速度設定を保存中です";return false;}if(!TryRead(out _,out error))return false;
            var bytes=Encode(speed);string temporary=null;writing=true;try{
                if(AtomicSettingsWrite.TryCommit(io,directory,ActivePath,BackupPath,".dialogue-",bytes,b=>{Decode(b);},Failure,out temporary))return true;
                error="文字表示速度設定を保存できません。現在の設定を維持します。";return false;
            }finally{writing=false;AtomicSettingsWrite.Cleanup(temporary,Failure);}

        }
    }

}

namespace Olca.Unity
{
    public enum CustomPadCommand { Left,Right,Up,Down,Decide,Cancel,Confirm,Customize,Undo,Details,Transform,PageUp,PageDown }
    public readonly struct CustomPadEvent
    {
        public readonly CustomPadCommand Command;public readonly long Timestamp;public readonly int Epoch;
        public CustomPadEvent(CustomPadCommand command,long timestamp,int epoch){Command=command;Timestamp=timestamp;Epoch=epoch;}
    }
    /// <summary>カスタム画面専用。押下だけを採取し、標準UIの同じSubmitとの二重送信を防ぐ。</summary>
    public sealed class CustomGamepadInput:System.IDisposable
    {
        private sealed class State {public readonly Dictionary<GamepadButton,bool> Buttons=new Dictionary<GamepadButton,bool>();public Vector2 Stick;public bool Armed;public int X,Y;}
        private static readonly GamepadButton[] Keys=UnityInputProvider.BindingButtons;
        private readonly string context;private int profileVersion=-1;
        private readonly HashSet<Gamepad> neutralRequired=new HashSet<Gamepad>();
        private readonly System.Func<bool> ownsKeyboard;
        private readonly System.Func<bool> usable,ownsUi;private readonly Dictionary<Gamepad,State> states=new Dictionary<Gamepad,State>();private readonly List<CustomPadEvent> pending=new List<CustomPadEvent>();private bool active,disposed;private readonly double offset;
        public int Epoch{get;private set;}
        public bool HasGamepad=>InputSystem.devices.OfType<Gamepad>().Any(p=>p.enabled);
        public CustomGamepadInput(System.Func<bool> usable,System.Func<bool> ownsUi=null,bool ownsKeyboard=true,System.Func<bool> keyboardOwned=null,string context="custom"){InputGuideActivity.EnsureInitialized();this.context=context;this.ownsKeyboard=keyboardOwned??(()=>ownsKeyboard);this.usable=usable??throw new System.ArgumentNullException(nameof(usable));this.ownsUi=ownsUi??usable;offset=System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency-Time.realtimeSinceStartupAsDouble;InputSystem.onEvent+=OnEvent;InputSystem.onDeviceChange+=Changed;}
        private static bool Neutral(State state)=>!state.Buttons.Values.Any(v=>v)&&state.Stick.sqrMagnitude<.25f;
        public void Reset(){Epoch++;pending.Clear();states.Clear();active=false;}
        private bool CheckActive()
        {
            if(profileVersion!=UnityInputProvider.GamepadProfileVersion){Reset();profileVersion=UnityInputProvider.GamepadProfileVersion;}
            bool now=!disposed&&usable();if(!now){if(active)Reset();return false;}if(active)return true;active=true;Epoch++;
            foreach(var pad in InputSystem.devices.OfType<Gamepad>().Where(p=>p.enabled)){var state=new State{Stick=pad.leftStick.ReadValue()};foreach(var key in Keys)state.Buttons[key]=pad[key].isPressed;state.Armed=Neutral(state)&&!neutralRequired.Contains(pad);states[pad]=state;}return true;
        }
        private void Changed(InputDevice device,InputDeviceChange change)
        {
            if(!(device is Gamepad pad))return;
            if(change==InputDeviceChange.Removed){Reset();neutralRequired.Remove(pad);}
            else if(change==InputDeviceChange.Disconnected||change==InputDeviceChange.Disabled||change==InputDeviceChange.Reconnected||change==InputDeviceChange.Enabled||change==InputDeviceChange.Added){Reset();neutralRequired.Add(pad);}
        }
        private void OnEvent(InputEventPtr evt,InputDevice device)
        {
            // キー側も既存カーソル／論理決定が所有する。マウスが残したButtonへのSubmitを重ねない。
            if(!disposed&&ownsKeyboard()&&device is Keyboard&&ownsUi()){UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);return;}
            if(!(device is Gamepad pad)||!pad.enabled||(!evt.IsA<StateEvent>()&&!evt.IsA<DeltaStateEvent>())||disposed)return;
            if(ownsUi())UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if(!CheckActive())return;
            if(!states.TryGetValue(pad,out var state))return;
            var before=new Dictionary<GamepadButton,bool>(state.Buttons);
            foreach(var key in Keys)if(pad[key].ReadValueFromEvent(evt,out float value))state.Buttons[key]=value>=pad[key].pressPointOrDefault;
            if(pad.leftStick.x.ReadUnprocessedValueFromEvent(evt,out float x))state.Stick.x=x;if(pad.leftStick.y.ReadUnprocessedValueFromEvent(evt,out float y))state.Stick.y=y;
            if(!state.Armed){if(Neutral(state)){state.Armed=true;neutralRequired.Remove(pad);}return;}
            long stamp=(long)System.Math.Round((evt.time+offset)*System.Diagnostics.Stopwatch.Frequency);
            void Add(CustomPadCommand command)=>pending.Add(new CustomPadEvent(command,stamp,Epoch));
            bool Held(string action)=>UnityInputProvider.ActiveGamepadProfile.Bindings.TryGetValue(("gamepad",context,action),out var value)&&value!=null&&state.Buttons[System.Enum.Parse<GamepadButton>(value)];
            bool Down(string action)=>UnityInputProvider.ActiveGamepadProfile.Bindings.TryGetValue(("gamepad",context,action),out var value)&&value!=null&&Held(action)&&!before[System.Enum.Parse<GamepadButton>(value)];
            int nextX=Held("Right")?1:Held("Left")?-1:state.Stick.x>.5f?1:state.Stick.x<-.5f?-1:0;
            int nextY=Held("Up")?1:Held("Down")?-1:state.Stick.y>.5f?1:state.Stick.y<-.5f?-1:0;
            if(nextX!=0&&nextX!=state.X)Add(nextX>0?CustomPadCommand.Right:CustomPadCommand.Left);else if(nextY!=0&&nextY!=state.Y)Add(nextY>0?CustomPadCommand.Up:CustomPadCommand.Down);state.X=nextX;state.Y=nextY;
            foreach(var command in new[]{CustomPadCommand.Cancel,CustomPadCommand.PageUp,CustomPadCommand.PageDown,CustomPadCommand.Details,CustomPadCommand.Transform,CustomPadCommand.Customize,CustomPadCommand.Undo,CustomPadCommand.Confirm,CustomPadCommand.Decide})if(Down(command.ToString())){Add(command);break;}

        }
        public CustomPadEvent[] Read(){CheckActive();var result=pending.ToArray();pending.Clear();return result;}
        public void Dispose(){if(disposed)return;disposed=true;InputSystem.onEvent-=OnEvent;InputSystem.onDeviceChange-=Changed;Reset();neutralRequired.Clear();}
    }
}

namespace Olca.Unity
{
    /// <summary>停止中メニューの画面内ナビゲーション。UI標準Updateの後のLateUpdateから呼ぶ。</summary>
    public sealed class MenuPadScope:System.IDisposable
    {
        private readonly CustomGamepadInput input;private readonly System.Func<bool> top;private readonly System.Func<IEnumerable<UnityEngine.UI.Button>> external;
        private UnityEngine.UI.Selectable[] linked=System.Array.Empty<UnityEngine.UI.Selectable>();
        private bool directionScroll=true;
        private Transform root;private System.Action cancel;private UnityEngine.UI.ScrollRect scroll;private UnityEngine.UI.Selectable focused;private string preferred;private Vector2 lastPoint;private bool havePoint;private int version;private static int cancelFrame=-1;
        public string FocusName=>focused!=null?focused.name:null;
        public static bool IsTopmost(Transform root)
        {
            if(root==null||!root.gameObject.activeInHierarchy)return false;var es=UnityEngine.EventSystems.EventSystem.current;if(es==null)return true;
            var hits=new List<UnityEngine.EventSystems.RaycastResult>();es.RaycastAll(new UnityEngine.EventSystems.PointerEventData(es){position=new Vector2(Screen.width/2f,Screen.height/2f)},hits);
            return hits.Count==0||hits[0].gameObject.transform==root||hits[0].gameObject.transform.IsChildOf(root);
        }
        public MenuPadScope(System.Func<bool> top,System.Func<IEnumerable<UnityEngine.UI.Button>> external=null,System.Func<bool> keyboardOwned=null)
        {this.top=top;this.external=external;input=new CustomGamepadInput(Usable,Usable,ownsKeyboard:false,keyboardOwned:keyboardOwned,context:"menu");}
        private bool Typing()=>root!=null&&root.GetComponentsInChildren<UnityEngine.UI.InputField>().Any(f=>f.isFocused);
        private bool Usable()=>root!=null&&root.gameObject.activeInHierarchy&&top()&&!Typing();
        private UnityEngine.UI.Selectable[] Buttons()=>root==null?System.Array.Empty<UnityEngine.UI.Selectable>():root.GetComponentsInChildren<UnityEngine.UI.Selectable>().Concat((external?.Invoke()??System.Array.Empty<UnityEngine.UI.Button>()).Cast<UnityEngine.UI.Selectable>()).Where(b=>b!=null&&(b is UnityEngine.UI.Button||b is UnityEngine.UI.Slider||b is UnityEngine.UI.Toggle)&&b.gameObject.activeInHierarchy&&b.IsInteractable()).Distinct().ToArray();
        private static Vector2 Point(UnityEngine.UI.Selectable b)=>RectTransformUtility.WorldToScreenPoint(null,b.transform.TransformPoint(((RectTransform)b.transform).rect.center));
        private static UnityEngine.UI.Selectable Near(UnityEngine.UI.Selectable from,Vector2 direction,UnityEngine.UI.Selectable[] buttons)
        {var p=Point(from);return buttons.Where(b=>b!=from&&Vector2.Dot(Point(b)-p,direction)>.01f).OrderByDescending(b=>Vector2.Dot(Point(b)-p,direction)/(Point(b)-p).sqrMagnitude).FirstOrDefault();}
        public void Bind(Transform root,string preferred,System.Action cancel,UnityEngine.UI.ScrollRect scroll=null,bool restorePosition=false,bool directionScroll=true)
        {version++;input.Reset();this.root=root;this.preferred=preferred;this.cancel=cancel;this.scroll=scroll;this.directionScroll=directionScroll;linked=System.Array.Empty<UnityEngine.UI.Selectable>();focused=null;if(!restorePosition)havePoint=false;
            if(Usable()){var initial=Buttons().FirstOrDefault(b=>b.name==preferred);if(initial!=null)Focus(initial);}Refresh();}
        public void Suspend(){version++;input.Reset();root=null;focused=null;cancel=null;scroll=null;}
        private void Focus(UnityEngine.UI.Selectable button)
        {focused=button;if(button==null)return;preferred=button.name;lastPoint=Point(button);havePoint=true;button.Select();Follow(button);}
        private void Follow(UnityEngine.UI.Selectable button)
        {
            foreach(var area in root.GetComponentsInChildren<UnityEngine.UI.ScrollRect>()){
                if(area.content==null||area.viewport==null||!button.transform.IsChildOf(area.content))continue;
                var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);var topPoint=area.viewport.InverseTransformPoint(corners[1]);var bottomPoint=area.viewport.InverseTransformPoint(corners[0]);float move=topPoint.y>area.viewport.rect.yMax?area.viewport.rect.yMax-topPoint.y:bottomPoint.y<area.viewport.rect.yMin?area.viewport.rect.yMin-bottomPoint.y:0;
                if(move!=0){area.StopMovement();area.content.anchoredPosition+=new Vector2(0,move);}
            }
        }
        private void Refresh()
        {
            if(!Usable())return;var buttons=Buttons();
            if(!linked.SequenceEqual(buttons)){linked=buttons;foreach(var b in buttons)b.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.Explicit,selectOnLeft=b is UnityEngine.UI.Slider?null:Near(b,Vector2.left,buttons),selectOnRight=b is UnityEngine.UI.Slider?null:Near(b,Vector2.right,buttons),selectOnUp=Near(b,Vector2.up,buttons),selectOnDown=Near(b,Vector2.down,buttons)};}
            var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;var actual=buttons.FirstOrDefault(b=>b.gameObject==selected);
            if(actual!=null){focused=actual;preferred=actual.name;lastPoint=Point(actual);havePoint=true;Follow(actual);return;}
            if(focused==null||!buttons.Contains(focused))focused=buttons.FirstOrDefault(b=>b.name==preferred)??(havePoint?buttons.OrderBy(b=>(Point(b)-lastPoint).sqrMagnitude).FirstOrDefault():buttons.FirstOrDefault());
            Focus(focused);
        }
        public void Tick()
        {
            var commands=input.Read();if(!Usable())return;Refresh();
            var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if(Input.GetKeyDown(KeyCode.Escape)&&cancelFrame!=Time.frameCount&&(selected==null||selected.GetComponent<UnityEngine.UI.InputField>()==null)){cancelFrame=Time.frameCount;cancel?.Invoke();return;}
            int current=version;
            foreach(var e in commands){if(current!=version||e.Epoch!=input.Epoch||!Usable())break;
                if(e.Command==CustomPadCommand.Cancel){cancel?.Invoke();continue;}
                if(scroll!=null&&(e.Command==CustomPadCommand.PageUp||e.Command==CustomPadCommand.PageDown||directionScroll&&(e.Command==CustomPadCommand.Up||e.Command==CustomPadCommand.Down))){scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+(e.Command==CustomPadCommand.Up||e.Command==CustomPadCommand.PageUp?.2f:-.2f));continue;}
                if(e.Command==CustomPadCommand.Decide){if(focused!=null&&focused.IsInteractable()){if(focused is UnityEngine.UI.Button button)button.onClick.Invoke();else if(focused is UnityEngine.UI.Toggle toggle)toggle.isOn=!toggle.isOn;}continue;}
                if(focused is UnityEngine.UI.Slider slider&&(e.Command==CustomPadCommand.Left||e.Command==CustomPadCommand.Right)){float step=slider.wholeNumbers?1:(slider.maxValue-slider.minValue)*.1f;slider.value=Mathf.Clamp(slider.value+(e.Command==CustomPadCommand.Right?step:-step),slider.minValue,slider.maxValue);continue;}
                Vector2 direction=e.Command==CustomPadCommand.Left?Vector2.left:e.Command==CustomPadCommand.Right?Vector2.right:e.Command==CustomPadCommand.Up?Vector2.up:e.Command==CustomPadCommand.Down?Vector2.down:Vector2.zero;
                if(direction!=Vector2.zero&&focused!=null)Focus(Near(focused,direction,Buttons())??focused);
            }
            if(scroll!=null){if(directionScroll&&Input.GetKeyDown(KeyCode.UpArrow)||Input.GetKeyDown(KeyCode.PageUp))scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition+.2f);if(directionScroll&&Input.GetKeyDown(KeyCode.DownArrow)||Input.GetKeyDown(KeyCode.PageDown))scroll.verticalNormalizedPosition=Mathf.Clamp01(scroll.verticalNormalizedPosition-.2f);}
        }
        public void Dispose(){input.Dispose();Suspend();}
    }
}

namespace Olca.Unity
{
    /// <summary>カスタム中の戦闘描画だけを停止する時計。UI入力とUI演出は既存時計を使う。</summary>
    public static class BattleVisualClock
    {
        private static readonly System.Collections.Generic.HashSet<object> owners=new System.Collections.Generic.HashSet<object>();
        private static readonly System.Collections.Generic.Dictionary<UnityEngine.Animator,float> animators=new System.Collections.Generic.Dictionary<UnityEngine.Animator,float>();
        private static readonly System.Collections.Generic.HashSet<UnityEngine.ParticleSystem> particles=new System.Collections.Generic.HashSet<UnityEngine.ParticleSystem>();
        public static bool IsPaused=>owners.Count>0;
        public static float DeltaTime=>IsPaused?0f:GameplayPause.DeltaTime;
        public static void SetPaused(object owner,bool paused)
        {
            if(paused){owners.Add(owner);Refresh();return;}
            owners.Remove(owner);if(IsPaused)return;
            foreach(var entry in animators)if(entry.Key!=null)entry.Key.speed=entry.Value;
            foreach(var particle in particles)if(particle!=null&&particle.isPaused)particle.Play(false);
            animators.Clear();particles.Clear();
        }
        public static void Refresh()
        {
            if(!IsPaused)return;
            foreach(var animator in UnityEngine.Object.FindObjectsByType<UnityEngine.Animator>(UnityEngine.FindObjectsSortMode.None)){
                if(animator.GetComponentInParent<UnityEngine.Canvas>()!=null)continue;
                if(!animators.ContainsKey(animator))animators.Add(animator,animator.speed);
                animator.speed=0f;
            }
            foreach(var particle in UnityEngine.Object.FindObjectsByType<UnityEngine.ParticleSystem>(UnityEngine.FindObjectsSortMode.None)){
                if(particle.GetComponentInParent<UnityEngine.Canvas>()!=null||!particle.isPlaying)continue;
                particles.Add(particle);particle.Pause(false);
            }
        }
        public static async Cysharp.Threading.Tasks.UniTask NextFrame(System.Threading.CancellationToken ct=default)
        {
            await GameplayPause.NextFrame(ct);
            while(IsPaused)await GameplayPause.NextFrame(ct);
        }
        public static async Cysharp.Threading.Tasks.UniTask Delay(int milliseconds,System.Threading.CancellationToken cancellationToken=default)
        {
            while(IsPaused)await NextFrame(cancellationToken);
            await GameplayPause.WaitAsync(cancellationToken);
            float remaining=milliseconds/1000f;
            while(remaining>0f){await NextFrame(cancellationToken);remaining-=DeltaTime;}
        }
        public static Cysharp.Threading.Tasks.UniTask Delay(System.TimeSpan duration,System.Threading.CancellationToken cancellationToken=default)
            =>Delay((int)duration.TotalMilliseconds,cancellationToken);
    }
}

namespace Olca.Unity
{
    public enum InputGuideDevice { KeyboardMouse, Gamepad, Touch }
    /// <summary>入力の処理や割当を変えず、実際に操作した機器だけを表示側へ伝える。</summary>
    public static class InputGuideActivity
    {
        public static InputGuideDevice Current { get; private set; } = InputGuideDevice.KeyboardMouse;
        private static bool initialized;
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            UnityEngine.InputSystem.InputSystem.onEvent-=Observe;
            initialized=false;Current=InputGuideDevice.KeyboardMouse;EnsureInitialized();
        }
        public static void EnsureInitialized()
        {
            if(initialized)return;initialized=true;UnityEngine.InputSystem.InputSystem.onEvent+=Observe;
        }
        private static void Observe(UnityEngine.InputSystem.LowLevel.InputEventPtr evt,UnityEngine.InputSystem.InputDevice device)
        {
            if(device==null||!device.enabled||(!evt.IsA<UnityEngine.InputSystem.LowLevel.StateEvent>()&&!evt.IsA<UnityEngine.InputSystem.LowLevel.DeltaStateEvent>()))return;
            foreach(var control in evt.EnumerateChangedControls(device,magnitudeThreshold:.19f)){
                if(control.noisy||control.synthetic)continue;
                if(device is UnityEngine.InputSystem.Gamepad||device is UnityEngine.InputSystem.Joystick){Current=InputGuideDevice.Gamepad;return;}
                if(device is UnityEngine.InputSystem.Keyboard||device is UnityEngine.InputSystem.Mouse){Current=InputGuideDevice.KeyboardMouse;return;}
                if(device is UnityEngine.InputSystem.Touchscreen){Current=InputGuideDevice.Touch;return;}
            }
        }
    }
}
