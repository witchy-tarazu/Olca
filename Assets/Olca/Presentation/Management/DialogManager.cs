using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Olca.Unity;

namespace Olca.Presentation.Management
{
    public interface IDialogOwner { bool IsCurrent { get; } }

    /// <summary>未移行の親表示に結び付くDialog専用寿命。画面や資源の所有を偽装しない。</summary>
    public sealed class DialogOwnerScope : IDialogOwner, IDisposable
    {
        private DialogManager manager;
        private Func<bool> valid;
        internal DialogOwnerScope(DialogManager manager,Func<bool> valid){this.manager=manager;this.valid=valid;}
        public bool IsCurrent=>manager!=null&&(valid?.Invoke()??false);
        public void Dispose(){var owner=manager;manager=null;valid=null;owner?.CancelOwner(this);}
    }
    public enum DialogResult { Confirmed, Cancelled, ParentClosed, Busy }

    /// <summary>借用するポップアップの表示と結果だけを所有する。親画面や保存は所有しない。</summary>
    public sealed class DialogManager : IDisposable
    {
        private sealed class Ticket
        {
            public IDialogOwner Owner;
            public Func<bool> ConfirmAttempt;
            public bool Attempting, HasAttempted;
            public int LastAttemptFrame;
            public GameObject Panel, PreviousFocus;
            public Button Confirm, Cancel;
            public UnityAction ConfirmAction, CancelAction;
            public MenuPadScope Menu;
            public Action<DialogResult> Completed;
            public readonly TaskCompletionSource<DialogResult> Result = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            public bool Closed;
        }
        private readonly Func<int> frame;
        private Ticket current;
        private IDialogOwner closedOwner;
        private int closedFrame;
        private bool disposed;
        public int ActiveCount => current == null ? 0 : 1;
        public DialogManager(Func<int> frame) { this.frame = frame ?? throw new ArgumentNullException(nameof(frame)); }
        public bool Blocks(IDialogOwner owner) => owner != null && (ReferenceEquals(current?.Owner, owner) || ReferenceEquals(closedOwner, owner) && closedFrame == frame());
        public DialogOwnerScope CreateOwner(Func<bool> valid)
        {
            if(disposed)throw new ObjectDisposedException(nameof(DialogManager));
            return new DialogOwnerScope(this,valid??throw new ArgumentNullException(nameof(valid)));
        }
        public Task<DialogResult> Show(IDialogOwner owner, GameObject panel, Button confirm, Button cancel, Action<DialogResult> completed)
            =>ShowCore(owner,panel,confirm,cancel,null,completed);
        /// <summary>保存の再試行は同ticket内で実行する。true時だけ閉じ、falseなら更新済み表示を保つ。</summary>
        public Task<DialogResult> ShowPersistent(IDialogOwner owner,GameObject panel,Button confirm,Button cancel,Func<bool> attempt,Action<DialogResult> completed)
            =>ShowCore(owner,panel,confirm,cancel,attempt??throw new ArgumentNullException(nameof(attempt)),completed);
        private Task<DialogResult> ShowCore(IDialogOwner owner,GameObject panel,Button confirm,Button cancel,Func<bool> attempt,Action<DialogResult> completed)
        {
            if (disposed || owner == null || !owner.IsCurrent || panel == null || confirm == null || cancel == null)
                return Task.FromResult(DialogResult.ParentClosed);
            if (current != null) return ReferenceEquals(current.Owner, owner) && current.Panel == panel ? current.Result.Task : Task.FromResult(DialogResult.Busy);
            var ticket = new Ticket { Owner = owner, Panel = panel, Confirm = confirm, Cancel = cancel, Completed = completed,
                PreviousFocus = EventSystem.current?.currentSelectedGameObject, ConfirmAttempt=attempt };
            // 閉じたticketを捕捉した旧クリックは、次のDialogを確定できない。
            ticket.ConfirmAction = () => Confirm(ticket);
            ticket.CancelAction = () => {if(CanOperate(ticket,ticket.Cancel))Close(ticket,DialogResult.Cancelled);};
            current = ticket;
            confirm.onClick.AddListener(ticket.ConfirmAction); cancel.onClick.AddListener(ticket.CancelAction);
            EventSystem.current?.SetSelectedGameObject(null);
            panel.SetActive(true); panel.transform.SetAsLastSibling();
            ticket.Menu = new MenuPadScope(() => !ticket.Closed && ticket.Owner.IsCurrent && MenuPadScope.IsTopmost(panel.transform));
            ticket.Menu.Bind(panel.transform, cancel.name, ticket.CancelAction.Invoke);
            return ticket.Result.Task;
        }
        private bool CanOperate(Ticket ticket,Button button)=>!ticket.Closed&&!ticket.Attempting&&ReferenceEquals(current,ticket)&&ticket.Owner.IsCurrent&&button!=null&&button.isActiveAndEnabled&&button.IsInteractable();
        private void Confirm(Ticket ticket)
        {
            if(!CanOperate(ticket,ticket.Confirm))return;
            if(ticket.ConfirmAttempt==null){Close(ticket,DialogResult.Confirmed);return;}
            if(ticket.HasAttempted&&ticket.LastAttemptFrame==frame())return;
            ticket.HasAttempted=true;ticket.LastAttemptFrame=frame();ticket.Attempting=true;
            bool close;
            try{close=ticket.ConfirmAttempt();}
            finally{ticket.Attempting=false;}
            if(ticket.Closed||!ReferenceEquals(current,ticket))return;
            if(close){Close(ticket,DialogResult.Confirmed);return;}
            // 再試行結果で確定/取消の表示が変わるため、現在有効な操作へ再束縛する。
            ticket.Menu.Bind(ticket.Panel.transform,ticket.Cancel.name,ticket.CancelAction.Invoke);
        }
        private void Close(Ticket ticket, DialogResult result)
        {
            if (ticket == null || ticket.Closed || !ReferenceEquals(current, ticket)) return;
            ticket.Closed = true; current = null; closedOwner = ticket.Owner; closedFrame = frame();
            if (ticket.Confirm != null) ticket.Confirm.onClick.RemoveListener(ticket.ConfirmAction);
            if (ticket.Cancel != null) ticket.Cancel.onClick.RemoveListener(ticket.CancelAction);
            ticket.Menu?.Dispose(); ticket.Menu = null;
            if (ticket.Panel != null) ticket.Panel.SetActive(false);
            var callback = ticket.Completed; ticket.Completed = null; ticket.ConfirmAttempt=null;
            bool restore = result != DialogResult.ParentClosed && ticket.Owner.IsCurrent;
            var focus = ticket.PreviousFocus; ticket.PreviousFocus = null;
            if (restore && focus != null && focus.activeInHierarchy && focus.GetComponent<Selectable>()?.IsInteractable() == true)
                EventSystem.current?.SetSelectedGameObject(focus);
            else {
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (selected != null && ticket.Panel != null && selected.transform.IsChildOf(ticket.Panel.transform)) EventSystem.current.SetSelectedGameObject(null);
            }
            try { if (restore) callback?.Invoke(result); }
            finally { ticket.Result.TrySetResult(result); }
        }
        public void CancelOwner(IDialogOwner owner)
        {
            if (ReferenceEquals(current?.Owner, owner)) Close(current, DialogResult.ParentClosed);
            if (ReferenceEquals(closedOwner, owner)) closedOwner = null;
        }
        public void Tick()
        {
            if (disposed) return;
            if (closedOwner != null && closedFrame != frame()) closedOwner = null;
            if (current != null && (!current.Owner.IsCurrent || current.Panel == null || !current.Panel.activeInHierarchy)) Close(current, DialogResult.ParentClosed);
            current?.Menu.Tick();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            if (current != null) Close(current, DialogResult.ParentClosed);
            closedOwner = null;
        }
    }
}
