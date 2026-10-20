using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Olca.Core.Interfaces
{
    /// <summary>
    /// 入力情報のインターフェース
    /// Unity非依存のロジックで入力を扱うための抽象化
    /// </summary>
    public interface IInputProvider
    {
        /// <summary>
        /// 横方向の入力（-1.0 ~ 1.0）
        /// </summary>
        float Horizontal { get; }

        /// <summary>
        /// 横方向の入力が押された瞬間か（-1.0 ~ 1.0）
        /// </summary>
        float HorizontalDown { get; }

        /// <summary>
        /// 縦方向の入力（-1.0 ~ 1.0）
        /// </summary>
        float Vertical { get; }

        /// <summary>
        /// ジャンプボタンが押された瞬間か
        /// </summary>
        bool JumpButtonDown { get; }

        /// <summary>
        /// ダッシュボタンが押されているか
        /// </summary>
        bool DashButton { get; }

        /// <summary>
        /// カード使用ボタンが押された瞬間か
        /// </summary>
        bool CardUseButtonDown { get; }

        /// <summary>
        /// 固有攻撃ボタンが押されているか
        /// </summary>
        bool BasicAttackButton { get; }

        /// <summary>
        /// 固有攻撃ボタンが押されている時間（秒）
        /// タメ攻撃の判定に使用
        /// </summary>
        float BasicAttackButtonHoldTime { get; }

        /// <summary>
        /// カスタム画面ボタンが押された瞬間か
        /// </summary>
        bool CustomScreenButtonDown { get; }
    }
}

namespace Olca.Core.Domain
{
    /// <summary>
    /// デバイス種別・場面・操作ごとの割当を保持する不変の設定。
    /// 識別子と必須操作は呼出し側から渡し、具体的なキー配置はここで決めない。
    /// nullは任意操作の未割当。物理入力やファイル保存には依存しない。
    /// </summary>
    public sealed class InputBindingProfile
    {
        private readonly Dictionary<(string Device, string Context, string Action), string> bindings;
        private readonly HashSet<(string Device, string Context, string Action)> required;
        public IReadOnlyDictionary<(string Device, string Context, string Action), string> Bindings { get; }

        public InputBindingProfile(IReadOnlyDictionary<(string Device, string Context, string Action), string> bindings,
            IEnumerable<(string Device, string Context, string Action)> requiredActions)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (requiredActions == null) throw new ArgumentNullException(nameof(requiredActions));
            this.bindings = bindings.ToDictionary(p => p.Key, p => p.Value);
            required = new HashSet<(string Device, string Context, string Action)>(requiredActions);
            if (!Validate(this.bindings, out var error)) throw new ArgumentException(error);
            Bindings = new ReadOnlyDictionary<(string Device, string Context, string Action), string>(this.bindings);
        }

        internal bool Validate(IReadOnlyDictionary<(string Device, string Context, string Action), string> candidate, out string error)
        {
            error = null;
            if (candidate.Any(p => string.IsNullOrWhiteSpace(p.Key.Device) || string.IsNullOrWhiteSpace(p.Key.Context) ||
                string.IsNullOrWhiteSpace(p.Key.Action) || (p.Value != null && string.IsNullOrWhiteSpace(p.Value))))
            { error = "割当の識別子が不正です"; return false; }
            if (required.Any(slot => !candidate.TryGetValue(slot, out var value) || value == null))
            { error = "必須操作を未割当にできません"; return false; }
            if (candidate.Where(p => p.Value != null).GroupBy(p => (p.Key.Device, p.Key.Context, p.Value)).Any(g => g.Count() > 1))
            { error = "同じデバイス・場面で割当が重複しています"; return false; }
            return true;
        }
        internal InputBindingProfile With(IReadOnlyDictionary<(string Device, string Context, string Action), string> candidate)
            => new InputBindingProfile(candidate, required);
        public InputBindingEdit BeginEdit() => new InputBindingEdit(this);
    }

    /// <summary>適用まで原本を変更しない編集セッション。保存I/Oは呼出し側の責務。</summary>
    public sealed class InputBindingEdit
    {
        private InputBindingProfile committed;
        private Dictionary<(string Device, string Context, string Action), string> draft;
        internal InputBindingEdit(InputBindingProfile profile) { committed = profile; Discard(); }
        // 外部へはスナップショットを返し、編集用辞書の書換えを許さない。
        public IReadOnlyDictionary<(string Device, string Context, string Action), string> Draft
            => new ReadOnlyDictionary<(string Device, string Context, string Action), string>(draft.ToDictionary(p => p.Key, p => p.Value));

        public bool TryAssign((string Device, string Context, string Action) slot, string binding, out string error)
        {
            error = null;
            if (!draft.TryGetValue(slot, out var previous)) { error = "未定義の操作です"; return false; }
            var candidate = draft.ToDictionary(p => p.Key, p => p.Value);
            if (binding != null)
                foreach (var other in draft.Keys)
                    if (other != slot && other.Device == slot.Device && other.Context == slot.Context && draft[other] == binding)
                        candidate[other] = previous;
            candidate[slot] = binding;
            // 交換で相手の必須操作を未割当にする場合も、全体を変更せず拒否する。
            if (!committed.Validate(candidate, out error)) return false;
            draft = candidate;
            return true;
        }

        /// <summary>検証済みの不変設定を返す。呼出し側が保存・実入力への適用を行う。</summary>
        // 保存成功前の候補取得では編集の破棄先を変更しない。
        public InputBindingProfile Preview() => committed.With(draft);
        public InputBindingProfile Apply()
        {
            committed = Preview();
            Discard();
            return committed;
        }
        public void Discard() { draft = committed.Bindings.ToDictionary(p => p.Key, p => p.Value); }
    }
}

namespace Olca.Core.Interfaces
{
    /// <summary>解放済みの短いタッチだけを一度受け取る任意入力。既存のキーボード軸とは分離する。</summary>
    public interface IHorizontalTapInput
    {
        float HorizontalTap { get; }
    }
}
