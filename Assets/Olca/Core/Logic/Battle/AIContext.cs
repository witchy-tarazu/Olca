using System;
using Olca.Core.Domain;
using Olca.Core.Interfaces;

namespace Olca.Core.Logic.Battle
{
    /// <summary>
    /// AI判断に必要な情報をまとめた構造体
    /// </summary>
    public struct AIContext
    {
        /// <summary>AI行動を行う敵自身</summary>
        public Enemy Self { get; }

        /// <summary>プレイヤーキャラクター</summary>
        public PlayerCharacter Player { get; }

        /// <summary>バトルフィールド</summary>
        public BattleField Field { get; }

        /// <summary>乱数プロバイダー</summary>
        public IRandomProvider Random { get; }

        /// <summary>ロガー</summary>
        public IGameLogger Logger { get; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public AIContext(Enemy self, PlayerCharacter player, BattleField field,
                         IRandomProvider random, IGameLogger logger)
        {
            Self = self;
            Player = player;
            Field = field;
            Random = random;
            Logger = logger;
        }

        /// <summary>盲目は相手位置の取得を止める。待機等の対象不要な行動は止めない。</summary>
        public bool HasVisiblePlayer => Self != null && !Self.IsDead && Player != null && !Player.IsDead && !Self.IsBlind;

        /// <summary>決定した方向入力を反転する。対象自体の所属・HP・座標は変更しない。</summary>
        public Vector2Int ResolveDirectionalInput(Vector2Int direction)
            => Self != null && Self.IsConfused ? new Vector2Int(-direction.X, -direction.Y) : direction;

        /// <summary>
        /// プレイヤーとの距離（マンハッタン距離）を取得
        /// </summary>
        public int GetDistanceToPlayer()
        {
            if (!HasVisiblePlayer) return int.MaxValue;
            return Math.Abs(Player.GridPosition.X - Self.GridPosition.X) +
                   Math.Abs(Player.GridPosition.Y - Self.GridPosition.Y);
        }

        /// <summary>
        /// プレイヤーが右側にいるか
        /// </summary>
        public bool IsPlayerOnRight()
        {
            if (!HasVisiblePlayer) return false;
            return Player.GridPosition.X > Self.GridPosition.X;
        }

        /// <summary>
        /// プレイヤーが左側にいるか
        /// </summary>
        public bool IsPlayerOnLeft()
        {
            if (!HasVisiblePlayer) return false;
            return Player.GridPosition.X < Self.GridPosition.X;
        }

        /// <summary>
        /// プレイヤーが上側にいるか
        /// </summary>
        public bool IsPlayerAbove()
        {
            if (!HasVisiblePlayer) return false;
            return Player.GridPosition.Y > Self.GridPosition.Y;
        }

        /// <summary>
        /// プレイヤーが下側にいるか
        /// </summary>
        public bool IsPlayerBelow()
        {
            if (!HasVisiblePlayer) return false;
            return Player.GridPosition.Y < Self.GridPosition.Y;
        }
    }
}
