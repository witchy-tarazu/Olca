using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Core.Logic.CardEffects;
using Cell = Olca.Core.Domain.Vector2Int;
using NUnit.Framework;
using UnityEngine;
using Olca.Presentation.Field;

namespace Olca.Tests
{
    public class SharedAttackPropCatalogTests
    {
        private Olca.Unity.GeneratedSpriteAssets.Lease generatedScope;
        [SetUp] public void AcquireGeneratedScope()=>generatedScope=Olca.Unity.GeneratedSpriteAssets.Acquire();
        [TearDown] public void ReleaseGeneratedScope(){generatedScope?.Dispose();generatedScope=null;Olca.Unity.GeneratedSpriteAssets.Prune();}

        [TestCase(SharedAttackPropCatalog.Sword, "shared_sword", 256, 256)]
        [TestCase(SharedAttackPropCatalog.ThrowingKnife, "shared_throwing_knife", 64, 64)]
        [TestCase(SharedAttackPropCatalog.ShotBolt, "shared_shot_bolt", 32, 32)]
        [TestCase(SharedAttackPropCatalog.Bomb, "shared_bomb", 64, 64)]
        [TestCase(SharedAttackPropCatalog.Blast, "shared_blast", 64, 32)]
        public void 共用素材は同じSpriteを返し実ファイルに透明と不透明の画素がある(string key,string file,int size,int height)
        {
            Assert.IsTrue(SharedAttackPropCatalog.TryGetSprite(key,out var first));
            Assert.IsTrue(SharedAttackPropCatalog.TryGetSprite(key,out var second));Assert.AreSame(first,second);
            Assert.AreEqual(100,first.pixelsPerUnit);Assert.AreEqual(FilterMode.Point,first.texture.filterMode);
            // PNGデコーダのチャンネル順は問わず、アルファ付き非圧縮32bitだけを許す。
            Assert.That(first.texture.format,Is.EqualTo(TextureFormat.RGBA32).Or.EqualTo(TextureFormat.ARGB32));Assert.IsFalse(first.texture.isReadable);
            var original=File.ReadAllBytes("Assets/Art/Sprites/AttackProps/"+file+".png");
            CollectionAssert.AreEqual(original,Resources.Load<TextAsset>("OlcaAttackProps/"+file).bytes);
            Assert.AreEqual(size,first.texture.width);Assert.AreEqual(height,first.texture.height);
            var image=new Texture2D(2,2);
            try {
                Assert.IsTrue(image.LoadImage(original));
                int transparent=0,opaque=0;
                foreach(var pixel in image.GetPixels32()){if(pixel.a==0)transparent++;else if(pixel.a==255)opaque++;else Assert.Fail("半透明の縁が残っています");}
                Assert.Greater(transparent,0);Assert.Greater(opaque,0);
                if(key==SharedAttackPropCatalog.Bomb||key==SharedAttackPropCatalog.Blast){
                    int firstRow=height,lastRow=-1,firstColumn=size,lastColumn=-1;var pixels=image.GetPixels32();
                    for(int y=0;y<height;y++)for(int x=0;x<size;x++)if(pixels[y*size+x].a!=0){firstRow=System.Math.Min(firstRow,y);lastRow=System.Math.Max(lastRow,y);firstColumn=System.Math.Min(firstColumn,x);lastColumn=System.Math.Max(lastColumn,x);}
                    Assert.AreEqual(key==SharedAttackPropCatalog.Bomb?SharedAttackPropCatalog.BombVisibleHeight:SharedAttackPropCatalog.BlastVisibleHeight,(lastRow-firstRow+1)/100f);
                    if(key==SharedAttackPropCatalog.Blast)Assert.AreEqual(SharedAttackPropCatalog.BlastVisibleWidth,(lastColumn-firstColumn+1)/100f);
                }
            }finally{Object.DestroyImmediate(image);}
        }
        private sealed class BombDisplayFixture : System.IDisposable
        {
            private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
            public readonly GameObject Root;
            public readonly GameFieldView View;
            public readonly BattleSystem Battle;
            public readonly Enemy Enemy;
            public readonly DelayedEffect Shot;
            private WorldBattleGridView grid;
            private bool disposed;
            public BombDisplayFixture(int enemyHp)
            {
                Root=new GameObject("bomb-impact-display-fixture");
                try {
                grid=Root.AddComponent<WorldBattleGridView>();
                // EditModeのAddComponentはAwakeを自動実行しないため、実Grid初期化を一度だけ行う。
                if(typeof(WorldBattleGridView).GetField("panelRenderers",Flags).GetValue(grid)==null)
                    typeof(WorldBattleGridView).GetMethod("Awake",Flags).Invoke(grid,null);
                View=Root.AddComponent<GameFieldView>();
                typeof(GameFieldView).GetField("battleGridView",Flags).SetValue(View,grid);
                Enemy=new Enemy("impact-target","対象",enemyHp,ElementType.None,new Cell(4,0));
                Battle=new BattleSystem(new PlayerCharacter(100,new Cell(1,0)),new List<Enemy>{Enemy},new Deck(),logger:new Olca.Tests.Mocks.MockLogger());
                View.OnBattleStarted(Battle,0);
                var card=new Olca.Application.Battle.SimpleCardFactory().CreateById("bomb");
                CardEffectFactory.CreateEffect(card).Execute(new BattleContext(Battle,card));
                Shot=Battle.DelayedEffects.GetActiveEffects().Single();Draw(0,Time.frameCount+10);
                Assert.AreEqual(1,Visible("Body"));
                }catch{Dispose();throw;}
            }
            public void Frames(int count){for(int n=0;n<count;n++)Battle.TickFrame(n%2==0?1:1000);}
            public void Draw(float delta,int renderFrame)=>typeof(GameFieldView).GetMethod("RefreshBombVisualsFrame",Flags).Invoke(View,new object[]{delta,renderFrame});
            public void Disable(){View.enabled=false;typeof(GameFieldView).GetMethod("OnDisable",Flags).Invoke(View,null);}
            public void DrawPausedWrapper()=>typeof(GameFieldView).GetMethod("RefreshBombVisuals",Flags).Invoke(View,null);
            public int Visible(string prefix)=>Root.GetComponentsInChildren<SpriteRenderer>(true).Count(x=>x.name.StartsWith(prefix)&&x.enabled&&x.gameObject.activeInHierarchy&&x.color.a>0);
            public int TailCount=>((System.Collections.IDictionary)typeof(GameFieldView).GetField("outcomeBombFrames",Flags).GetValue(View)).Count;
            public int RootCount=>Root.GetComponentsInChildren<Transform>(true).Count(x=>x.name=="BombProjectile");
            public void Dispose()
            {
                if(disposed)return;disposed=true;
                var floor=grid!=null?(Sprite)typeof(WorldBattleGridView).GetField("floorSprite",Flags).GetValue(grid):null;
                var white=grid!=null?(Sprite)typeof(WorldBattleGridView).GetField("whiteSprite",Flags).GetValue(grid):null;
                var texture=white!=null?white.texture:null;
                var fallback=View!=null?(Sprite)typeof(GameFieldView).GetField("obstacleWhiteSprite",Flags).GetValue(View):null;
                var fallbackTexture=fallback!=null?fallback.texture:null;
                try {
                    // 手動AwakeのEditMode fixtureはEngineの破棄通知に依存せず既存owner終了を呼ぶ。
                    if(!UnityEngine.Application.isPlaying&&View!=null){
                        typeof(GameFieldView).GetMethod("OnDestroy",Flags).Invoke(View,null);
                        Assert.IsNull(typeof(GameFieldView).GetField("generatedAssets",Flags).GetValue(View));
                    }
                } finally {
                    try {if(!UnityEngine.Application.isPlaying&&grid!=null)typeof(WorldBattleGridView).GetMethod("OnDestroy",Flags).Invoke(grid,null);}
                    finally {if(Root!=null)UnityEngine.Object.DestroyImmediate(Root);}
                }
                Assert.IsTrue(floor==null);Assert.IsTrue(white==null);Assert.IsTrue(texture==null);
                Assert.IsTrue(fallback==null);Assert.IsTrue(fallbackTexture==null);
            }
        }

