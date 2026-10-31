using System;
using UnityEngine;
using UnityEngine.EventSystems;
namespace Olca.Unity
{
    /// <summary>共通OverlayとUI入力口の寿命。内容・選択は一度に一つのhostへ貸し出す。</summary>
    public sealed class UIRuntimeManager : IDisposable
    {
        private readonly ResourceManager resources;
        private readonly Transform parent;
        private ResourceManager.InstanceLease canvas, events;
        private object owner;
        private GameObject content;
        private bool disposed;
        public UIRuntimeManager(ResourceManager resources, Transform parent) { this.resources=resources;this.parent=parent; }
        public GameObject AcquireContent(object requestedOwner)
        {
            if(disposed)throw new ObjectDisposedException(nameof(UIRuntimeManager));
            if(requestedOwner==null)throw new ArgumentNullException(nameof(requestedOwner));
            if(owner!=null) { if(ReferenceEquals(owner,requestedOwner)&&content!=null)return content;throw new InvalidOperationException("UIのhost所有者が重複しています"); }
            EnsureInfrastructure();
            var value=new GameObject("GameplayPauseCanvas",typeof(RectTransform));
            var rect=(RectTransform)value.transform;rect.SetParent(canvas.Instance.transform,false);rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
            content=value;owner=requestedOwner;return value;
        }
        private void EnsureInfrastructure()
        {
            if(canvas!=null&&events!=null)return;
            if(EventSystem.current!=null)throw new InvalidOperationException("既存EventSystemと共通UIを二重起動できません");
            try { canvas=resources.InstantiatePrefab("OlcaSystem/UIRoot",parent);events=resources.InstantiatePrefab("OlcaSystem/EventSystem",parent); }
            catch {events?.Dispose();canvas?.Dispose();events=null;canvas=null;throw;}
        }
        public void ReleaseContent(object requestedOwner)
        {
            if(!ReferenceEquals(owner,requestedOwner))return;
            var value=content;content=null;owner=null;
            var selected=EventSystem.current?.currentSelectedGameObject;
            if(value!=null&&selected!=null&&selected.transform.IsChildOf(value.transform))EventSystem.current.SetSelectedGameObject(null);
            if(value!=null){value.SetActive(false);if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        }
        public void Dispose(){if(disposed)return;disposed=true;ReleaseContent(owner);events?.Dispose();canvas?.Dispose();events=null;canvas=null;}
    }
}
