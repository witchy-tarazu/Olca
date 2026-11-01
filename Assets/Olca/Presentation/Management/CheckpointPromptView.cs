using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.Management
{
    /// <summary>checkpoint案内の表示世代と操作だけを管理し、復元/保存の判断は所有しない。</summary>
    public sealed class CheckpointPromptView : MonoBehaviour
    {
        private readonly Dictionary<Button,Button.ButtonClickedEvent> actions=new Dictionary<Button,Button.ButtonClickedEvent>();
        private Func<bool> allowed;
        private bool clicked;private int clickFrame;
        public ManagedScreenContext Context {get;private set;}
        public void Initialize(Func<bool> allowed){this.allowed=allowed;foreach(var b in GetComponentsInChildren<Button>(true))actions.Add(b,b.onClick);}
        public void Show(ManagedScreenContext context)
        {
            Context=context;clicked=false;
            foreach(var pair in actions){var action=pair.Value;var button=pair.Key;button.onClick=new Button.ButtonClickedEvent();button.onClick.AddListener(()=>{if(this!=null&&button!=null&&button.isActiveAndEnabled&&button.IsInteractable()&&ReferenceEquals(Context,context)&&context.IsCurrent&&!context.DialogBlocksInput&&(allowed?.Invoke()??false)&&(!clicked||clickFrame!=Time.frameCount)){clicked=true;clickFrame=Time.frameCount;action.Invoke();}});}
            gameObject.SetActive(true);transform.SetAsLastSibling();
        }
        public void Hide(){Context=null;gameObject.SetActive(false);}
        private void OnDisable(){Context?.NotifyHidden();Context=null;}
        private void OnDestroy(){Context?.NotifyHidden();Context=null;allowed=null;actions.Clear();}
    }
}
