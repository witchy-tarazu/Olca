using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
namespace Olca.Unity
{
    /// <summary>個別戦闘通知の共有仮音。最大4声でUI音と所有寿命を分離する。</summary>
    public sealed class CombatFeedbackAudio : System.IDisposable
    {
        public const int MaximumVoices = 4;
        private static AudioClip damage, defense, healing;
        private readonly Transform parent;
        private readonly GameplayAudioRouter router;
        private readonly CombatFeedbackSubscription subscription;
        private readonly AudioSource[] voices = new AudioSource[MaximumVoices];
        private BattleSystem battle;
        private bool disposed;
        public CombatFeedbackAudio(Transform parent, GameplayAudioRouter router)
        {
            this.parent = parent != null ? parent : throw new System.ArgumentNullException(nameof(parent));
            this.router = router ?? throw new System.ArgumentNullException(nameof(router));
            subscription = new CombatFeedbackSubscription(feedback => TryPlay(feedback.Kind));
        }
        public static AudioClip GetClip(CombatFeedbackKind kind)
        {
            switch (kind) {
                case CombatFeedbackKind.Damage: return damage != null ? damage : damage = Resources.Load<AudioClip>("OlcaBattleSounds/damage");
                case CombatFeedbackKind.Healing: return healing != null ? healing : healing = Resources.Load<AudioClip>("OlcaBattleSounds/healing");
                case CombatFeedbackKind.Guard: case CombatFeedbackKind.BarrierDamage: case CombatFeedbackKind.Immune:
                    return defense != null ? defense : defense = Resources.Load<AudioClip>("OlcaBattleSounds/defense");
                default: return null;
            }
        }
        public void Bind(BattleSystem value)
        {
            if (disposed || ReferenceEquals(battle, value)) return;
            Stop(); battle = value; subscription.Bind(value);
        }
        public bool TryPlay(CombatFeedbackKind kind)
        {
            if (disposed || router.IsPaused || battle == null || battle.IsGameOver) return false;
            var clip = GetClip(kind); if (clip == null) return false;
            for (int i = 0; i < voices.Length; i++) {
                var source = voices[i]; if (source != null && source.isPlaying) continue;
                if (source == null) {
                    var obj = new GameObject("CombatFeedbackAudio_" + i); obj.transform.SetParent(parent, false);
                    source = obj.AddComponent<AudioSource>(); source.playOnAwake = false; source.loop = false; source.spatialBlend = 0;
                    router.Register(source, GameplayAudioChannel.Effects); voices[i] = source;
                }
                source.clip = clip; source.Play(); return true;
            }
            // 音だけを抑制し、ダメージ通知・判定・論理時間には触れない。
            return false;
        }
        public void Stop() { foreach (var source in voices) if (source != null) { source.Stop(); source.clip = null; } }
        public void Dispose()
        {
            if (disposed) return; subscription.Dispose(); Stop(); battle = null; disposed = true;
            foreach (var source in voices) if (source != null) { router.Unregister(source); if (Application.isPlaying) Object.Destroy(source.gameObject); else Object.DestroyImmediate(source.gameObject); }
        }
    }
}
