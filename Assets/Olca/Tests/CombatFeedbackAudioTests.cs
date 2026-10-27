using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Unity;
namespace Olca.Tests
{
    public class CombatFeedbackAudioTests
    {
        private static BattleSystem Battle(out Enemy enemy)
        {
            enemy=new Enemy("fixture","テスト",100,ElementType.None,new Olca.Core.Domain.Vector2Int(4,0));
            var card=new Card("fixture","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");
            return new BattleSystem(new PlayerCharacter(100),new List<Enemy>{enemy},new Deck(new List<Card>{card}));
        }
        [TestCase("damage",96,CombatFeedbackKind.Damage)] [TestCase("defense",72,CombatFeedbackKind.Guard)] [TestCase("healing",128,CombatFeedbackKind.Healing)]
        public void 戦闘仮音は低ピーク短音PCMで両端ゼロと微小DCを保つ(string name,int milliseconds,CombatFeedbackKind kind)
        {
            var clip=CombatFeedbackAudio.GetClip(kind);Assert.IsNotNull(clip);Assert.AreSame(clip,CombatFeedbackAudio.GetClip(kind));Assert.AreEqual(1,clip.channels);Assert.AreEqual(48000,clip.frequency);Assert.AreEqual(milliseconds*48,clip.samples);
            using(var input=new BinaryReader(File.OpenRead(Path.Combine(UnityEngine.Application.dataPath,"Resources/OlcaBattleSounds",name+".wav")))){
                Assert.AreEqual("RIFF",new string(input.ReadChars(4)));input.ReadInt32();Assert.AreEqual("WAVE",new string(input.ReadChars(4)));Assert.AreEqual("fmt ",new string(input.ReadChars(4)));Assert.AreEqual(16,input.ReadInt32());Assert.AreEqual(1,input.ReadInt16());Assert.AreEqual(1,input.ReadInt16());Assert.AreEqual(48000,input.ReadInt32());input.ReadBytes(6);Assert.AreEqual(16,input.ReadInt16());Assert.AreEqual("data",new string(input.ReadChars(4)));int count=input.ReadInt32()/2;var samples=Enumerable.Range(0,count).Select(_=>input.ReadInt16()/32768.0).ToArray();
                Assert.AreEqual(0,samples[0]);Assert.AreEqual(0,samples.Last());Assert.Less(samples.Max(System.Math.Abs),.081);Assert.Greater(samples.Max(System.Math.Abs),.079);Assert.Less(System.Math.Abs(samples.Average()),.00001);Assert.Less(System.Math.Abs(samples[1]),.0001);Assert.Less(System.Math.Abs(samples[count-2]),.0001);Assert.Less(samples.Max(System.Math.Abs)*CombatFeedbackAudio.MaximumVoices,1);}
        }
        [Test] public void 防御系は素材を共有し未知の種類は発音しない()
        {
            Assert.AreSame(CombatFeedbackAudio.GetClip(CombatFeedbackKind.Guard),CombatFeedbackAudio.GetClip(CombatFeedbackKind.Immune));Assert.AreSame(CombatFeedbackAudio.GetClip(CombatFeedbackKind.Guard),CombatFeedbackAudio.GetClip(CombatFeedbackKind.BarrierDamage));Assert.IsNull(CombatFeedbackAudio.GetClip((CombatFeedbackKind)99));
        }
        [Test] public void 個別通知だけを一度ずつ受け同じ所有者の再束縛で重複せず解除後は受けない()
        {
            var b=Battle(out var enemy);var events=new List<CombatFeedbackKind>();using(var subscription=new CombatFeedbackSubscription(f=>events.Add(f.Kind))){subscription.Bind(b);subscription.Bind(b);enemy.TakeDamage(2,ElementType.None);enemy.Heal(1);enemy.Guard.Set(true,false);enemy.TakeDamage(2,ElementType.None);
                CollectionAssert.AreEqual(new[]{CombatFeedbackKind.Damage,CombatFeedbackKind.Healing,CombatFeedbackKind.Guard},events);subscription.Bind(null);enemy.Guard.Clear();enemy.TakeDamage(1,ElementType.None);Assert.AreEqual(3,events.Count);subscription.Bind(b);enemy.TakeDamage(1,ElementType.None);Assert.AreEqual(4,events.Count);subscription.Dispose();b.Player.TakeDamage(1);Assert.AreEqual(4,events.Count);subscription.Bind(b);enemy.TakeDamage(1,ElementType.None);Assert.AreEqual(4,events.Count);}
        }
        [Test] public void 召喚も一度ずつ購読し戦闘交換後は旧敵旧プレイヤーから受けない()
        {
            var old=Battle(out var enemy);var next=Battle(out var other);var events=new List<CombatFeedbackKind>();using(var subscription=new CombatFeedbackSubscription(f=>events.Add(f.Kind))){subscription.Bind(old);var summon=new Enemy("summon","テスト",100,ElementType.None,new Olca.Core.Domain.Vector2Int(5,0),isSummoned:true);old.AddSummon(summon,new List<IEnemyAIAction>{new BasicAttackAction(0,10000)});subscription.Bind(old);summon.TakeDamage(1,ElementType.None);Assert.AreEqual(1,events.Count);subscription.Bind(next);enemy.TakeDamage(1,ElementType.None);old.Player.TakeDamage(1);summon.TakeDamage(1,ElementType.None);Assert.AreEqual(1,events.Count);other.TakeDamage(1,ElementType.None);next.Player.TakeDamage(1);Assert.AreEqual(3,events.Count);}
        }
        [Test] public void 無効Controllerへの開始通知で音声を再束縛せず再有効後だけ束縛できる()
        {
            var host=new GameObject("DisabledCombatAudio");var controller=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var router=new GameplayAudioRouter(host.transform);var audio=new CombatFeedbackAudio(host.transform,router);var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;var type=controller.GetType();type.GetField("combatFeedbackAudio",flags).SetValue(controller,audio);var bind=type.GetMethod("BindBattleFeedback",flags);var battle=Battle(out var enemy);
            try{controller.enabled=false;bind.Invoke(controller,new object[]{battle});enemy.TakeDamage(1,ElementType.None);Assert.IsFalse(audio.TryPlay(CombatFeedbackKind.Damage));Assert.AreEqual(0,host.GetComponentsInChildren<AudioSource>().Count(x=>x.name.StartsWith("CombatFeedbackAudio_")));
                controller.enabled=true;bind.Invoke(controller,new object[]{battle});Assert.AreSame(battle,typeof(CombatFeedbackAudio).GetField("battle",flags).GetValue(audio));controller.enabled=false;type.GetMethod("OnDisable",flags).Invoke(controller,null);enemy.TakeDamage(1,ElementType.None);Assert.IsFalse(audio.TryPlay(CombatFeedbackKind.Damage));
            }finally{audio.Dispose();router.Dispose();Object.DestroyImmediate(host);}
        }
        [Test] public void 借用音声の登録解除は元音量を戻し以後の設定と休止の対象から外す()
        {
            var host=new GameObject("FeedbackAudioTest");var source=host.AddComponent<AudioSource>();source.volume=.4f;using(var router=new GameplayAudioRouter(host.transform)){
                router.Register(source,GameplayAudioChannel.Effects);router.Apply(new AudioVolumeSettings(20,100,50,100));Assert.AreEqual(.04f,source.volume,.00001f);router.Unregister(source);Assert.AreEqual(.4f,source.volume,.00001f);router.Apply(new AudioVolumeSettings(0));Assert.AreEqual(.4f,source.volume,.00001f);router.Unregister(source);router.Register(source,GameplayAudioChannel.Effects);Assert.AreEqual(0,source.volume);router.Unregister(source);Assert.AreEqual(.4f,source.volume,.00001f);
            }Object.DestroyImmediate(host);
        }
    }
}
