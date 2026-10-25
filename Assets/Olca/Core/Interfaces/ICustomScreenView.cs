using System;
using Olca.Core.Logic.Battle;

namespace Olca.Core.Interfaces
{
    /// <summary>
    /// カスタム画面Viewの抽象インターフェース。
    /// Application層がPresentation層のCustomScreenViewに依存しないようにするための抽象化。
    /// </summary>
    public interface ICombatantInspectionView
    {
        void BindCombatants(BattleSystem battle);
    }

    public interface ICustomScreenView
    {
        /// <summary>
        /// カスタム画面を初期化する。
        /// バトル開始時に CustomScreenSystem を紐づける。
        /// </summary>
        void Initialize(CustomScreenSystem customScreen);

        /// <summary>現在の戦闘の変身仮選択を接続する。セッション終了時はnullで解除する。</summary>
        void BindTransformations(TransformationSelectionState transformations);

        /// <summary>
        /// 決定ボタンが押されてカスタム画面が確定された時に発行されるイベント。
        /// </summary>
        event Action OnCustomScreenConfirmed;
    }
}
