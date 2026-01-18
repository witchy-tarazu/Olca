using Olca.Core.Interfaces;
using UnityEngine;

namespace Olca.Unity
{
    /// <summary>
    /// UnityEngine.Timeを使用した時間プロバイダー
    /// 整数ベース（ミリ秒）で時間を提供し、浮動小数点の累積誤差を回避
    /// </summary>
    public class UnityTimeProvider : ITimeProvider
    {
        /// <summary>
        /// 前フレームからの経過時間（ミリ秒）
        /// </summary>
        public int DeltaTimeMs => Mathf.RoundToInt(UnityEngine.Time.deltaTime * 1000f);

        /// <summary>
        /// ゲーム開始からの経過時間（ミリ秒）
        /// </summary>
        public long TimeMs => (long)(UnityEngine.Time.time * 1000f);

        /// <summary>
        /// スケールされていない前フレームからの経過時間（ミリ秒）
        /// </summary>
        public int UnscaledDeltaTimeMs => Mathf.RoundToInt(UnityEngine.Time.unscaledDeltaTime * 1000f);

        /// <summary>
        /// 前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        public float DeltaTime => UnityEngine.Time.deltaTime;

        /// <summary>
        /// ゲーム開始からの経過時間（秒）- View層での互換性用
        /// </summary>
        public float Time => UnityEngine.Time.time;

        /// <summary>
        /// スケールされていない前フレームからの経過時間（秒）- View層での互換性用
        /// </summary>
        public float UnscaledDeltaTime => UnityEngine.Time.unscaledDeltaTime;
    }
}
