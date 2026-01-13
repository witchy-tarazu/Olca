using UnityEngine;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;

namespace Olca.Presentation.Effects
{
    /// <summary>
    /// ダメージ数値のオブジェクトプール
    /// DamageNumberViewを再利用してGCアロケーションを削減
    /// </summary>
    public class DamageNumberPool : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private DamageNumberView damageNumberPrefab;

        [Header("Pool Settings")]
        [SerializeField] private int initialPoolSize = 10;     // 初期プールサイズ
        [SerializeField] private int maxPoolSize = 20;         // 最大プールサイズ
        [SerializeField] private Transform poolParent;         // プールの親Transform

        private Queue<DamageNumberView> pool = new Queue<DamageNumberView>();
        private HashSet<DamageNumberView> activeInstances = new HashSet<DamageNumberView>();

        private void Awake()
        {
            // プールの親が未設定なら自身を使用
            if (poolParent == null)
            {
                poolParent = transform;
            }

            // 初期プール生成
            for (int i = 0; i < initialPoolSize; i++)
            {
                CreateNewInstance();
            }
        }

        /// <summary>
        /// プールから取得（なければ新規作成）
        /// </summary>
        private DamageNumberView Get()
        {
            DamageNumberView instance;

            if (pool.Count > 0)
            {
                instance = pool.Dequeue();
            }
            else
            {
                // プールが空なら新規作成（最大サイズ制限あり）
                if (activeInstances.Count >= maxPoolSize)
                {
                    Debug.LogWarning($"[DamageNumberPool] 最大プールサイズ({maxPoolSize})に達しています。");
                    return null;
                }
                instance = CreateNewInstance();
            }

            activeInstances.Add(instance);
            return instance;
        }

        /// <summary>
        /// プールに返却
        /// </summary>
        private void Return(DamageNumberView instance)
        {
            if (instance == null) return;

            activeInstances.Remove(instance);
            instance.gameObject.SetActive(false);
            pool.Enqueue(instance);
        }

        /// <summary>
        /// 新しいインスタンスを生成
        /// </summary>
        private DamageNumberView CreateNewInstance()
        {
            DamageNumberView instance = Instantiate(damageNumberPrefab, poolParent);
            instance.gameObject.SetActive(false);
            pool.Enqueue(instance);
            return instance;
        }

        /// <summary>
        /// ダメージ数値を表示（通常）
        /// </summary>
        public async UniTask ShowDamageAsync(int damage, Vector2 worldPosition, CancellationToken ct)
        {
            await ShowDamageAsync(damage, worldPosition, DamageNumberView.DamageType.Normal, ct);
        }

        /// <summary>
        /// ダメージ数値を表示（タイプ指定）
        /// </summary>
        public async UniTask ShowDamageAsync(int damage, Vector2 worldPosition, DamageNumberView.DamageType damageType, CancellationToken ct)
        {
            DamageNumberView instance = Get();
            if (instance == null)
            {
                Debug.LogWarning("[DamageNumberPool] インスタンスを取得できませんでした。");
                return;
            }

            try
            {
                // anchoredPositionをそのまま渡す（EnemyViewと同じ親・同じAnchor設定）
                await instance.ShowDamageAsync(damage, worldPosition, damageType, ct);
            }
            finally
            {
                // アニメーション終了後にプールへ返却
                Return(instance);
            }
        }

        /// <summary>
        /// すべてのアクティブなインスタンスをクリア
        /// </summary>
        public void ClearAll()
        {
            foreach (var instance in activeInstances)
            {
                if (instance != null)
                {
                    instance.gameObject.SetActive(false);
                    pool.Enqueue(instance);
                }
            }
            activeInstances.Clear();
        }

        private void OnDestroy()
        {
            ClearAll();
        }
    }
}
