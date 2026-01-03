using System;
using Olca.Core.Domain;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// 個別の遅延効果データ
    /// 投擲カードなど、時間差で発動する効果を表現する
    /// </summary>
    public class DelayedEffect
    {
        /// <summary>残り時間（秒）</summary>
        public float RemainingTime { get; set; }

        /// <summary>着弾位置（絶対座標）</summary>
        public Vector2Int TargetPosition { get; set; }

        /// <summary>穴パネルで無効化されるか</summary>
        public bool CancelOnHole { get; set; }

        /// <summary>着弾時に実行する処理</summary>
        public Action ExecuteAction { get; set; }

        /// <summary>キャンセル時に実行する処理（オプション）</summary>
        public Action OnCancelled { get; set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public DelayedEffect(float delayTime, Vector2Int targetPosition, bool cancelOnHole = true)
        {
            RemainingTime = delayTime;
            TargetPosition = targetPosition;
            CancelOnHole = cancelOnHole;
        }

        /// <summary>
        /// 効果を実行
        /// </summary>
        public void Execute()
        {
            ExecuteAction?.Invoke();
        }
    }
}
