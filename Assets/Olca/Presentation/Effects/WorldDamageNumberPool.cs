using UnityEngine;
using System.Linq;
using Olca.Core.Domain;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// World Space 用ダメージ数値のオブジェクトプール。
    /// WorldEnemyView から参照され、ダメージ表示リクエストを受け付ける。
    /// Prefab の外観設定（フォント・サイズ・色）をそのまま使う。
    /// </summary>
    public class WorldDamageNumberPool : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private WorldDamageNumberView prefab;

        [Header("Pool Settings")]
        [SerializeField] private int initialPoolSize = 10;
        [SerializeField] private int maxPoolSize = 20;
        [SerializeField] private Transform poolParent;  // 未設定時は自身

        private Queue<WorldDamageNumberView> pool = new Queue<WorldDamageNumberView>();
        private HashSet<WorldDamageNumberView> activeInstances = new HashSet<WorldDamageNumberView>();

        private CancellationTokenSource clearCts = new CancellationTokenSource();
        private bool isDestroyed;
        private readonly Dictionary<WorldDamageNumberView,object> owners = new Dictionary<WorldDamageNumberView,object>();
        private readonly HashSet<object> hiddenOwners = new HashSet<object>();
        /// <summary>敵に属する数値だけ抑止する。解除時に古い数値は再表示しない。</summary>
        public void SetOwnerVisible(object owner,bool visible)
        {
            if(owner==null)return;
            if(visible){hiddenOwners.Remove(owner);return;}
            hiddenOwners.Add(owner);
            foreach(var pair in owners)if(ReferenceEquals(pair.Value,owner)&&pair.Key!=null)pair.Key.gameObject.SetActive(false);
        }
        public void ShowOwnedDamage(int damage,Vector3 position,CancellationToken ct,object owner)
            => ShowOwnedDamageAsync(damage,position,ct,owner).Forget();

        /// <summary>合算前の一効果を表示する。同一対象の同時表示をずらし、一時的な連続ヒットは破棄しない。</summary>
        public void ShowFeedback(CombatFeedback feedback,Vector3 position,CancellationToken ct,object owner=null)
            =>ShowFeedbackAsync(feedback,position,ct,owner).Forget();
        private UniTask ShowFeedbackAsync(CombatFeedback feedback,Vector3 position,CancellationToken ct,object owner)
        {
            string text=feedback.Kind==CombatFeedbackKind.Immune?"ムコウ":feedback.Kind==CombatFeedbackKind.Guard?"ガード":
                feedback.Kind==CombatFeedbackKind.Healing?"+"+feedback.Amount:feedback.Amount.ToString();
            return ShowOwnedTextAsync(text,position,ct,owner,true);
        }

        private void Awake()
        {
            if (poolParent == null)
                poolParent = transform;

            for (int i = 0; i < initialPoolSize; i++)
            {
                var instance = CreateNewInstance();
                if (instance != null) pool.Enqueue(instance);
            }
        }

        /// <summary>
        /// 指定 World 座標にダメージ数値を表示する。
        /// </summary>
        public void ShowDamage(int damage, Vector3 worldPosition, CancellationToken ct)
        {
            ShowDamageAsync(damage, worldPosition, ct).Forget();
        }

        private UniTask ShowDamageAsync(int damage, Vector3 worldPosition, CancellationToken ct)
            => ShowOwnedDamageAsync(damage,worldPosition,ct,null);
        private UniTask ShowOwnedDamageAsync(int damage,Vector3 worldPosition,CancellationToken ct,object owner)
            =>ShowOwnedTextAsync(damage.ToString(),worldPosition,ct,owner,false);
        private async UniTask ShowOwnedTextAsync(string text,Vector3 worldPosition,CancellationToken ct,object owner,bool perEffect)
        {
            if (isDestroyed || (owner!=null && hiddenOwners.Contains(owner))) return;
            var instance = TakeInstance(!perEffect);
            if (instance == null) return;
            if(perEffect&&owner!=null){
                var bounds=instance.PrepareTextBounds(text,worldPosition);
                float bottom=bounds.min.y,gap=Mathf.Max(.04f,bounds.size.y*.12f);
                // 残っている数値の実外周から積む。先頭だけ終了した連続表示でも段を重ねない。
                foreach(var pair in owners)if(ReferenceEquals(pair.Value,owner)&&pair.Key!=null&&pair.Key.gameObject.activeSelf)
                    bottom=Mathf.Max(bottom,pair.Key.UnclampedTextBounds.max.y+gap);
                worldPosition.y+=bottom-bounds.min.y;
            }
            owners[instance]=owner;
            // 貸出時に表示中の敵ラベル参照を集める。boundsと可視性は各描画frameで再評価する。
            var labels=perEffect&&owner!=null?FeedbackLabels(owner):null;
            instance.AlignViewport=perEffect&&owner!=null?()=>WorldDamageNumberView.KeepGroupBelowViewportTop(owners.Where(pair=>ReferenceEquals(pair.Value,owner)).Select(pair=>pair.Key),labels):null;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, clearCts.Token);
            try
            {
                await instance.ShowTextAsync(text, worldPosition, linkedCts.Token);
            }
            finally
            {
                Return(instance);
            }
        }

        private static Renderer[] FeedbackLabels(object owner)
        {
            var own=owner is Component component?component.GetComponentsInChildren<TextMesh>():new TextMesh[0];
            return own.Concat(UnityEngine.Object.FindObjectsByType<Olca.Presentation.Field.WorldEnemyView>(FindObjectsSortMode.None)
                .SelectMany(view=>view.GetComponentsInChildren<TextMesh>())).Select(label=>label.GetComponent<Renderer>())
                .Where(renderer=>renderer!=null).Distinct().ToArray();
        }

        private WorldDamageNumberView Get()=>TakeInstance(true);
        private WorldDamageNumberView TakeInstance(bool enforceLimit)
        {
            if (isDestroyed) return null;
            WorldDamageNumberView instance = null;
            // 外部で破棄された待機Viewは再貸出しない。
            while (pool.Count > 0 && instance == null)
                instance = pool.Dequeue();

            if (instance == null)
            {
                activeInstances.RemoveWhere(view => view == null);
                if (enforceLimit && activeInstances.Count >= maxPoolSize)
                {
                    Debug.LogWarning($"[WorldDamageNumberPool] 最大プールサイズ({maxPoolSize})に達しています。");
                    return null;
                }
                instance = CreateNewInstance();
            }

            if (instance != null) activeInstances.Add(instance);
            return instance;
        }

        private void Return(WorldDamageNumberView instance)
        {
            // 完了済みの貸出だけを一度返却する。破棄済みViewも管理対象から除く。
            if (ReferenceEquals(instance, null) || !activeInstances.Remove(instance)) return;
            owners.Remove(instance);
            if (instance == null || isDestroyed) return;
            instance.AlignViewport=null;
            instance.gameObject.SetActive(false);
            // 同時ヒットの一時超過は表示するが、待機プールの保持数は増やさない。
            if(pool.Count>=maxPoolSize){DestroyInstance(instance);return;}
            pool.Enqueue(instance);
        }

        private WorldDamageNumberView CreateNewInstance()
        {
            if (prefab == null)
            {
                Debug.LogError("[WorldDamageNumberPool] Prefab が未設定です。Inspector でアサインしてください。");
                return null;
            }
            var instance = Instantiate(prefab, poolParent);
            instance.gameObject.SetActive(false);
            return instance;
        }

        public void ClearAll()
        {
            if (isDestroyed) return;
            foreach (var instance in activeInstances)
            {
                if (instance != null) instance.gameObject.SetActive(false);
            }
            // キャンセル完了前に再貸出すると、古い表示のfinallyが新しい表示を消してしまう。
            // 待機キューへの返却はShowDamageAsyncのfinallyに任せる。
            var previous = clearCts;
            clearCts = new CancellationTokenSource();
            previous.Cancel();
            previous.Dispose();
        }

        private void OnDestroy()
        {
            if (isDestroyed) return;
            isDestroyed = true;
            clearCts.Cancel();
            clearCts.Dispose();
            // poolParentが外部Transformの場合も所有するViewを残さない。
            foreach (var instance in activeInstances) DestroyInstance(instance);
            foreach (var instance in pool) DestroyInstance(instance);
            activeInstances.Clear();
            owners.Clear();hiddenOwners.Clear();
            pool.Clear();
        }

        private static void DestroyInstance(WorldDamageNumberView instance)
        {
            if (instance == null) return;
            if (UnityEngine.Application.isPlaying) Destroy(instance.gameObject);
            else DestroyImmediate(instance.gameObject);
        }
    }
}