        [Test]
        public void 非致死ボムの爆風は描画時間で延長せず六論理frameで終了し停止中は保持する()
        {
            using(var fixture=new BombDisplayFixture(100)) {
                fixture.Frames(90);Assert.IsTrue(fixture.Shot.WasExecuted);Assert.IsFalse(fixture.Battle.IsGameOver);
                Assert.AreEqual(60,fixture.Enemy.CurrentHp);long completed=fixture.Shot.CompletedFrame;
                fixture.Draw(1000,Time.frameCount+20);Assert.AreEqual(3,fixture.Visible("Blast"));Assert.AreEqual(0,fixture.Visible("Body"));
                fixture.Battle.Pause(BattlePauseReason.User);fixture.Frames(10);
                Assert.AreEqual(completed,fixture.Battle.LogicalFrame);
                fixture.Draw(1000,Time.frameCount+21);Assert.AreEqual(3,fixture.Visible("Blast"));
                fixture.Battle.Resume(BattlePauseReason.User);fixture.Frames(5);fixture.Draw(1000,Time.frameCount+22);
                Assert.AreEqual(completed+5,fixture.Battle.LogicalFrame);Assert.AreEqual(3,fixture.Visible("Blast"));
                fixture.Frames(1);fixture.Draw(0,Time.frameCount+23);Assert.AreEqual(0,fixture.RootCount);Assert.AreEqual(0,fixture.TailCount);
            }
        }

