using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Olca.Presentation.Management
{
    /// <summary>pauseの表示寿命だけを所有する。時間停止・保存・進行はControllerが判断する。</summary>
    public sealed class GameplayPauseView : MonoBehaviour
    {
        private ManagedScreenContext context;
        private Func<bool> inputAllowed;
        private readonly Dictionary<Button,Button.ButtonClickedEvent> actions=new Dictionary<Button,Button.ButtonClickedEvent>();
        private bool covered;
        private CanvasGroup presentation;
        private GameObject previousFocus;
        private string restoredFocus;
        public string TakeRestoredFocus(){var value=restoredFocus;restoredFocus=null;return value;}
        public ManagedScreenContext Context=>context;
        public bool Covered=>covered;
        public bool BlocksInput=>context?.DialogBlocksInput==true;
        public void Initialize(Func<bool> allowed)
        {
            inputAllowed=allowed;presentation=gameObject.AddComponent<CanvasGroup>();
            foreach(var button in GetComponentsInChildren<Button>(true))actions.Add(button,button.onClick);
        }
        public void Show(ManagedScreenContext owner)
        {
            context=owner;covered=false;restoredFocus=null;presentation.alpha=1;presentation.blocksRaycasts=true;
            foreach(var pair in actions){
                var captured=owner;var action=pair.Value;var button=pair.Key;
                button.onClick=new Button.ButtonClickedEvent();
                button.onClick.AddListener(()=>{if(this!=null&&ReferenceEquals(context,captured)&&captured.IsCurrent&&!captured.DialogBlocksInput&&(inputAllowed?.Invoke()??false))action.Invoke();});
            }
            gameObject.SetActive(true);transform.SetAsLastSibling();
        }
        public void Hide(){context=null;covered=false;previousFocus=null;restoredFocus=null;presentation.alpha=1;presentation.blocksRaycasts=true;gameObject.SetActive(false);}
        public void CoverForDialog()
        {
            previousFocus=EventSystem.current?.currentSelectedGameObject;covered=true;presentation.alpha=0;presentation.blocksRaycasts=false;
        }
        public void RestoreAfterDialog()
        {
            if(context?.IsCurrent!=true)return;
            covered=false;presentation.alpha=1;presentation.blocksRaycasts=true;gameObject.SetActive(true);transform.SetAsLastSibling();
            var focus=previousFocus;previousFocus=null;
            if(focus!=null&&focus.activeInHierarchy&&focus.GetComponent<Selectable>()?.IsInteractable()==true){EventSystem.current?.SetSelectedGameObject(focus);restoredFocus=focus.name;}
        }
        private void OnDisable(){context?.NotifyHidden();context=null;}
        private void OnDestroy(){context?.NotifyHidden();context=null;inputAllowed=null;previousFocus=null;actions.Clear();}
    }
}
