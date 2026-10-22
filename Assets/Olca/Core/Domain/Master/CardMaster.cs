using Olca.Core.Logic.CardEffects.Effects;

namespace Olca.Core.Domain.Master
{
    /// <summary>
    /// カードマスターデータ
    /// MasterMemoryで管理されるカード情報
    /// </summary>
#if !OLCA_PORTABLE
    [MasterMemory.MemoryTable("cards"), MessagePack.MessagePackObject(true)]
#endif
    public class CardMaster
    {
#if !OLCA_PORTABLE
        [MasterMemory.PrimaryKey]
#endif
        public string Id { get; set; }

        public string Name { get; set; }

        public int Cost { get; set; }

        public CardRarity Rarity { get; set; }

        public CardCategory Category { get; set; }

        public CardSubCategory SubCategory { get; set; }

        public ElementType Element { get; set; }

        public AttackProperty Properties { get; set; }
        public int? PoiseDamage { get; set; }
        public int HitInvincibilityMs { get; set; }

        public SeriesType Series { get; set; }

        public int Power { get; set; }

        public string Description { get; set; }

        // ========== 攻撃範囲指定 ==========
        /// <summary>
        /// 攻撃カードの基本対象タイプ（Single, All, FrontTile等）
        /// CategoryがAttackの場合に使用される
        /// </summary>
        public DamageTargetType TargetType { get; set; }

        // ========== フィールド操作カード用 ==========
        /// <summary>
        /// 変化させるパネルの種類（CategoryがFieldの場合に使用）
        /// 例: "Ice", "Magma", "Grass" など
        /// </summary>
        public string PanelChangeType { get; set; }

        // ========== 一時移動カード専用 ==========
        /// <summary>
        /// 一時移動方向（"Right", "Left", "Up", "Down"）
        /// MoveMotionTime > 0 の場合に使用される
        /// </summary>
        public string MoveDirection { get; set; }

        /// <summary>
        /// 一時移動距離（マス数）
        /// MoveMotionTime > 0 の場合に使用される
        /// </summary>
        public int MoveDistance { get; set; }

        /// <summary>
        /// 一時移動カードの移動モーション時間（秒）
        /// 0より大きい場合、一時移動カードとして扱われる
        /// </summary>
        public float MoveMotionTime { get; set; }

        /// <summary>
        /// 一時移動カードの攻撃モーション時間（秒）
        /// </summary>
        public float AttackMotionTime { get; set; }

        /// <summary>
        /// 一時移動時の貫通モード
        /// </summary>
        public MovePenetrationMode PenetrationMode { get; set; }

        // ========== 硬直時間 ==========
        /// <summary>
        /// 前隙時間（秒） - カード発動からヒット判定が出るまでの時間
        /// この時間中、ダメージ判定は発生しない
        /// </summary>
        public float StartupTime { get; set; }

        /// <summary>
        /// 後隙時間（秒） - ヒット判定後、行動可能になるまでの時間
        /// この時間中、プレイヤーは移動や他のカード使用ができない
        /// </summary>
        public float RecoveryTime { get; set; }
        /// <summary>明示フレーム値。省略時は既存秒値をそのまま使用する。</summary>
        public int? StartupFrames { get; set; }
        public int? RecoveryFrames { get; set; }
        /// <summary>後隙開始からの受付開始。省略時は後隙を短縮しない。</summary>
        public int? RecoveryCancelAfterFrames { get; set; }

        /// <summary>
        /// 派生効果中も硬直を継続するか
        /// true: 派生効果終了まで硬直が続く（例: 三段突き）
        /// false: 派生効果中でも動ける（例: スプレッドガン）
        /// </summary>
        public bool ExtendRecoveryDuringDerivative { get; set; }

        /// <summary>
        /// 暗転系カード（前隙・後隙なしで即座に発動）
        /// true: 前隙・後隙を無視して即座に効果発動
        /// false: 通常の前隙・後隙処理を行う
        /// </summary>
        public bool IsInstantEffect { get; set; }

        // ========== 追加効果（派生効果）==========
        /// <summary>
        /// 追加効果のタイプ（カンマ区切りで複数指定可能）
        /// 例: "ChainCard", "PanelIceAll", "MultiHit"
        /// </summary>
        public string AdditionalEffectTypes { get; set; }

        /// <summary>
        /// チェイン発動するカードID
        /// - 空文字列またはnull: 自分自身を再発動
        /// - カードID指定: 別カードを発動（爆風カードなど）
        /// </summary>
        public string ChainCardId { get; set; }

        /// <summary>
        /// 追加効果の威力
        /// - ChainCardで自分自身を再発動する場合: 威力倍率（%）
        ///   例: 60 = 60%の威力（元が25なら15ダメージ）
        /// - 状態異常の場合: 継続ダメージ量
        /// </summary>
        public int AdditionalEffectPower { get; set; }

        /// <summary>
        /// 追加効果の持続時間（秒）
        /// 状態異常の持続時間など
        /// </summary>
        public float AdditionalEffectDuration { get; set; }
        public string PoisonKind { get; set; } = "poison";
        public int? PoisonIntervalFrames { get; set; }

        /// <summary>
        /// ヒット回数（多段ヒット、チェイン回数など）
        /// </summary>
        public int AdditionalEffectHitCount { get; set; }
        public bool[] AdditionalEffectHitInvincibilityStages { get; set; }

        /// <summary>
        /// 派生効果のトリガータイミング
        /// "Immediate", "OnHit", "OnDamage"
        /// </summary>
        public string DerivativeTrigger { get; set; }
    }
}
