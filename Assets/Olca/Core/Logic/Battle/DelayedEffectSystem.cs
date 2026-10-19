using System.Collections.Generic;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 遅延効果を管理するシステム
    /// 投擲カードなど、時間差で発動する効果を時間経過に応じて管理・実行する
    /// 整数ベース（ミリ秒）で時間を管理し、浮動小数点の累積誤差を回避
    /// </summary>
    public class DelayedEffectSystem
    {
        private readonly List<DelayedEffect> activeEffects = new List<DelayedEffect>();
        private readonly BattleSystem battleSystem;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="battleSystem">バトルシステムの参照（パネル状態確認用）</param>
        public DelayedEffectSystem(BattleSystem battleSystem)
        {
            this.battleSystem = battleSystem;
        }

        /// <summary>
        /// 遅延効果を登録
        /// </summary>
        /// <param name="effect">登録する遅延効果</param>
        public void RegisterDelayedEffect(DelayedEffect effect)
        {
            if (battleSystem.IsGameOver) return;
            effect.RegisteredFrame = battleSystem.ActionFrame;
            activeEffects.Add(effect);

#if UNITY_EDITOR || DEBUG
            System.Diagnostics.Debug.WriteLine($"[DelayedEffectSystem] 遅延効果登録: 位置={effect.TargetPosition}, 残り時間={effect.RemainingTimeMs}ms");
#endif
        }

        /// <summary>
        /// 時間経過を更新し、発動タイミングに達した効果を実行 - 整数ベース版
        /// </summary>
        /// <param name="deltaTimeMs">経過時間（ミリ秒）</param>
        public void Update(int deltaTimeMs, bool logicalFrame = false)
        {
            if (battleSystem.IsGameOver) return;
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                var effect = activeEffects[i];
                // 今のフレームの継続処理で登録された効果に、過去の1フレームを渡さない。
                if (logicalFrame)
                {
                    if (effect.RegisteredFrame < battleSystem.ActionFrame) effect.TickFrame();
                }
                else effect.Tick(deltaTimeMs);

                if (effect.RemainingTimeMs <= 0)
                {
                    // コールバック内の戦闘終了でClearされるため、実行前に取り除く。
                    activeEffects.RemoveAt(i);
                    // 投擲物タイプの場合のみ穴パネルチェック
                    if (effect.EffectType == DelayedEffectType.Projectile &&
                        effect.CancelOnHole &&
                        battleSystem.Field.IsHole(effect.TargetPosition))
                    {
#if UNITY_EDITOR || DEBUG
                        System.Diagnostics.Debug.WriteLine($"[DelayedEffectSystem] 穴パネルに落下してキャンセル: {effect.TargetPosition}");
#endif
                        // 穴パネルに落下 → キャンセル
                        effect.OnCancelled?.Invoke();
                    }
                    else
                    {
#if UNITY_EDITOR || DEBUG
                        string effectTypeName = effect.EffectType == DelayedEffectType.Projectile
                            ? $"着弾実行: {effect.TargetPosition}"
                            : $"{effect.EffectType}実行";
                        System.Diagnostics.Debug.WriteLine($"[DelayedEffectSystem] {effectTypeName}");
#endif
                        // 効果を実行
                        effect.Execute();
                    }

                    if (battleSystem.IsGameOver) return;
                }
            }
        }

        /// <summary>
        /// 現在アクティブな遅延効果のリストを取得（表示層用）
        /// </summary>
        public IReadOnlyList<DelayedEffect> GetActiveEffects() => activeEffects;

        /// <summary>
        /// すべての遅延効果をクリア（バトル終了時など）
        /// </summary>
        public void Clear()
        {
            activeEffects.Clear();
        }

        /// <summary>
        /// 現在アクティブな遅延効果の数を取得
        /// </summary>
        public int ActiveEffectCount => activeEffects.Count;
    }
}
