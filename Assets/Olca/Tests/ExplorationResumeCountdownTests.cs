using System;
using System.Linq;
using Olca.Core.Domain;
using Olca.Core.Logic.Dungeon;
using Olca.Core.Logic.Battle;
using NUnit.Framework;
using Olca.Core.Logic.Integration;
using Olca.Core.Logic.Customization;
namespace Olca.Tests
{
    public class ExplorationResumeCountdownTests
    {
        private sealed class Style231Store:IClearReturnStore
        {
            public ClearReturnProfile Value=new ClearReturnProfile{Tokens=7};public bool Fail;public int Writes;
            public ClearReturnProfile Read(out string error){error=null;return Value.Copy();}
            public bool TryWrite(long expected,ClearReturnProfile next,out string error){Writes++;if(Fail){error="style231 explicit failure";return false;}Assert.AreEqual(Value.Revision,expected);ClearReturnProfile.Validate(next);Value=next.Copy();error=null;return true;}
        }
        private static CustomizationCatalog Shop236Catalog()=>new CustomizationCatalog("shop236-board",new PartsBoardDefinition(1,1,new[]{new Vector2Int(0,0)}),Array.Empty<string>(),new System.Collections.Generic.Dictionary<string,PartShape>{{"part236",new PartShape(new[]{new Vector2Int(0,0)})}},null);
        [Test] public void 外界パーツ購入は支払所持在庫を一括保存して自動配置せずcoldと追加所持を守る()
        {
            var catalog=Shop236Catalog();var shop=new OutsideShopDefinition("shop236",new[]{OutsideShopOffer.ForPart("part-offer","part236","仮パーツ",4)},catalog);var store=new Style231Store{Value=new ClearReturnProfile{Tokens=9,OutsideInventory=new OutsideInventoryData{Currency=10,ClaimedSources=new[]{"unknown236"}},Customization=new CustomizationProgress{OwnedParts=new[]{new CustomizationItemCount{Id="part236",Count=2}}}}};var save=new ClearReturnSession(store);var before=PortableCheckpointJson.Serialize(store.Value);store.Fail=true;Assert.IsFalse(save.TryPurchaseOutsideShop(shop,"part-offer",out _));CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(store.Value));Assert.IsFalse(save.HasPendingSave);Assert.AreEqual(1,new ClearReturnSession(store).OutsideShopRemaining(shop,"part-offer"));store.Fail=false;Assert.IsTrue(save.TryPurchaseOutsideShop(shop,"part-offer",out var error),error);var cold=new ClearReturnSession(store);Assert.AreEqual(6,cold.OutsideInventorySnapshot.Currency);Assert.AreEqual(3,cold.CustomizationSnapshot.OwnedParts.Single().Count);Assert.AreEqual(catalog.BoardId,cold.CustomizationSnapshot.BoardId);Assert.AreEqual(0,cold.CustomizationSnapshot.Placements.Length);Assert.AreEqual(0,cold.CustomizationSnapshot.EquippedAccessoryIds.Length);Assert.AreEqual(9,cold.Snapshot.Tokens);Assert.Contains("unknown236",cold.OutsideInventorySnapshot.ClaimedSources);Assert.AreEqual(0,cold.OutsideShopRemaining(shop,"part-offer"));int writes=store.Writes;Assert.IsFalse(cold.TryPurchaseOutsideShop(shop,"part-offer",out _));Assert.AreEqual(writes,store.Writes);
            var layout=new[]{new PartPlacement("shop236-instance","part236",new Vector2Int(0,0),PartRotation.None)};Assert.IsTrue(cold.TrySetCustomizationLoadout(catalog,Array.Empty<string>(),layout),cold.Error);var abilities=new CustomizationAbilityCatalog("shop236-abilities",null,new System.Collections.Generic.Dictionary<string,CustomizationAbilityValue>{{"part236",new CustomizationAbilityValue(new ElementResistances(fire:5))}});var restored=new ClearReturnSession(store);Assert.AreEqual(5,abilities.Resolve(new CustomizationLoadoutSources(catalog,restored.CustomizationSnapshot)).CombineElements(new ElementResistances()).Fire);
        }
        [Test] public void 外界パーツ販売は未登録盤面不一致所持overflowと攻略中を保存前に拒否する()
        {
            var catalog=Shop236Catalog();var offer=OutsideShopOffer.ForPart("part-offer","part236","仮パーツ",4);Assert.Throws<ArgumentException>(()=>new OutsideShopDefinition("shop236",new[]{offer}));Assert.Throws<ArgumentException>(()=>new OutsideShopDefinition("shop236",new[]{OutsideShopOffer.ForPart("unknown","unknown","不明",1)},catalog));var shop=new OutsideShopDefinition("shop236",new[]{offer},catalog);
            foreach(int mode in new[]{0,1,2}){var store=new Style231Store{Value=new ClearReturnProfile{OutsideInventory=new OutsideInventoryData{Currency=10},Customization=mode==0?new CustomizationProgress{BoardId="other236"}:mode==1?new CustomizationProgress{OwnedParts=new[]{new CustomizationItemCount{Id="part236",Count=int.MaxValue}}}:null}};var save=new ClearReturnSession(store,isCustomizationRunActive:()=>mode==2);var before=PortableCheckpointJson.Serialize(store.Value);Assert.IsFalse(save.TryPurchaseOutsideShop(shop,offer.Id,out _));Assert.AreEqual(0,store.Writes);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(store.Value));}
        }
        [TestCase(1)][TestCase(2)] public void 外界限定商品は支払取得在庫を一括保存し失敗coldと売切れを保つ(int limit)
        {
            var shop=new OutsideShopDefinition("s/235",new[]{new OutsideShopOffer("p/235",OutsideRecoveryItemDefinition.PoisonProvisional.Id,"毒消し",1,99,limit)});var store=new Style231Store{Value=new ClearReturnProfile{OutsideInventory=new OutsideInventoryData{Currency=10,ClaimedSources=null}}};var save=new ClearReturnSession(store);var old=PortableCheckpointJson.Serialize(store.Value);Assert.AreEqual(limit,save.OutsideShopRemaining(shop,"p/235"));Assert.AreEqual(0,store.Writes);Assert.AreEqual(old,PortableCheckpointJson.Serialize(store.Value));
            for(int i=0;i<limit;i++){store.Fail=true;old=PortableCheckpointJson.Serialize(store.Value);Assert.IsFalse(save.TryPurchaseOutsideShop(shop,"p/235",out _));Assert.AreEqual(old,PortableCheckpointJson.Serialize(store.Value));Assert.AreEqual(limit-i,new ClearReturnSession(store).OutsideShopRemaining(shop,"p/235"));Assert.IsFalse(save.HasPendingSave);store.Fail=false;Assert.IsTrue(save.TryPurchaseOutsideShop(shop,"p/235",out var error),error);save=new ClearReturnSession(store);Assert.AreEqual(limit-i-1,save.OutsideShopRemaining(shop,"p/235"));Assert.AreEqual(9-i,save.OutsideInventorySnapshot.Currency);Assert.AreEqual(i+1,save.OutsideInventorySnapshot.Items.Single().Count);}
            int writes=store.Writes;old=PortableCheckpointJson.Serialize(store.Value);Assert.IsFalse(save.TryPurchaseOutsideShop(shop,"p/235",out var sold));StringAssert.Contains("ウリキレ",sold);Assert.AreEqual(writes,store.Writes);Assert.AreEqual(old,PortableCheckpointJson.Serialize(store.Value));
        }
        [Test] public void 外界限定在庫は未知履歴と欠番を保持し店商品と無制限から分離する()
        {
            var offer=new OutsideShopOffer("a/",OutsideRecoveryItemDefinition.PoisonProvisional.Id,"毒",1,99,3);var shop=new OutsideShopDefinition("x",new[]{offer});string prefix="outside-shop-purchase-v1/1:x/2:a//";var store=new Style231Store{Value=new ClearReturnProfile{OutsideInventory=new OutsideInventoryData{Currency=10,ClaimedSources=new[]{"unknown235",prefix+"invalid",prefix+"2"}}}};var save=new ClearReturnSession(store);Assert.AreEqual(1,save.OutsideShopRemaining(shop,offer.Id));Assert.AreEqual(3,save.OutsideShopRemaining(new OutsideShopDefinition("x/",new[]{offer}),offer.Id));Assert.IsTrue(save.TryPurchaseOutsideShop(shop,offer.Id,out var error),error);Assert.AreEqual(0,save.OutsideShopRemaining(shop,offer.Id));CollectionAssert.IsSubsetOf(new[]{"unknown235",prefix+"invalid",prefix+"2",prefix+"1"},save.OutsideInventorySnapshot.ClaimedSources);var cold=new ClearReturnSession(store);Assert.AreEqual(0,cold.OutsideShopRemaining(shop,offer.Id));Assert.AreEqual(int.MaxValue,cold.OutsideShopRemaining(OutsideShopDefinition.Trial,"poison"));Assert.AreEqual(2,cold.OutsideShopRemaining(OutsideShopDefinition.TrialLimited,"poison-limited-v1"));var snap=cold.OutsideInventorySnapshot;snap.ClaimedSources[0]="mutated";Assert.Contains("unknown235",cold.OutsideInventorySnapshot.ClaimedSources);Assert.Throws<ArgumentException>(()=>new OutsideShopOffer("x","x","x",1,99,-1));
        }
        [Test] public void 外界店は任意資金と購入を一括保存し失敗と取消相当読取で旧所持を変えない()
        {
            var store=new Style231Store{Fail=true,Value=new ClearReturnProfile{Tokens=7,OutsideInventory=new OutsideInventoryData{Currency=0,Items=new[]{new OutsideItemCount{Id="unknown233",Count=9}},ClaimedSources=new[]{"old233"}}}};var save=new ClearReturnSession(store);var before=PortableCheckpointJson.Serialize(store.Value);Assert.IsFalse(save.TryClaimOutsideShopTrialFunds(out _));Assert.AreEqual(before,PortableCheckpointJson.Serialize(store.Value));Assert.IsFalse(save.HasPendingSave);store.Fail=false;Assert.IsTrue(save.TryClaimOutsideShopTrialFunds(out var error),error);Assert.AreEqual(10,save.OutsideInventorySnapshot.Currency);int writes=store.Writes;Assert.IsFalse(save.TryClaimOutsideShopTrialFunds(out _));Assert.AreEqual(writes,store.Writes);
            before=PortableCheckpointJson.Serialize(store.Value);store.Fail=true;Assert.IsFalse(save.TryPurchaseOutsideShop(OutsideShopDefinition.Trial,"heal",out _));Assert.AreEqual(before,PortableCheckpointJson.Serialize(store.Value));Assert.AreEqual(10,save.OutsideInventorySnapshot.Currency);Assert.IsFalse(save.HasPendingSave);store.Fail=false;Assert.IsTrue(save.TryPurchaseOutsideShop(OutsideShopDefinition.Trial,"heal",out error),error);Assert.AreEqual(8,save.OutsideInventorySnapshot.Currency);Assert.AreEqual(1,save.OutsideInventorySnapshot.Items.Single(v=>v.Id==OutsideRecoveryItemDefinition.Provisional.Id).Count);Assert.AreEqual(7,store.Value.Tokens);Assert.AreEqual(9,save.OutsideInventorySnapshot.Items.Single(v=>v.Id=="unknown233").Count);Assert.Contains("old233",save.OutsideInventorySnapshot.ClaimedSources);
            var cold=new ClearReturnSession(store);Assert.AreEqual(8,cold.OutsideInventorySnapshot.Currency);Assert.IsFalse(cold.TryClaimOutsideShopTrialFunds(out _));Assert.IsTrue(cold.TryPurchaseOutsideShop(OutsideShopDefinition.Trial,"poison",out error),error);Assert.AreEqual(7,cold.OutsideInventorySnapshot.Currency);Assert.IsNull(cold.Snapshot.OutsidePlayerSave);Assert.IsNull(cold.Snapshot.OutsideRuntimeSave);
        }
        [Test] public void 外界店は不足と上限と未知商品と資金overflowを保存前に拒否する()
        {
            foreach(int mode in new[]{0,1,2,3}){var store=new Style231Store{Value=new ClearReturnProfile{OutsideInventory=new OutsideInventoryData{Currency=mode==3?long.MaxValue:mode==0?1:10,Items=mode==1?new[]{new OutsideItemCount{Id=OutsideRecoveryItemDefinition.Provisional.Id,Count=99}}:Array.Empty<OutsideItemCount>()}}};var save=new ClearReturnSession(store);var before=PortableCheckpointJson.Serialize(store.Value);Assert.IsFalse(mode==3?save.TryClaimOutsideShopTrialFunds(out _):save.TryPurchaseOutsideShop(OutsideShopDefinition.Trial,mode==2?"unknown":"heal",out _));Assert.AreEqual(0,store.Writes);Assert.AreEqual(before,PortableCheckpointJson.Serialize(store.Value));}
            var legacy=new Style231Store();new ClearReturnSession(legacy);Assert.AreEqual(0,legacy.Writes);Assert.IsNull(legacy.Value.OutsideInventory);Assert.Throws<ArgumentException>(()=>new OutsideShopOffer("x","item","name",0,99));Assert.Throws<ArgumentException>(()=>new OutsideShopDefinition("x",new[]{OutsideShopDefinition.Trial.Offers[0],OutsideShopDefinition.Trial.Offers[0]}));
        }
        [Test] public void 外界試用店は専用地点の接地時だけ利用可能でジャンプと旧mapを混同しない()
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationLandingTerrain);Assert.IsTrue(OutsideTrialRoute.CanVisitShop(core));for(int i=0;i<20;i++)core.TickFrame(1);Assert.IsFalse(OutsideTrialRoute.CanVisitShop(core));for(int i=0;i<20;i++)core.TickFrame(-1);Assert.IsTrue(OutsideTrialRoute.CanVisitShop(core));core.TickFrame(0,true,true);Assert.IsFalse(OutsideTrialRoute.CanVisitShop(core));Assert.IsFalse(OutsideTrialRoute.CanVisitShop(new OutsideExplorationCore(new OutsideMovementDefinition("legacy233",32*OutsideMovementDefinition.UnitsPerWorldUnit,8*OutsideMovementDefinition.UnitsPerWorldUnit,6*OutsideMovementDefinition.UnitsPerWorldUnit))));
        }
        [Test] public void 外界神依の失敗は個体と時計手札を保ち成功とcoldと通常復帰で火から通常へ戻せる()
        {
            using(var s=Sword161()){
                var store=new Style231Store{Fail=true};var save=new ClearReturnSession(store);var defs=Kick214Definitions(s);var cooldowns=(System.Collections.Generic.Dictionary<string,long>)typeof(OutsideExplorationSession).GetField("itemCooldowns",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s);cooldowns["style231-cd"]=s.Core.Frame+120;s.Player.SetHp(80);s.Player.SetBarrier(5);var player=s.Player;var cards=s.Cards;var hand=cards.Hand.ToArray();
                using(var l=s.AttachPresentation()){
                    s.TickTraversal(1,true,true);var before=PortableCheckpointJson.Serialize(s.Core.CaptureState());Assert.IsFalse(s.Core.IsGrounded);Assert.IsFalse(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,defs,out _));Assert.AreSame(player,s.Player);Assert.AreEqual("basic",player.DepartureStyle.Id);Assert.IsNull(save.Snapshot.SelectedStyleId);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(s.Core.CaptureState()));
                    store.Fail=false;Assert.IsTrue(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,defs,out var error),error);Assert.AreSame(player,s.Player);Assert.AreSame(cards,s.Cards);CollectionAssert.AreEqual(hand,cards.Hand);CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(s.Core.CaptureState()));Assert.AreEqual(80,player.CurrentHp);Assert.AreEqual(5,player.BarrierHp);Assert.AreEqual(-50,player.BaseElementResistances.Water);int writes=store.Writes;Assert.IsTrue(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,defs,out _));Assert.AreEqual(writes,store.Writes);Assert.AreEqual(7,save.Snapshot.Tokens);
                }
                var saved=save.Snapshot;Assert.AreEqual(119,saved.OutsideItemState.Cooldowns.Single().RemainingFrames);using(var cold=new OutsideExplorationSession()){cold.RestoreForPresentation(saved.OutsidePlayerSave,saved.OutsideRuntimeSave,null,defs,itemState:saved.OutsideItemState);Assert.AreEqual(119,cold.ItemCooldownRemaining("style231-cd"));Assert.AreEqual(ElementType.Fire,cold.Player.ActiveElement);Assert.AreEqual(5,cold.Player.BarrierHp);using(var l=cold.AttachPresentation()){Assert.IsTrue(cold.TrySwitchOutsideStyle(save,DepartureStyleDefinition.Basic,defs,out var error),error);Assert.IsTrue(cold.Player.BaseElementResistances.IsNeutral);Assert.AreEqual(ElementType.None,cold.Player.ActiveElement);}Assert.IsNull(save.Snapshot.SelectedStyleId);Assert.AreEqual(DepartureStyleDefinition.TrialFire.Id,saved.OutsidePlayerSave.Player.Style.Id);}
                using(var normal=new OutsideExplorationSession()){normal.ClassifyStartupRestore(true);normal.RestoreForPresentation(saved.OutsidePlayerSave,saved.OutsideRuntimeSave,null,defs);Assert.AreEqual(ElementType.Fire,normal.Player.ActiveElement);Assert.AreEqual(100,normal.Player.CurrentHp);Assert.AreEqual(0,normal.Player.BarrierHp);}
            }
        }
        [Test] public void 外界神依は旧基礎耐性と攻略固定と反映できない無敵を上書きしない()
        {
            using(var s=Sword161())using(var l=s.AttachPresentation()){
                var store=new Style231Store();var save=new ClearReturnSession(store);s.Player.SetElementResistances(new ElementResistances(fire:5));Assert.IsFalse(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,null,out _));Assert.AreEqual(5,s.Player.BaseElementResistances.Fire);s.Player.SetElementResistances(ElementResistances.Neutral);s.Player.SetInvincible(1000);Assert.IsFalse(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,null,out _));s.Player.TickInvincible(1000);Hit229(s.Player,"FreezeDepartureState");Assert.IsFalse(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,null,out _));Assert.AreEqual(0,store.Writes);
            }
        }
        [Test] public void 外界カエンは火の試用キックを一回命中し動作中の切替を拒否する()
        {
            using(var s=Sword161())using(var l=s.AttachPresentation()){
                var save=new ClearReturnSession(new Style231Store());s.EnableTrialNormalKick();ElementType? element=null;int hits=0;s.TrialTarget.OnDamageObserved+=hit=>{element=hit.Element;hits++;};Assert.IsTrue(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.TrialFire,Kick214Definitions(s),out _));Assert.IsTrue(s.ProcessCardUseInput(false,false,true,0,1,out _));Assert.IsFalse(s.TrySwitchOutsideStyle(save,DepartureStyleDefinition.Basic,null,out _));for(int i=0;i<30;i++)Kick214Tick(s);Assert.AreEqual(ElementType.Fire,element);Assert.AreEqual(1,hits);Assert.AreEqual(110,s.TrialTarget.CurrentHp);
            }
        }
        private static object Hit229(PlayerCharacter p,string method,params object[] args)=>typeof(PlayerCharacter).GetMethod(method,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(p,args);
        [Test] public void 無敵中の落下死亡から回復後は残無敵を解除して保存とackを完了できる()
        {
            using(var s=Sword161()){
                s.Player.SetInvincible(3000);Assert.Throws<ArgumentException>(()=>OutsidePlayerSaveData.Capture(s.Player));Hit229(s.Player,"TakeFallDamage",100);Assert.IsTrue(s.Player.IsDead);var recovered=OutsidePlayerSaveData.ForRecovery(s.Player);s.RecoverOutside(null);Assert.IsFalse(s.Player.IsInvincible);Assert.AreEqual(0,s.Player.InvincibleDurationMs);Assert.AreEqual(100,s.Player.CurrentHp);Assert.DoesNotThrow(()=>s.AcknowledgeRecovery(recovered));Assert.IsNull(OutsidePlayerSaveData.Capture(s.Player).HitState);
            }
        }
        public sealed class LegacyHitActor229 {public int Version;public string Id;public CheckpointPlayerDto Player;public int[] RawBaseElements;}
        [Test] public void 外界被弾ゼロ欄省略は旧Actorのbytesとkeyを維持する()
        {
            var actor=OutsidePlayerSaveData.Capture(new PlayerCharacter(100));var old=new LegacyHitActor229{Version=actor.Version,Id=actor.Id,Player=actor.Player,RawBaseElements=actor.RawBaseElements};var bytes=PortableCheckpointJson.Serialize(old);var loaded=PortableCheckpointJson.Deserialize<OutsidePlayerSaveData>(bytes);Assert.IsNull(loaded.HitState);CollectionAssert.AreEqual(bytes,PortableCheckpointJson.Serialize(loaded));Assert.AreEqual(actor.Key(),loaded.Key());
        }
        [TestCase(false)] [TestCase(true)] public void 強靭蓄積と怯みの端数unitを厳密復元して二重被害通知を起こさない(bool stagger)
        {
            var p=new PlayerCharacter(100,poise:30,staggerDurationFrames:18,poiseRecoveryDelayFrames:180);p.TakeDamage(stagger?30:10);Hit229(p,"TickPoiseRecovery",1,false);if(stagger)Hit229(p,"TickStagger",1,false);var actor=OutsidePlayerSaveData.Capture(p);Assert.NotNull(actor.HitState);Assert.AreEqual(8997,actor.HitState.RecoveryUnits);Assert.AreEqual(stagger?897:0,actor.HitState.StaggerUnits);var q=PortableCheckpointJson.Deserialize<OutsidePlayerSaveData>(PortableCheckpointJson.Serialize(actor)).Restore();Assert.AreEqual(p.CurrentHp,q.CurrentHp);Assert.AreEqual(p.AccumulatedDamage,q.AccumulatedDamage);Assert.AreEqual(p.StaggerRemainingFrames,q.StaggerRemainingFrames);int notifications=0;q.OnHpChanged+=(_,__)=>notifications++;q.OnStaggered+=()=>notifications++;Hit229(q,"RestoreHitState",actor.HitState);Assert.AreEqual(0,notifications);
            for(int i=0;i<180;i++){Hit229(p,"TickPoiseRecovery",0,true);Hit229(q,"TickPoiseRecovery",0,true);Hit229(p,"TickStagger",0,true);Hit229(q,"TickStagger",0,true);Assert.AreEqual(p.AccumulatedDamage,q.AccumulatedDamage);Assert.AreEqual(p.StaggerRemainingFrames,q.StaggerRemainingFrames);Assert.AreEqual(p.CurrentHp,q.CurrentHp);}Assert.AreEqual(0,q.AccumulatedDamage);
        }
        [Test] public void 不正被弾残時間と反映途中と無敵中は外界保存を拒否する()
        {
            var p=new PlayerCharacter(100);Hit229(p,"BeginHpFrame");Assert.Throws<ArgumentException>(()=>OutsidePlayerSaveData.Capture(p));Hit229(p,"CommitHpFrame");Hit229(p,"PublishHpFrame");p.SetInvincible(10);Assert.Throws<ArgumentException>(()=>OutsidePlayerSaveData.Capture(p));Hit229(p,"ClearInvincibility");var actor=OutsidePlayerSaveData.Capture(p);actor.HitState=new PlayerHitStateData{AccumulatedDamage=1,StaggerUnits=-1};Assert.Throws<ArgumentException>(()=>actor.Restore());actor.HitState.StaggerUnits=19*50;Assert.Throws<ArgumentException>(()=>actor.Validate());actor.HitState.StaggerUnits=0;actor.HitState.RecoveryUnits=1;Assert.Throws<ArgumentException>(()=>actor.Validate());actor.HitState.RecoveryUnits=0;actor.HitState.AccumulatedDamage=p.Poise;Assert.Throws<ArgumentException>(()=>actor.Validate());
        }
        [Test] public void 再挑戦の実Buildは入場前強靭を保持し初回帰還と回復個体は解除する()
        {
            var p=new PlayerCharacter(100);p.TakeDamage(10);var actor=OutsidePlayerSaveData.Capture(p);var retry=new OutsideRetryReturnData{EntryLevel=1,Entry=actor};var result=retry.Build(Guid.NewGuid().ToString("N"),1,new ClearReturnSettings());Assert.AreEqual(10,result.Restore().AccumulatedDamage);Assert.AreNotSame(actor.HitState,result.HitState);var first=new OutsideClearEndData{EntryLevel=1,Entry=actor,End=actor.Player.Copy()}.Build(Guid.NewGuid().ToString("N"),1,new ClearReturnSettings());Assert.IsNull(first.HitState);Assert.IsNull(OutsidePlayerSaveData.ForRecovery(p).HitState);Assert.NotNull(actor.HitState);
        }
        [Test] public void 試用敵v2の強靭被害は保存cold後の次撃で一度だけ怯みへ進む()
        {
            using(var s=Sword161(stagger:18)){
                var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){Assert.IsTrue(s.SetTrialEnemyAttack(true,true));for(int i=0;i<150;i++)s.TickTraversal(0);}Assert.AreEqual(80,s.Player.CurrentHp);Assert.AreEqual(20,s.Player.AccumulatedDamage);var rt=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=rt.Id;Assert.AreEqual("trial-shield-enemy-v2",rt.TrialEnemyAttack.DefinitionId);
                using(var q=new OutsideExplorationSession()){q.RestoreSavedState(actor,rt,null,defs);using(var a=s.AttachPresentation())using(var b=q.AttachPresentation()){for(int i=0;i<90;i++){s.TickTraversal(0);q.TickTraversal(0);}Assert.AreEqual(70,q.Player.CurrentHp);Assert.AreEqual(0,q.Player.AccumulatedDamage);Assert.IsTrue(q.Player.IsStaggered);Assert.AreEqual(s.Player.StaggerRemainingFrames,q.Player.StaggerRemainingFrames);}}
            }
        }


        [TestCase(0,true)] [TestCase(60000,true)] [TestCase(180000,false)]
        public void 任意試用敵の連続攻撃は地上と低空でバリアを削り破壊後にHPへ通す(long height,bool hit)
        {
            using(var s=Sword161())using(var lease=s.AttachPresentation()){
                var state=s.Core.CaptureState();state.Height=height;state.SupportId=height==0?"floor":null;state.Velocity=0;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{state});
                s.Player.SetBarrier(15);Assert.IsTrue(s.SetTrialEnemyAttack(true));long first=s.Core.Frame+OutsideExplorationSession.TrialEnemyInitialDelay;
                // 空中の射程境界を明示し、最初の着弾だけ高度を維持する。
                for(int i=0;i<60;i++){s.Core.TickFrame(0);var q=s.Core.CaptureState();q.Height=height;q.SupportId=height==0?"floor":null;q.Velocity=0;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{q});s.AdvancePlayerFrame();}
                Assert.AreEqual(first,s.Core.Frame);Assert.AreEqual(hit?5:15,s.Player.BarrierHp);Assert.AreEqual(100,s.Player.CurrentHp);
                if(!hit)return;
                for(int i=0;i<90;i++)s.TickTraversal(0);Assert.AreEqual(0,s.Player.BarrierHp);Assert.AreEqual(100,s.Player.CurrentHp);
                for(int i=0;i<90;i++)s.TickTraversal(0);Assert.AreEqual(90,s.Player.CurrentHp);
            }
        }
        [Test] public void 試用敵の本体被弾後も未保存の強靭状態を作らず厳密復元後の連続被弾が一致する()
        {
            using(var s=Sword161()){
                var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){s.Player.SetBarrier(15);Assert.IsTrue(s.SetTrialEnemyAttack(true));for(int i=0;i<240;i++)s.TickTraversal(0);}
                Assert.AreEqual(90,s.Player.CurrentHp);Assert.AreEqual(0,s.Player.BarrierHp);Assert.AreEqual(0,s.Player.AccumulatedDamage);Assert.AreEqual(0,s.Player.PoiseRecoveryRemainingFrames);Assert.IsFalse(s.Player.IsStaggered);Assert.IsFalse(s.Player.IsInvincible);
                var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                using(var cold=new OutsideExplorationSession()){
                    cold.RestoreSavedState(PortableCheckpointJson.Deserialize<OutsidePlayerSaveData>(PortableCheckpointJson.Serialize(actor)),PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime)),null,defs);
                    Assert.AreEqual(90,cold.Player.CurrentHp);Assert.AreEqual(0,cold.Player.AccumulatedDamage);Assert.AreEqual(0,cold.Player.PoiseRecoveryRemainingFrames);Assert.IsFalse(cold.Player.IsStaggered);Assert.IsFalse(cold.Player.IsInvincible);
                    using(var a=s.AttachPresentation())using(var b=cold.AttachPresentation()){for(int i=0;i<90;i++){s.TickTraversal(0);cold.TickTraversal(0);}Assert.AreEqual(80,s.Player.CurrentHp);Assert.AreEqual(80,cold.Player.CurrentHp);Assert.AreEqual(0,cold.Player.AccumulatedDamage);Assert.IsFalse(cold.Player.IsStaggered);Assert.IsFalse(cold.Player.IsInvincible);}
                }
            }
        }
        [Test] public void 試用敵は任意OFFで旧標的動作を維持し敵撃破後に攻撃しない()
        {
            using(var s=Sword161())using(var lease=s.AttachPresentation()){
                for(int i=0;i<240;i++)s.TickTraversal(0);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsTrue(s.SetTrialEnemyAttack(true));s.TrialTarget.TakeDamage(120,ElementType.None);s.TickTraversal(0);Assert.IsFalse(lease.TrialEnemyAttackEnabled);for(int i=0;i<200;i++)s.TickTraversal(0);Assert.AreEqual(100,s.Player.CurrentHp);
            }
        }
        [Test] public void 試用敵の着弾予定とバリアを厳密保存し復元後に二重発動せず帰還と通常continueで解除する()
        {
            using(var s=Sword161()){
                var defs=Kick214Definitions(s);using(var lease=s.AttachPresentation()){s.Player.SetBarrier(15);Assert.IsTrue(s.SetTrialEnemyAttack(true));for(int i=0;i<30;i++)s.TickTraversal(0);}
                var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var copy=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime));Assert.AreEqual(runtime.Key(),copy.Key());
                using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(actor,copy,null,defs));using(var a=s.AttachPresentation())using(var b=cold.AttachPresentation()){for(int i=0;i<30;i++){s.TickTraversal(0);cold.TickTraversal(0);}Assert.AreEqual(5,s.Player.BarrierHp);Assert.AreEqual(5,cold.Player.BarrierHp);Assert.AreEqual(100,cold.Player.CurrentHp);}}
                var returned=runtime.ForRun(Guid.NewGuid().ToString("N"));Assert.IsNull(returned.TrialEnemyAttack);Assert.NotNull(runtime.TrialEnemyAttack);
                using(var normal=new OutsideExplorationSession()){normal.ClassifyStartupRestore(true);normal.RestoreForPresentation(actor,runtime,null,defs);using(var l=normal.AttachPresentation()){Assert.IsFalse(l.TrialEnemyAttackEnabled);Assert.AreEqual(0,normal.Player.BarrierHp);}}
            }
        }
        [Test] public void 不正な試用敵攻撃は旧個体を置換せず旧OFF保存には新欄を出力しない()
        {
            using(var s=Sword161()){
                var defs=Kick214Definitions(s);var old=s.Player;var data=s.CaptureRuntime(defs);Assert.IsFalse(System.Text.Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(data)).Contains("TrialEnemyAttack"));var actor=OutsidePlayerSaveData.Capture(old);actor.Id=data.Id;data.TrialEnemyAttack=new OutsideTrialEnemyAttackData{DefinitionId="unknown",NextFrame=data.Core.Frame+1};Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,data,null,defs));Assert.AreSame(old,s.Player);data.TrialEnemyAttack.DefinitionId="trial-shield-enemy-v1";data.TrialEnemyAttack.NextFrame=data.Core.Frame;Assert.Throws<ArgumentException>(()=>data.Validate());data.TrialEnemyAttack.NextFrame=data.Core.Frame+91;Assert.Throws<ArgumentException>(()=>data.Validate());
            }
        }

        private static OutsideExplorationSession Shield227()
        {
            var s=Heal158();for(int n=0;n<100&&!s.Cards.Hand.Any(c=>c.Id=="shield");n++)s.Cards.Reload();Assert.IsTrue(s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="shield")));return s;
        }
        [TestCase(false)] [TestCase(true)] public void 試用外界シールドは地上空中で一度だけ消費し既存防御で耐久以上も本体へ通さない(bool air)
        {
            using(var s=Shield227())using(var l=s.AttachPresentation()){
                if(air)s.TickTraversal(0,true,true);var c=s.Cards.SelectedCard;long rev=s.Cards.HandRevision;long gauge=s.Core.CardGauge.CurrentUnits;
                Assert.IsTrue(s.TryUseSelectedCard(c,rev,out _));Assert.AreEqual(15,s.Player.BarrierHp);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.AreEqual(gauge-c.GetFinalCost()*60,s.Core.CardGauge.CurrentUnits);Assert.IsFalse(s.TryUseSelectedCard(c,rev,out _));
                s.Player.TakeDamage(10,affectsPoise:false);Assert.AreEqual(100,s.Player.CurrentHp);Assert.AreEqual(5,s.Player.BarrierHp);s.Player.TakeDamage(50,affectsPoise:false);Assert.AreEqual(100,s.Player.CurrentHp);Assert.AreEqual(0,s.Player.BarrierHp);
            }
        }
        [Test] public void 試用バリアは残量をcold復元し帰還のForRunでは解除して元保存を変更しない()
        {
            using(var s=Shield227()){
                var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));s.Player.TakeDamage(4,affectsPoise:false);}
                var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;Assert.AreEqual(11,runtime.TrialBarrierHp);
                var round=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime));Assert.AreEqual(runtime.Key(),round.Key());using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(actor,round,null,defs));Assert.AreEqual(11,cold.Player.BarrierHp);Assert.IsFalse(cold.RestoreSavedState(actor,round,null,defs));}
                var returned=runtime.ForRun(Guid.NewGuid().ToString("N"));Assert.AreEqual(0,returned.TrialBarrierHp);Assert.AreEqual(11,runtime.TrialBarrierHp);Assert.AreEqual(0,actor.Restore().BarrierHp);
                round.TrialBarrierHp=16;Assert.Throws<ArgumentException>(()=>round.Validate());round.TrialBarrierHp=-1;Assert.Throws<ArgumentException>(()=>round.Validate());
            }
        }
        [Test] public void 旧Runtimeのバリア0は新欄を出力せず未反映フレームの保存は拒否する()
        {
            using(var s=Shield227()){
                var defs=Kick214Definitions(s);Assert.IsFalse(System.Text.Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(s.CaptureRuntime(defs))).Contains("TrialBarrierHp"));s.Player.SetBarrier(15);
                typeof(PlayerCharacter).GetMethod("BeginHpFrame",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Player,null);Assert.Throws<ArgumentException>(()=>s.CaptureRuntime(defs));
            }
        }

        [Test] public void 試用バリアの通常continueは既存の回復地点契約で解除し厳密保存を変更しない()
        {
            using(var s=Shield227()){
                var defs=Kick214Definitions(s);s.Player.SetBarrier(15);var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                using(var cold=new OutsideExplorationSession()){cold.ClassifyStartupRestore(true);Assert.IsTrue(cold.RestoreForPresentation(actor,runtime,null,defs));Assert.AreEqual(0,cold.Player.BarrierHp);Assert.AreEqual(15,runtime.TrialBarrierHp);Assert.IsFalse(cold.RestoreForPresentation(actor,runtime,null,defs));}
            }
        }
        [Test] public void 試用バリアの再付与は加算せず時間停止と風剥離は既存防御に従う()
        {
            using(var s=Shield227()){
                using(var l=s.AttachPresentation()){s.Player.SetBarrier(4);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Assert.AreEqual(15,s.Player.BarrierHp);}
                for(int i=0;i<20;i++)s.Core.TickFrame(0);Assert.AreEqual(15,s.Player.BarrierHp);
                s.Player.TakeDamage(1,affectsPoise:false,attackProperties:AttackProperty.Wind);Assert.AreEqual(0,s.Player.BarrierHp);Assert.AreEqual(100,s.Player.CurrentHp);
            }
        }

        private static CheckpointDefinitions Kick214Definitions(OutsideExplorationSession s)
        {var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);return DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;}
        private static void Kick214Tick(OutsideExplorationSession s,float horizontal=0){s.TickTraversal(horizontal);s.ProcessCardUseInput(false,false,false,0,0,out _);}
        [TestCase(0)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(9)] [TestCase(20)] [TestCase(21)] [TestCase(25)] [TestCase(38)]
        public void 試用蹴りは前隙と命中済みと後隙と継続猶予を同じ論理位置から復元する(int age)
        {
            using(var s=Sword161(175)){
                s.EnableTrialNormalKick();var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){Assert.IsTrue(s.ProcessCardUseInput(false,false,true,0,1,out _));for(int n=0;n<age;n++)Kick214Tick(s);}
                var runtime=s.CaptureRuntime(defs);Assert.NotNull(runtime.NormalKick);Assert.AreEqual(age<21,runtime.NormalKick.Active);Assert.AreEqual(age>=6&&age<21?2:0,runtime.NormalKick.HitTargets.Length);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                var round=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime));Assert.AreEqual(runtime.Key(),round.Key());using(var cold=new OutsideExplorationSession()){
                    Assert.IsTrue(cold.RestoreSavedState(actor,round,null,defs));Assert.IsFalse(cold.RestoreSavedState(actor,round,null,defs));Assert.AreEqual(AttackReservationKind.None,cold.PendingAttack);
                    using(var a=s.AttachPresentation())using(var b=cold.AttachPresentation())for(int n=0;n<45;n++){Kick214Tick(s);Kick214Tick(cold);Assert.AreEqual(s.Core.Frame,cold.Core.Frame);Assert.AreEqual(s.Core.Position,cold.Core.Position);Assert.AreEqual(s.Core.Height,cold.Core.Height);Assert.AreEqual(s.TrialTarget.CurrentHp,cold.TrialTarget.CurrentHp);Assert.AreEqual(s.SecondTrialTarget.CurrentHp,cold.SecondTrialTarget.CurrentHp);Assert.AreEqual(s.NormalKick.IsActive,cold.NormalKick.IsActive);Assert.AreEqual(s.NormalKick.NextGroundStage,cold.NormalKick.NextGroundStage);}
                    Assert.AreEqual(110,cold.TrialTarget.CurrentHp);Assert.AreEqual(110,cold.SecondTrialTarget.CurrentHp);
                }
            }
        }
        [Test] public void 一体だけ命中済みの保存は対象IDを分離して復元し再命中しない()
        {
            using(var s=Sword161(182)){
                s.EnableTrialNormalKick();var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=0;n<6;n++)Kick214Tick(s);}
                var runtime=s.CaptureRuntime(defs);CollectionAssert.AreEqual(new[]{1},runtime.NormalKick.HitTargets);var player=OutsidePlayerSaveData.Capture(s.Player);player.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(player,runtime,null,defs);using(var l=cold.AttachPresentation())for(int n=0;n<10;n++)Kick214Tick(cold);Assert.AreEqual(120,cold.TrialTarget.CurrentHp);Assert.AreEqual(110,cold.SecondTrialTarget.CurrentHp);}
                var copy=runtime.Copy();copy.NormalKick.HitTargets[0]=0;CollectionAssert.AreEqual(new[]{1},runtime.NormalKick.HitTargets);
            }
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)]
        public void 不正な蹴り保存は既存Sessionを一部変更せず拒否する(int fault)
        {
            using(var s=Sword161(175)){
                s.EnableTrialNormalKick();var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=0;n<6;n++)Kick214Tick(s);}var runtime=s.CaptureRuntime(defs);var player=OutsidePlayerSaveData.Capture(s.Player);player.Id=runtime.Id;var state=runtime.NormalKick;long frame=s.Core.Frame;var actor=s.Player;var sequence=s.NormalKick;
                switch(fault){case 0:state.DefinitionKey=new string('a',64);break;case 1:state.Kind=(OutsideKickKind)99;break;case 2:state.EndsAt++;break;case 3:state.HitTargets=new[]{0,0};break;case 4:state.HitTargets=new[]{2};break;case 5:state.StartedAt=frame;state.EndsAt=frame+21;break;case 6:state.AttackFacing=-s.Core.Facing;break;case 7:runtime.SecondTrialTarget=null;break;}
                Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(player,runtime,null,defs));Assert.AreSame(actor,s.Player);Assert.AreSame(sequence,s.NormalKick);Assert.AreEqual(frame,s.Core.Frame);Assert.IsTrue(s.NormalKick.IsActive);Assert.AreEqual(110,s.TrialTarget.CurrentHp);
            }
        }
        [Test] public void 空中蹴り保存は向きと横移動と落下を保ち着地で中断する()
        {
            using(var s=Sword161(175)){
                s.EnableTrialNormalKick();Position188(s,175*6000,60000);var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _, -1);for(int n=0;n<6;n++)Kick214Tick(s,-1);}
                var runtime=s.CaptureRuntime(defs);Assert.AreEqual(OutsideKickKind.AirDown,runtime.NormalKick.Kind);Assert.AreEqual(1,runtime.NormalKick.AttackFacing);Assert.AreEqual(-1,runtime.Core.Facing);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,runtime,null,defs);using(var a=s.AttachPresentation())using(var b=cold.AttachPresentation())for(int n=0;n<50;n++){Kick214Tick(s,-1);Kick214Tick(cold,-1);Assert.AreEqual(s.Core.Position,cold.Core.Position);Assert.AreEqual(s.Core.Height,cold.Core.Height);Assert.AreEqual(s.Core.VerticalVelocity,cold.Core.VerticalVelocity);Assert.AreEqual(s.SecondTrialTarget.CurrentHp,cold.SecondTrialTarget.CurrentHp);}Assert.IsTrue(cold.Core.IsGrounded);Assert.IsFalse(cold.IsAttacking);}
            }
        }
        [Test] public void 蹴り継続猶予は保存後も十八frame境界で失効し入力予約は復元しない()
        {
            using(var s=Sword161(175)){
                s.EnableTrialNormalKick();var defs=Kick214Definitions(s);using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=0;n<22;n++)Kick214Tick(s);}
                var runtime=s.CaptureRuntime(defs);var player=OutsidePlayerSaveData.Capture(s.Player);player.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(player,runtime,null,defs);using(var l=cold.AttachPresentation()){Assert.IsFalse(cold.ProcessCardUseInput(false,false,true,0,2,out _));Assert.IsFalse(cold.IsAttacking);for(int n=0;n<17;n++)Kick214Tick(cold);Assert.AreEqual(1,cold.NormalKick.NextGroundStage);cold.TickTraversal(0);Assert.IsTrue(cold.ProcessCardUseInput(false,false,true,0,3,out _));Assert.AreEqual(OutsideKickKind.GroundFirst,cold.NormalKick.Kind);}}
            }
        }
        [Test] public void 試用登録外の地形へ蹴り保存を混入させても旧Sessionへ採用しない()
        {
            using(var source=Sword161(175))using(var foreign=new OutsideExplorationSession()){
                source.EnableTrialNormalKick();var defs=Kick214Definitions(source);using(var l=source.AttachPresentation())source.ProcessCardUseInput(false,false,true,0,1,out _);var runtime=source.CaptureRuntime(defs);var player=OutsidePlayerSaveData.Capture(source.Player);player.Id=runtime.Id;
                // 試験だけでreadonly地形を故障注入し、通常経路では作れない外国定義を検証する。
                var core=new OutsideExplorationCore(new OutsideMovementDefinition("unregistered-kick-map",32*60000,6*60000,6*60000));typeof(OutsideExplorationSession).GetField("<Core>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(foreign,core);foreign.TryInitializePlayer(()=>new PlayerCharacter(100));var actor=foreign.Player;var sequence=foreign.NormalKick;
                runtime.Core=core.CaptureState();runtime.NormalKick.StartedAt=0;runtime.NormalKick.EndsAt=21;Assert.Throws<ArgumentException>(()=>foreign.RestoreSavedState(player,runtime,null,defs));Assert.AreSame(actor,foreign.Player);Assert.AreSame(core,foreign.Core);Assert.IsFalse(sequence.IsConfigured);
                sequence.Configure(OutsideKickTrialDefinition.Timings());using(var l=foreign.AttachPresentation())sequence.TryRequest(0,true,1);Assert.Throws<ArgumentException>(()=>foreign.CaptureRuntime());
            }
        }
        [Test] public void 命中済みの途中保存が失敗しても旧bytesを保ち同じ計画の再試行で二重命中しない()
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"kick214-"+Guid.NewGuid().ToString("N"));try{using(var s=Sword161(175)){
                s.EnableTrialNormalKick();var defs=Kick214Definitions(s);var old=s.CaptureRuntime(defs);var oldPlayer=OutsidePlayerSaveData.Capture(s.Player);oldPlayer.Id=old.Id;var io=new Action159Io{Fail=false};var store=new Olca.Unity.ClearReturnFileStore(dir,io);
                var baseline=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=oldPlayer,OutsideRuntimeSave=old,OutsideItemState=new OutsideItemStateData{ActorKey=oldPlayer.Key(),RuntimeKey=old.Key()}};Assert.IsTrue(store.TryWrite(0,baseline,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
                using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=0;n<6;n++)Kick214Tick(s);}var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                var plan=new ClearReturnProfile{Revision=2,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=actor,OutsideRuntimeSave=runtime,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};io.Fail=true;Assert.IsFalse(store.TryWrite(1,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(1,plan,out error),error);Assert.IsTrue(store.TryWrite(1,plan,out _));var loaded=store.Read(out error);Assert.IsTrue(loaded.OutsideRuntimeSave.NormalKick.Active);
                using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var l=cold.AttachPresentation())for(int n=0;n<10;n++)Kick214Tick(cold);Assert.AreEqual(110,cold.TrialTarget.CurrentHp);Assert.AreEqual(110,cold.SecondTrialTarget.CurrentHp);}
            }}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [Test] public void 試用蹴り三段は範囲内の二体へ各段一度だけ命中し手札とゲージを消費しない()
        {
            using(var s=Sword161(175))using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();long revision=s.Cards.HandRevision,gauge=s.Core.CardGauge.CurrentUnits;Assert.IsTrue(s.ProcessCardUseInput(false,false,true,0,1,out _));
                for(int n=1;n<=51;n++){s.TickTraversal(0);s.ProcessCardUseInput(false,false,n==11||n==26,0,n,out _);}
                Assert.AreEqual(90,s.TrialTarget.CurrentHp);Assert.AreEqual(90,s.SecondTrialTarget.CurrentHp);Assert.AreEqual(revision,s.Cards.HandRevision);Assert.GreaterOrEqual(s.Core.CardGauge.CurrentUnits,gauge);Assert.IsFalse(s.NormalKick.IsActive);
            }
        }
        [TestCase(140,1,120,120)] [TestCase(175,1,110,110)] [TestCase(215,-1,120,110)]
        public void 試用範囲は空振りと左右を分け範囲外や死亡した敵へ再命中しない(int position,int facing,int first,int second)
        {
            using(var s=Sword161(position,facing))using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=0;n<10;n++){s.TickTraversal(0);s.ProcessCardUseInput(false,false,false,0,0,out _);}Assert.AreEqual(first,s.TrialTarget.CurrentHp);Assert.AreEqual(second,s.SecondTrialTarget.CurrentHp);
            }
        }
        [TestCase(OutsideKickKind.AirUp,60000,false)] [TestCase(OutsideKickKind.AirDown,60000,true)] [TestCase(OutsideKickKind.AirForward,60000,true)]
        public void 空中試用範囲は方向別の縦区間で地上標的を選ぶ(OutsideKickKind kind,long height,bool expected)
        {Assert.AreEqual(expected,OutsideKickTrialDefinition.Contains(kind,17*60000,height,1,18*60000));Assert.IsFalse(OutsideKickTrialDefinition.Contains(kind,17*60000,height,1,16*60000));}
        [Test] public void 試用蹴りは着地で中断しその後の有効期間に敵へ命中しない()
        {
            using(var s=Sword161(175)){
                s.EnableTrialNormalKick();Position188(s,175*6000,3000);using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _,1);while(!s.Core.IsGrounded)s.TickTraversal(0);
                long hp=s.TrialTarget.CurrentHp;s.TickTraversal(0);Assert.AreEqual(120,hp);Assert.AreEqual(hp,s.TrialTarget.CurrentHp);Assert.IsFalse(s.NormalKick.IsActive);}
            }
        }
        [Test] public void カード取消成功前の蹴り命中とカード効果は別々で取消後の蹴りを再実行しない()
        {
            using(var s=Sword161(175))using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();long rev=s.Cards.HandRevision;s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=1;n<=15;n++){s.TickTraversal(0);s.ProcessCardUseInput(n==11,false,false,2,0,out _);}Assert.AreEqual(80,s.TrialTarget.CurrentHp);Assert.AreEqual(110,s.SecondTrialTarget.CurrentHp);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.IsFalse(s.NormalKick.IsActive);for(int n=0;n<20;n++)s.TickTraversal(0);Assert.AreEqual(110,s.SecondTrialTarget.CurrentHp);
            }
        }
        [Test] public void 試用二体のHP保存は旧省略欄を保ち終了後復元で命中を二重適用しない()
        {
            using(var s=Sword161(175)){
                s.EnableTrialNormalKick();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var old=s.CaptureRuntime(defs);Assert.IsNull(old.SecondTrialTarget);var oldJson=PortableCheckpointJson.Serialize(old);Assert.IsFalse(System.Text.Encoding.UTF8.GetString(oldJson).Contains("SecondTrialTarget"));Assert.AreEqual(old.Key(),PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(oldJson).Key());
                using(var l=s.AttachPresentation()){s.ProcessCardUseInput(false,false,true,0,1,out _);for(int n=0;n<40;n++){s.TickTraversal(0);s.ProcessCardUseInput(false,false,false,0,0,out _);}}
                var runtime=s.CaptureRuntime(defs);Assert.AreEqual(110,runtime.SecondTrialTarget.Hp);var player=OutsidePlayerSaveData.Capture(s.Player);player.Id=runtime.Id;var serialized=PortableCheckpointJson.Serialize(runtime);var round=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(serialized);Assert.AreEqual(runtime.Key(),round.Key());
                using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(player,round,null,defs);Assert.AreEqual(110,cold.TrialTarget.CurrentHp);Assert.AreEqual(110,cold.SecondTrialTarget.CurrentHp);using(var l=cold.AttachPresentation())for(int n=0;n<10;n++)cold.TickTraversal(0);Assert.AreEqual(110,cold.SecondTrialTarget.CurrentHp);}
                var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"kick213-"+Guid.NewGuid().ToString("N"));try{
                    var io=new Action159Io{Fail=false};var store=new Olca.Unity.ClearReturnFileStore(dir,io);var oldPlayer=OutsidePlayerSaveData.Capture(s.Player);oldPlayer.Id=old.Id;
                    var baseline=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=oldPlayer,OutsideRuntimeSave=old,OutsideItemState=new OutsideItemStateData{ActorKey=oldPlayer.Key(),RuntimeKey=old.Key()}};Assert.IsTrue(store.TryWrite(0,baseline,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);
                    var plan=new ClearReturnProfile{Revision=2,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=player,OutsideRuntimeSave=round,OutsideItemState=new OutsideItemStateData{ActorKey=player.Key(),RuntimeKey=round.Key()}};io.Fail=true;Assert.IsFalse(store.TryWrite(1,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(1,plan,out error),error);var saved=store.Read(out error);Assert.AreEqual(110,saved.OutsideRuntimeSave.SecondTrialTarget.Hp);Assert.IsTrue(store.TryWrite(1,plan,out _));Assert.AreEqual(110,store.Read(out _).OutsideRuntimeSave.TrialTarget.Hp);
                }finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
                round.SecondTrialTarget.Id="unknown";Assert.Throws<ArgumentException>(()=>round.Validate());round.SecondTrialTarget.Id="trial-kick-target-v1";round.SecondTrialTarget.Hp=121;Assert.Throws<ArgumentException>(()=>round.Validate());
            }
        }
        [Test] public void ダッシュ中の後入力蹴りが拒否されても古いカード予約を復活させない()
        {
            using(var s=Sword161(startup:3,recovery:8))using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();s.TickTraversal(1,dashDown:true,dashHeld:true);Assert.IsTrue(s.Core.IsDashing);Assert.IsTrue(s.ProcessCardUseInput(true,false,false,1,0,out _));long rev=s.Cards.HandRevision;
                for(int n=1;n<=9;n++){s.TickTraversal(0);s.ProcessCardUseInput(n==8,false,n==9,2,3,out _);if(n==8)Assert.AreEqual(AttackReservationKind.Card,s.PendingAttack);}
                Assert.IsTrue(s.Core.IsDashing);Assert.IsFalse(s.NormalKick.IsActive);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);
                for(int n=0;n<15;n++){s.TickTraversal(0);s.ProcessCardUseInput(false,false,false,0,0,out _);}Assert.AreEqual(rev,s.Cards.HandRevision);Assert.IsFalse(s.NormalKick.IsActive);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 蹴り中のカード予約は六frame境界と取消可能化で一枚だけ消費する(bool late)
        {
            using(var s=Sword161())using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();long at=s.Core.Frame;long revision=s.Cards.HandRevision;Assert.IsTrue(s.ProcessCardUseInput(false,false,true,0,1,out _));
                for(int n=1;n<=15;n++){s.TickTraversal(0);s.ProcessCardUseInput(n==(late?10:9),false,false,2,0,out _);}
                Assert.AreEqual(revision+(late?1:0),s.Cards.HandRevision);Assert.AreEqual(late?false:true,s.NormalKick.IsActive);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 同frameのカードと蹴りは不明ならカードで後の蹴りだけ上書きする(bool basicLater)
        {
            using(var s=Sword161())using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();long revision=s.Cards.HandRevision;Assert.IsTrue(s.ProcessCardUseInput(true,false,true,basicLater?1:0,basicLater?2:0,out _));Assert.AreEqual(basicLater?revision:revision+1,s.Cards.HandRevision);Assert.AreEqual(basicLater,s.NormalKick.IsActive);
            }
        }
        [Test] public void カード前隙中の蹴り予約は消費済みカードを巻き戻さず取消境界で発動する()
        {
            using(var s=Sword161(startup:3,recovery:8))using(var l=s.AttachPresentation()){
                s.EnableTrialNormalKick();Assert.IsTrue(s.ProcessCardUseInput(true,false,false,1,0,out _));long rev=s.Cards.HandRevision;
                for(int n=1;n<=11;n++){s.TickTraversal(0);s.ProcessCardUseInput(false,false,n==7,0,2,out _);}Assert.IsTrue(s.NormalKick.IsActive);Assert.IsNull(s.AttackState);Assert.AreEqual(rev,s.Cards.HandRevision);
            }
        }
        private static OutsideExplorationSession Deck188(out CheckpointDefinitions definitions)
        {
            var data=Deck155();definitions=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;var session=new OutsideExplorationSession();session.TryInitializePlayer(()=>new PlayerCharacter(100));Assert.IsTrue(session.QueueSelectedDeck("reload188",data,Owned(data),new SeededBattleRandom(188),out _));Walk155(session,10*60000);session.Core.CardGauge.Configure(100,0);Assert.IsTrue(session.Cards.TryConsumeSelected(session.Cards.SelectedCard,session.Cards.HandRevision,session.Core.CardGauge,out _));return session;
        }
        private static void Position188(OutsideExplorationSession session,long x,long height)
        {
            var state=session.Core.CaptureState();state.Position=x;state.Height=height;state.Velocity=0;state.SupportId=height==0?"floor":null;state.Moving=false;state.MovementFrame=state.IdleFrame=0;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(session.Core,new object[]{state});using(var lease=session.AttachPresentation()){session.Core.TickFrame(0);session.AdvancePlayerFrame();}
        }
        private static void PointTick188(OutsideExplorationSession session,float direction=0,bool jump=false,bool held=false){session.Core.TickFrame(direction,jump,held);session.AdvancePlayerFrame();session.AdvanceDeckLocation();}
        [TestCase(-1)][TestCase(1)] public void 地上利用していない空中通過は手札乱数ゲージとHUD状態を変更しない(int direction)
        {
            using(var session=Deck188(out var defs)){
                long start=direction>0?214*6000:226*6000,end=direction>0?226*6000:214*6000;Position188(session,start,120000);string key=session.Cards.CaptureState(defs).Key();long gauge=session.Core.CardGauge.CurrentUnits,revision=session.Cards.HandRevision;bool seen=false;
                using(var lease=session.AttachPresentation()){for(int n=0;n<30&&(direction>0?session.Core.Position<end:session.Core.Position>end);n++){PointTick188(session,direction);if(OutsideTrialRoute.IsReloadZone(session.Core)){seen=true;Assert.IsFalse(session.Core.IsGrounded);Assert.IsFalse(session.IsDeckEditingLocation);Assert.IsFalse(lease.IsDeckEditingLocation);}}Assert.IsTrue(seen);Assert.IsFalse(session.Core.IsGrounded);}
                Assert.AreEqual(26,session.Cards.RemainingCount);Assert.AreEqual(revision,session.Cards.HandRevision);Assert.AreEqual(key,session.Cards.CaptureState(defs).Key());Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);
            }
        }
        [Test] public void 地上利用後のジャンプと再表示と空中退出では一度だけ再装填する()
        {
            using(var session=Deck188(out var defs)){
                Position188(session,22*60000,0);session.AdvanceDeckLocation();using(var lease=session.AttachPresentation()){PointTick188(session,0,true,true);Assert.IsFalse(session.Core.IsGrounded);Assert.IsTrue(session.IsDeckEditingLocation);Assert.AreEqual(26,session.Cards.RemainingCount);}
                using(var lease=session.AttachPresentation()){Assert.IsTrue(lease.IsDeckEditingLocation);for(int n=0;n<20&&session.Core.Position<226*6000;n++)PointTick188(session,1,false,true);Assert.IsFalse(session.Core.IsGrounded);Assert.AreEqual(27,session.Cards.RemainingCount);Assert.AreEqual(session.Core.CardGauge.MaximumUnits,session.Core.CardGauge.CurrentUnits);string key=session.Cards.CaptureState(defs).Key();for(int n=0;n<5;n++)PointTick188(session);Assert.AreEqual(key,session.Cards.CaptureState(defs).Key());}
            }
        }
        [Test] public void 地点内で地上へ着地した後だけ利用状態となり退出で補充する()
        {
            using(var session=Deck188(out _)){
                Position188(session,22*60000,1000);Assert.IsFalse(session.IsDeckEditingLocation);using(var lease=session.AttachPresentation()){for(int n=0;n<10&&!session.Core.IsGrounded;n++)PointTick188(session);Assert.IsTrue(session.Core.IsGrounded);Assert.IsTrue(session.IsDeckEditingLocation);Assert.AreEqual(26,session.Cards.RemainingCount);for(int n=0;n<20&&session.Core.Position<226*6000;n++)PointTick188(session,1);Assert.AreEqual(27,session.Cards.RemainingCount);}
            }
        }
        [Test] public void 初回の地点管理を空中で有効化しても無料のゲージ全回復を起こさない()
        {
            using(var session=new OutsideExplorationSession()){
                session.TryInitializePlayer(()=>new PlayerCharacter(100));var cards=CardFixture();Assert.IsTrue(session.TryInitializeCards(cards,Owned(cards),new SeededBattleRandom(188),out _));Position188(session,22*60000,120000);session.Core.CardGauge.Configure(100,0);session.Core.CardGauge.TrySpend(30);Assert.IsTrue(session.QueueSelectedDeck(null,null,null,null,out _));Assert.IsFalse(session.IsDeckEditingLocation);long gauge=session.Core.CardGauge.CurrentUnits;using(var lease=session.AttachPresentation())for(int n=0;n<20&&session.Core.Position<226*6000;n++)PointTick188(session,1);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(20,session.Cards.TotalCount);
            }
        }
        [Test] public void 履歴のない空中Runtimeの地点推定は管理開始前から従来互換を保持する()
        {
            using(var source=Deck188(out var defs))using(var restored=new OutsideExplorationSession()){
                Position188(source,22*60000,120000);Assert.IsFalse(source.IsDeckEditingLocation);var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;restored.RestoreSavedState(actor,runtime,null,defs);Assert.IsFalse(restored.IsDeckEditingLocation);restored.QueueSelectedDeck(null,null,null,null,out _);Assert.IsTrue(restored.IsDeckEditingLocation,"保存に利用履歴がないため旧横範囲推定を残す");using(var lease=restored.AttachPresentation())for(int n=0;n<20&&restored.Core.Position<226*6000;n++)PointTick188(restored,1);Assert.AreEqual(27,restored.Cards.RemainingCount);
            }
        }
        [Test] public void 地点外Runtimeの復元は以前の利用ラッチを解除する()
        {
            using(var source=Deck188(out var defs))using(var target=Deck188(out _)){
                Position188(target,22*60000,0);target.AdvanceDeckLocation();Assert.IsTrue(target.IsDeckEditingLocation);Position188(source,23*60000,0);var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;target.RestoreSavedState(actor,runtime,null,defs);Position188(target,22*60000,120000);Assert.IsFalse(target.IsDeckEditingLocation);using(var lease=target.AttachPresentation())for(int n=0;n<20&&target.Core.Position<226*6000;n++)PointTick188(target,1);Assert.AreEqual(26,target.Cards.RemainingCount);
            }
        }
        [Test] public void 新規空中通過の範囲内でも許可済みカードと六フレーム予約は地点取消されない()
        {
            using(var session=Buffer160()){
                Position188(session,22*60000,120000);session.QueueSelectedDeck(null,null,null,null,out _);using(var lease=session.AttachPresentation()){Start160(session);Tick160(session,true);Assert.AreEqual(AttackReservationKind.Card,session.PendingAttack);session.AdvanceDeckLocation();Assert.IsTrue(session.IsAttacking);Assert.AreEqual(AttackReservationKind.Card,session.PendingAttack);Assert.IsFalse(session.IsDeckEditingLocation);}
            }
        }

        private static void Startup187(bool thirty,out OutsidePlayerSaveData actor,out OutsideRuntimeSaveData runtime,out CheckpointDefinitions definitions)
        {
            var data=thirty?Deck155():CardFixture();definitions=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;
            using(var source=new OutsideExplorationSession()){
                source.TryConfigureTraversal(OutsideTraversalProfile.ValidationWater);source.TryInitializePlayer(()=>new PlayerCharacter(100));source.Player.SetHp(37);source.Player.ApplyPoison("startup187",new StatusEffect(StatusEffectType.Poison,60000,1),600);
                if(thirty){Assert.IsTrue(source.QueueSelectedDeck("startup187",data,Owned(data),new SeededBattleRandom(187),out _));Walk155(source,10*60000);}else Assert.IsTrue(source.TryInitializeCards(data,Owned(data),new SeededBattleRandom(187),out _));
                source.Core.CardGauge.Configure(140,7);source.Core.CardGauge.TrySpend(70);for(int n=0;n<2;n++)Assert.IsTrue(source.Cards.TryConsumeSelected(source.Cards.SelectedCard,source.Cards.HandRevision,source.Core.CardGauge,out _));
                using(var lease=source.AttachPresentation())source.AdvancePlayerFrame();runtime=source.CaptureRuntime(definitions);runtime.Core.Position=20*60000;runtime.Core.Moving=false;runtime.Core.MovementFrame=runtime.Core.IdleFrame=0;runtime.TrialTarget=new OutsideTrialTargetData{Hp=90,Hits=1};runtime.Validate();actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;
            }
        }
        private static OutsideItemStateData StartupItems187(OutsidePlayerSaveData actor,OutsideRuntimeSaveData runtime)=>new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key(),Cooldowns=new[]{new OutsideItemCooldownData{Id="startup187",RemainingFrames=120}}};
        [TestCase(false)][TestCase(true)] public void 通常cold再開だけ復帰地点で全快再装填し元保存と再表示の個体乱数を保つ(bool thirty)
        {
            Startup187(thirty,out var actor,out var runtime,out var defs);var items=StartupItems187(actor,runtime);var bytes=PortableCheckpointJson.Serialize(runtime);using(var cold=new OutsideExplorationSession()){
                cold.ClassifyStartupRestore(true);Assert.IsTrue(cold.RestoreForPresentation(actor,runtime,OutsideTrialRoute.RecoveryPointId,defs,OutsideTraversalProfile.ValidationWater,items));Assert.AreEqual(100,cold.Player.CurrentHp);Assert.IsEmpty(cold.Player.CapturePoisons());Assert.AreEqual(OutsideTrialRoute.RecoveryX,cold.Core.Position);Assert.AreEqual(runtime.Core.Frame,cold.Core.Frame);Assert.AreEqual(140*OutsideCardGauge.UnitsPerPoint,cold.Core.CardGauge.CurrentUnits);Assert.AreEqual(thirty?30:20,cold.Cards.TotalCount);Assert.AreEqual(cold.Cards.TotalCount-3,cold.Cards.RemainingCount);Assert.AreEqual(0,cold.Cards.SelectedIndex);Assert.AreEqual(120,cold.TrialTarget.CurrentHp);Assert.AreEqual(120,cold.ItemCooldownRemaining("startup187"));
                var player=cold.Player;var cards=cold.Cards;string deck=cards.CaptureState(defs).Key();using(var lease=cold.AttachPresentation()){for(int n=0;n<10;n++){cold.Core.TickFrame(1);cold.AdvancePlayerFrame();}}cold.Player.SetHp(56);long x=cold.Core.Position;int cd=cold.ItemCooldownRemaining("startup187");cold.ClassifyStartupRestore(true);Assert.IsFalse(cold.RestoreForPresentation(actor,runtime,OutsideTrialRoute.RecoveryPointId,defs,null,items));Assert.AreSame(player,cold.Player);Assert.AreSame(cards,cold.Cards);Assert.AreEqual(56,player.CurrentHp);Assert.AreEqual(x,cold.Core.Position);Assert.AreEqual(deck,cards.CaptureState(defs).Key());Assert.AreEqual(cd,cold.ItemCooldownRemaining("startup187"));
                var changed=runtime.Copy();changed.Core.GaugeUnits--;Assert.Throws<ArgumentException>(()=>cold.RestoreForPresentation(actor,changed,OutsideTrialRoute.RecoveryPointId,defs));Assert.AreEqual(x,cold.Core.Position);CollectionAssert.AreEqual(bytes,PortableCheckpointJson.Serialize(runtime));
            }
        }
        [Test] public void 攻略再開や同プロセスの帰還は通常continueへ再分類せず厳密復元する()
        {
            Startup187(true,out var actor,out var runtime,out var defs);using(var session=new OutsideExplorationSession()){
                session.ClassifyStartupRestore(false);session.ClassifyStartupRestore(true);Assert.IsTrue(session.RestoreForPresentation(actor,runtime,OutsideTrialRoute.RecoveryPointId,defs,OutsideTraversalProfile.ValidationWater,StartupItems187(actor,runtime)));Assert.AreEqual(37,session.Player.CurrentHp);Assert.IsNotEmpty(session.Player.CapturePoisons());Assert.AreEqual(20*60000,session.Core.Position);Assert.AreEqual(25,session.Cards.RemainingCount);Assert.AreEqual(90,session.TrialTarget.CurrentHp);Assert.AreEqual(runtime.Cards.Key(),session.Cards.CaptureState(defs).Key());
            }
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)] public void 通常continueの候補不正はliveと分類と乱数を変えず修正後だけ採用する(int invalid)
        {
            Startup187(true,out var actor,out var runtime,out var defs);var bad=runtime.Copy();var person=actor.Copy();var items=StartupItems187(actor,runtime);string point=OutsideTrialRoute.RecoveryPointId;
            if(invalid==0)point="missing";if(invalid==1)bad.Core.DefinitionKey=new string('0',64);if(invalid==2)bad.Cards.Cards[0].ValueHash=new string('0',64);if(invalid==3)items.ActorKey=new string('0',64);if(invalid==4)bad.FixedCameraCenter=long.MaxValue;if(invalid==5)bad.TrialTarget.Id="missing";if(invalid==6)person.Player.Hp=0;
            using(var target=new OutsideExplorationSession()){
                target.ClassifyStartupRestore(true);string before=CoreKey141(target.Core);Assert.Catch<ArgumentException>(()=>target.RestoreForPresentation(person,bad,point,defs,OutsideTraversalProfile.ValidationWater,invalid==3?items:null));Assert.IsNull(target.Player);Assert.IsNull(target.Cards);Assert.AreEqual(before,CoreKey141(target.Core));Assert.IsTrue(target.RestoreForPresentation(actor,runtime,OutsideTrialRoute.RecoveryPointId,defs,OutsideTraversalProfile.ValidationWater,StartupItems187(actor,runtime)));Assert.AreEqual(100,target.Player.CurrentHp);Assert.AreEqual(27,target.Cards.RemainingCount);
            }
        }
        [Test] public void 旧Runtimeなしの通常continueも所持や形式を書き換えず保存個体を全回復する()
        {
            Startup187(false,out var actor,out _,out var defs);using(var target=new OutsideExplorationSession()){target.ClassifyStartupRestore(true);Assert.IsTrue(target.RestoreForPresentation(actor,null,OutsideTrialRoute.RecoveryPointId,defs,OutsideTraversalProfile.ValidationWater));Assert.AreEqual(100,target.Player.CurrentHp);Assert.IsEmpty(target.Player.CapturePoisons());Assert.AreEqual(OutsideTrialRoute.RecoveryX,target.Core.Position);Assert.IsNull(target.Cards);target.Player.SetHp(42);Assert.IsFalse(target.RestoreForPresentation(actor,null,OutsideTrialRoute.RecoveryPointId,defs));Assert.AreEqual(42,target.Player.CurrentHp);}
        }

        [TestCase(false)][TestCase(true)] public void 水中保存の通常continueは乾いた復帰地点で呼吸を全快し無制限設定を保つ(bool unlimited)
        {
            using(var source=Water167())using(var cold=new OutsideExplorationSession()){
                source.SetWaterBreathingUnlimited(unlimited);using(var lease=source.AttachPresentation())for(int n=0;n<10;n++)WaterTick167(source);Assert.AreEqual(unlimited?600:590,source.BreathFramesRemaining);source.Player.SetHp(37);var runtime=source.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;cold.ClassifyStartupRestore(true);Assert.IsTrue(cold.RestoreForPresentation(actor,runtime,OutsideTrialRoute.RecoveryPointId,null,OutsideTraversalProfile.ValidationWater));Assert.AreEqual(100,cold.Player.CurrentHp);Assert.IsFalse(cold.Core.IsInWater);Assert.AreEqual(600,cold.BreathFramesRemaining);Assert.AreEqual(unlimited,cold.CaptureRuntime().Water.Unlimited);
            }
        }

        private static OutsideExplorationSession Water167(long x=60000,long y=0,long velocity=0,OutsideTraversalProfile profile=null)
        {var s=new OutsideExplorationSession();s.TryConfigureTraversal(profile??OutsideTraversalProfile.ValidationWater);var data=s.Core.CaptureState();data.Position=x;data.Height=y;data.Velocity=velocity;data.SupportId=y==0?"floor":null;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{data});s.TryInitializePlayer(()=>new PlayerCharacter(100));return s;}
        private static void WaterTick167(OutsideExplorationSession s,float x=0,bool down=false,bool held=false,bool dash=false){s.Core.TickFrame(x,down,held,dash,dash,s.IsLandingLockedForNextFrame);s.AdvancePlayerFrame();}
        [Test] public void 空中ヒールの共有予約は期限内に同じ空中判定で二枚目を開始する()
        {
            using(var s=Buffer160())using(var lease=s.AttachPresentation()){
                s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();Start160(s);long revision=s.Cards.HandRevision;
                for(int i=1;i<=11;i++){int facing=s.Core.Facing;bool grounded=s.Core.IsGrounded;s.Core.TickFrame(0,false,true);s.ObserveAttackBoundary(facing,grounded);s.AdvancePlayerFrame();s.ProcessCardUseInput(i==6,false,false,0,0,out _);if(i==6)Assert.AreEqual(AttackReservationKind.Card,s.PendingAttack);}
                Assert.IsFalse(s.Core.IsGrounded);Assert.AreEqual(revision+1,s.Cards.HandRevision);Assert.AreEqual(s.Core.Frame,s.AttackState.StartedAt);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);for(int i=0;i<12;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.AreEqual(50,s.Player.CurrentHp);
            }
        }
        [Test] public void 水中の空中ヒールは軌道と呼吸と水域境界を変更しない()
        {
            using(var s=Heal158())using(var control=Heal158()){
                foreach(var model in new[]{s,control}){Assert.IsTrue(model.TryConfigureTraversal(OutsideTraversalProfile.ValidationWater));var state=model.Core.CaptureState();state.Position=60000;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(model.Core,new object[]{state});}
                s.Player.SetHp(50);using(var a=s.AttachPresentation())using(var b=control.AttachPresentation())for(int i=0;i<100;i++){foreach(var model in new[]{s,control}){model.Core.TickFrame(0,i==0,i<8);model.AdvancePlayerFrame();}if(i==1){Assert.IsTrue(s.Core.IsInWater);Assert.IsFalse(s.Core.IsGrounded);Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out var error),error);}Assert.AreEqual(control.Core.Height,s.Core.Height);Assert.AreEqual(control.Core.VerticalVelocity,s.Core.VerticalVelocity);Assert.AreEqual(control.Core.IsInWater,s.Core.IsInWater);Assert.AreEqual(control.BreathFramesRemaining,s.BreathFramesRemaining);}Assert.AreEqual(70,s.Player.CurrentHp);
            }
        }

        [Test] public void 外界空中使用は明示した通常三種だけを許可し未知と上位を自動登録しない()
        {
            Assert.IsTrue(OutsideCardUseDefinition.AllowsAir(new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().First(c=>c.Id=="heal")));
            using(var s=Sword161(level:ExceedLevel.Lv1)){Assert.IsFalse(OutsideCardUseDefinition.AllowsAir(s.Cards.SelectedCard));using(var view=s.AttachPresentation()){s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();long revision=s.Cards.HandRevision,gauge=s.Core.CardGauge.CurrentUnits;Assert.IsFalse(s.TryUseSelectedCard(s.Cards.SelectedCard,revision,out _));Assert.AreEqual(revision,s.Cards.HandRevision);Assert.AreEqual(gauge,s.Core.CardGauge.CurrentUnits);}}
            Assert.IsFalse(OutsideCardUseDefinition.AllowsAir(new Card("unknown","fixture",1,CardRarity.Normal,CardCategory.Defense,ElementType.None,CardSeries.None,1,"fixture")));Assert.IsFalse(OutsideCardUseDefinition.AllowsAir(null));Assert.IsTrue(OutsideCardUseDefinition.AllowsAir(new Olca.Application.Battle.SimpleCardFactory().CreateById("sword")));Assert.AreEqual(3,OutsideCardUseDefinition.TrialDefinitions.Count(d=>d.CanUseInAir));
        }
        [TestCase(50,70)] [TestCase(100,100)] public void 空中ヒールは一度だけ消費し未使用と同じ軌道と着地になる(int hp,int expected)
        {
            using(var s=Heal158())using(var control=Heal158())using(var view=s.AttachPresentation())using(var other=control.AttachPresentation()){
                s.Player.SetHp(hp);for(int i=0;i<120;i++){bool down=i==0,held=i<8;s.Core.TickFrame(0,down,held);s.AdvancePlayerFrame();control.Core.TickFrame(0,down,held);control.AdvancePlayerFrame();if(i==1){long revision=s.Cards.HandRevision;var card=s.Cards.SelectedCard;long gauge=s.Core.CardGauge.CurrentUnits;Assert.IsFalse(s.Core.IsGrounded);Assert.IsTrue(s.TryUseSelectedHealingCard(card,revision,out var error),error);Assert.AreEqual(expected,s.Player.CurrentHp);Assert.AreEqual(revision+1,s.Cards.HandRevision);Assert.AreEqual(gauge-card.GetFinalCost()*60,s.Core.CardGauge.CurrentUnits);Assert.IsFalse(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));}Assert.AreEqual(control.Core.Height,s.Core.Height);Assert.AreEqual(control.Core.VerticalVelocity,s.Core.VerticalVelocity);Assert.AreEqual(control.Core.IsGrounded,s.Core.IsGrounded);}Assert.IsTrue(s.Core.IsGrounded);Assert.AreEqual(expected,s.Player.CurrentHp);
            }
        }
        [TestCase(0)] [TestCase(2)] [TestCase(3)] [TestCase(6)] public void 空中開始したヒールは保存失敗再試行とcold復元でも二重回復消費しない(int saveAt)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"air170-"+Guid.NewGuid().ToString("N"));try{using(var s=Heal158(3,4)){var view=s.AttachPresentation();s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();s.Player.SetHp(50);long revision=s.Cards.HandRevision;Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,revision,out _));for(int i=0;i<saveAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.IsFalse(s.Core.IsGrounded);view.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var plan=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsideRuntimeSave=runtime,OutsidePlayerSave=actor,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,plan,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(0,plan,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,plan,out error),error);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var saved=store.Read(out error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(saved.OutsidePlayerSave,saved.OutsideRuntimeSave,null,defs);Assert.AreEqual(revision+1,cold.Cards.HandRevision);using(var lease=cold.AttachPresentation())for(int i=0;i<10;i++){cold.Core.TickFrame(0);cold.AdvancePlayerFrame();}Assert.AreEqual(70,cold.Player.CurrentHp);Assert.AreEqual(revision+1,cold.Cards.HandRevision);Assert.IsNull(cold.AttackState);}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }

        [Test] public void 外界名称は論理百二十と六十で消えView再生成と同じ保存の再適用では戻らない()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new PlayerCharacter(100));Assert.AreEqual(0,s.MapNameOpacity);var view=s.AttachPresentation();Assert.AreEqual(1,view.MapNameOpacity);Assert.That(view.MapName,Does.Contain("カリ"));
                for(int i=0;i<119;i++)s.Core.TickFrame(0);Assert.AreEqual(1,view.MapNameOpacity);s.Core.TickFrame(0);Assert.AreEqual(1,view.MapNameOpacity);view.Dispose();using(var next=s.AttachPresentation()){Assert.AreEqual(1,next.MapNameOpacity);for(int i=0;i<59;i++)s.Core.TickFrame(0);Assert.AreEqual(1d/60,next.MapNameOpacity,.000001);s.Core.TickFrame(0);Assert.AreEqual(0,next.MapNameOpacity);}using(var next=s.AttachPresentation())Assert.AreEqual(0,next.MapNameOpacity);
                var runtime=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,runtime,null);using(var attached=cold.AttachPresentation()){Assert.AreEqual(1,attached.MapNameOpacity);for(int i=0;i<181;i++)cold.Core.TickFrame(0);}Assert.IsFalse(cold.RestoreSavedState(actor,runtime,null));using(var attached=cold.AttachPresentation())Assert.AreEqual(0,attached.MapNameOpacity);}
            }
        }

        [Test] public void 外界移動制限照会は付与解除予約と残一frameを副作用なく判定する()
        {
            using(var s=new OutsideExplorationSession()){
                s.TryInitializePlayer(()=>new PlayerCharacter(100));using(var view=s.AttachPresentation()){var p=s.Player;p.ApplyParalysis(1);
                Assert.IsTrue(s.IsMovementLockedForNextFrame);Assert.IsTrue(s.IsMovementLockedForNextFrame);Assert.AreEqual(0,p.ParalysisRemainingFrames,"照会だけで予約をcommitしない");
                s.Core.TickFrame(1,movementLocked:s.IsMovementLockedForNextFrame);s.AdvancePlayerFrame();Assert.AreEqual(s.Core.Definition.Start,s.Core.Position);Assert.AreEqual(0,p.ParalysisRemainingFrames);Assert.IsFalse(s.IsMovementLockedForNextFrame);
                p.ApplyParalysis(30);Assert.IsTrue(s.IsMovementLockedForNextFrame);p.ClearParalysis();Assert.IsFalse(s.IsMovementLockedForNextFrame,"次frameの解除予約を優先する");
                s.Core.TickFrame(1,movementLocked:s.IsMovementLockedForNextFrame);s.AdvancePlayerFrame();Assert.Greater(s.Core.Position,s.Core.Definition.Start);Assert.IsFalse(p.IsParalyzed);
            }}
        }

        [Test] public void 水中呼吸は六百論理frameだけで死亡し非表示と同frameは減らさない()
        {
            using(var s=Water167()){Assert.AreEqual(600,s.BreathFramesRemaining);using(var view=s.AttachPresentation()){for(int n=0;n<599;n++)WaterTick167(s);Assert.AreEqual(1,s.BreathFramesRemaining);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(1,s.BreathFramesRemaining);int deaths=0;s.Player.OnDeath+=()=>deaths++;WaterTick167(s);Assert.IsTrue(s.Player.IsDead);Assert.AreEqual(1,deaths);}Assert.IsFalse(s.AdvancePlayerFrame());Assert.Throws<ArgumentException>(()=>s.CaptureRuntime());s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.AreEqual(100,s.Player.CurrentHp);Assert.AreEqual(600,s.BreathFramesRemaining);Assert.IsFalse(s.ShouldShowBreath);}
        }
        [Test] public void 水中呼吸無制限は隠し解除の遷移時だけ満タンから再開する()
        {using(var s=Water167())using(var view=s.AttachPresentation()){Assert.IsTrue(s.SetWaterBreathingUnlimited(true));for(int n=0;n<700;n++)WaterTick167(s);Assert.IsFalse(s.ShouldShowBreath);Assert.AreEqual(600,s.BreathFramesRemaining);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsTrue(s.SetWaterBreathingUnlimited(false));Assert.AreEqual(600,s.BreathFramesRemaining);WaterTick167(s);Assert.AreEqual(599,s.BreathFramesRemaining);Assert.IsFalse(s.SetWaterBreathingUnlimited(false));Assert.AreEqual(599,s.BreathFramesRemaining);}}
        [Test] public void 水域から出た同frameに呼吸を全回復し表示を消す()
        {using(var s=Water167(149000))using(var view=s.AttachPresentation()){WaterTick167(s);Assert.AreEqual(599,s.BreathFramesRemaining);WaterTick167(s,1);Assert.IsFalse(s.Core.IsInWater);Assert.AreEqual(600,s.BreathFramesRemaining);Assert.IsFalse(s.ShouldShowBreath);}}
        [TestCase(false)] [TestCase(true)] public void 着水と水中着地は旧高所のダメージや追加硬直を発生させない(bool through)
        {var profile=through?OutsideTraversalProfile.ValidationDoubleJump.WithWater("thin-water",OutsideWaterProfile.Validation,new OutsideWaterArea("thin",30000,150000,60000,120000)):OutsideTraversalProfile.ValidationWater;using(var s=Water167(60000,240000,-240000,profile))using(var view=s.AttachPresentation()){WaterTick167(s);Assert.IsTrue(s.Core.IsGrounded);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsFalse(s.IsLandingLocked);Assert.IsTrue(s.Core.LastWaterContactHeight.HasValue);Assert.AreEqual(!through,s.Core.IsInWater);Assert.AreEqual(through?600:599,s.BreathFramesRemaining);}}
        [TestCase(false)] [TestCase(true)] public void 水中呼吸は保存復元やView再入だけで満タンへ戻らず余分に減らさない(bool unlimited)
        {using(var s=Water167()){s.SetWaterBreathingUnlimited(unlimited);using(var v=s.AttachPresentation())for(int n=0;n<50;n++)WaterTick167(s);var data=s.CaptureRuntime();data=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(data));var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,data,null,null,OutsideTraversalProfile.ValidationWater);Assert.AreEqual(unlimited?600:550,cold.BreathFramesRemaining);Assert.AreEqual(unlimited,cold.IsWaterBreathingUnlimited);using(var v=cold.AttachPresentation()){Assert.AreEqual(unlimited?600:550,cold.BreathFramesRemaining);WaterTick167(cold);Assert.AreEqual(unlimited?600:549,cold.BreathFramesRemaining);}}}}
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] public void 水域と時計に矛盾した呼吸保存はliveを部分置換せず拒否する(int mode)
        {using(var s=Water167()){var data=s.CaptureRuntime();if(mode==0)data.Water=null;else if(mode==1)data.Water.RemainingFrames=601;else if(mode==2)data.Water.Frame++;else if(mode==3)data.Water.Submerged=false;else data.Water.RemainingFrames=0;var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;var player=s.Player;Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,data,null,null,OutsideTraversalProfile.ValidationWater));Assert.AreSame(player,s.Player);Assert.AreEqual(600,s.BreathFramesRemaining);Assert.AreEqual(0,s.Core.Frame);}}
        [Test] public void 水中の地上ダッシュと二段ジャンプは既存入力で補正し別操作を追加しない()
        {using(var s=Water167())using(var view=s.AttachPresentation()){long x=s.Core.Position;WaterTick167(s,1,dash:true);Assert.IsTrue(s.Core.IsDashing);Assert.AreEqual(x+6000,s.Core.Position);WaterTick167(s,0,true,true);Assert.AreEqual(8800,s.Core.VerticalVelocity);WaterTick167(s);WaterTick167(s,0,true,true);Assert.IsTrue(s.Core.AirJumpUsed);Assert.AreEqual(8800,s.Core.VerticalVelocity);}}
        [TestCase(1)] [TestCase(7)] [TestCase(120)] public void 水中移動と呼吸は描画分割で変わらず六十Hzに従う(int parts)
        {using(var s=Water167())using(var t=Water167())using(var a=s.AttachPresentation())using(var b=t.AttachPresentation()){void Step(OutsideExplorationSession v,long f)=>WaterTick167(v,0,f==1||f==12,f<25);for(int f=1;f<=120;f++)Step(s,f);var clock=new LogicalFrameClock();long consumed=0;for(int n=1;n<=parts;n++){long target=2*LogicalFrameClock.WallTicksPerSecond*n/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((f,ms)=>Step(t,f));}Assert.AreEqual(s.Core.Height,t.Core.Height);Assert.AreEqual(s.Core.VerticalVelocity,t.Core.VerticalVelocity);Assert.AreEqual(s.Player.CurrentHp,t.Player.CurrentHp);Assert.AreEqual(s.BreathFramesRemaining,t.BreathFramesRemaining);}}
        private static OutsideExplorationSession Air166(){var s=new OutsideExplorationSession();s.TryConfigureTraversal(OutsideTraversalProfile.ValidationDoubleJump);s.TryInitializePlayer(()=>new PlayerCharacter(100));return s;}
        [TestCase(false)] [TestCase(true)] public void 二段ジャンプ使用済みは空中保存復元し再表示や冷起動で回数を戻さない(bool used)
        {
            using(var s=Air166()){
                using(var view=s.AttachPresentation()){s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();s.Core.TickFrame(0);s.AdvancePlayerFrame();if(used){s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();}}
                var data=s.CaptureRuntime();var bytes=PortableCheckpointJson.Serialize(data);data=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(bytes);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;
                using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,data,null,null,OutsideTraversalProfile.ValidationDoubleJump);Assert.AreEqual(used,cold.Core.AirJumpUsed);using(var view=cold.AttachPresentation()){cold.Core.TickFrame(0,true,true);cold.AdvancePlayerFrame();Assert.AreEqual(!used,cold.Core.DidAirJumpThisFrame);}}
            }
        }
        [TestCase(-1L)] [TestCase(999999L)] public void 二段ジャンプの不正保存はliveを部分変更しない(long when)
        {
            using(var s=Air166()){var data=s.CaptureRuntime();data.Core.AirJumpAt=when;var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;long frame=s.Core.Frame;Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,data,null,null,OutsideTraversalProfile.ValidationDoubleJump));Assert.AreEqual(frame,s.Core.Frame);Assert.IsFalse(s.Core.AirJumpUsed);}
        }
        [Test] public void 二段ジャンプは前の落下頂点を捨てPause非表示は時計を進めず着地硬直で再発動しない()
        {
            using(var s=Air166()){
                using(var view=s.AttachPresentation()){
                    s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();s.Core.TickFrame(0);s.AdvancePlayerFrame();typeof(OutsideExplorationSession).GetField("fallPeak",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(s,20L*60000);
                    s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();Assert.AreEqual(s.Core.Height,(long)typeof(OutsideExplorationSession).GetField("fallPeak",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s));
                    long frame=s.Core.Frame;Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(frame,s.Core.Frame);for(int n=0;n<200&&!s.Core.IsGrounded;n++){s.Core.TickFrame(0,jumpHeld:true);s.AdvancePlayerFrame();}Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsFalse(s.IsLandingLocked);
                }
                long stopped=s.Core.Frame;Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(stopped,s.Core.Frame);
            }
        }
        [Test] public void 穴のダッシュ残像の負高度も保存し旧地形では負残像を拒否する()
        {
            using(var s=Void165()){
                using(var v=s.AttachPresentation()){FallTick164(s,-1,false,true);for(int n=0;n<30&&!s.Core.Afterimages.Any(a=>a.Y<0);n++)FallTick164(s,-1);Assert.IsFalse(s.Player.IsDead);Assert.IsTrue(s.Core.Afterimages.Any(a=>a.Y<0));}
                var data=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,data,null,null,OutsideTraversalProfile.ValidationPit);Assert.AreEqual(s.Core.Height,cold.Core.Height);CollectionAssert.AreEqual(s.Core.Afterimages.Select(a=>a.Y),cold.Core.Afterimages.Select(a=>a.Y));using(var v=cold.AttachPresentation()){for(int n=0;n<100&&!cold.Player.IsDead;n++)FallTick164(cold);Assert.IsTrue(cold.Player.IsDead);}}
            }
            using(var old=new OutsideExplorationSession()){old.TryInitializePlayer(()=>new PlayerCharacter(100));var data=old.CaptureRuntime();data.Core.Afterimages=new[]{new OutsideAfterimageStateData{Frame=0,X=old.Core.Position,Y=-1,Facing=1,Pose=OutsideAfterimagePose.JumpFall}};var actor=OutsidePlayerSaveData.Capture(old.Player);actor.Id=data.Id;Assert.Throws<ArgumentException>(()=>old.RestoreSavedState(actor,data,null));}
        }
        [Test] public void 死亡済みの穴下端を生存状態として保存復元しない()
        {
            using(var s=Void165()){var data=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;data.Core.Height=-60000;data.Core.Velocity=0;data.Core.SupportId=null;data.Fall=new OutsideFallStateData{Peak=0,Grounded=false};Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,data,null,null,OutsideTraversalProfile.ValidationPit));using(var v=s.AttachPresentation())for(int i=0;i<100&&!s.Player.IsDead;i++)FallTick164(s,-1);Assert.Throws<ArgumentException>(()=>s.CaptureRuntime());}
        }

        [TestCase(-1,258000L)] [TestCase(1,222000L)] public void 床交差後の終点が穴内なら架空の床を付けず同frame保存をcold復元できる(int direction,long x)
        {
            using(var s=Void165(x,1000,-4000)){
                using(var view=s.AttachPresentation()){FallTick164(s,direction);Assert.IsFalse(s.Core.IsGrounded);Assert.AreEqual(-3000,s.Core.Height);Assert.AreEqual(x+direction*6000,s.Core.Position);}
                var runtime=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,null,OutsideTraversalProfile.ValidationPit));Assert.AreEqual(s.Core.Height,cold.Core.Height);Assert.IsFalse(cold.Core.IsGrounded);}
            }
        }
        [TestCase(false)] [TestCase(true)] public void 壁滑りの距離resetは下端死亡を防がず壁蹴りで下端到達前なら脱出できる(bool kick)
        {
            var profile=OutsideTraversalProfile.ValidationWall(true).WithFloorGaps("explicit-wall-void-fixture",60000,new OutsideFloorGap("wall-gap",23*60000,25*60000));
            using(var s=new OutsideExplorationSession()){s.TryConfigureTraversal(profile);var data=s.Core.CaptureState();data.Position=24*60000-31200;data.SupportId=null;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{data});s.TryInitializePlayer(()=>new PlayerCharacter(100));using(var v=s.AttachPresentation()){
                for(int i=0;i<10;i++)FallTick164(s,1);Assert.IsTrue(s.Core.IsWallSliding);Assert.IsFalse(s.Core.HasFallenOut);
                if(kick){FallTick164(s,1,true);Assert.IsTrue(s.Core.IsWallKicking);for(int i=0;i<70;i++){s.Core.TickFrame(-1,false,true);s.AdvancePlayerFrame();}Assert.IsFalse(s.Player.IsDead);Assert.IsTrue(s.Core.IsGrounded);Assert.AreEqual(100,s.Player.CurrentHp);}else{for(int i=0;i<100&&!s.Player.IsDead;i++)FallTick164(s,1);Assert.IsTrue(s.Core.HasFallenOut);Assert.IsTrue(s.Player.IsDead);}
            }}
        }
        private static OutsideExplorationSession Void165(long x=300000,long height=0,long velocity=0)
        {
            var s=new OutsideExplorationSession();s.TryConfigureTraversal(OutsideTraversalProfile.ValidationPit);var state=s.Core.CaptureState();state.Position=x;state.Height=height;state.Velocity=velocity;if(height!=0)state.SupportId=null;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{state});s.TryInitializePlayer(()=>new PlayerCharacter(100));return s;
        }
        [TestCase(false)] [TestCase(true)] public void 下の足場がない試用穴は防御無敵毒と無関係に死亡し通常復帰で下端を解除する(bool invincible)
        {
            using(var s=Void165())using(var view=s.AttachPresentation()){
                int deaths=0;s.Player.OnDeath+=()=>deaths++;if(invincible)s.Player.SetInvincible(10000);s.Player.SetElementResistances(new ElementResistances(100,100,100,100));
                for(int i=0;i<100&&!s.Player.IsDead;i++)FallTick164(s,-1);
                Assert.IsTrue(s.Core.HasFallenOut);Assert.IsTrue(s.Player.IsDead);Assert.AreEqual(1,deaths);Assert.IsFalse(s.CanPerformAttack);s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.IsFalse(s.Core.HasFallenOut);Assert.AreEqual(6*60000,s.Core.Position);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsTrue(s.Core.IsGrounded);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 穴の下降途中は表示終了と冷起動で保存復元しPause相当の無更新では進行しない(bool cold)
        {
            using(var s=Void165()){
                using(var v=s.AttachPresentation()){while(s.Core.Height>=0)FallTick164(s,-1);long frame=s.Core.Frame,y=s.Core.Height;Assert.IsFalse(s.Core.HasFallenOut);Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(y,s.Core.Height);Assert.AreEqual(frame,s.Core.Frame);}
                var runtime=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                if(cold){using(var next=new OutsideExplorationSession()){next.RestoreSavedState(actor,runtime,null,null,OutsideTraversalProfile.ValidationPit);using(var v=next.AttachPresentation()){for(int i=0;i<100&&!next.Player.IsDead;i++)FallTick164(next);Assert.IsTrue(next.Player.IsDead);}}}else using(var v=s.AttachPresentation()){for(int i=0;i<100&&!s.Player.IsDead;i++)FallTick164(s);Assert.IsTrue(s.Player.IsDead);}
            }
        }
        [Test] public void 穴の旧保存は旧床を維持し新規試走だけ穴を選ぶ()
        {
            using(var s=new OutsideExplorationSession()){s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));s.TryInitializePlayer(()=>new PlayerCharacter(100));var old=s.CaptureRuntime();Assert.AreEqual(0,OutsideTrialRoute.ResolveTraversal(old).FloorGaps.Count);Assert.AreEqual(1,OutsideTrialRoute.ResolveTraversal(null).FloorGaps.Count);Assert.AreEqual(0,s.Core.TraversalProfile.FloorGaps.Count);Assert.AreEqual("28459110E8810EF3EE319D2B63AD770D4E867109CDE631DB865656B0BB069DFB",s.Core.StateDefinitionKey());}
        }
        [Test] public void 試用穴の境界は床を保持しジャンプで越えれば死亡しない()
        {
            using(var s=Void165())using(var v=s.AttachPresentation()){
                for(int i=0;i<5;i++)FallTick164(s,-1);Assert.AreEqual(270000,s.Core.Position);Assert.IsTrue(s.Core.IsGrounded);FallTick164(s,-1,true);for(int i=0;i<80;i++)FallTick164(s,-1);Assert.IsTrue(s.Core.IsGrounded);Assert.IsFalse(s.Core.HasFallenOut);Assert.AreEqual(100,s.Player.CurrentHp);
            }
        }
        [Test] public void 穴を横切って床の下へ入っても上面へ吸着せず下端で停止する()
        {
            using(var s=Void165())using(var v=s.AttachPresentation()){for(int i=0;i<100&&!s.Player.IsDead;i++)FallTick164(s,-1,false,i==0);Assert.IsTrue(s.Core.HasFallenOut);long x=s.Core.Position;for(int i=0;i<10;i++)s.Core.TickFrame(1,true,true,true,true);Assert.AreEqual(x,s.Core.Position);Assert.IsFalse(s.Core.IsGrounded);Assert.AreEqual(-60000,s.Core.Height);}
        }


        [TestCase(false)] [TestCase(true)] public void 外界落下は通常無敵と防御を迂回し追加無敵を発生させない(bool invincible)
        {
            using(var s=Fall164())using(var view=s.AttachPresentation()){s.Player.SetElementResistances(new ElementResistances(100,100,100,100));if(invincible)s.Player.SetInvincible(10000);Land164(s);Assert.AreEqual(95,s.Player.CurrentHp);Assert.AreEqual(invincible,s.Player.IsInvincible);}
        }
        [Test] public void 外界の致死落下は同frame毒があってもHP一で生存させず死亡を一度だけ通知する()
        {
            using(var s=Fall164())using(var view=s.AttachPresentation()){s.Player.SetHp(5);int deaths=0;s.Player.OnDeath+=()=>deaths++;while(s.Core.Height>10000)FallTick164(s);s.Player.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,2000,2,poisonKind:"fixture",poisonIntervalFrames:1),1);Land164(s);Assert.IsTrue(s.Player.IsDead);Assert.AreEqual(0,s.Player.CurrentHp);Assert.AreEqual(1,deaths);}
        }
        [TestCase(false)] [TestCase(true)] public void 外界の壁滑りと壁蹴りはそれまでの落下距離をリセットする(bool kick)
        {
            using(var s=Fall164(420000,true))using(var view=s.AttachPresentation()){
                typeof(OutsideExplorationSession).GetField("fallPeak",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(s,20L*60000);
                if(kick){FallTick164(s,1,true);Assert.IsTrue(s.Core.IsWallKicking);}else{FallTick164(s,1);FallTick164(s,1);Assert.IsTrue(s.Core.IsWallSliding);for(int n=0;n<40;n++)FallTick164(s,1);}
                // 蹴り後は次frameにボタンを離す。新頂点から7.56worldの落下は10ダメージ。旧20world分は加算しない。
                Land164(s);Assert.AreEqual(kick?90:100,s.Player.CurrentHp);Assert.AreEqual(kick,s.IsLandingLocked);
            }
        }
        [Test] public void 外界硬直は表示閉鎖中に進まず回復地点でのみ初期化する()
        {
            using(var s=Fall164()){using(var view=s.AttachPresentation())Land164(s);long frame=s.Core.Frame;Assert.IsFalse(s.AdvancePlayerFrame());Assert.AreEqual(frame,s.Core.Frame);Assert.AreEqual(12,s.LandingFramesRemaining);using(var v=s.AttachPresentation()){FallTick164(s);Assert.AreEqual(11,s.LandingFramesRemaining);}s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.IsFalse(s.IsLandingLocked);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsTrue(s.Core.IsGrounded);}
        }

        [TestCase(false)] [TestCase(true)] public void 外界落下の保存失敗は現個体を変えず同じ計画の再試行と冷起動で二重ダメージを生まない(bool landed)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"fall164-"+Guid.NewGuid().ToString("N"));try{using(var s=Fall164()){
                using(var v=s.AttachPresentation()){if(landed)Land164(s);else for(int i=0;i<10;i++)FallTick164(s);}
                var runtime=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var plan=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=actor,OutsideRuntimeSave=runtime,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};int hp=s.Player.CurrentHp;long frame=s.Core.Frame;var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,plan,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));Assert.AreEqual(hp,s.Player.CurrentHp);Assert.AreEqual(frame,s.Core.Frame);io.Fail=false;Assert.IsTrue(store.TryWrite(0,plan,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,null,s.Core.TraversalProfile);using(var v=cold.AttachPresentation()){if(!landed)Land164(cold);else FallTick164(cold);Assert.AreEqual(95,cold.Player.CurrentHp);}}
            }}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase(0)] [TestCase(1)] public void 外界落下の頂点上限と硬直期限の不正保存を拒否する(int mode)
        {
            using(var s=Fall164()){var data=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;if(mode==0)data.Fall.Peak=long.MaxValue;else data.Fall.LockedUntil=100;var old=s.Player;Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,data,null,null,s.Core.TraversalProfile));Assert.AreSame(old,s.Player);}
        }
        [TestCase(390000L,100,0)] [TestCase(395999L,100,0)] [TestCase(396000L,100,1)]
        [TestCase(420000L,100,5)] [TestCase(450000L,101,10)] [TestCase(long.MaxValue,int.MaxValue,int.MaxValue)]
        public void 外界落下は安全高度超過分を最大HP比で切り捨て最低一補正しない(long distance,int hp,int expected)
        {Assert.AreEqual(expected,OutsideFallRules.Damage(distance,hp));}
        [TestCase(1)] [TestCase(15)] [TestCase(29)] [TestCase(40)]
        public void 平地の通常二段ジャンプは押し直す時点によらず落下ダメージと硬直を発生させない(int secondAt)
        {
            using(var s=new OutsideExplorationSession()){
                Assert.IsTrue(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationDoubleJump));s.TryInitializePlayer(()=>new PlayerCharacter(100));
                using(var lease=s.AttachPresentation()){
                    s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();
                    for(int n=0;n<secondAt;n++){s.Core.TickFrame(0,jumpHeld:true);s.AdvancePlayerFrame();}
                    s.Core.TickFrame(0);s.AdvancePlayerFrame();s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();Assert.IsTrue(s.Core.AirJumpUsed);
                    long peak=s.Core.Height;for(int n=0;n<200&&!s.Core.IsGrounded;n++){s.Core.TickFrame(0,jumpHeld:true);s.AdvancePlayerFrame();peak=Math.Max(peak,s.Core.Height);}
                    Assert.IsTrue(s.Core.IsGrounded);Assert.AreEqual(100,s.Player.CurrentHp);Assert.IsFalse(s.IsLandingLocked);Assert.LessOrEqual(peak,372000L);
                }
            }
        }
        private static OutsideExplorationSession Fall164(long height=420000,bool wall=false)
        {
            var s=new OutsideExplorationSession();s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(wall));var state=s.Core.CaptureState();if(wall)state.Position=24*60000-31200;state.Height=height;state.SupportId=null;state.Velocity=0;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{state});s.TryInitializePlayer(()=>new PlayerCharacter(100));return s;
        }
        private static void FallTick164(OutsideExplorationSession s,float horizontal=0,bool jump=false,bool dash=false)
        {s.Core.TickFrame(horizontal,jump,jump,dash,dash,s.IsLandingLockedForNextFrame);s.AdvancePlayerFrame();}
        private static void Land164(OutsideExplorationSession s)
        {for(int i=0;i<200&&!s.Core.IsGrounded;i++)FallTick164(s);Assert.IsTrue(s.Core.IsGrounded);}
        [TestCase(390000L,100,0)] [TestCase(395999L,100,0)] [TestCase(420000L,95,12)]
        public void 外界落下は接地で一度だけ集計し正ダメージだけ十二フレーム拘束する(long height,int hp,int locked)
        {
            using(var s=Fall164(height))using(var view=s.AttachPresentation()){
                Land164(s);Assert.AreEqual(hp,s.Player.CurrentHp);Assert.AreEqual(locked,s.LandingFramesRemaining);
                long x=s.Core.Position;if(locked==0)return;for(int i=1;i<12;i++){FallTick164(s,1,true,true);Assert.AreEqual(x,s.Core.Position);Assert.IsTrue(s.Core.IsGrounded);Assert.IsFalse(s.Core.IsDashing);Assert.AreEqual(hp,s.Player.CurrentHp);if(locked>0)Assert.IsFalse(s.CanPerformAttack);else break;}
                if(locked>0){FallTick164(s,1,true,true);Assert.Greater(s.Core.Position,x);Assert.IsTrue(s.Core.IsGrounded);Assert.IsFalse(s.Core.IsDashing);Assert.IsTrue(s.CanPerformAttack);}
            }
        }
        [TestCase(false)] [TestCase(true)] public void 外界落下の空中履歴と着地硬直は保存復元し同じダメージを再適用しない(bool landed)
        {
            using(var s=Fall164()){
                using(var view=s.AttachPresentation()){if(landed)Land164(s);else for(int i=0;i<10;i++)FallTick164(s);}
                var runtime=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                var json=PortableCheckpointJson.Serialize(runtime);var copy=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(json);Assert.AreEqual(runtime.Key(),copy.Key());
                using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedState(actor,copy,null,null,s.Core.TraversalProfile));using(var view=cold.AttachPresentation()){
                    Assert.AreEqual(s.LandingFramesRemaining,cold.LandingFramesRemaining);if(!landed)Land164(cold);else FallTick164(cold);Assert.AreEqual(95,cold.Player.CurrentHp);
                }}
            }
        }
        [Test] public void 外界の旧空中保存は未知の頂点を創作せず復元高度から追跡し地上旧hashを変えない()
        {
            using(var s=Fall164()){
                using(var v=s.AttachPresentation())for(int i=0;i<25;i++)FallTick164(s);
                var data=s.CaptureRuntime();data.Fall=null;var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;
                using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,data,null,null,s.Core.TraversalProfile);using(var v=cold.AttachPresentation()){Land164(cold);Assert.AreEqual(100,cold.Player.CurrentHp);}}
            }
            using(var s=new OutsideExplorationSession()){s.TryInitializePlayer(()=>new PlayerCharacter(100));var data=s.CaptureRuntime();Assert.IsNull(data.Fall);Assert.IsFalse(System.Text.Encoding.UTF8.GetString(PortableCheckpointJson.Serialize(data)).Contains("Fall"));Assert.AreEqual(s.Core.StateDefinitionKey(),data.Core.DefinitionKey);}
        }
        [Test] public void 外界の不正落下保存は旧個体と時計を部分置換しない()
        {
            using(var s=Fall164()){var data=s.CaptureRuntime();var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=data.Id;data.Fall.Peak=-1;var old=s.Player;Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,data,null,null,s.Core.TraversalProfile));Assert.AreSame(old,s.Player);Assert.AreEqual(0,s.Core.Frame);}
        }


        [TestCase(ExceedLevel.Lv1,170,1,45)] [TestCase(ExceedLevel.Lv1,190,-1,45)] [TestCase(ExceedLevel.Lv2,170,1,60)] [TestCase(ExceedLevel.Lv2,190,-1,60)] public void 登録済み上位ソードは元定義の威力と個体を保って一度だけ命中する(ExceedLevel level,int x,int facing,int power){using(var s=Sword161(x,facing,level:level))using(var lease=s.AttachPresentation()){var c=s.Cards.SelectedCard;string hash=CollectionDisplay.DefinitionHash(c);long rev=s.Cards.HandRevision;Assert.AreEqual(power,c.GetFinalPower());Assert.IsTrue(s.TryUseSelectedCard(c,rev,out _));Assert.AreEqual(120-power,s.TrialTarget.CurrentHp);Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(c));Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.IsFalse(s.TryUseSelectedCard(c,rev,out _));for(int n=0;n<20;n++)Tick160(s,false);Assert.AreEqual(120-power,s.TrialTarget.CurrentHp);}}
        [TestCase(ExceedLevel.Lv1)] [TestCase(ExceedLevel.Lv2)] public void 上位ソードの空中と不足コストと背面は既存の試用契約を維持する(ExceedLevel level){using(var s=Sword161(190,1,level:level))using(var lease=s.AttachPresentation()){var c=s.Cards.SelectedCard;Assert.IsTrue(s.TryUseSelectedCard(c,s.Cards.HandRevision,out _));Assert.AreEqual(120,s.TrialTarget.CurrentHp);}using(var s=Sword161(level:level))using(var lease=s.AttachPresentation()){s.Core.TickFrame(0,true,true);long rev=s.Cards.HandRevision;Assert.IsFalse(s.TryUseSelectedCard(s.Cards.SelectedCard,rev,out _));Assert.AreEqual(rev,s.Cards.HandRevision);}using(var s=Sword161(level:level))using(var lease=s.AttachPresentation()){Assert.IsTrue(s.Core.CardGauge.TrySpend(100));long rev=s.Cards.HandRevision;Assert.IsFalse(s.TryUseSelectedCard(s.Cards.SelectedCard,rev,out _));Assert.AreEqual(rev,s.Cards.HandRevision);}}
        [TestCase(ExceedLevel.Lv1,0)] [TestCase(ExceedLevel.Lv1,3)] [TestCase(ExceedLevel.Lv2,0)] [TestCase(ExceedLevel.Lv2,3)] public void 上位ソードの途中保存と失敗再試行と復元は再消費再命中しない(ExceedLevel level,int at){string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sword163-"+Guid.NewGuid().ToString("N"));try{using(var s=Sword161(startup:3,recovery:8,level:level)){var c=s.Cards.SelectedCard;int power=c.GetFinalPower();long rev=s.Cards.HandRevision;using(var lease=s.AttachPresentation()){Assert.IsTrue(s.TryUseSelectedCard(c,rev,out _));for(int n=0;n<at;n++)Tick160(s,false);}var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var p=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=actor,OutsideRuntimeSave=runtime,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,p,out _));io.Fail=false;Assert.IsTrue(store.TryWrite(0,p,out _));var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,p,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var read=store.Read(out _);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(read.OutsidePlayerSave,read.OutsideRuntimeSave,null,defs);using(var lease=cold.AttachPresentation())for(int n=at;n<20;n++)Tick160(cold,false);Assert.AreEqual(120-power,cold.TrialTarget.CurrentHp);Assert.AreEqual(rev+1,cold.Cards.HandRevision);Assert.AreEqual(1,cold.CaptureRuntime(defs).TrialTarget.Hits);}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}}
        [TestCase(ExceedLevel.Lv1)] [TestCase(ExceedLevel.Lv2)] public void 上位ソードも共通六frame予約で通常ソードへ一度だけ繋がる(ExceedLevel level){using(var s=Sword161(startup:3,recovery:8,level:level))using(var lease=s.AttachPresentation()){int power=s.Cards.SelectedCard.GetFinalPower();Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"&&c.ExceedLevel==ExceedLevel.Normal));for(int n=1;n<=20;n++)Tick160(s,n==6);Assert.AreEqual(120-power-30,s.TrialTarget.CurrentHp);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}}
        [Test] public void 試用ソードはコスト不足を拒否して標的も手札も変更しない()
        {
            using(var s=Sword161())using(var view=s.AttachPresentation()){var card=s.Cards.SelectedCard;long rev=s.Cards.HandRevision;Assert.IsTrue(s.Core.CardGauge.TrySpend(100));long units=s.Core.CardGauge.CurrentUnits;int remaining=s.Cards.RemainingCount;Assert.IsFalse(s.TryUseSelectedCard(card,rev,out _));Assert.AreEqual(120,s.TrialTarget.CurrentHp);Assert.AreEqual(rev,s.Cards.HandRevision);Assert.AreEqual(remaining,s.Cards.RemainingCount);Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);Assert.IsNull(s.TrialStrike);}
        }
        [Test] public void 試用ソードは未対応の命中無敵時間を無消費で拒否する()
        {
            using(var s=Sword161())using(var lease=s.AttachPresentation()){var prior=s.Cards.SelectedCard;var card=new Card("sword","ソード",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,30,"明示検証定義",hitInvincibilityMs:100);var deck=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().Select(c=>c.Id=="sword"?card.Clone():c).ToArray();var blocked=new OutsideExplorationSession();try{blocked.TryInitializePlayer(()=>new PlayerCharacter(100));Assert.IsTrue(blocked.TryInitializeCards(deck,Owned(deck),new SeededBattleRandom(161),out _));for(int n=0;n<100&&!blocked.Cards.Hand.Any(c=>c.Id=="sword");n++)blocked.Cards.Reload();blocked.Cards.TrySelect(blocked.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));using(var view=blocked.AttachPresentation()){long revision=blocked.Cards.HandRevision;long gauge=blocked.Core.CardGauge.CurrentUnits;Assert.IsFalse(blocked.TryUseSelectedCard(blocked.Cards.SelectedCard,revision,out _));Assert.AreEqual(revision,blocked.Cards.HandRevision);Assert.AreEqual(gauge,blocked.Core.CardGauge.CurrentUnits);Assert.AreEqual(120,blocked.TrialTarget.CurrentHp);Assert.IsFalse(blocked.TrialTarget.IsInvincible);}}finally{blocked.Dispose();}Assert.AreEqual(0,prior.HitInvincibilityMs);}
        }
        private static OutsideExplorationSession Fire180(int tenths=170,int facing=1)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100));var f=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"fire_sword",new PlayerAttackTiming(3,8)}});var cards=f.CreateStarterDeck().GetAllCards().ToArray();cards[Array.FindIndex(cards,c=>c.Id=="sword")]=f.CreateById("fire_sword");Assert.IsTrue(s.TryInitializeCards(cards,Owned(cards),new SeededBattleRandom(180),out _));for(int n=0;n<1000&&!s.Cards.Hand.Any(c=>c.Id=="fire_sword");n++)s.Cards.Reload();s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="fire_sword"));long x=tenths*6000L+(facing<0?6000:0);while(s.Core.Position<x)s.Core.TickFrame(1);if(facing<0)s.Core.TickFrame(-1);return s;
        }
        [TestCase(164,1,false)] [TestCase(165,1,true)] [TestCase(170,1,true)] [TestCase(190,-1,true)] [TestCase(190,1,false)]
        public void ファイアソードは実定義と暫定範囲と前隙を保ち一度だけ命中する(int x,int facing,bool hit)
        {using(var s=Fire180(x,facing))using(var lease=s.AttachPresentation()){var c=s.Cards.SelectedCard;string hash=CollectionDisplay.DefinitionHash(c);long rev=s.Cards.HandRevision,gauge=s.Core.CardGauge.CurrentUnits;int remain=s.Cards.RemainingCount;ElementType? observed=null;s.TrialTarget.OnDamageObserved+=notice=>observed=notice.Element;Assert.AreEqual(ElementType.Fire,c.GetFinalElement());Assert.AreEqual(60,c.GetFinalPower());Assert.AreEqual(2,c.GetFinalCost());Assert.IsTrue(s.TryUseSelectedCard(c,rev,out _));Assert.AreEqual(gauge-120,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.AreEqual(remain-1,s.Cards.RemainingCount);for(int paused=0;paused<20;paused++)s.AdvancePlayerFrame();Assert.AreEqual(120,s.TrialTarget.CurrentHp);for(int i=0;i<2;i++)Tick160(s,false);Assert.AreEqual(120,s.TrialTarget.CurrentHp);Tick160(s,false);Assert.AreEqual(hit?60:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hit?(ElementType?)ElementType.Fire:null,observed);for(int i=0;i<20;i++)Tick160(s,false);Assert.AreEqual(hit?60:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hash,CollectionDisplay.DefinitionHash(c));}}
        [TestCase(false)] [TestCase(true)] public void ファイアソードの空中とコスト不足は無消費で拒否する(bool air)
        {using(var s=Fire180())using(var lease=s.AttachPresentation()){if(air)s.Core.TickFrame(0,true,true);else s.Core.CardGauge.TrySpend(100);long rev=s.Cards.HandRevision,gauge=s.Core.CardGauge.CurrentUnits;Assert.IsFalse(s.TryUseSelectedCard(s.Cards.SelectedCard,rev,out _));Assert.AreEqual(rev,s.Cards.HandRevision);Assert.AreEqual(gauge,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(120,s.TrialTarget.CurrentHp);}}
        [TestCase(0)] [TestCase(3)] public void ファイアソードの前隙と命中済みを復元して二重消費と再命中を防ぐ(int at)
        {using(var s=Fire180()){using(var lease=s.AttachPresentation()){Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));for(int i=0;i<at;i++)Tick160(s,false);}var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,runtime,null,defs);using(var lease=cold.AttachPresentation()){long rev=cold.Cards.HandRevision;for(int i=at;i<20;i++)Tick160(cold,false);Assert.AreEqual(rev,cold.Cards.HandRevision);Assert.AreEqual(60,cold.TrialTarget.CurrentHp);lease.Dispose();Assert.AreEqual(1,cold.CaptureRuntime(defs).TrialTarget.Hits);}}}}

        [TestCase(170,1,1,true)] [TestCase(190,-1,1,true)] [TestCase(170,1,90000,true)] [TestCase(190,-1,90000,true)] [TestCase(170,1,90001,false)] [TestCase(190,-1,90001,false)]
        public void 空中通常ソードは試用縦境界と左右を守り一度だけ命中する(int x,int facing,long height,bool hit)
        {using(var s=Sword161(x,facing))using(var view=s.AttachPresentation()){var state=s.Core.CaptureState();state.Height=height;state.SupportId=null;state.Velocity=0;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{state});long rev=s.Cards.HandRevision;Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,rev,out _));Assert.AreEqual(hit?90:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hit,s.TrialStrike.Hit);Assert.AreEqual(height,s.TrialStrike.Y);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.IsFalse(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));Assert.AreEqual(hit?90:120,s.TrialTarget.CurrentHp);}}
        [Test] public void 空中通常ソードは落下を停止せず未使用と同じ軌道で着地する()
        {using(var s=Sword161())using(var control=Sword161())using(var a=s.AttachPresentation())using(var b=control.AttachPresentation()){for(int i=0;i<120;i++){foreach(var model in new[]{s,control}){model.Core.TickFrame(0,i==0,i<8);model.AdvancePlayerFrame();}if(i==1){Assert.IsFalse(s.Core.IsGrounded);Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));}Assert.AreEqual(control.Core.Height,s.Core.Height);Assert.AreEqual(control.Core.VerticalVelocity,s.Core.VerticalVelocity);}Assert.IsTrue(s.Core.IsGrounded);Assert.AreEqual(90,s.TrialTarget.CurrentHp);}}
        [TestCase(1,0)] [TestCase(-1,0)] [TestCase(1,3)] [TestCase(-1,3)]
        public void 壁ずりからの通常ソードは壁と反対を向いて開始する(int side,int startup)
        {
            using(var s=new OutsideExplorationSession()){
                Assert.IsTrue(s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true)));
                s.TryInitializePlayer(()=>new PlayerCharacter(100));
                var f=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"sword",new PlayerAttackTiming(startup,8)}});
                var cards=f.CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(cards,Owned(cards),new SeededBattleRandom(191),out _));
                while(!s.Cards.Hand.Any(c=>c.Id=="sword"))s.Cards.Reload();s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));
                var state=s.Core.CaptureState();state.Position=24*60000-side*31200;state.Height=60000;state.Velocity=-1800;state.SupportId=null;state.Frame=100;state.Facing=side;typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(s.Core,new object[]{state});
                using(var lease=s.AttachPresentation()){
                    s.Core.TickFrame(side);s.AdvancePlayerFrame();Assert.IsTrue(s.Core.IsWallSliding);Assert.AreEqual(side,s.Core.WallContactDirection);
                    long x=s.Core.Position,revision=s.Cards.HandRevision;
                    Assert.IsFalse(s.TryUseSelectedCard(null,revision,out _));Assert.AreEqual(side,s.Core.Facing);Assert.AreEqual(revision,s.Cards.HandRevision);
                    s.Core.TickFrame(side);s.AdvancePlayerFrame();Assert.IsTrue(s.Core.CardGauge.TrySpend(100));
                    Assert.IsFalse(s.TryUseSelectedCard(s.Cards.SelectedCard,revision,out _));Assert.AreEqual(side,s.Core.Facing);Assert.AreEqual(revision,s.Cards.HandRevision);
                    s.Core.CardGauge.RestoreFull();s.Core.TickFrame(side);s.AdvancePlayerFrame();
                    Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,revision,out var error),error);
                    Assert.AreEqual(-side,s.Core.Facing);Assert.AreEqual(x,s.Core.Position);Assert.AreEqual(revision+1,s.Cards.HandRevision);
                    if(startup>0){
                        var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);
                        var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;
                        lease.Dispose();var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                        var bytes=PortableCheckpointJson.Serialize(runtime);var restored=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(bytes);Assert.AreEqual(runtime.Key(),restored.Key());
                        using(var cold=new OutsideExplorationSession()){
                            Assert.IsTrue(cold.RestoreSavedState(actor,restored,null,defs,OutsideTraversalProfile.ValidationWall(true)));
                            using(var coldLease=cold.AttachPresentation()){
                                Assert.AreEqual(-side,cold.Core.Facing);Assert.AreEqual(revision+1,cold.Cards.HandRevision);
                                for(int n=0;n<startup;n++){cold.Core.TickFrame(0);cold.AdvancePlayerFrame();}
                                Assert.AreEqual(-side,cold.TrialStrike.Facing);Assert.AreEqual(revision+1,cold.Cards.HandRevision);
                            }
                        }
                    }
                    var resumed=startup>0?s.AttachPresentation():null;
                    try{
                        for(int n=0;n<startup+8;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}
                        Assert.AreEqual(-side,s.TrialStrike.Facing);Assert.IsFalse(s.IsAttacking);
                        s.Core.TickFrame(side);Assert.AreEqual(side,s.Core.Facing);
                    }finally{resumed?.Dispose();}
                }
            }
        }

        private static OutsideExplorationSession Sword161(int tenths=170,int facing=1,int startup=0,int recovery=0,ExceedLevel level=ExceedLevel.Normal,int stagger=0)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100,staggerDurationFrames:stagger));var factory=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"sword",new PlayerAttackTiming(startup,recovery)}});var source=factory.CreateStarterDeck().GetAllCards().ToArray();if(level!=ExceedLevel.Normal){int index=Array.FindIndex(source,c=>c.Id=="sword");source[index]=factory.CreateExceeded(source[index],level);}Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(161),out _));for(int n=0;n<1000&&(s.Cards.Hand.Count(c=>c.Id=="sword")<2||!s.Cards.Hand.Any(c=>c.Id=="sword"&&c.ExceedLevel==level));n++)s.Cards.Reload();Assert.GreaterOrEqual(s.Cards.Hand.Count(c=>c.Id=="sword"),2);s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"&&c.ExceedLevel==level));long x=tenths*6000L+(facing<0?6000:0);while(s.Core.Position<x)s.Core.TickFrame(1);if(facing<0)s.Core.TickFrame(-1);Assert.AreEqual(tenths*6000L,s.Core.Position);return s;
        }
        [TestCase(164,1,false)] [TestCase(165,1,true)] [TestCase(170,1,true)] [TestCase(180,1,true)] [TestCase(185,1,false)] [TestCase(190,-1,true)] [TestCase(196,-1,false)] public void 試用ソードは向きと暫定一点範囲を判定し実カード威力を一度だけ与える(int x,int facing,bool hit)
        {
            using(var s=Sword161(x,facing))using(var view=s.AttachPresentation()){var card=s.Cards.SelectedCard;long rev=s.Cards.HandRevision;int count=0;s.TrialTarget.OnCombatFeedback+=e=>{if(e.Kind==CombatFeedbackKind.Damage)count++;};Assert.IsTrue(s.TryUseSelectedCard(card,rev,out _));Assert.AreEqual(hit?90:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hit,s.TrialStrike.Hit);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.IsFalse(s.TryUseSelectedCard(card,rev,out _));for(int n=0;n<20;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.AreEqual(hit?90:120,s.TrialTarget.CurrentHp);Assert.AreEqual(hit?1:0,count);Assert.AreEqual(100,s.Player.CurrentHp);}
        }
        [TestCase(0,false)] [TestCase(3,false)] [TestCase(6,false)] [TestCase(0,true)] [TestCase(3,true)] [TestCase(6,true)] public void 試用ソードの前隙と命中済みは保存失敗再試行と冷起動で再命中しない(int at,bool air)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"sword161-"+Guid.NewGuid().ToString("N"));try{using(var s=Sword161(startup:3,recovery:8)){var view=s.AttachPresentation();if(air){s.Core.TickFrame(0,true,true);s.AdvancePlayerFrame();}Assert.IsTrue(s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));for(int n=0;n<at;n++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}view.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;Assert.AreEqual(at<3?null:(int?)90,runtime.TrialTarget?.Hp);var plan=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsidePlayerSave=actor,OutsideRuntimeSave=runtime,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,plan,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(0,plan,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var lease=cold.AttachPresentation()){for(int n=at;n<20;n++){cold.Core.TickFrame(0);cold.AdvancePlayerFrame();cold.ProcessCardUseInput(false,false,false,0,0,out _);}Assert.AreEqual(90,cold.TrialTarget.CurrentHp);}Assert.AreEqual(1,cold.CaptureRuntime(defs).TrialTarget.Hits);}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [Test] public void 試用ソードは既存六frame予約で二枚だけ発動し撃破後は報酬や自動保存を作らない()
        {
            using(var s=Sword161(startup:3,recovery:8))using(var lease=s.AttachPresentation()){s.TryUseSelectedCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _);s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="sword"));long rev=s.Cards.HandRevision;for(int n=1;n<=20;n++)Tick160(s,n==6);Assert.AreEqual(60,s.TrialTarget.CurrentHp);Assert.AreEqual(rev+1,s.Cards.HandRevision);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}
        }
        [Test] public void 試用標的は撃破を保持し回復地点で再生するが新規保存へ強制追加しない()
        {
            using(var s=Sword161()){var view=s.AttachPresentation();var before=s.TrialTarget;Assert.AreEqual(120,before.CurrentHp);before.TakeDamage(120,ElementType.None);view.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);Assert.AreEqual(0,runtime.TrialTarget.Hp);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(actor,runtime,null,defs);Assert.IsTrue(cold.TrialTarget.IsDead);}var recovery=OutsideTrialRoute.RecoveryPointId; s.RecoverOutside(recovery);Assert.AreEqual(120,s.TrialTarget.CurrentHp);Assert.AreNotSame(before,s.TrialTarget);Assert.IsNull(s.CaptureRuntime(defs).TrialTarget);}
        }
        [TestCase(-1)] [TestCase(121)] public void 不正な試用標的保存は旧個体とHPを変更せず拒否する(int hp)
        {
            using(var s=Sword161()){var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);runtime.TrialTarget=new OutsideTrialTargetData{Hp=hp};var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var before=s.TrialTarget;Assert.Throws<ArgumentException>(()=>s.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(before,s.TrialTarget);Assert.AreEqual(120,before.CurrentHp);}
        }
        [TestCase(2)] [TestCase(6)] public void 探索の予約入力は保存せず失敗再試行と冷起動で二枚目を重複消費しない(int at)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"buffer160-"+Guid.NewGuid().ToString("N"));try{using(var s=Buffer160()){var lease=s.AttachPresentation();Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=at;n++)Tick160(s,n==at);Assert.AreEqual(AttackReservationKind.Card,s.PendingAttack);lease.Dispose();Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var plan=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsideRuntimeSave=runtime,OutsidePlayerSave=actor,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,plan,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));io.Fail=false;Assert.IsTrue(store.TryWrite(0,plan,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,plan,out _));CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var view=cold.AttachPresentation()){Assert.AreEqual(AttackReservationKind.None,cold.PendingAttack);for(int n=at;n<20;n++)Tick160(cold);Assert.AreEqual(30,cold.Player.CurrentHp);Assert.AreEqual(rev,cold.Cards.HandRevision);}}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索の後隙キャンセルで次カード消費を拒否しても旧後隙を消さない(bool pending)
        {
            using(var s=Buffer160(2))using(var view=s.AttachPresentation()){Start160(s);typeof(OutsideExplorationCards).GetProperty("HandRevision").SetValue(s.Cards,long.MaxValue);for(int n=1;n<=5;n++)Tick160(s,pending?n==1:n==5);Assert.IsTrue(s.IsAttacking);Assert.AreEqual(0,s.AttackState.StartedAt);Assert.AreEqual(8,s.AttackState.RecoveryFrames);Assert.AreEqual(long.MaxValue,s.Cards.HandRevision);for(int n=6;n<11;n++){Tick160(s);Assert.IsTrue(s.IsAttacking);}Tick160(s);Assert.IsFalse(s.IsAttacking);Assert.AreEqual(30,s.Player.CurrentHp);}
        }
        private static OutsideExplorationSession Buffer160(int? cancel=null)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100,staggerDurationFrames:2));var source=new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"heal",new PlayerAttackTiming(3,8,cancel)}}).CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(160),out _));for(int n=0;n<1000&&s.Cards.Hand.Count(c=>c.Id=="heal")<2;n++)s.Cards.Reload();Assert.GreaterOrEqual(s.Cards.Hand.Count(c=>c.Id=="heal"),2);s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));s.Player.SetHp(10);return s;
        }
        private static void Start160(OutsideExplorationSession s){Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));}
        private static void Tick160(OutsideExplorationSession s,bool press=false,bool selection=false,bool basic=false,long cardAt=0,long basicAt=0){s.Core.TickFrame(0);s.AdvancePlayerFrame();s.ProcessCardUseInput(press,selection,basic,cardAt,basicAt,out _);}
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] public void 探索カード予約は六フレーム目の開始で失効し予約時に消費しない(int at)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=15;n++){Tick160(s,n==at);if(n==at)Assert.AreEqual(rev,s.Cards.HandRevision);}Assert.AreEqual(at>=6?50:30,s.Player.CurrentHp);Assert.AreEqual(rev+(at>=6?1:0),s.Cards.HandRevision);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索カード予約の押し直しだけが期限を更新し同frameの多重呼出は延長しない(bool refresh)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=15;n++){Tick160(s,n==5||refresh&&n==8);if(n==5)for(int i=0;i<10;i++)s.ProcessCardUseInput(true,false,false,0,0,out _);}Assert.AreEqual(refresh?50:30,s.Player.CurrentHp);Assert.AreEqual(rev+(refresh?1:0),s.Cards.HandRevision);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索カード予約は入力時と発動時の不足コストを拒否して後で自動復活しない(bool atExecution)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=20;n++){if(n==(atExecution?11:6))Assert.IsTrue(s.Core.CardGauge.TrySpend((int)(s.Core.CardGauge.CurrentUnits/60)));Tick160(s,n==6);}Assert.AreEqual(30,s.Player.CurrentHp);Assert.AreEqual(rev,s.Cards.HandRevision);Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] public void 探索カード予約は選択と場面身体状態の変化で取り消す(int mode)
        {
            using(var s=Buffer160()){var view=s.AttachPresentation();try{Start160(s);long rev=s.Cards.HandRevision;for(int n=1;n<=6;n++)Tick160(s,n==6);Assert.AreEqual(AttackReservationKind.Card,s.PendingAttack);
                if(mode==0){s.Core.TickFrame(0);s.AdvancePlayerFrame();s.ProcessCardUseInput(false,true,false,0,0,out _);}if(mode==1){int facing=s.Core.Facing;bool grounded=s.Core.IsGrounded;s.Core.TickFrame(-1);s.ObserveAttackBoundary(facing,grounded);}if(mode==2){int facing=s.Core.Facing;bool grounded=s.Core.IsGrounded;s.Core.TickFrame(0,true,true);s.ObserveAttackBoundary(facing,grounded);}if(mode==3){s.Player.TakeDamage(0,forceStagger:true);Tick160(s);}if(mode==4){s.Player.ApplyParalysis(2);Tick160(s);}if(mode==5){view.Dispose();view=s.AttachPresentation();}if(mode==6)s.CancelAttackReservation();Assert.AreEqual(AttackReservationKind.None,s.PendingAttack);for(int n=0;n<20;n++)Tick160(s);Assert.AreEqual(30,s.Player.CurrentHp);Assert.AreEqual(rev,s.Cards.HandRevision);
            }finally{view.Dispose();}}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] public void 探索の同frame優先は選択が最優先で前後不明はカードを優先し未実装キックを実行しない(int mode)
        {
            using(var s=Buffer160())using(var view=s.AttachPresentation()){Start160(s);for(int n=1;n<6;n++)Tick160(s);Tick160(s,true,mode==3,true,mode==0?10:mode==1?20:0,mode==0?20:mode==1?10:0);Assert.AreEqual(mode==0||mode==3?AttackReservationKind.None:AttackReservationKind.Card,s.PendingAttack);for(int n=7;n<=15;n++)Tick160(s);Assert.AreEqual(mode==0||mode==3?30:50,s.Player.CurrentHp);}
        }
        [TestCase(0)] [TestCase(2)] [TestCase(8)] public void 探索カード予約は既存の後隙キャンセル開始か終了の最速で発動する(int cancel)
        {
            using(var s=Buffer160(cancel))using(var view=s.AttachPresentation()){Start160(s);long rev=s.Cards.HandRevision;Tick160(s,true);long expected=3+cancel;for(int n=2;n<=Math.Max(11,expected);n++){Tick160(s);if(n<expected)Assert.AreEqual(rev,s.Cards.HandRevision);}Assert.AreEqual(rev+(expected<7?1:0),s.Cards.HandRevision);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] public void 探索の開始済ヒールは前隙と発動済を保存し復元後一度だけ適用する(int saveAt)
        {
            using(var s=Heal158(3,4)){var lease=s.AttachPresentation();s.Player.SetHp(50);var card=s.Cards.SelectedCard;Assert.IsTrue(s.TryUseSelectedHealingCard(card,s.Cards.HandRevision,out _));Assert.AreEqual(50,s.Player.CurrentHp);Assert.IsFalse(s.CanPerformAttack);
                for(int i=0;i<saveAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}lease.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var player=OutsidePlayerSaveData.Capture(s.Player);player.Id=runtime.Id;
                var bytes=PortableCheckpointJson.Serialize(runtime);var loaded=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(bytes);using(var restored=new OutsideExplorationSession()){Assert.IsTrue(restored.RestoreSavedState(player,loaded,null,defs));using(var view=restored.AttachPresentation()){Assert.IsFalse(restored.AdvancePlayerFrame());Assert.AreEqual(saveAt<3?50:70,restored.Player.CurrentHp);for(int n=saveAt;n<7;n++){restored.Core.TickFrame(0);restored.AdvancePlayerFrame();}Assert.AreEqual(70,restored.Player.CurrentHp);Assert.IsNull(restored.AttackState);for(int n=0;n<8;n++){restored.Core.TickFrame(0);restored.AdvancePlayerFrame();}Assert.AreEqual(70,restored.Player.CurrentHp);}}}
        }
        [TestCase(0)] [TestCase(2)] [TestCase(3)] [TestCase(4)] public void 探索ヒールは開始後の中断で消費を戻さず発動後の効果を消さない(int interruptAt)
        {
            using(var s=Heal158(3,4))using(var view=s.AttachPresentation()){s.Player.SetHp(50);long rev=s.Cards.HandRevision;Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,rev,out _));for(int i=0;i<interruptAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}s.Player.TakeDamage(0,forceStagger:true);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsNull(s.AttackState);Assert.AreEqual(interruptAt<3?50:70,s.Player.CurrentHp);Assert.AreEqual(rev+1,s.Cards.HandRevision);for(int i=0;i<10;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}Assert.AreEqual(interruptAt<3?50:70,s.Player.CurrentHp);}
        }
        private sealed class Action159Io:Olca.Unity.CheckpointFileIo{public bool Fail=true;public override void WriteAndFlush(string path,byte[] bytes){if(Fail)throw new System.IO.IOException("action159 explicit fault");base.WriteAndFlush(path,bytes);}}
        [TestCase(2)] [TestCase(4)] public void 探索行動の保存失敗再試行は前隙と発動済を同じ計画で復元し二重適用しない(int saveAt)
        {
            string dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"action159-"+Guid.NewGuid().ToString("N"));try{using(var s=Heal158(3,4)){var lease=s.AttachPresentation();s.Player.SetHp(50);Assert.IsTrue(s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _));for(int i=0;i<saveAt;i++){s.Core.TickFrame(0);s.AdvancePlayerFrame();}lease.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;var p=new ClearReturnProfile{Revision=1,CommitId=Guid.NewGuid().ToString("N"),OutsideRuntimeSave=runtime,OutsidePlayerSave=actor,OutsideItemState=new OutsideItemStateData{ActorKey=actor.Key(),RuntimeKey=runtime.Key()}};var io=new Action159Io();var store=new Olca.Unity.ClearReturnFileStore(dir,io);Assert.IsFalse(store.TryWrite(0,p,out _));Assert.IsFalse(System.IO.File.Exists(store.ActivePath));Assert.AreEqual(saveAt<3?50:70,s.Player.CurrentHp);io.Fail=false;Assert.IsTrue(store.TryWrite(0,p,out var error),error);var bytes=System.IO.File.ReadAllBytes(store.ActivePath);Assert.IsTrue(store.TryWrite(0,p,out error),error);CollectionAssert.AreEqual(bytes,System.IO.File.ReadAllBytes(store.ActivePath));var loaded=store.Read(out error);Assert.IsNotNull(loaded,error);using(var cold=new OutsideExplorationSession()){cold.RestoreSavedState(loaded.OutsidePlayerSave,loaded.OutsideRuntimeSave,null,defs);using(var view=cold.AttachPresentation()){for(int i=saveAt;i<10;i++){cold.Core.TickFrame(0);cold.AdvancePlayerFrame();}Assert.AreEqual(70,cold.Player.CurrentHp);Assert.IsNull(cold.AttackState);}}}}finally{if(System.IO.Directory.Exists(dir))System.IO.Directory.Delete(dir,true);}
        }
        [TestCase(false)] [TestCase(true)] public void 探索行動の不正な時間と発動済改変は旧Sessionを変更せず拒否する(bool applied)
        {
            using(var s=Heal158(3,4)){var lease=s.AttachPresentation();s.Player.SetHp(50);s.TryUseSelectedHealingCard(s.Cards.SelectedCard,s.Cards.HandRevision,out _);lease.Dispose();var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;if(applied)runtime.Attack.EffectApplied=true;else runtime.Attack.StartupFrames++;using(var target=new OutsideExplorationSession()){target.TryInitializePlayer(()=>new PlayerCharacter(123));var before=target.Player;Assert.Throws<ArgumentException>(()=>target.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(before,target.Player);Assert.AreEqual(123,target.Player.CurrentHp);Assert.AreEqual(0,target.Core.Frame);Assert.IsNull(target.Cards);}}
        }
        [TestCase(false)] [TestCase(true)]
        public void 行動不能の最終フレームは移動と同様カードも禁止し次フレームから受け付ける(bool stagger)
        {
            using(var s=stagger?Heal158():Sword161())using(var lease=s.AttachPresentation()){
                var card=s.Cards.SelectedCard;long revision=s.Cards.HandRevision,gauge=s.Core.CardGauge.CurrentUnits;
                if(stagger)s.Player.TakeDamage(0,forceStagger:true);else s.Player.ApplyParalysis(1);
                int frames=stagger?2:1;
                for(int n=0;n<frames;n++){
                    s.Core.TickFrame(0);s.AdvancePlayerFrame(out var allowed);Assert.IsFalse(allowed);
                    Assert.IsFalse(s.CanPerformAttack,"同じフレームの入力段階でも禁止を保持する");
                    Assert.IsFalse(s.ProcessCardUseInput(true,false,false,0,0,out _));Assert.IsNull(s.AttackState);
                    Assert.AreEqual(revision,s.Cards.HandRevision);Assert.AreEqual(gauge,s.Core.CardGauge.CurrentUnits);
                }
                Assert.IsFalse(s.AdvancePlayerFrame());Assert.IsFalse(s.CanPerformAttack);
                Assert.IsFalse(s.TryUseSelectedCard(card,revision,out _));Assert.AreEqual(revision,s.Cards.HandRevision);
                lease.Dispose();
                var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(s.Cards);
                var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;
                var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;
                using(var cold=new OutsideExplorationSession()){
                    Assert.IsTrue(cold.RestoreSavedState(actor,PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime)),null,defs));
                    using(var coldLease=cold.AttachPresentation()){
                        cold.Core.TickFrame(0);cold.AdvancePlayerFrame(out var available);Assert.IsTrue(available);
                        Assert.IsTrue(cold.ProcessCardUseInput(true,false,false,0,0,out _));Assert.AreEqual(revision+1,cold.Cards.HandRevision);
                    }
                }
                using(var replacement=s.AttachPresentation()){
                    Assert.IsFalse(s.CanPerformAttack,"再表示は当該フレームの禁止を解除しない");
                    s.Core.TickFrame(0);s.AdvancePlayerFrame(out var resumed);Assert.IsTrue(resumed);Assert.IsTrue(s.CanPerformAttack);
                    Assert.IsTrue(s.ProcessCardUseInput(true,false,false,0,0,out var error),error);Assert.AreEqual(revision+1,s.Cards.HandRevision);
                }
            }
        }

        private static OutsideExplorationSession Heal158(int timing=0,int recovery=0)
        {
            var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100,staggerDurationFrames:2));var f=timing==0&&recovery==0?new Olca.Application.Battle.SimpleCardFactory():new Olca.Application.Battle.SimpleCardFactory(cardTimings:new System.Collections.Generic.Dictionary<string,PlayerAttackTiming>{{"heal",new PlayerAttackTiming(timing,recovery)}});var source=f.CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(158),out _));for(int n=0;n<100&&!s.Cards.Hand.Any(c=>c.Id=="heal");n++)s.Cards.Reload();Assert.IsTrue(s.Cards.Hand.Any(c=>c.Id=="heal"));s.Cards.TrySelect(s.Cards.Hand.ToList().FindIndex(c=>c.Id=="heal"));return s;
        }
        [TestCase(50,70)] [TestCase(100,100)] public void 外界ヒールは既存二十回復と同じコストを開始時に一度だけ消費する(int hp,int expected)
        {
            using(var s=Heal158())using(var lease=s.AttachPresentation()){s.Player.SetHp(hp);var c=s.Cards;var selected=c.SelectedCard;var old=c.Hand.ToArray();long rev=c.HandRevision;Assert.IsTrue(s.TryUseSelectedHealingCard(selected,rev,out var error),error);Assert.AreEqual(expected,s.Player.CurrentHp);Assert.AreEqual(6000-selected.GetFinalCost()*60,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(rev+1,c.HandRevision);Assert.IsFalse(c.Hand.Contains(selected));CollectionAssert.AreEqual(old.Where(x=>!ReferenceEquals(x,selected)).ToArray(),c.Hand.Take(2));Assert.IsFalse(s.TryUseSelectedHealingCard(c.SelectedCard,c.HandRevision,out _));Assert.AreEqual(rev+1,c.HandRevision);Assert.AreEqual(0,s.Core.Frame);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(8)] [TestCase(9)]
        public void 外界ヒールの不許可はHP手札ゲージと乱数を変更せず予約を作らない(int mode)
        {
            using(var s=Heal158(mode==7?1:0)){var lease=s.AttachPresentation();try{s.Player.SetHp(50);var c=s.Cards;var expected=mode==0?c.SelectedCard.Clone():c.SelectedCard;long rev=c.HandRevision+(mode==1?1:0);if(mode==2)s.Core.CardGauge.TrySpend(100);if(mode==3){s.Player.ApplyParalysis(2);s.Core.TickFrame(0);s.AdvancePlayerFrame();Assert.IsTrue(s.Player.IsParalyzed);}if(mode==4)s.Player.TakeDamage(0,forceStagger:true);if(mode==5){int i=c.Hand.ToList().FindIndex(x=>x.Id!="heal");Assert.GreaterOrEqual(i,0);c.TrySelect(i);expected=c.SelectedCard;}if(mode==6)s.Core.TickFrame(0,true,true);if(mode==8)lease.Dispose();if(mode==9){var data=Deck155();Assert.IsTrue(s.QueueSelectedDeck("edit158",data,Owned(data),new SeededBattleRandom(158),out _));}
                var hand=c.Hand;long units=s.Core.CardGauge.CurrentUnits;var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(c);var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),deck).Definitions;var random=PortableCheckpointJson.Serialize(c.CaptureState(defs));Assert.IsFalse(s.TryUseSelectedHealingCard(expected,rev,out _));Assert.AreSame(hand,c.Hand);Assert.AreEqual(50,s.Player.CurrentHp);Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);CollectionAssert.AreEqual(random,PortableCheckpointJson.Serialize(c.CaptureState(defs)));Assert.AreEqual(rev-(mode==1?1:0),c.HandRevision);
            }finally{lease.Dispose();}}
        }

        [TestCase(false)] [TestCase(true)] public void 枯渇した空の使用枠からは前後の実在手札へ巡回する(bool previous)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("cycle157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;c.TrySelect(2);for(int i=0;i<28;i++)Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,s.Core.CardGauge,out _));Assert.AreEqual(2,c.Hand.Count);Assert.AreEqual(2,c.SelectedIndex);Assert.IsNull(c.SelectedCard);var repeat=new OutsideSelectionRepeat();repeat.Queue(0,false,false,false,false,false,false,0,0);repeat.Queue(1,previous,!previous,previous,!previous,false,true,0,0);repeat.AdvanceFrame(c);Assert.AreEqual(previous?1:0,c.SelectedIndex);}
        }
        [Test] public void 未知乱数の消費は未変更で拒否して保存可能な専用乱数の境界を維持する()
        {
            var data=CardFixture();Assert.IsTrue(OutsideExplorationCards.TryCreate(data,Owned(data),new Olca.Tests.Mocks.MockRandomProvider(),out var c,out _));var hand=c.Hand;var gauge=OutsideCardGauge.ValidationDefault;Assert.IsFalse(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,gauge,out _));Assert.AreSame(hand,c.Hand);Assert.AreEqual(0,c.HandRevision);Assert.AreEqual(100*60,gauge.CurrentUnits);
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 探索一枚使用は選択個体だけを左詰め末尾補充し使用枠と定義を保つ(int slot)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("use157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var cards=s.Cards;cards.TrySelect(slot);var hand=cards.Hand.ToArray();long revision=cards.HandRevision,units=s.Core.CardGauge.CurrentUnits;var expected=cards.SelectedCard;Assert.IsTrue(cards.TryConsumeSelected(expected,revision,s.Core.CardGauge,out var used));Assert.AreSame(expected,used);Assert.AreEqual(slot,cards.SelectedIndex);Assert.AreEqual(revision+1,cards.HandRevision);Assert.AreEqual(3,cards.Hand.Count);Assert.AreEqual(26,cards.RemainingCount);CollectionAssert.AreEqual(hand.Where(c=>!ReferenceEquals(c,expected)).ToArray(),cards.Hand.Take(2));Assert.IsFalse(cards.Hand.Contains(expected));Assert.AreEqual(units-expected.GetFinalCost()*60,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(30,cards.TotalCount);}
        }
        [Test] public void 探索一枚使用は三十回で枯渇し自動循環せず空の使用枠と再装填を保つ()
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("empty157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;var used=new System.Collections.Generic.List<Card>();c.TrySelect(2);for(int i=0;i<30;i++){if(c.SelectedCard==null)c.TrySelect(c.Hand.Count-1);var expected=c.SelectedCard;int slot=c.SelectedIndex;Assert.IsTrue(c.TryConsumeSelected(expected,c.HandRevision,s.Core.CardGauge,out var taken));used.Add(taken);Assert.AreEqual(slot,c.SelectedIndex);Assert.AreEqual(Math.Min(3,29-i),c.Hand.Count);Assert.AreEqual(Math.Max(0,26-i),c.RemainingCount);}Assert.AreEqual(30,used.Distinct().Count());Assert.IsNull(c.SelectedCard);long units=s.Core.CardGauge.CurrentUnits,rev=c.HandRevision;Assert.IsFalse(c.TryConsumeSelected(null,rev,s.Core.CardGauge,out _));var repeat=new OutsideSelectionRepeat();repeat.Queue(0,false,false,false,false,false,false,0,0);repeat.Queue(1,false,true,false,true,false,true,0,0);repeat.AdvanceFrame(c);Assert.AreEqual(0,c.SelectedIndex);Assert.AreEqual(units,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(rev,c.HandRevision);c.Reload();Assert.AreEqual(3,c.Hand.Count);Assert.AreEqual(27,c.RemainingCount);Assert.AreEqual(0,c.SelectedIndex);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void 探索一枚使用の拒否は乱数履歴ゲージ手札世代を変えない(int mode)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("reject157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;var definitions=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;var expected=mode==0?c.SelectedCard.Clone():c.SelectedCard;long revision=c.HandRevision-(mode==1?1:0);if(mode==2)Assert.IsTrue(s.Core.CardGauge.TrySpend(100));var before=PortableCheckpointJson.Serialize(c.CaptureState(definitions));long gauge=s.Core.CardGauge.CurrentUnits;Assert.IsFalse(c.TryConsumeSelected(expected,revision,s.Core.CardGauge,out _));CollectionAssert.AreEqual(before,PortableCheckpointJson.Serialize(c.CaptureState(definitions)));Assert.AreEqual(gauge,s.Core.CardGauge.CurrentUnits);}
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(10)]
        public void 探索使用後の手札は枯渇までcold復元し次抽選と使用済みを維持する(int count)
        {
            var source=Deck155();using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("cold157",source,Owned(source),new SeededBattleRandom(157),out _));Walk155(s,10*60000);var c=s.Cards;c.TrySelect(2);for(int i=0;i<30-count;i++){if(c.SelectedCard==null)c.TrySelect(c.Hand.Count-1);Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,s.Core.CardGauge,out _));}var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;var state=c.CaptureState(defs);var restored=OutsideExplorationCards.RestoreState(state,defs);Assert.AreEqual(state.Key(),restored.CaptureState(defs).Key());Assert.AreEqual(Math.Min(3,count),restored.Hand.Count);if(count>0){c.TrySelect(0);restored.TrySelect(0);Assert.IsTrue(c.TryConsumeSelected(c.SelectedCard,c.HandRevision,s.Core.CardGauge,out _));var gauge=OutsideCardGauge.ValidationDefault;Assert.IsTrue(restored.TryConsumeSelected(restored.SelectedCard,restored.HandRevision,gauge,out _));Assert.AreEqual(c.CaptureState(defs).Key(),restored.CaptureState(defs).Key());}else Assert.IsNull(restored.SelectedCard);}
        }

        [TestCase(9,false)] [TestCase(9.5,true)] [TestCase(10,true)] [TestCase(10.5,true)] [TestCase(11,false)] [TestCase(12,false)] [TestCase(6,false)]
        public void 試用カード箱は半径と接地を守り既存操作地点と競合しない(double x,bool expected)
        {
            using(var s=new OutsideExplorationSession()){Walk155(s,(long)(x*60000));Assert.AreEqual(expected,OutsideTrialRoute.CanClaimCardSupply(s.Core));if(expected){Assert.IsFalse(OutsideTrialRoute.CanRecover(s.Core));Assert.IsFalse(OutsideTrialRoute.CanClaimSupply(s.Core));Assert.IsFalse(OutsideTrialRoute.CanTryPoison(s.Core));s.Core.TickFrame(0,true,true);Assert.IsFalse(OutsideTrialRoute.CanClaimCardSupply(s.Core));}}
        }

        [TestCase(214,false)] [TestCase(215,true)] [TestCase(220,true)] [TestCase(225,true)] [TestCase(226,false)] public void 試用リロード専用地点は独立した閉範囲と地上編成を使う(int x,bool inside)
        {using(var model=new OutsideExplorationSession()){Walk155(model,x*6000L);Assert.AreEqual(inside,OutsideTrialRoute.IsReloadZone(model.Core));Assert.AreEqual(inside,OutsideTrialRoute.CanEditDeck(model.Core));if(inside){Assert.IsFalse(OutsideTrialRoute.CanRecover(model.Core));Assert.IsFalse(OutsideTrialRoute.CanInspect(model.Core));Assert.IsFalse(OutsideTrialRoute.CanClaimSupply(model.Core));model.Core.TickFrame(0,true,true);Assert.IsFalse(OutsideTrialRoute.CanEditDeck(model.Core));Assert.IsTrue(OutsideTrialRoute.IsReloadZone(model.Core));}}}
        private static OutsideExplorationSession Reload182()
        {var s=new OutsideExplorationSession();s.TryInitializePlayer(()=>new PlayerCharacter(100));var data=Deck155();Assert.IsTrue(s.QueueSelectedDeck("reload182",data,Owned(data),new SeededBattleRandom(182),out _));Walk155(s,10*60000);s.Player.SetHp(37);s.Player.ApplyPoison("reload182",new StatusEffect(StatusEffectType.Poison,60000,1),600);s.TrialTarget.TakeDamage(30,ElementType.None);s.Core.CardGauge.Configure(100,0);for(int i=0;i<4;i++)Assert.IsTrue(s.Cards.TryConsumeSelected(s.Cards.SelectedCard,s.Cards.HandRevision,s.Core.CardGauge,out _));s.Core.CardGauge.TrySpend(60);return s;}
        [Test] public void 専用地点退出は三十枚とゲージだけを一度再装填しHP状態標的を維持する()
        {using(var s=Reload182()){var target=s.TrialTarget;var cards=s.Cards;var rng=(SeededBattleRandom)typeof(OutsideExplorationCards).GetField("random",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(cards);long gauge=s.Core.CardGauge.CurrentUnits;Assert.Less(gauge,6000);Walk155(s,22*60000);long draws=rng.Draws;Assert.IsTrue(s.IsDeckEditingLocation);for(int i=0;i<65;i++){s.Core.TickFrame(0,i==0,true);Assert.IsFalse(s.AdvanceDeckLocation());}using(var lease=s.AttachPresentation())Assert.IsFalse(s.AdvanceDeckLocation());Assert.AreEqual(draws,rng.Draws);Assert.AreEqual(gauge,s.Core.CardGauge.CurrentUnits);Walk155(s,23*60000);Assert.AreEqual(27,s.Cards.RemainingCount);Assert.AreEqual(3,s.Cards.Hand.Count);Assert.AreEqual(0,s.Cards.SelectedIndex);Assert.AreEqual(6000,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(37,s.Player.CurrentHp);Assert.Greater(s.Player.CapturePoisons().Length,0);Assert.AreSame(target,s.TrialTarget);Assert.AreEqual(90,target.CurrentHp);long after=rng.Draws;s.Core.CardGauge.TrySpend(1);using(var lease=s.AttachPresentation())Assert.IsFalse(s.AdvanceDeckLocation());Assert.AreEqual(after,rng.Draws);Assert.AreEqual(5940,s.Core.CardGauge.CurrentUnits);}}
        [Test] public void 専用地点で保存済み別デッキを準備して退出時だけ交換する()
        {using(var s=Reload182()){var old=s.Cards;var data=Deck155().Reverse().ToArray();Walk155(s,22*60000);Assert.IsTrue(s.QueueSelectedDeck("replacement182",data,Owned(data),new SeededBattleRandom(999),out _));Assert.AreSame(old,s.Cards);Assert.Less(s.Core.CardGauge.CurrentUnits,6000);Walk155(s,23*60000);Assert.AreNotSame(old,s.Cards);Assert.AreEqual(30,s.Cards.TotalCount);Assert.AreEqual(27,s.Cards.RemainingCount);Assert.AreEqual(6000,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(37,s.Player.CurrentHp);Assert.Greater(s.Player.CapturePoisons().Length,0);Assert.AreEqual(90,s.TrialTarget.CurrentHp);}}
        [Test] public void 専用地点内coldとView再生成は再抽選せず退出で一度だけ全回復する()
        {using(var s=Reload182())using(var cold=new OutsideExplorationSession()){Walk155(s,22*60000);var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;cold.QueueSelectedDeck("reload182",data,Owned(data),new SeededBattleRandom(999),out _);cold.RestoreSavedState(actor,runtime,null,defs);Assert.IsTrue(cold.QueueSelectedDeck("reload182",data,Owned(data),new SeededBattleRandom(999),out _));Assert.AreEqual(runtime.Cards.Key(),cold.Cards.CaptureState(defs).Key());using(var a=cold.AttachPresentation())Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(runtime.Cards.Key(),cold.Cards.CaptureState(defs).Key());Assert.AreEqual(runtime.Core.GaugeUnits,cold.Core.CardGauge.CurrentUnits);Walk155(s,23*60000);Walk155(cold,23*60000);Assert.AreEqual(s.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());Assert.AreEqual(6000,cold.Core.CardGauge.CurrentUnits);Assert.AreEqual(37,cold.Player.CurrentHp);Assert.Greater(cold.Player.CapturePoisons().Length,0);Assert.AreEqual(90,cold.TrialTarget.CurrentHp);}}
        [TestCase(6)] [TestCase(8)] public void 既存編成地点の退出ゲージ規則は変更しない(int x)
        {using(var s=Reload182()){long before=s.Core.CardGauge.CurrentUnits;Walk155(s,x*60000);Walk155(s,10*60000);Assert.AreEqual(before,s.Core.CardGauge.CurrentUnits);Assert.AreEqual(27,s.Cards.RemainingCount);}}
        [TestCase(false)] [TestCase(true)] public void 専用地点は未登録と旧二十枚を三十枚へ自動変換しない(bool legacy)
        {using(var s=new OutsideExplorationSession()){s.TryInitializePlayer(()=>new PlayerCharacter(100));if(legacy){var data=new Olca.Application.Battle.SimpleCardFactory().CreateStarterDeck().GetAllCards().ToArray();Assert.IsTrue(s.TryInitializeCards(data,Owned(data),new SeededBattleRandom(182),out _));}s.QueueSelectedDeck(null,null,null,null,out _);s.Core.CardGauge.Configure(100,0);s.Core.CardGauge.TrySpend(60);Walk155(s,22*60000);Walk155(s,23*60000);Assert.AreEqual(6000,s.Core.CardGauge.CurrentUnits);if(legacy)Assert.AreEqual(20,s.Cards.TotalCount);else Assert.IsNull(s.Cards);}}
        private static Card[] Deck155()=>Enumerable.Range(0,30).Select(i=>new Card("selected155-"+i,"fixture",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.None,0,"")).ToArray();
        private static void Walk155(OutsideExplorationSession s,long target){for(int i=0;i<400&&s.Core.Position!=target;i++){s.Core.TickFrame(Math.Sign(target-s.Core.Position));s.AdvanceDeckLocation();}Assert.AreEqual(target,s.Core.Position);}
        [Test] public void 選択30枚は保存準備だけで抽選せず地点退出で一度だけ適用する()
        {
            var data=Deck155();var rng=new SeededBattleRandom(155);using(var s=new OutsideExplorationSession()){Assert.IsTrue(s.QueueSelectedDeck("one",data,Owned(data),rng,out _));Assert.IsTrue(s.IsDeckEditingLocation);Assert.IsNull(s.Cards);Assert.AreEqual(0,rng.Draws);using(var lease=s.AttachPresentation()){for(int i=0;i<4;i++)Assert.IsFalse(s.AdvanceDeckLocation());}Assert.IsTrue(s.QueueSelectedDeck("one",data,Owned(data),rng,out _));Assert.AreEqual(0,rng.Draws);Walk155(s,10*60000);var cards=s.Cards;Assert.AreEqual(30,cards.TotalCount);Assert.AreEqual(27,cards.RemainingCount);Assert.AreEqual(0,cards.SelectedIndex);long draws=rng.Draws;Assert.Greater(draws,0);Assert.IsFalse(s.AdvanceDeckLocation());using(var lease=s.AttachPresentation())Assert.IsFalse(s.AdvanceDeckLocation());Assert.AreSame(cards,s.Cards);Assert.AreEqual(draws,rng.Draws);
                Walk155(s,8*60000);Assert.IsTrue(s.IsDeckEditingLocation);Assert.IsTrue(s.QueueSelectedDeck("one",data.Reverse().ToArray(),Owned(data),rng,out _));Assert.AreSame(cards,s.Cards);Assert.AreEqual(draws,rng.Draws);Walk155(s,10*60000);Assert.AreNotSame(cards,s.Cards);Assert.AreEqual(30,s.Cards.TotalCount);Assert.Greater(rng.Draws,draws);}
        }
        [Test] public void 地点内のジャンプや無効選択では再装填も乱数進行もない()
        {
            var data=Deck155();var rng=new SeededBattleRandom(156);using(var s=new OutsideExplorationSession()){Assert.IsFalse(s.QueueSelectedDeck("bad",data.Take(20).ToArray(),Owned(data),rng,out _));Assert.IsFalse(s.IsDeckEditingLocation);Assert.IsTrue(s.QueueSelectedDeck("one",data,Owned(data),rng,out _));for(int i=0;i<65;i++){s.Core.TickFrame(0,i==0,true);Assert.IsFalse(s.AdvanceDeckLocation());}Assert.IsTrue(s.Core.IsGrounded);Assert.IsNull(s.Cards);Assert.AreEqual(0,rng.Draws);Assert.IsTrue(OutsideTrialRoute.CanEditDeck(s.Core));Walk155(s,10*60000);Assert.IsFalse(OutsideTrialRoute.CanEditDeck(s.Core));}
        }
        [Test] public void 三十枚Runtimeはcold復元と通常再入で個体順序と乱数を保持する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var s=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){s.TryInitializePlayer(()=>new PlayerCharacter(100));s.QueueSelectedDeck("one",data,Owned(data),new SeededBattleRandom(157),out _);Walk155(s,10*60000);s.Cards.TrySelect(2);var runtime=s.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(s.Player);actor.Id=runtime.Id;Assert.AreEqual(30,runtime.Cards.Cards.Length);Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,defs));var before=cold.Cards.CaptureState(defs).Key();Assert.IsTrue(cold.QueueSelectedDeck("one",data,Owned(data),new SeededBattleRandom(999),out _));Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(before,cold.Cards.CaptureState(defs).Key());using(var lease=cold.AttachPresentation())Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(before,cold.Cards.CaptureState(defs).Key());}
        }
        [Test] public void 回復操作と地点退出は別々に一回ずつ再装填し再表示では増えない()
        {
            var data=Deck155();var rng=new SeededBattleRandom(158);using(var s=new OutsideExplorationSession()){s.TryInitializePlayer(()=>new PlayerCharacter(100));s.QueueSelectedDeck("one",data,Owned(data),rng,out _);Walk155(s,10*60000);Walk155(s,6*60000);long before=rng.Draws;s.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.Greater(rng.Draws,before);long recovered=rng.Draws;using(var lease=s.AttachPresentation())s.AdvanceDeckLocation();Assert.AreEqual(recovered,rng.Draws);Walk155(s,10*60000);Assert.Greater(rng.Draws,recovered);long exited=rng.Draws;Assert.IsFalse(s.AdvanceDeckLocation());Assert.AreEqual(exited,rng.Draws);}
        }
        [Test] public void pending選択の前にRuntimeが復元されても最新乱数を使いwarmとcoldで一致する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var warm=new OutsideExplorationSession())using(var recovered=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                warm.TryInitializePlayer(()=>new PlayerCharacter(100));warm.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(159),out _);Walk155(warm,10*60000);var original=warm.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(warm.Player);actor.Id=original.Id;recovered.RestoreSavedState(actor,original,null,defs);warm.QueueSelectedDeck("b",data.Reverse().ToArray(),Owned(data),new SeededBattleRandom(160),out _);recovered.RecoverOutside(OutsideTrialRoute.RecoveryPointId);var restored=recovered.CaptureRuntime(defs);var next=OutsidePlayerSaveData.Capture(recovered.Player);next.Id=restored.Id;warm.RestoreSavedState(next,restored,OutsideTrialRoute.RecoveryPointId,defs);cold.RestoreSavedState(next,restored,OutsideTrialRoute.RecoveryPointId,defs);cold.QueueSelectedDeck("b",data.Reverse().ToArray(),Owned(data),new SeededBattleRandom(999),out _);Walk155(warm,10*60000);Walk155(cold,10*60000);Assert.AreEqual(warm.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());
            }
        }
        [Test] public void 同じ選択の回復保存から再開してもwarmとcoldの退出抽選が一致する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var warm=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                warm.TryInitializePlayer(()=>new PlayerCharacter(100));var rng=new SeededBattleRandom(160);warm.QueueSelectedDeck("same",data,Owned(data),rng,out _);Walk155(warm,10*60000);Assert.AreEqual(32,rng.Draws,"新planも全30枚Shuffle29回とDraw3回");Walk155(warm,6*60000);warm.RecoverOutside(OutsideTrialRoute.RecoveryPointId);var runtime=warm.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(warm.Player);actor.Id=runtime.Id;var bytes=PortableCheckpointJson.Serialize(runtime);var decoded=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(bytes);Assert.AreEqual(runtime.Key(),decoded.Key());cold.RestoreSavedState(actor,decoded,OutsideTrialRoute.RecoveryPointId,defs);warm.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(888),out _);cold.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(999),out _);Walk155(warm,10*60000);Walk155(cold,10*60000);Assert.AreEqual(warm.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());
            }
        }
        [TestCase("bad")] [TestCase("ZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZZ")]
        public void 三十枚の選択識別が不正なら復元せず既存状態を保持する(string key)
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var s=new OutsideExplorationSession()){s.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(161),out _);Walk155(s,10*60000);var value=s.Cards.CaptureState(defs);var before=value.Key();value.SourceKey=key;Assert.Throws<ArgumentException>(()=>OutsideExplorationCards.RestoreState(value,defs));Assert.AreEqual(before,s.Cards.CaptureState(defs).Key());}
        }
        [Test] public void A適用後にBを復元してもA再選択のpendingを作りwarmとcoldの退出で一致する()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var warm=new OutsideExplorationSession())using(var replacement=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                warm.TryInitializePlayer(()=>new PlayerCharacter(100));warm.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(162),out _);Walk155(warm,10*60000);replacement.TryInitializePlayer(()=>new PlayerCharacter(100));replacement.QueueSelectedDeck("b",data.Reverse().ToArray(),Owned(data),new SeededBattleRandom(163),out _);Walk155(replacement,10*60000);var runtime=replacement.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(replacement.Player);actor.Id=runtime.Id;warm.RestoreSavedState(actor,runtime,null,defs);cold.RestoreSavedState(actor,runtime,null,defs);warm.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(164),out _);cold.QueueSelectedDeck("a",data,Owned(data),new SeededBattleRandom(165),out _);Walk155(warm,8*60000);Walk155(cold,8*60000);Walk155(warm,10*60000);Walk155(cold,10*60000);Assert.AreEqual(warm.Cards.CaptureState(defs).Key(),cold.Cards.CaptureState(defs).Key());
            }
        }
        [Test] public void 未選択を読み戻した場合は未適用planを破棄し自動登録しない()
        {
            var data=Deck155();var rng=new SeededBattleRandom(166);using(var s=new OutsideExplorationSession()){s.QueueSelectedDeck("a",data,Owned(data),rng,out _);s.QueueSelectedDeck(null,null,null,null,out _);Walk155(s,10*60000);Assert.IsNull(s.Cards);Assert.AreEqual(0,rng.Draws);}
        }
        [Test] public void 地点内から地点外Runtimeへ復元しても歩行退出と誤認して再抽選しない()
        {
            var data=Deck155();var defs=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(data.ToList())).Definitions;using(var source=new OutsideExplorationSession())using(var warm=new OutsideExplorationSession())using(var cold=new OutsideExplorationSession()){
                source.TryInitializePlayer(()=>new PlayerCharacter(100));source.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(167),out _);Walk155(source,10*60000);var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;warm.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(168),out _);Assert.IsTrue(warm.IsDeckEditingLocation);warm.RestoreSavedState(actor,runtime,null,defs);cold.RestoreSavedState(actor,runtime,null,defs);warm.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(169),out _);cold.QueueSelectedDeck("same",data,Owned(data),new SeededBattleRandom(170),out _);string key=source.Cards.CaptureState(defs).Key();warm.Core.TickFrame(0);cold.Core.TickFrame(0);Assert.IsFalse(warm.AdvanceDeckLocation());Assert.IsFalse(cold.AdvanceDeckLocation());Assert.AreEqual(key,warm.Cards.CaptureState(defs).Key());Assert.AreEqual(key,cold.Cards.CaptureState(defs).Key());
            }
        }
        private static OutsideExplorationSession RuntimeSession141(bool cards,int pose,out CheckpointDefinitions definitions)
        {
            var source=CardFixture();definitions=DungeonCheckpointSession.CreateDefault(DungeonConfig.CreateTestDungeon(8,30,2),new Deck(source.ToList())).Definitions;var s=new OutsideExplorationSession();s.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));s.TryConfigureCamera(OutsideCameraPolicy.FixedAt(17*60000));var actor=new PlayerCharacter(100);actor.SetHp(37);actor.ApplyPoison("runtime141",new StatusEffect(StatusEffectType.Poison,60000,1),600);s.TryInitializePlayer(()=>actor);if(cards)Assert.IsTrue(s.TryInitializeCards(source,Owned(source),new SeededBattleRandom(141),out _));s.Core.CardGauge.Configure(140,7);s.Core.CardGauge.TrySpend(70);
            using(var lease=s.AttachPresentation()){
                Action<float,bool,bool,bool,bool> tick=(x,j,h,d,dh)=>{s.Core.TickFrame(x,j,h,d,dh);s.AdvancePlayerFrame();};
                if(pose==0){for(int i=0;i<7;i++)tick(1,false,false,false,false);}
                else if(pose==1)tick(1,true,true,true,true);
                else if(pose==2||pose==3){for(int i=0;i<400&&s.Core.Position<24*60000-31200;i++)tick(1,false,false,false,false);tick(1,true,true,false,false);for(int i=0;i<100&&!s.Core.IsWallSliding;i++)tick(1,false,true,false,false);Assert.IsTrue(s.Core.IsWallSliding);if(pose==3){tick(1,false,false,false,false);tick(1,true,true,false,false);Assert.IsTrue(s.Core.IsWallKicking);}}
                else if(pose==4){tick(0,false,false,true,true);while(s.Core.IsDashing)tick(0,false,false,false,false);tick(0,false,false,true,true);while(s.Core.IsDashing)tick(0,false,false,false,false);while(s.Core.DashCooldownRemaining>1)tick(0,false,false,false,false);Assert.AreEqual(1,s.Core.DashCooldownRemaining);}
                else {tick(0,false,false,true,true);while(s.Core.DashFrame<OutsideTraversalProfile.ValidationWall(true).DashFrames-1)tick(0,false,false,false,true);Assert.IsTrue(s.Core.IsDashing);}
            }
            if(cards)s.Cards.TrySelect(2);return s;
        }
        private static string CoreKey141(OutsideExplorationCore core)=>PortableCheckpointJson.Hash(PortableCheckpointJson.Serialize(core.CaptureState()));
        [TestCase(false,0)][TestCase(true,0)][TestCase(false,1)][TestCase(true,1)][TestCase(false,2)][TestCase(true,3)][TestCase(false,4)][TestCase(true,5)]
        public void 外界の退避は位置時計空中ダッシュゲージとカードを抽選せず復元し同じIDを再適用しない(bool cards,int pose)
        {
            using(var source=RuntimeSession141(cards,pose,out var defs))using(var cold=new OutsideExplorationSession()){
                var runtime=source.CaptureRuntime(defs);runtime=PortableCheckpointJson.Deserialize<OutsideRuntimeSaveData>(PortableCheckpointJson.Serialize(runtime));var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;var key=runtime.Key();var core=source.Core;var oldCards=source.Cards;var frame=core.Frame;long units=core.CardGauge.CurrentUnits;var cardKey=runtime.Cards?.Key();core.TickFrame(-1);source.Player.SetHp(1);
                Assert.IsTrue(source.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(core,source.Core);Assert.AreSame(oldCards,source.Cards);Assert.AreEqual(frame,core.Frame);Assert.AreEqual(units,core.CardGauge.CurrentUnits);Assert.AreEqual(37,source.Player.CurrentHp);Assert.AreEqual(cardKey,source.Cards?.CaptureState(defs).Key());Assert.AreEqual(key,runtime.Key());
                cold.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));Assert.IsTrue(cold.RestoreSavedState(actor,runtime,null,defs));Assert.AreEqual(CoreKey141(source.Core),CoreKey141(cold.Core));Assert.AreEqual(source.Camera.FixedCenter,cold.Camera.FixedCenter);Assert.AreEqual(cardKey,cold.Cards?.CaptureState(defs).Key());if(cards){Assert.AreEqual(17,cold.Cards.RemainingCount);Assert.AreEqual(2,cold.Cards.SelectedIndex);Assert.AreNotSame(source.Cards.Hand[0],cold.Cards.Hand[0]);}else Assert.IsNull(cold.Cards);
                using(var a=source.AttachPresentation())using(var b=cold.AttachPresentation()){Assert.IsFalse(cold.AdvancePlayerFrame());Assert.IsFalse(cold.RestoreSavedState(actor,runtime,null,defs));for(int i=0;i<20;i++){source.Core.TickFrame(0);cold.Core.TickFrame(0);Assert.IsTrue(source.AdvancePlayerFrame());Assert.IsTrue(cold.AdvancePlayerFrame());Assert.IsFalse(cold.AdvancePlayerFrame());Assert.AreEqual(CoreKey141(source.Core),CoreKey141(cold.Core));}Assert.AreEqual(frame+20,cold.Core.Frame);Assert.IsFalse(source.RestoreSavedState(actor,runtime,null,defs));}
                var changed=runtime.Copy();changed.Core.GaugeUnits--;Assert.Throws<ArgumentException>(()=>source.RestoreSavedState(actor,changed,null,defs));Assert.AreEqual(frame+20,core.Frame);
            }
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)][TestCase(5)][TestCase(6)][TestCase(7)][TestCase(8)][TestCase(9)][TestCase(10)][TestCase(11)][TestCase(12)][TestCase(13)][TestCase(14)]
        public void 不正な外界退避はActorだけでなく全Sessionを変更する前に拒否する(int invalid)
        {
            using(var source=RuntimeSession141(true,0,out var defs))using(var target=new OutsideExplorationSession()){
                target.TryConfigureTraversal(OutsideTraversalProfile.ValidationWall(true));target.TryInitializePlayer(()=>new PlayerCharacter(120));target.TryConfigureCamera(OutsideCameraPolicy.FixedAt(60000));target.Core.CardGauge.TrySpend(7);var old=target.Player;var coreKey=CoreKey141(target.Core);var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;
                if(invalid==0)runtime.Version=2;else if(invalid==1)actor.Id=Guid.NewGuid().ToString("N");else if(invalid==2)runtime.Core.DefinitionKey=new string('0',64);else if(invalid==3)runtime.Core.GaugeUnits=long.MaxValue;else if(invalid==4)runtime.Core.Height=long.MaxValue;else if(invalid==5)runtime.Core.SupportId="missing";else if(invalid==6)runtime.Core.Frame=long.MaxValue;else if(invalid==7)runtime.Cards.Hand[1]=runtime.Cards.Hand[0];else if(invalid==8)runtime.Cards.Random.State=0;else if(invalid==9)runtime.Cards.Cards[0].ValueHash=new string('0',64);else if(invalid==10)runtime.FixedCameraCenter=long.MaxValue;else if(invalid==11)runtime.Core.Afterimages=new[]{new OutsideAfterimageStateData{Facing=1,Frame=runtime.Core.Frame,Pose=(OutsideAfterimagePose)99}};else if(invalid==12){runtime.Core.Height=10000;runtime.Core.Velocity=1000;runtime.Core.SupportId=null;runtime.Core.DashJumping=true;runtime.Core.DashStartedAt=-1;}else if(invalid==13){runtime.Core.Height=10000;runtime.Core.Velocity=-400;runtime.Core.SupportId=null;runtime.Core.WallSliding=true;runtime.Core.WallContact=1;}else {runtime.Core.DashStartedAt=1;runtime.Core.DashEndsAt=13;runtime.Core.CooldownUntil=8;runtime.Core.ConsecutiveDashes=2;runtime.Core.LastDashAt=runtime.Core.Frame;}
                if(invalid==10)Assert.Throws<ArgumentOutOfRangeException>(()=>target.RestoreSavedState(actor,runtime,null,defs));else Assert.Throws<ArgumentException>(()=>target.RestoreSavedState(actor,runtime,null,defs));Assert.AreSame(old,target.Player);Assert.AreEqual(coreKey,CoreKey141(target.Core));Assert.AreEqual(60000,target.Camera.FixedCenter);Assert.IsNull(target.Cards);Assert.IsFalse(target.IsPresented);
            }
        }
        [Test] public void 外界cold起動の既知試走設定も全候補検証後にだけ適用する()
        {
            using(var source=RuntimeSession141(false,1,out var defs))using(var target=new OutsideExplorationSession()){
                var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;target.TryInitializePlayer(()=>new PlayerCharacter(120));var old=target.Player;string before=CoreKey141(target.Core);var invalid=runtime.Copy();invalid.FixedCameraCenter=long.MaxValue;Assert.Throws<ArgumentOutOfRangeException>(()=>target.RestoreSavedState(actor,invalid,null,defs,OutsideTraversalProfile.ValidationWall(true)));Assert.AreSame(old,target.Player);Assert.AreEqual(before,CoreKey141(target.Core));Assert.AreEqual("validation-traversal-v1",target.Core.TraversalProfile.Id);Assert.IsTrue(target.RestoreSavedState(actor,runtime,null,defs,OutsideTraversalProfile.ValidationWall(true)));Assert.AreEqual(CoreKey141(source.Core),CoreKey141(target.Core));Assert.IsTrue(target.Core.TraversalProfile.WallActionsUnlocked);
            }
        }
        [Test] public void 外界の固定小数点余りも復元し未定義のダッシュ状態は受け入れない()
        {
            var map=new OutsideMovementDefinition("fraction141",32*60000,6*60000,360017);var a=new OutsideExplorationCore(map,OutsideTraversalProfile.ValidationDash);var b=new OutsideExplorationCore(map,OutsideTraversalProfile.ValidationDash);for(int i=0;i<5;i++)a.TickFrame(1);var saved=a.CaptureState();Assert.AreNotEqual(0,saved.Remainder);var restore=typeof(OutsideExplorationCore).GetMethod("RestoreState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);restore.Invoke(b,new object[]{saved});for(int i=0;i<60;i++){a.TickFrame(i<30?1:-1);b.TickFrame(i<30?1:-1);Assert.AreEqual(CoreKey141(a),CoreKey141(b));}
            var plain=new OutsideExplorationCore(map);var invalid=plain.CaptureState();invalid.SupportId=null;invalid.Height=1000;invalid.Velocity=1000;invalid.DashJumping=true;string before=CoreKey141(plain);var error=Assert.Throws<System.Reflection.TargetInvocationException>(()=>restore.Invoke(plain,new object[]{invalid}));Assert.IsInstanceOf<ArgumentException>(error.InnerException);Assert.AreEqual(before,CoreKey141(plain));
        }
        [Test] public void 外界カードの使用履歴返却順と乱数継続は保存復元時に抽選を消費しない()
        {
            using(var source=RuntimeSession141(true,0,out var defs)){
                var cards=source.Cards;cards.Reload();cards.TrySelect(2);var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var deck=(Deck)typeof(OutsideExplorationCards).GetField("deck",flags).GetValue(cards);var random=(SeededBattleRandom)typeof(OutsideExplorationCards).GetField("random",flags).GetValue(cards);var returned=deck.DrawCards(2,random);typeof(Deck).GetMethod("ReturnPreviewDraws",flags).Invoke(deck,new object[]{returned});var data=cards.CaptureState(defs);long draws=random.Draws;var restored=OutsideExplorationCards.RestoreState(data,defs);Assert.AreEqual(data.Key(),restored.CaptureState(defs).Key());Assert.AreEqual(draws,random.Draws);Assert.AreEqual(1,restored.HandRevision);var other=(Deck)typeof(OutsideExplorationCards).GetField("deck",flags).GetValue(restored);var otherRandom=(SeededBattleRandom)typeof(OutsideExplorationCards).GetField("random",flags).GetValue(restored);for(int n=0;n<4;n++){var a=deck.DrawCards(1,random).Single();var b=other.DrawCards(1,otherRandom).Single();Assert.AreEqual(Array.IndexOf(deck.GetAllCards().ToArray(),a),Array.IndexOf(other.GetAllCards().ToArray(),b));Assert.AreEqual(random.Draws,otherRandom.Draws);}Assert.AreEqual(2,restored.SelectedIndex);
            }
        }
        [Test] public void 外界退避は表示中と未知乱数を拒否し回復後の古いRuntimeを再生しない()
        {
            using(var source=RuntimeSession141(true,0,out var defs)){
                using(var lease=source.AttachPresentation())Assert.Throws<InvalidOperationException>(()=>source.CaptureRuntime(defs));var runtime=source.CaptureRuntime(defs);var actor=OutsidePlayerSaveData.Capture(source.Player);actor.Id=runtime.Id;Assert.IsTrue(source.RestoreSavedState(actor,runtime,null,defs));var recovered=OutsidePlayerSaveData.ForRecovery(source.Player);source.RecoverOutside(null);source.AcknowledgeRecovery(recovered);source.Player.SetHp(56);Assert.Throws<ArgumentException>(()=>source.RestoreSavedState(actor,runtime,null,defs));Assert.AreEqual(56,source.Player.CurrentHp);Assert.IsFalse(source.RestoreSavedState(recovered,null,null,defs));Assert.AreEqual(56,source.Player.CurrentHp);
            }
            using(var unknown=SelectionSession()){var cards=unknown.Cards;Assert.Throws<ArgumentException>(()=>unknown.CaptureRuntime());Assert.AreSame(cards,unknown.Cards);}
        }
        [TestCase(0)][TestCase(1)][TestCase(2)][TestCase(3)][TestCase(4)] public void 不正な外界保存個体は生きた個体を置き換える前に拒否する(int invalid)
        {
            using(var session=new OutsideExplorationSession()){var actor=new Olca.Core.Domain.PlayerCharacter(100);actor.SetHp(37);if(invalid==4){actor.SetElementResistances(Olca.Core.Domain.ElementResistances.Restore(150,0,0,0,null));actor.SetDepartureAbilities(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="raw139",Sources=System.Array.Empty<Olca.Core.Logic.Customization.CustomizationAbilitySourceDto>()}));}session.TryInitializePlayer(()=>actor);var data=Olca.Core.Domain.OutsidePlayerSaveData.Capture(actor);if(invalid==0)data.Version=2;else if(invalid==1)data.Id="unknown";else if(invalid==2)data.RawBaseElements=new int[3];else if(invalid==3)data.Player.Hp=0;else data.RawBaseElements[0]=151;Assert.Throws<System.ArgumentException>(()=>session.RestoreSavedPlayer(data,null));Assert.AreSame(actor,session.Player);Assert.AreEqual(37,actor.CurrentHp);}
        }
        [Test] public void 外界保存個体の差替えは永久能力と共有状態を保ち旧Actorを表示側へ残さない()
        {
            using(var session=SelectionSession()){
                var actor=new Olca.Core.Domain.PlayerCharacter(120,attackPower:13,statusResistances:new Olca.Core.Domain.TimedStatusResistances(80,20,30,40));actor.SetAttackGrowth(Olca.Core.Domain.AttackMilestoneGrowth.ForLevel(5));actor.SetDepartureAbilities(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="fixture139",Sources=new[]{new Olca.Core.Logic.Customization.CustomizationAbilitySourceDto{Kind="part",SourceId="slot139",ItemId="part139",Floating=true,Fire=7,Poison=10}}}));actor.SetElementResistances(Olca.Core.Domain.ElementResistances.Restore(150,-30,70,20,new[]{10,20,30,40}));actor.SetHp(37);actor.ApplyPoison("saved139",new Olca.Core.Domain.StatusEffect(Olca.Core.Domain.StatusEffectType.Poison,60000,2),600);session.TryInitializePlayer(()=>actor);var core=session.Core;var cards=session.Cards;var hand=System.Linq.Enumerable.ToArray(cards.Hand);using(var lease=session.AttachPresentation()){core.TickFrame(1);session.AdvancePlayerFrame();}var saved=Olca.Core.Domain.OutsidePlayerSaveData.Capture(actor);long frame=core.Frame,position=core.Position,gauge=core.CardGauge.CurrentUnits;
                Assert.IsTrue(session.RestoreSavedPlayer(saved,null));Assert.AreNotSame(actor,session.Player);Assert.AreSame(core,session.Core);Assert.AreSame(cards,session.Cards);CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(frame,core.Frame);Assert.AreEqual(position,core.Position);Assert.AreEqual(gauge,core.CardGauge.CurrentUnits);Assert.AreEqual(150,session.Player.BaseElementResistances.Fire);Assert.AreEqual(80,session.Player.BaseStatusResistances.Poison);Assert.IsTrue(session.Player.DepartureAbilities.Floating);Assert.AreEqual(90,session.Player.StatusResistances.Poison);Assert.AreEqual("fixture139",session.Player.DepartureAbilities.Capture().DefinitionId);Assert.AreEqual(5,session.Player.AttackGrowth.Level);Assert.AreEqual(saved.Player.Poisons[0].RemainingUnits,session.Player.CapturePoisons()[0].RemainingUnits);actor.SetHp(1);
                using(var lease=session.AttachPresentation()){Assert.AreSame(session.Player,lease.Player);Assert.AreEqual(37,lease.Player.CurrentHp);Assert.IsFalse(session.RestoreSavedPlayer(saved,null));var next=saved.Copy();next.Id=System.Guid.NewGuid().ToString("N");Assert.Throws<System.InvalidOperationException>(()=>session.RestoreSavedPlayer(next,null));Assert.AreSame(session.Player,lease.Player);Assert.IsFalse(session.AdvancePlayerFrame());core.TickFrame(0);Assert.IsTrue(session.AdvancePlayerFrame());Assert.IsFalse(session.AdvancePlayerFrame());}
                var invalid=saved.Copy();invalid.Player.Hp=0;Assert.Throws<System.ArgumentException>(()=>session.RestoreSavedPlayer(invalid,null));Assert.AreEqual(37,session.Player.CurrentHp);
            }
        }
        [Test] public void 外界装備の差替えは同じ個体と一時状態とカードと時計を保ち補正を加算し直さない()
        {
            using(var s=SelectionSession()){
                var actor=new PlayerCharacter(120);actor.SetHp(37);actor.ApplyPoison("equipment225",new StatusEffect(StatusEffectType.Poison,60000,2),600);actor.SetInvincible(3000);s.TryInitializePlayer(()=>actor);
                var core=s.Core;var cards=s.Cards;var hand=cards.Hand.ToArray();long frame=core.Frame,position=core.Position,gauge=core.CardGauge.CurrentUnits;var poisons=actor.CapturePoisons();
                Olca.Core.Logic.Customization.ResolvedCustomizationAbilities Abilities(int fire)=>new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="equipment225",Sources=new[]{new Olca.Core.Logic.Customization.CustomizationAbilitySourceDto{Kind="part",SourceId="slot225",ItemId="part225",Fire=fire,Floating=fire>0}}});
                using(var lease=s.AttachPresentation()){
                    Assert.IsTrue(s.TryApplyEquipment(Abilities(20)));Assert.AreSame(actor,lease.Player);Assert.AreEqual(20,actor.Resistances.Fire);Assert.IsTrue(actor.TerrainState.IsFloating);
                    Assert.IsTrue(s.TryApplyEquipment(Abilities(5)));Assert.AreEqual(5,actor.Resistances.Fire,"置換なので以前の20を累積しない");
                    Assert.IsTrue(s.TryApplyEquipment(new Olca.Core.Logic.Customization.ResolvedCustomizationAbilities(new Olca.Core.Logic.Customization.CustomizationAbilitiesDto{DefinitionId="equipment225",Sources=Array.Empty<Olca.Core.Logic.Customization.CustomizationAbilitySourceDto>()})));Assert.AreEqual(0,actor.Resistances.Fire);Assert.IsFalse(actor.TerrainState.IsFloating);
                    Assert.AreEqual(37,actor.CurrentHp);Assert.IsTrue(actor.IsInvincible);Assert.AreEqual(3000,actor.InvincibleDurationMs);Assert.AreEqual(poisons[0].RemainingUnits,actor.CapturePoisons()[0].RemainingUnits);Assert.AreSame(core,s.Core);Assert.AreSame(cards,s.Cards);CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(frame,core.Frame);Assert.AreEqual(position,core.Position);Assert.AreEqual(gauge,core.CardGauge.CurrentUnits);
                }
                Assert.Throws<ArgumentException>(()=>OutsidePlayerSaveData.Capture(actor),"装備の状態保持と、未対応の無敵途中保存は区別する");actor.TickInvincible(3000);var saved=OutsidePlayerSaveData.Capture(actor);using(var cold=new OutsideExplorationSession()){Assert.IsTrue(cold.RestoreSavedPlayer(saved,null));Assert.IsTrue(cold.TryApplyEquipment(Abilities(7)));Assert.AreEqual(7,cold.Player.Resistances.Fire);}
                typeof(PlayerCharacter).GetMethod("FreezeDepartureState",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(actor,null);Assert.IsFalse(s.CanReplaceEquipment);Assert.IsFalse(s.TryApplyEquipment(Abilities(99)));Assert.AreEqual(0,actor.Resistances.Fire,"攻略固定後は変更不可");
            }
        }
        [Test] public void 外界装備は未生成と終了済みSessionへ反映しない()
        {
            var s=new OutsideExplorationSession();Assert.IsFalse(s.CanReplaceEquipment);Assert.IsFalse(s.TryApplyEquipment(null));s.TryInitializePlayer(()=>new PlayerCharacter(100));s.Dispose();Assert.IsFalse(s.CanReplaceEquipment);Assert.IsFalse(s.TryApplyEquipment(null));
        }
        private static Olca.Core.Domain.Card[] CardFixture()=>System.Linq.Enumerable.ToArray(System.Linq.Enumerable.SelectMany(System.Linq.Enumerable.Range(0,5),id=>System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),n=>new Olca.Core.Domain.Card("outside-fixture-"+id,"fixture",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.None,1,"fixture"))));
        private static System.Collections.Generic.Dictionary<(string Id,Olca.Core.Domain.ExceedLevel Level),int> Owned(Olca.Core.Domain.Card[] cards)=>System.Linq.Enumerable.ToDictionary(System.Linq.Enumerable.GroupBy(cards,c=>(c.Id,c.ExceedLevel)),g=>g.Key,g=>System.Linq.Enumerable.Count(g));
        private static OutsideExplorationSession SelectionSession()
        {
            var source=CardFixture();var session=new OutsideExplorationSession();Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));return session;
        }
        private static void SelectionSample(OutsideSelectionRepeat repeat,long time,bool previous=false,bool next=false,bool previousDown=false,bool nextDown=false,bool snapshot=false,long previousAt=0,long nextAt=0)
            =>repeat.Queue(time,previous,next,previousDown,nextDown,snapshot,previous||next,previousAt,nextAt);
        private static OutsideSelectionRepeat ReadySelection(OutsideExplorationCards cards)
        {
            var repeat=new OutsideSelectionRepeat();SelectionSample(repeat,0);repeat.AdvanceFrame(cards);return repeat;
        }
        [Test] public void 外界巡回は即時一回と二十四九フレームで端を循環する()
        {
            using(var session=SelectionSession()){
                var cards=session.Cards;var repeat=ReadySelection(cards);SelectionSample(repeat,1,previous:true,previousDown:true);repeat.AdvanceFrame(cards);Assert.AreEqual(2,cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(cards);Assert.AreEqual(2,cards.SelectedIndex);repeat.AdvanceFrame(cards);Assert.AreEqual(1,cards.SelectedIndex);
                for(int i=0;i<8;i++)repeat.AdvanceFrame(cards);Assert.AreEqual(1,cards.SelectedIndex);repeat.AdvanceFrame(cards);Assert.AreEqual(0,cards.SelectedIndex);
                Assert.AreEqual(0,session.Core.Frame);Assert.AreEqual(6000,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(17,cards.RemainingCount);
            }
        }
        [TestCase(false)] [TestCase(true)] public void 外界巡回の同時刻両押下は列挙順によらず次を一回採用する(bool reverse)
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);
                SelectionSample(repeat,10,previous:!reverse,next:reverse,previousDown:!reverse,nextDown:reverse);
                SelectionSample(repeat,10,previous:true,next:true,previousDown:reverse,nextDown:!reverse);
                repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
                for(int i=0;i<24;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は後押し優先と優先解除後の反対保持待機を守る()
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);SelectionSample(repeat,1,previous:true,previousDown:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
                SelectionSample(repeat,2,previous:true,next:true,nextDown:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                SelectionSample(repeat,3,previous:true);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(2,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は同フレーム短押しを失わず初回保持を解除まで拒否する()
        {
            using(var session=SelectionSession()){
                var repeat=new OutsideSelectionRepeat();SelectionSample(repeat,1,next:true,snapshot:true);repeat.AdvanceFrame(session.Cards);for(int i=0;i<60;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                SelectionSample(repeat,2);SelectionSample(repeat,3,next:true,nextDown:true);SelectionSample(repeat,4);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
                for(int i=0;i<60;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
            }
        }
        [TestCase(20,10,2)] [TestCase(10,20,1)] [TestCase(20,20,1)]
        public void 外界巡回の再開保持は新押下とせず時刻優先で二十四フレーム待つ(long previousAt,long nextAt,int expected)
        {
            using(var session=SelectionSession()){
                var repeat=new OutsideSelectionRepeat();repeat.Reset(true);SelectionSample(repeat,30,previous:true,next:true,snapshot:true,previousAt:previousAt,nextAt:nextAt);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<23;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(0,session.Cards.SelectedIndex);repeat.AdvanceFrame(session.Cards);Assert.AreEqual(expected,session.Cards.SelectedIndex);
            }
        }
        [Test] public void 外界巡回は入力禁止中の選択を進めず解除後の保持を継続する()
        {
            using(var session=SelectionSession()){
                var repeat=ReadySelection(session.Cards);SelectionSample(repeat,1,next:true,nextDown:true);repeat.AdvanceFrame(session.Cards,false);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<24;i++)repeat.AdvanceFrame(session.Cards,false);Assert.AreEqual(0,session.Cards.SelectedIndex);
                for(int i=0;i<9;i++)repeat.AdvanceFrame(session.Cards);Assert.AreEqual(1,session.Cards.SelectedIndex);
            }
        }
        [TestCase(-1)] [TestCase(3)] [TestCase(int.MaxValue)]
        public void 外界の直接選択は初期左端と範囲を守りカードと時計を変更しない(int invalid)
        {
            var source=CardFixture();using(var session=new OutsideExplorationSession()){
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=session.Cards;var hand=System.Linq.Enumerable.ToArray(cards.Hand);long gauge=session.Core.CardGauge.CurrentUnits;
                Assert.AreEqual(0,cards.SelectedIndex);Assert.AreSame(hand[0],cards.SelectedCard);Assert.IsTrue(cards.TrySelect(2));Assert.AreSame(hand[2],cards.SelectedCard);
                Assert.IsFalse(cards.TrySelect(invalid));Assert.AreEqual(2,cards.SelectedIndex);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(20,cards.TotalCount);CollectionAssert.AreEqual(hand,cards.Hand);Assert.AreEqual(gauge,session.Core.CardGauge.CurrentUnits);Assert.AreEqual(0,session.Core.Frame);
                var first=session.AttachPresentation();first.Dispose();var next=session.AttachPresentation();Assert.AreSame(cards,next.Cards);Assert.AreEqual(2,next.Cards.SelectedIndex);next.Dispose();
            }
        }
        [Test] public void 外界初期手札は独立した二十枚から三枚を引き表示借用と時計で再抽選しない()
        {
            var source=CardFixture();var random=new Olca.Tests.Mocks.MockRandomProvider();using(var session=new OutsideExplorationSession()){
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),random,out _));var cards=session.Cards;Assert.AreEqual(20,cards.TotalCount);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(3,cards.Hand.Count);
                foreach(var card in cards.Hand)foreach(var original in source)Assert.AreNotSame(original,card);
                var first=session.AttachPresentation();var hand=System.Linq.Enumerable.ToArray(cards.Hand);for(int n=0;n<120;n++)session.Core.TickFrame(0);first.Dispose();var next=session.AttachPresentation();Assert.AreSame(cards,next.Cards);CollectionAssert.AreEqual(hand,next.Cards.Hand);Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));next.Dispose();Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));
            }
        }
        [Test] public void 外界初回登録は所持と構築制限を共用し拒否で状態を作らない()
        {
            var source=CardFixture();var random=new Olca.Tests.Mocks.MockRandomProvider();using(var session=new OutsideExplorationSession()){
                Assert.IsFalse(session.TryInitializeCards(System.Array.Empty<Olca.Core.Domain.Card>(),Owned(source),random,out _));Assert.IsNull(session.Cards);
                Assert.IsFalse(session.TryInitializeCards(source,new System.Collections.Generic.Dictionary<(string,Olca.Core.Domain.ExceedLevel),int>(),random,out _));Assert.IsNull(session.Cards);
                using(var lease=session.AttachPresentation())Assert.IsFalse(session.TryInitializeCards(source,Owned(source),random,out _));Assert.IsNull(session.Cards);
                Assert.Throws<System.ArgumentNullException>(()=>session.TryInitializeCards(source,Owned(source),null,out _));Assert.IsNull(session.Cards);
                var over=(Olca.Core.Domain.Card[])source.Clone();over[4]=source[0];Assert.IsFalse(session.TryInitializeCards(over,Owned(over),random,out _));Assert.IsNull(session.Cards);
                Assert.IsTrue(session.TryInitializeCards(source,Owned(source),random,out _));
            }
        }
        [TestCase(1)] [TestCase(5)] [TestCase(90)]
        public void 論理フレームのまとめ方にかかわらず三二一は各三十で完了通知は一回(int batch)
        {
            var timer=new ExplorationResumeCountdown();timer.Begin();int finished=0;
            for(int start=0;start<90;start+=batch)for(int i=start;i<System.Math.Min(start+batch,90);i++)
            {Assert.AreEqual(3-i/30,timer.DisplayCount);if(timer.AdvanceFrame())finished++;}
            Assert.AreEqual(1,finished);Assert.AreEqual(0,timer.DisplayCount);Assert.IsFalse(timer.AdvanceFrame());
        }
        [Test] public void 再開連打は延長せず取消後だけ全長で始める()
        {
            var timer=new ExplorationResumeCountdown();Assert.IsFalse(timer.AdvanceFrame());timer.Begin();timer.AdvanceFrame();timer.Begin();Assert.AreEqual(89,timer.RemainingFrames);
            timer.Cancel();Assert.AreEqual(0,timer.DisplayCount);timer.Begin();Assert.AreEqual(90,timer.RemainingFrames);
        }
        [Test] public void 外界Sessionは表示借用を返して再表示しても同じ状態を保持し旧借用は新表示を閉じない()
        {
            var session=new OutsideExplorationSession();var first=session.AttachPresentation();Assert.IsTrue(first.IsCurrent);first.Dispose();Assert.IsFalse(session.IsPresented);Assert.IsFalse(session.IsEnded);
            var second=session.AttachPresentation();var third=session.AttachPresentation();Assert.IsFalse(second.IsCurrent);second.Dispose();Assert.IsTrue(third.IsCurrent);Assert.IsTrue(session.IsPresented);third.Dispose();Assert.IsFalse(session.IsPresented);
        }
        [Test] public void 外界SessionはRoot終了後の再表示と旧入力を受けない()
        {
            var session=new OutsideExplorationSession();var lease=session.AttachPresentation();session.Dispose();session.Dispose();Assert.IsTrue(session.IsEnded);Assert.IsFalse(lease.IsCurrent);Assert.IsFalse(session.IsPresented);lease.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>session.AttachPresentation());
        }

        [TestCase(1)] [TestCase(10)] [TestCase(60)]
        public void 外界横移動は描画分割に依存せず六十Hzで進み停止と反転ができる(int parts)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();
            long consumed=0;
            for(int i=1;i<=parts;i++){long target=LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-consumed);consumed=target;clock.Drain((frame,ms)=>core.TickFrame(1));}
            Assert.AreEqual(60,core.Frame);Assert.AreEqual(14*OutsideMovementDefinition.UnitsPerWorldUnit,core.Position);
            long stopped=core.Position;for(int i=0;i<60;i++)core.TickFrame(0);Assert.AreEqual(stopped,core.Position);
            for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(core.Definition.Start,core.Position);Assert.AreEqual(-1,core.Facing);
        }
        [Test] public void 外界の境界で止まり逆入力は即座に離れ検証状態は表示借用と別に保持される()
        {
            var s=new OutsideExplorationSession();var lease=s.AttachPresentation();for(int i=0;i<1000;i++)s.Core.TickFrame(-1);Assert.AreEqual(0,s.Core.Position);
            lease.Dispose();long frame=s.Core.Frame;var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual(frame,next.Core.Frame);
            s.Core.TickFrame(1);Assert.Greater(s.Core.Position,0);for(int i=0;i<1000;i++)s.Core.TickFrame(1);Assert.AreEqual(s.Core.Definition.Width,s.Core.Position);
            s.Core.TickFrame(-1);Assert.Less(s.Core.Position,s.Core.Definition.Width);
        }
        [Test] public void 外界定義が受け付ける整数極値でも端から反対側へ桁あふれしない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,long.MaxValue,long.MaxValue));
            core.TickFrame(1);Assert.AreEqual(long.MaxValue,core.Position);core.TickFrame(-1);Assert.Greater(core.Position,0);Assert.Less(core.Position,long.MaxValue);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("edge",long.MaxValue,0,long.MaxValue));left.TickFrame(-1);Assert.AreEqual(0,left.Position);left.TickFrame(1);Assert.Greater(left.Position,0);
        }
        [Test] public void 外界低速設定でも端から逆入力で離れ整数未満の余りを失わない()
        {
            var core=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,100,1));for(int i=0;i<60;i++)core.TickFrame(-1);Assert.AreEqual(99,core.Position);
            var left=new OutsideExplorationCore(new OutsideMovementDefinition("slow",100,0,1));for(int i=0;i<60;i++)left.TickFrame(1);Assert.AreEqual(1,left.Position);
        }

        [Test] public void 外界Idleは移動から停止するたびゼロで開始し停止中だけ進む()
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);
            Assert.AreEqual(0,core.IdleFrame);
            for(int cycle=0;cycle<4;cycle++){
                for(int i=0;i<17;i++)core.TickFrame(1);
                Assert.AreEqual(0,core.IdleFrame);core.TickFrame(0);
                Assert.AreEqual(0,core.IdleFrame);Assert.AreEqual(0,core.MovementFrame);
                for(int i=1;i<=13;i++){core.TickFrame(0);Assert.AreEqual(i,core.IdleFrame);}
            }
        }
        [Test] public void 外界Idleは着地と境界停止でもゼロから始まり表示再入で時計を変えない()
        {
            using(var session=new OutsideExplorationSession()){
                var core=session.Core;for(int i=0;i<19;i++)core.TickFrame(0);
                core.TickFrame(0,true,true);Assert.AreEqual(0,core.IdleFrame);
                for(int i=0;i<120&&!core.IsGrounded;i++)core.TickFrame(0,false,true);
                Assert.IsTrue(core.IsGrounded);Assert.AreEqual(0,core.IdleFrame);
                core.TickFrame(0);Assert.AreEqual(1,core.IdleFrame);
                var first=session.AttachPresentation();long frame=core.Frame;first.Dispose();
                var second=session.AttachPresentation();Assert.AreEqual(1,core.IdleFrame);Assert.AreEqual(frame,core.Frame);second.Dispose();
                while(core.Position>0)core.TickFrame(-1);
                core.TickFrame(-1);Assert.AreEqual(0,core.IdleFrame);
                core.TickFrame(-1);Assert.AreEqual(1,core.IdleFrame);
            }
        }

        [TestCase(1)] [TestCase(7)] [TestCase(60)]
        public void 外界ジャンプは保持で高くなり描画分割で軌道が変わらない(int parts)
        {
            var held=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var tap=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);var clock=new LogicalFrameClock();long prior=0,peak=0,shortPeak=0;
            for(int i=1;i<=parts;i++){long target=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(target-prior);prior=target;clock.Drain((f,ms)=>{held.TickFrame(1,f==1,true);tap.TickFrame(1,f==1,f==1);peak=System.Math.Max(peak,held.Height);shortPeak=System.Math.Max(shortPeak,tap.Height);});}
            Assert.AreEqual(186000,peak);Assert.Less(shortPeak,peak);Assert.IsTrue(held.IsGrounded);Assert.IsTrue(tap.IsGrounded);Assert.AreEqual(20*OutsideMovementDefinition.UnitsPerWorldUnit,held.Position);Assert.AreEqual(120,held.Frame);
        }
        [Test] public void 外界ジャンプは保持着地と空中押下で再ジャンプを予約しない()
        {
            var c=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
            c.TickFrame(0,true,true);Assert.IsTrue(c.IsGrounded);c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,0);
            c.TickFrame(0,false,false);c.TickFrame(0,true,true);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.IsTrue(c.IsGrounded);
        }
        [Test] public void 外界空中状態はView借用終了で進まず再表示時も保持する()
        {
            var s=new OutsideExplorationSession();var a=s.AttachPresentation();s.Core.TickFrame(-1,true,true);long y=s.Core.Height,v=s.Core.VerticalVelocity,f=s.Core.Frame;a.Dispose();var b=s.AttachPresentation();Assert.AreSame(s.Core,b.Core);Assert.AreEqual(y,b.Core.Height);Assert.AreEqual(v,b.Core.VerticalVelocity);Assert.AreEqual(f,b.Core.Frame);b.Dispose();
        }
        private static OutsideExplorationCore LandingCore(params OutsideLandingSurface[] surfaces)=>new OutsideExplorationCore(new OutsideMovementDefinition("landing-test",32*60000,8*60000,6*60000,surfaces));
        private static void LandOnValidationTop(OutsideExplorationCore c){for(int i=0;i<80;i++){c.TickFrame(1,i==0,true);if(c.SupportId=="top")return;}Assert.Fail("検証上面へ着地しない");}
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界上面接地は描画分割に依存せず下から通過して下降時だけ止まる(int parts)
        {
            var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));var clock=new LogicalFrameClock();long prior=0;bool landed=false,passedBelow=false;
            for(int i=1;i<=parts;i++){long t=2*LogicalFrameClock.WallTicksPerSecond*i/parts;clock.Accumulate(t-prior);prior=t;clock.Drain((f,ms)=>{c.TickFrame(landed?0:1,f==1,true);if(c.VerticalVelocity>0&&c.Height<60000)passedBelow=true;if(c.SupportId=="top")landed=true;});}
            Assert.IsTrue(passedBelow);Assert.IsTrue(landed);Assert.IsTrue(c.IsGrounded);Assert.AreEqual("top",c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(0,c.VerticalVelocity);Assert.AreEqual(120,c.Frame);
        }
        [Test] public void 外界足場上の保持は再ジャンプせず新しい押下だけで飛び両端から落ちる()
        {
            foreach(int direction in new[]{-1,1}){var c=LandingCore(new OutsideLandingSurface("top",12*60000,16*60000,60000));LandOnValidationTop(c);for(int i=0;i<120;i++)c.TickFrame(0,false,true);Assert.AreEqual(60000,c.Height);Assert.AreEqual("top",c.SupportId);
                c.TickFrame(0,false,false);c.TickFrame(0,true,true);Assert.Greater(c.Height,60000);Assert.IsFalse(c.IsGrounded);for(int i=0;i<100;i++)c.TickFrame(0,false,true);Assert.AreEqual("top",c.SupportId);
                for(int i=0;i<80&&c.IsGrounded;i++)c.TickFrame(direction);Assert.IsFalse(c.IsGrounded);Assert.IsNull(c.SupportId);Assert.AreEqual(60000,c.Height);Assert.AreEqual(-400,c.VerticalVelocity);
                for(int i=0;i<60;i++)c.TickFrame(direction);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);}
        }
        [Test] public void 外界重複上面は配列順でなく最初に横切る高さと識別子で決まる()
        {
            var low=new OutsideLandingSurface("low",0,32*60000,58000);var a=new OutsideLandingSurface("a",0,32*60000,60000);var b=new OutsideLandingSurface("b",0,32*60000,60000);
            foreach(var surfaces in new[]{new[]{low,b,a},new[]{a,low,b}}){var c=LandingCore(surfaces);for(int i=0;i<100;i++)c.TickFrame(0,i==0,true);Assert.AreEqual("a",c.SupportId);Assert.AreEqual(60000,c.Height);}
        }
        [Test] public void 外界上面の支持は高さと端を検証し旧入力配列の変更から独立する()
        {
            var top=new OutsideLandingSurface("top",12*60000,16*60000,60000);Assert.IsTrue(top.Contains(top.Left));Assert.IsTrue(top.Contains(top.Right));Assert.IsFalse(top.Contains(top.Left-1));Assert.IsFalse(top.Contains(top.Right+1));
            var input=new[]{top};var c=LandingCore(input);input[0]=new OutsideLandingSurface("other",0,32*60000,90000);LandOnValidationTop(c);Assert.AreEqual("top",c.SupportId);
            Assert.Throws<System.ArgumentException>(()=>LandingCore(top,top));Assert.Throws<System.ArgumentException>(()=>LandingCore(new OutsideLandingSurface("overflow",0,32*60000,long.MaxValue)));
        }
        [Test] public void 外界上面を同frameで横断し終点が面外なら検証モデルでは着地しない()
        {
            var c=new OutsideExplorationCore(new OutsideMovementDefinition("fast",100*60000,0,60*60000,new OutsideLandingSurface("thin",334000,338000,60000)));
            // 初めはその場で跳び、下降時だけ高速横移動する。狭い上面を飛び越してfloorへ戻る。
            for(int i=0;i<55;i++)c.TickFrame(i>=50?1:0,i==0,true);Assert.AreEqual(66000,c.Height);Assert.AreEqual(300000,c.Position);c.TickFrame(1,false,true);Assert.AreEqual(56000,c.Height);Assert.IsNull(c.SupportId);for(int i=0;i<64;i++)c.TickFrame(1,false,true);Assert.AreEqual("floor",c.SupportId);Assert.AreEqual(0,c.Height);
        }
        [Test] public void 外界支持面の識別子と位置は表示借用を返しても保持される()
        {
            var s=new OutsideExplorationSession();var first=s.AttachPresentation();for(int i=0;i<80&&s.Core.SupportId!="validation-top";i++)s.Core.TickFrame(1,i==0,true);Assert.AreEqual("validation-top",s.Core.SupportId);long y=s.Core.Height,f=s.Core.Frame,x=s.Core.Position;first.Dispose();var next=s.AttachPresentation();Assert.AreSame(s.Core,next.Core);Assert.AreEqual("validation-top",next.Core.SupportId);Assert.AreEqual(y,next.Core.Height);Assert.AreEqual(f,next.Core.Frame);Assert.AreEqual(x,next.Core.Position);next.Dispose();
        }
        [Test] public void 外界カメラ追従は既存の両端余白と狭いマップの中央を保つ()
        {
            var p=OutsideCameraPolicy.Follow;const long u=60000;
            Assert.AreEqual(4,p.ResolveHorizontalCenter(0,32*u,10));Assert.AreEqual(8,p.ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(28,p.ResolveHorizontalCenter(32*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界固定カメラは移動で動かず画面比率とマップ範囲に応じて拘束する()
        {
            const long u=60000;var p=OutsideCameraPolicy.FixedAt(12*u);
            foreach(long x in new[]{0,8*u,16*u,32*u}){Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,10));Assert.AreEqual(12,p.ResolveHorizontalCenter(x,32*u,20));}
            Assert.AreEqual(4,OutsideCameraPolicy.FixedAt(0).ResolveHorizontalCenter(8*u,32*u,10));Assert.AreEqual(16,p.ResolveHorizontalCenter(8*u,32*u,40));
        }
        [Test] public void 外界カメラ設定は非表示だけで変更しView世代や位置と時計を初期化しない()
        {
            using(var s=new OutsideExplorationSession()){
                var core=s.Core;core.TickFrame(1);long x=core.Position,f=core.Frame;var fixedPolicy=OutsideCameraPolicy.FixedAt(12*60000);
                Assert.IsTrue(s.TryConfigureCamera(fixedPolicy));var a=s.AttachPresentation();Assert.AreSame(fixedPolicy,a.Camera);Assert.IsFalse(s.TryConfigureCamera(OutsideCameraPolicy.Follow));var b=s.AttachPresentation();a.Dispose();Assert.IsTrue(b.IsCurrent);Assert.AreSame(fixedPolicy,b.Camera);b.Dispose();Assert.AreEqual(x,core.Position);Assert.AreEqual(f,core.Frame);Assert.IsTrue(s.TryConfigureCamera(OutsideCameraPolicy.Follow));Assert.AreSame(OutsideCameraPolicy.Follow,s.Camera);
            }
        }
        [Test] public void 外界カメラ不正値と終了後の設定を拒否する()
        {
            const long u=60000;Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(-1));var p=OutsideCameraPolicy.Follow;
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,float.NaN));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(0,32*u,0));Assert.Throws<System.ArgumentOutOfRangeException>(()=>p.ResolveHorizontalCenter(-1,32*u,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>OutsideCameraPolicy.FixedAt(33*u).ResolveHorizontalCenter(0,32*u,10));
            var s=new OutsideExplorationSession();Assert.Throws<System.ArgumentNullException>(()=>s.TryConfigureCamera(null));Assert.Throws<System.ArgumentOutOfRangeException>(()=>s.TryConfigureCamera(OutsideCameraPolicy.FixedAt(33*u)));s.Dispose();Assert.Throws<System.ObjectDisposedException>(()=>s.TryConfigureCamera(p));
        }
        [TestCase(1)] [TestCase(7)] [TestCase(120)]
        public void 外界カードゲージは描画分割に依存せず移動ジャンプ中も六十Hzで回復する(int batch)
        {
            var core=new OutsideExplorationCore(OutsideMovementDefinition.ValidationTerrain);Assert.IsTrue(core.CardGauge.TrySpend(100));
            for(int i=0;i<120;i+=batch)for(int j=i;j<System.Math.Min(i+batch,120);j++)core.TickFrame(j<60?1:-1,j==0,j<24);
            Assert.AreEqual(20*OutsideCardGauge.UnitsPerPoint,core.CardGauge.CurrentUnits);Assert.AreEqual(120,core.Frame);
        }
        [Test] public void 外界カードゲージ不足と不正消費は残量を変えず回復に待ち時間を設けない()
        {
            var g=new OutsideCardGauge(100,10,20);Assert.IsFalse(g.TrySpend(21));Assert.AreEqual(1200,g.CurrentUnits);Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.TrySpend(-1));Assert.IsTrue(g.TrySpend(20));g.AdvanceFrame();Assert.AreEqual(10,g.CurrentUnits);Assert.IsTrue(g.TrySpend(0));Assert.AreEqual(10,g.CurrentUnits);
        }
        [Test] public void 外界カードゲージ最大量変更は残量を維持し超過だけ切り捨てる()
        {
            var g=new OutsideCardGauge(100,10,80);g.AdvanceFrame();long amount=g.CurrentUnits;g.Configure(140,20);Assert.AreEqual(amount,g.CurrentUnits);g.Configure(50,0);Assert.AreEqual(3000,g.CurrentUnits);Assert.IsTrue(g.TrySpend(10));for(int i=0;i<120;i++)g.AdvanceFrame();Assert.AreEqual(2400,g.CurrentUnits);g.RestoreFull();Assert.AreEqual(3000,g.CurrentUnits);
        }
        [Test] public void 外界カードゲージは無効設定を原子的に拒否し表現上限でも溢れない()
        {
            var g=new OutsideCardGauge(100,10,80);Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.Configure(0,10));Assert.Throws<System.ArgumentOutOfRangeException>(()=>g.Configure(200,-1));Assert.AreEqual(100,g.Maximum);Assert.AreEqual(10,g.RecoveryPerSecond);Assert.AreEqual(4800,g.CurrentUnits);Assert.Throws<System.ArgumentOutOfRangeException>(()=>new OutsideCardGauge(100,1,101));
            var large=new OutsideCardGauge(int.MaxValue,int.MaxValue,int.MaxValue);Assert.IsTrue(large.TrySpend(int.MaxValue));for(int i=0;i<61;i++)large.AdvanceFrame();Assert.AreEqual(large.MaximumUnits,large.CurrentUnits);
        }
        [Test] public void 外界カードゲージはView世代と切断で初期化せずSessionごとに独立する()
        {
            using(var a=new OutsideExplorationSession())using(var b=new OutsideExplorationSession()){a.Core.CardGauge.TrySpend(20);long amount=a.Core.CardGauge.CurrentUnits;var first=a.AttachPresentation();var second=a.AttachPresentation();first.Dispose();Assert.IsTrue(second.IsCurrent);second.Dispose();Assert.AreEqual(amount,a.Core.CardGauge.CurrentUnits);Assert.AreEqual(6000,b.Core.CardGauge.CurrentUnits);}
        }
        [TestCase(1,0,"0.9 / 1","1.0 / 1")] [TestCase(100,99,"99.9 / 100","100.0 / 100")]
        public void 外界ゲージ表示は整数コスト直前と満量直前に消費可能量を過大表示しない(int maximum,int initial,string before,string full)
        {
            var gauge=new OutsideCardGauge(maximum,1,initial);for(int i=0;i<59;i++)gauge.AdvanceFrame();long units=gauge.CurrentUnits;
            StringAssert.EndsWith(before,Olca.Presentation.Exploration.OutsideExplorationView.FormatCardGauge(gauge));Assert.AreEqual(units,gauge.CurrentUnits);Assert.IsFalse(gauge.TrySpend(maximum));
            gauge.AdvanceFrame();StringAssert.EndsWith(full,Olca.Presentation.Exploration.OutsideExplorationView.FormatCardGauge(gauge));Assert.IsTrue(gauge.TrySpend(maximum));Assert.AreEqual(0,gauge.CurrentUnits);
        }
        [Test] public void 外界の回復ポイントは二十枚全体を再装填し左端へ戻すが再表示だけでは変えない()
        {
            var source=CardFixture();using(var session=new OutsideExplorationSession()){
                session.TryInitializePlayer(()=>new Olca.Core.Domain.PlayerCharacter(100));Assert.IsTrue(session.TryInitializeCards(source,Owned(source),new Olca.Tests.Mocks.MockRandomProvider(),out _));var cards=session.Cards;cards.TrySelect(2);long revision=cards.HandRevision;var old=cards.Hand;session.RecoverOutside(OutsideTrialRoute.RecoveryPointId);Assert.AreSame(cards,session.Cards);Assert.AreEqual(revision+1,cards.HandRevision);Assert.AreEqual(0,cards.SelectedIndex);Assert.AreEqual(20,cards.TotalCount);Assert.AreEqual(17,cards.RemainingCount);Assert.AreEqual(3,cards.Hand.Count);Assert.AreNotSame(old,cards.Hand);using(var lease=session.AttachPresentation()){}Assert.AreEqual(revision+1,cards.HandRevision);
            }
        }
    }
}
