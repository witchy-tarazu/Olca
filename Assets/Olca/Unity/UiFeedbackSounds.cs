using UnityEngine;
namespace Olca.Unity
{
    /// <summary>既存UIの共通仮音。個別指定を優先し、新しい発音契機は作らない。</summary>
    public static class UiFeedbackSounds
    {
        private static AudioClip press, cancel;
        public static AudioClip ForButton(string name, AudioClip explicitClip = null)
        {
            if (explicitClip != null) return explicitClip;
            if (name == "CancelRetreat" || name == "CancelProgressAction")
                return cancel != null ? cancel : cancel = Resources.Load<AudioClip>("OlcaUiSounds/cancel");
            return press != null ? press : press = Resources.Load<AudioClip>("OlcaUiSounds/press");
        }
    }
}
