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

        public int RemainingFrames { get; private set; }

        /// <summary>毒の場合の1ティックあたりのダメージ</summary>
        public int DamagePerTick { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="type">状態異常の種類</param>
        /// <param name="durationMs">持続時間（ミリ秒）</param>
        /// <param name="damagePerTick">毒ダメージ（ミリ秒ごと）。毒以外は0</param>
        public StatusEffect(StatusEffectType type, int durationMs, int damagePerTick = 0, int durationResistancePercent = 0)
        {
            if (durationMs <= 0) throw new ArgumentException("持続時間は1以上である必要があります", nameof(durationMs));
            if (durationResistancePercent < 0 || durationResistancePercent > 100) throw new ArgumentOutOfRangeException(nameof(durationResistancePercent));
            Type = type;
            long frames = ((long)durationMs * 60 + 999) / 1000;
            RemainingFrames = (int)(frames * (100 - durationResistancePercent) / 100);
            // 既存ms APIは耐性なしの場合の値を維持。論理更新では整数フレームを使用する。
            RemainingMs = durationResistancePercent == 0 ? durationMs : (int)Math.Min(int.MaxValue, ((long)RemainingFrames * 1000 + 59) / 60);
            DamagePerTick = damagePerTick;
        }

        /// <summary>
        /// 時間を経過させる。返り値がtrueなら消滅
        /// </summary>
        /// <param name="elapsedMs">経過時間（ミリ秒）</param>
        /// <returns>消滅するか</returns>
        public bool TickFrame()
        {
            RemainingFrames = Math.Max(0, RemainingFrames - 1);
            RemainingMs = (int)Math.Min(int.MaxValue, ((long)RemainingFrames * 1000 + 59) / 60);
            return RemainingFrames == 0;
        }

        public bool Tick(int elapsedMs)
        {
            RemainingMs -= elapsedMs;
            RemainingFrames = (int)(((long)Math.Max(0, RemainingMs) * 60 + 999) / 1000);
            return RemainingMs <= 0;
        }
    }
}
