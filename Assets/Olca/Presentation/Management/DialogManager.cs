using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Olca.Unity;

namespace Olca.Presentation.Management
{
    public enum DialogResult { Confirmed, Cancelled, ParentClosed, Busy }

    /// <summary>借用するポップアップの表示と結果だけを所有する。親画面や保存は所有しない。</summary>
    public sealed class DialogManager : IDisposable
    {
        private sealed class Ticket
        {
            public ManagedScreenContext Owner;
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
        private ManagedScreenContext closedOwner;
        private int closedFrame;
        private bool disposed;
        public int ActiveCount => current == null ? 0 : 1;
        public DialogManager(Func<int> frame) { this.frame = frame ?? throw new ArgumentNullException(nameof(frame)); }
        public bool Blocks(ManagedScreenContext owner) => owner != null && (ReferenceEquals(current?.Owner, owner) || ReferenceEquals(closedOwner, owner) && closedFrame == frame());
        public Task<DialogResult> Show(ManagedScreenContext owner, GameObject panel, Button confirm, Button cancel, Action<DialogResult> completed)
        {
            if (disposed || owner == null || !owner.IsCurrent || panel == null || confirm == null || cancel == null)
                return Task.FromResult(DialogResult.ParentClosed);
            if (current != null) return ReferenceEquals(current.Owner, owner) && current.Panel == panel ? current.Result.Task : Task.FromResult(DialogResult.Busy);
            var ticket = new Ticket { Owner = owner, Panel = panel, Confirm = confirm, Cancel = cancel, Completed = completed,
                PreviousFocus = EventSystem.current?.currentSelectedGameObject };
            // 閉じたticketを捕捉した旧クリックは、次のDialogを確定できない。
            ticket.ConfirmAction = () => Close(ticket, DialogResult.Confirmed);
            ticket.CancelAction = () => Close(ticket, DialogResult.Cancelled);
            current = ticket;
            confirm.onClick.AddListener(ticket.ConfirmAction); cancel.onClick.AddListener(ticket.CancelAction);
            EventSystem.current?.SetSelectedGameObject(null);
            panel.SetActive(true); panel.transform.SetAsLastSibling();
            ticket.Menu = new MenuPadScope(() => !ticket.Closed && ticket.Owner.IsCurrent && MenuPadScope.IsTopmost(panel.transform));
            ticket.Menu.Bind(panel.transform, cancel.name, ticket.CancelAction.Invoke);
            return ticket.Result.Task;
        }
        private void Close(Ticket ticket, DialogResult result)
        {
            if (ticket == null || ticket.Closed || !ReferenceEquals(current, ticket)) return;
            ticket.Closed = true; current = null; closedOwner = ticket.Owner; closedFrame = frame();
            if (ticket.Confirm != null) ticket.Confirm.onClick.RemoveListener(ticket.ConfirmAction);
            if (ticket.Cancel != null) ticket.Cancel.onClick.RemoveListener(ticket.CancelAction);
            ticket.Menu?.Dispose(); ticket.Menu = null;
            if (ticket.Panel != null) ticket.Panel.SetActive(false);
            var callback = ticket.Completed; ticket.Completed = null;
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
        public void CancelOwner(ManagedScreenContext owner)
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