        [Test]
        public void 致死ボムは勝利後も着弾だけを描画し重複描画と停止で時間を消費せず有限で返却する()
        {
            bool wasPaused=Olca.Unity.GameplayPause.IsPaused;
            try { using(var fixture=new BombDisplayFixture(20)) {
                fixture.Frames(90);Assert.IsTrue(fixture.Shot.WasExecuted);Assert.AreEqual(BattleState.Victory,fixture.Battle.State);
                long frame=fixture.Battle.LogicalFrame;int playerHp=fixture.Battle.Player.CurrentHp;int targetHp=fixture.Enemy.CurrentHp;
                int first=Time.frameCount+100;
                fixture.Draw(1000,first);Assert.AreEqual(3,fixture.Visible("Blast"));Assert.AreEqual(0,fixture.Visible("Body"));
                fixture.Draw(5f/60f,first+1);Assert.AreEqual(3,fixture.Visible("Blast"));
                fixture.Draw(1000,first+1);Assert.AreEqual(3,fixture.Visible("Blast"),"同じ描画frameの再更新で寿命を消費しない");
                Olca.Unity.GameplayPause.SetPaused(true);fixture.DrawPausedWrapper();Assert.AreEqual(3,fixture.Visible("Blast"));
                Olca.Unity.GameplayPause.SetPaused(wasPaused);
                fixture.Draw(1f/60f,first+2);Assert.AreEqual(0,fixture.RootCount);Assert.AreEqual(0,fixture.TailCount);
                Assert.AreEqual(frame,fixture.Battle.LogicalFrame);Assert.AreEqual(playerHp,fixture.Battle.Player.CurrentHp);
                Assert.AreEqual(targetHp,fixture.Enemy.CurrentHp);Assert.AreEqual(BattleState.Victory,fixture.Battle.State);
            }} finally {
                Olca.Unity.GameplayPause.SetPaused(wasPaused);
                if(!wasPaused){Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();}
            }
        }

        [TestCase("end")][TestCase("disable")][TestCase("next")]
        public void 勝利爆風の所有は終了無効化次戦へ持ち越さない(string boundary)
        {
            using(var fixture=new BombDisplayFixture(20)) {
                fixture.Frames(90);fixture.Draw(0,Time.frameCount+200);Assert.AreEqual(3,fixture.Visible("Blast"));Assert.AreEqual(1,fixture.TailCount);
                long frame=fixture.Battle.LogicalFrame;
                if(boundary=="end")fixture.View.OnBattleEnded();
                else if(boundary=="disable")fixture.Disable();
                else fixture.View.OnBattleStarted(new BattleSystem(new PlayerCharacter(100,new Cell(1,0)),new List<Enemy>{new Enemy("next","次",100,ElementType.None,new Cell(4,0))},new Deck()),0);
                Assert.AreEqual(0,fixture.RootCount);Assert.AreEqual(0,fixture.TailCount);Assert.AreEqual(0,fixture.Visible("Blast"));
                Assert.AreEqual(frame,fixture.Battle.LogicalFrame);Assert.AreEqual(BattleState.Victory,fixture.Battle.State);
            }
        }

        [Test]
        public void 未着弾ボムは敗北で爆風を生成せず描画所有を返す()
        {
            using(var fixture=new BombDisplayFixture(100)) {
                fixture.Battle.Player.TakeDamage(1000);fixture.Frames(1);Assert.AreEqual(BattleState.Defeat,fixture.Battle.State);
                fixture.Draw(0,Time.frameCount+300);Assert.AreEqual(0,fixture.RootCount);Assert.AreEqual(0,fixture.TailCount);Assert.AreEqual(0,fixture.Visible("Blast"));
                Assert.IsFalse(fixture.Shot.WasExecuted);
            }
        }

        [Test] public void 未登録キーは既存の別素材へ置換しない(){Assert.IsFalse(SharedAttackPropCatalog.TryGetSprite("missing",out var sprite));Assert.IsNull(sprite);Assert.IsFalse(SharedAttackPropCatalog.TryGetSprite(null,out sprite));}
    }
}
