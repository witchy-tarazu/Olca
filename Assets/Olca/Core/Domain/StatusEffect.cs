using System;

namespace Olca.Core.Domain
{
    /// <summary>
    /// 状態異常の種類
    /// </summary>
    public enum StatusEffectType
    {
        None,
        Paralyze,   // 麻痺: 行動不能
        Poison,     // 毒: 継続ダメージ
        Confusion,  // 混乱: 操作反転
        Blind,      // 盲目
    }

    /// <summary>
    /// 状態異常インスタンス
    /// </summary>
    public class StatusEffect
    {
        /// <summary>状態異常の種類</summary>
        public StatusEffectType Type { get; }

        /// <summary>残り時間（ミリ秒）</summary>
        public int RemainingMs { get; private set; }

        /// <summary>毒の場合の1ティックあたりのダメージ</summary>
        public int DamagePerTick { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="type">状態異常の種類</param>
        /// <param name="durationMs">持続時間（ミリ秒）</param>
        /// <param name="damagePerTick">毒ダメージ（ミリ秒ごと）。毒以外は0</param>
        public StatusEffect(StatusEffectType type, int durationMs, int damagePerTick = 0)
        {
            if (durationMs <= 0) throw new ArgumentException("持続時間は1以上である必要があります", nameof(durationMs));
            Type = type;
            RemainingMs = durationMs;
            DamagePerTick = damagePerTick;
        }

        /// <summary>
        /// 時間を経過させる。返り値がtrueなら消滅
        /// </summary>
        /// <param name="elapsedMs">経過時間（ミリ秒）</param>
        /// <returns>消滅するか</returns>
        public bool Tick(int elapsedMs)
        {
            RemainingMs -= elapsedMs;
            return RemainingMs <= 0;
        }
    }
}
