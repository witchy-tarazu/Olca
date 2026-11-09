using System;
using System.Linq;
using UnityEngine;
namespace Olca.Unity
{
    /// <summary>カメラの排他借用とPrefab基準への返却。追従・論理時計は利用Viewが所有する。</summary>
    public sealed class CameraRuntimeManager : IDisposable
    {
        public sealed class Rental : IDisposable
        {
            private CameraRuntimeManager owner;
            public Camera Camera { get; private set; }
            public object Borrower { get; private set; }
            internal Rental(CameraRuntimeManager owner,Camera camera,object borrower){this.owner=owner;Camera=camera;Borrower=borrower;}
            internal void Detach(){owner=null;Camera=null;Borrower=null;}
            public void Dispose(){owner?.Return(this);}
        }
        public sealed class DisplayLease : IDisposable
        {
            private CameraRuntimeManager owner;
            internal DisplayLease(CameraRuntimeManager value){owner=value;}
            internal void Detach(){owner=null;}
            public void Dispose(){owner?.ReleaseDisplay(this);}
        }
        private readonly System.Collections.Generic.HashSet<DisplayLease> displays=new System.Collections.Generic.HashSet<DisplayLease>();
        private readonly ResourceManager resources;
        private readonly Transform parent;
        private ResourceManager.InstanceLease instance;
        private Rental active;
        private GameObject prefab;
        private bool disposed;
        public bool IsBorrowed=>active!=null;
        public Camera BorrowedCamera=>active?.Camera;
        public CameraRuntimeManager(ResourceManager resources,Transform parent){this.resources=resources;this.parent=parent;}
        public Rental Borrow(object borrower)
        {
            if(disposed)throw new ObjectDisposedException(nameof(CameraRuntimeManager));
            if(borrower==null)throw new ArgumentNullException(nameof(borrower));
            if(active!=null)throw new InvalidOperationException("カメラは既に借用中です");
            RejectForeignRendering();EnsureInstance();
            Reset();var camera=instance.Instance.GetComponent<Camera>();active=new Rental(this,camera,borrower);instance.Instance.SetActive(true);return active;
        }
        /// <summary>タイトルの表示期間だけ同じRootCameraを保持。host借用とは排他的な別Cameraを作らない。</summary>
        public DisplayLease HoldDisplay()
        {
            if(disposed)throw new ObjectDisposedException(nameof(CameraRuntimeManager));
            RejectForeignRendering();EnsureInstance();if(active==null&&displays.Count==0)Reset();
            var lease=new DisplayLease(this);displays.Add(lease);instance.Instance.SetActive(true);return lease;
        }
        private void EnsureInstance()
        {
            if(instance!=null&&instance.Instance!=null)return;
            instance?.Dispose();prefab=resources.GetShared<GameObject>("OlcaSystem/GameplayCamera");instance=resources.InstantiatePrefab("OlcaSystem/GameplayCamera",parent);instance.Instance.SetActive(false);
        }
        private void RejectForeignRendering()
        {
            var own=instance?.Instance;var camera=own!=null?own.GetComponent<Camera>():null;var listener=own!=null?own.GetComponent<AudioListener>():null;
            if(UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Any(x=>x!=camera&&x.isActiveAndEnabled)||UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Any(x=>x!=listener&&x.isActiveAndEnabled))throw new InvalidOperationException("既存カメラ/Listenerとの二重起動を拒否します");
        }
        private void ReleaseDisplay(DisplayLease value)
        {
            if(!displays.Remove(value))return;value.Detach();if(active==null&&displays.Count==0&&instance?.Instance!=null)instance.Instance.SetActive(false);
        }
        private void Reset()
        {
            if(instance?.Instance==null)return;
            var value=instance.Instance;value.SetActive(false);
            var source=prefab.GetComponent<Camera>();var camera=value.GetComponent<Camera>();camera.CopyFrom(source);
            // 利用Viewの一時行列/表示比率/RTを次のホストへ持ち越さない。元Prefabは自動投影。
            camera.ResetAspect();camera.ResetProjectionMatrix();camera.ResetWorldToCameraMatrix();camera.ResetCullingMatrix();
            value.transform.localPosition=prefab.transform.localPosition;value.transform.localRotation=prefab.transform.localRotation;value.transform.localScale=prefab.transform.localScale;
            camera.enabled=source.enabled;value.GetComponent<AudioListener>().enabled=prefab.GetComponent<AudioListener>().enabled;
            // 追加URP componentはPrefabのまま保持。既存ViewはCamera/Transformだけを更新する。

        }
        private void Return(Rental value)
        {
            if(!ReferenceEquals(active,value))return;
            // リセット失敗時は所有を保持してRetryReturnで再試行する。
            Reset();active=null;value.Detach();if(displays.Count!=0)instance.Instance.SetActive(true);
        }
        public void RetryReturn(){if(active!=null)Return(active);}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            try{active?.Dispose();}catch(Exception error){Debug.LogException(error);}
            finally{foreach(var lease in displays)lease.Detach();displays.Clear();active?.Detach();active=null;instance?.Dispose();instance=null;prefab=null;}
        }
    }
}
