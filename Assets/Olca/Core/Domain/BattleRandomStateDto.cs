using System;
using System.Runtime.Serialization;
using Olca.Core.Logic.Battle;
namespace Olca.Core.Domain
{
    /// <summary>戦闘開始境界だけの版付き状態。途中セーブや旧System.Randomの状態を表さない。</summary>
    [DataContract]
    public sealed class BattleRandomStateDto
    {
        public const string AlgorithmId="olca.xorshift32";
        [DataMember(IsRequired=true)] public string Algorithm;
        [DataMember(IsRequired=true)] public int Version;
        [DataMember(IsRequired=true)] public uint Seed;
        [DataMember(IsRequired=true)] public uint State;
        [DataMember(IsRequired=true)] public long Draws;
        public void Validate()
        {if(Algorithm!=AlgorithmId||Version!=1||State==0||Draws<0)throw new ArgumentException("戦闘乱数の識別・版・状態が不正または未対応です");}
        public static void ValidateRecord(int? format, BattleRandomStateDto state)
        {
            if(format.HasValue){if(format!=1||state==null)throw new ArgumentException("戦闘乱数保存の版が不正、または状態が欠損しています");state.Validate();}
            else if(state!=null)throw new ArgumentException("戦闘乱数保存の版が欠損しています");
        }
        public BattleRandomStateDto Copy()=>(BattleRandomStateDto)MemberwiseClone();
        public SeededBattleRandom Restore(){Validate();return new SeededBattleRandom(Seed,State,Draws);}
        public bool Matches(SeededBattleRandom random)=>random!=null&&Seed==random.Seed&&State==random.State&&Draws==random.Draws;
    }
}
