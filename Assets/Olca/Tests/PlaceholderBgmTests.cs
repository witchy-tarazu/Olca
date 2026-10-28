using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Olca.Unity;
namespace Olca.Tests
{
    public class PlaceholderBgmTests
    {
        [TestCase(false,"exploration")] [TestCase(true,"battle")]
        public void 共有仮曲は八秒の元PCMを圧縮せず端点ゼロ低ピーク微小DCで保持する(bool battle,string name)
        {
            var clip=battle?PlaceholderBgm.Battle():PlaceholderBgm.Exploration();Assert.IsNotNull(clip);Assert.AreSame(clip,battle?PlaceholderBgm.Battle():PlaceholderBgm.Exploration());Assert.AreEqual(1,clip.channels);Assert.AreEqual(48000,clip.frequency);Assert.AreEqual(384000,clip.samples);
            var bytes=File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath,"Audio/Bgm",name+".wav"));CollectionAssert.AreEqual(bytes,Resources.Load<TextAsset>("OlcaBgm/"+name).bytes);
            var actual=new float[clip.samples];Assert.IsTrue(clip.GetData(actual,0));var expected=new float[clip.samples];using(var reader=new BinaryReader(new MemoryStream(bytes))){reader.BaseStream.Position=44;for(int i=0;i<expected.Length;i++)expected[i]=reader.ReadInt16()/32768f;}CollectionAssert.AreEqual(expected,actual);
            Assert.AreEqual(0,actual[0]);Assert.AreEqual(0,actual[1]);Assert.AreEqual(0,actual[actual.Length-2]);Assert.AreEqual(0,actual.Last());Assert.Less(System.Math.Abs(actual.Average()),.00001);float peak=actual.Max(System.Math.Abs);Assert.Less(peak,.0351f);Assert.Greater(peak,.0349f);Assert.Less(peak+4*.079986572265625+2*.100006103515625,1);
        }
        [Test] public void 明示曲を優先し戦闘曲がない既存設定では探索曲へ戻り既定二曲は共有する()
        {
            var exploration=PlaceholderBgm.Exploration();var battle=PlaceholderBgm.Battle();Assert.AreNotSame(exploration,battle);var custom=AudioClip.Create("custom-explore",48,1,48000,false);var customBattle=AudioClip.Create("custom-battle",48,1,48000,false);
            try{Assert.AreSame(custom,PlaceholderBgm.Exploration(custom));Assert.AreSame(custom,PlaceholderBgm.Battle(null,custom));Assert.AreSame(customBattle,PlaceholderBgm.Battle(customBattle,custom));}finally{Object.DestroyImmediate(custom);Object.DestroyImmediate(customBattle);}Assert.AreSame(exploration,PlaceholderBgm.Exploration());Assert.AreSame(battle,PlaceholderBgm.Battle());
        }
        [Test] public void Controller休止復帰は音声を止め再開し共有曲を破棄しない()
        {
            float previousTimeScale=Time.timeScale;var host=new GameObject("BgmLifecycleTest");var c=host.AddComponent<Olca.Application.Dungeon.DungeonBattleController>();var router=new GameplayAudioRouter(host.transform);var type=c.GetType();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;type.GetField("gameplayAudio",flags).SetValue(c,router);var clip=PlaceholderBgm.Exploration();router.Bgm.clip=clip;
            var factory=new Olca.Application.Battle.SimpleCardFactory();var manager=new Olca.Core.Logic.Integration.DungeonBattleManager(Olca.Core.Domain.DungeonConfig.CreateTestDungeon(6,30,3),new Olca.Core.Domain.PlayerCharacter(100),factory.CreateStarterDeck(),new Olca.Tests.Mocks.MockRandomProvider(),new Olca.Tests.Mocks.MockLogger());type.GetField("manager",flags).SetValue(c,manager);
            try{Assert.IsTrue(router.Bgm.loop);c.SetUserPaused(true);c.enabled=false;type.GetMethod("OnDisable",flags).Invoke(c,null);Assert.IsTrue(router.IsPaused);type.GetMethod("ApplyUserPauseImmediate",flags).Invoke(c,new object[]{false});Assert.IsTrue(router.IsPaused,"無効中の外部ポーズ解除で音声を再開しない");c.enabled=true;type.GetMethod("OnEnable",flags).Invoke(c,null);Assert.IsFalse(router.IsPaused);Assert.AreSame(clip,router.Bgm.clip);router.Dispose();Assert.IsNotNull(clip);Assert.AreSame(clip,PlaceholderBgm.Exploration());}finally{router.Dispose();Object.DestroyImmediate(host);Time.timeScale=previousTimeScale;}
        }
        [Test] public void 無効音源の指定曲を保持し有効化同期で一度だけ開始する()
        {
            var host=new GameObject("DeferredBgmTest");host.SetActive(false);using(var router=new GameplayAudioRouter(host.transform)){
                var clip=PlaceholderBgm.Exploration();var pending=typeof(GameplayAudioRouter).GetField("bgmPending",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);router.PlayBgm(clip);Assert.AreSame(clip,router.Bgm.clip);Assert.IsFalse(router.Bgm.isPlaying);Assert.IsTrue((bool)pending.GetValue(router));router.SetPaused(true);host.SetActive(true);Assert.IsTrue((bool)pending.GetValue(router));router.SetPaused(false);Assert.IsFalse((bool)pending.GetValue(router));router.SetPaused(true);router.PlayBgm(PlaceholderBgm.Battle());Assert.IsTrue((bool)pending.GetValue(router));router.PlayBgm(null);Assert.IsFalse((bool)pending.GetValue(router));Assert.IsNull(router.Bgm.clip);
            }Object.DestroyImmediate(host);
        }
    }
}
