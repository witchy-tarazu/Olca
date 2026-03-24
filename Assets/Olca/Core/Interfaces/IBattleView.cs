using System.Threading.Tasks;

namespace Olca.Core.Interfaces
{
    /// <summary>
    /// バトルViewの抽象インターフェース。
    /// Application層がPresentation層のBattleViewに依存しないようにするための抽象化。
    /// </summary>
    public interface IBattleView
    {
        /// <summary>カスタム画面をスライドインで表示する</summary>
        void OpenCustomScreen();

        /// <summary>カスタム画面をスライドアウトで閉じる（アニメーション完了まで待つ）</summary>
        Task CloseCustomScreenAsync();
    }
}
