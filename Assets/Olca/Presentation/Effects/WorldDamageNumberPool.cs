using UnityEngine;
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

        private void Awake()
        {
            if (poolParent == null)
                poolParent = transform;

            for (int i = 0; i < initialPoolSize; i++)
                CreateNewInstance();
        }

        /// <summary>
        /// 指定 World 座標にダメージ数値を表示する。
        /// </summary>
        public void ShowDamage(int damage, Vector3 worldPosition, CancellationToken ct)
        {
            ShowDamageAsync(damage, worldPosition, ct).Forget();
        }

        private async UniTask ShowDamageAsync(int damage, Vector3 worldPosition, CancellationToken ct)
        {
            var instance = Get();
            if (instance == null) return;

            try
            {
                await instance.ShowAsync(damage, worldPosition, ct);
            }
            finally
            {
                Return(instance);
            }
        }

        private WorldDamageNumberView Get()
        {
            WorldDamageNumberView instance;

            if (pool.Count > 0)
            {
                instance = pool.Dequeue();
            }
            else
            {
                if (activeInstances.Count >= maxPoolSize)
                {
                    Debug.LogWarning($"[WorldDamageNumberPool] 最大プールサイズ({maxPoolSize})に達しています。");
                    return null;
                }
                instance = CreateNewInstance();
            }

            activeInstances.Add(instance);
            return instance;
        }

        private void Return(WorldDamageNumberView instance)
        {
            if (instance == null) return;
            activeInstances.Remove(instance);
            instance.gameObject.SetActive(false);
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
            pool.Enqueue(instance);
            return instance;
        }

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
