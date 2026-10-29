using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Olca.Core.Domain;
using Olca.Core.Logic.Battle;
using Olca.Presentation.CustomScreen;
using Olca.Presentation.Field;
using Olca.Presentation.Dungeon;
using Olca.Tests.Mocks;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Tests
{
    public class CustomScreenCostViewTests
    {
        private static void RequireInside(RectTransform viewport,RectTransform control)
        {
            var corners=new Vector3[4];control.GetWorldCorners(corners);
            foreach(var point in corners){var local=viewport.InverseTransformPoint(point);Assert.That(local.x,Is.InRange(viewport.rect.xMin-.1f,viewport.rect.xMax+.1f),control.name);Assert.That(local.y,Is.InRange(viewport.rect.yMin-.1f,viewport.rect.yMax+.1f),control.name);}
        }
        [TestCase(1920f,1080f,false)][TestCase(1662.77f,1247.08f,false)][TestCase(2199.64f,942.70f,false)]
        [TestCase(1920f,1080f,true)][TestCase(1662.77f,1247.08f,true)][TestCase(2199.64f,942.70f,true)]
        public void 詳細を開いてリサイズしても見出しと戻るが欠けず帰還と編成へ復帰する(float width,float height,bool departure)
        {
            var root=new GameObject("DetailsViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var host=new GameObject("DetailsView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{});var card=new Card("details","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,"テスト");var owned=new OwnedCardCount{Id=card.Id,Name=card.Name,Count=1};var profile=new ClearReturnProfile{Owned=new[]{owned}};view.ConfigureMenuNavigation(()=>new Button[0],_=>card);
                for(int repeat=0;repeat<2;repeat++){
                    if(departure)view.RenderOwnedDeparture(profile,new[]{card},new[]{card},null,true,(_,__)=>{},()=>{},_=>{},()=>{},false);else view.Render(profile,new ClearReturnData{Stage=ClearReturnStage.Returned},false,null);
                    host.GetComponentsInChildren<Button>().Single(b=>b.name==(departure?"DepartureDetails_0":"OwnedCardDetails_0")).onClick.Invoke();Assert.IsTrue(view.HasMenuModal);
                    viewport.sizeDelta=new Vector2(width,height);typeof(ClearReturnView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Canvas.ForceUpdateCanvases();
                    var title=host.GetComponentsInChildren<Text>().Single(t=>t.name=="ReadonlyCardTitle");var close=host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseReadonlyCardDetails");RequireInside(viewport,title.rectTransform);RequireInside(viewport,(RectTransform)close.transform);Assert.AreSame(font,title.font);
                    var scroll=host.GetComponentsInChildren<ScrollRect>().Single();Assert.IsFalse(scroll.horizontal);Assert.IsTrue(scroll.vertical);Assert.AreEqual(ScrollRect.MovementType.Clamped,scroll.movementType);Assert.Greater(title.transform.position.y-title.rectTransform.rect.height/2,scroll.viewport.position.y+scroll.viewport.rect.height/2);Assert.Greater(scroll.viewport.position.y-scroll.viewport.rect.height/2,close.transform.position.y+((RectTransform)close.transform).rect.height/2);
                    if(width==1920){Assert.AreEqual(450,viewport.InverseTransformPoint(title.transform.position).y,.1);Assert.AreEqual(-440,viewport.InverseTransformPoint(close.transform.position).y,.1);}
                    close.onClick.Invoke();Assert.IsFalse(view.HasMenuModal);Assert.AreEqual(1,profile.Owned.Single().Count);Assert.IsTrue(host.transform.Find(departure?"DepartureContents":"ReturnContents").gameObject.activeSelf);
                }
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 長い詳細は開いたまま幅を狭めても全文の高さとスクロール位置を更新する()
        {
            var root=new GameObject("LongDetailsViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var host=new GameObject("LongDetailsView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
                var card=new Card("long-details","テスト",1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,string.Concat(Enumerable.Repeat("LONG DETAILS WITH MULTIPLE EFFECTS AND TUNING ",200)));view.Render(new ClearReturnProfile(),new ClearReturnData{Stage=ClearReturnStage.Returned},false,null);typeof(ClearReturnView).GetMethod("OpenCardDetails",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[]{card,null});Canvas.ForceUpdateCanvases();
                var scroll=host.GetComponentInChildren<ScrollRect>();var body=scroll.content.GetComponent<Text>();float original=scroll.content.rect.height;Assert.Greater(original,scroll.viewport.rect.height);scroll.verticalNormalizedPosition=.25f;var update=typeof(ClearReturnView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);update.Invoke(view,null);float position=scroll.verticalNormalizedPosition;
                viewport.sizeDelta=new Vector2(1662.77f,1247.08f);update.Invoke(view,null);Canvas.ForceUpdateCanvases();Assert.Greater(body.preferredHeight,original);Assert.GreaterOrEqual(scroll.content.rect.height,body.preferredHeight);Assert.AreEqual(position,scroll.verticalNormalizedPosition,.01f);
                scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,scroll.verticalNormalizedPosition,.01f);Assert.GreaterOrEqual(scroll.content.rect.height,body.preferredHeight);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,0,false)] [TestCase(1662.77f,1247.08f,0,false)] [TestCase(2199.64f,942.70f,0,false)]
        [TestCase(1920f,1080f,0,true)] [TestCase(1662.77f,1247.08f,0,true)] [TestCase(2199.64f,942.70f,0,true)]
        [TestCase(1920f,1080f,1,false)] [TestCase(1662.77f,1247.08f,1,false)] [TestCase(2199.64f,942.70f,1,false)]
        [TestCase(1920f,1080f,1,true)] [TestCase(1662.77f,1247.08f,1,true)] [TestCase(2199.64f,942.70f,1,true)]
        [TestCase(1920f,1080f,2,false)] [TestCase(1662.77f,1247.08f,2,false)] [TestCase(2199.64f,942.70f,2,false)]
        [TestCase(1920f,1080f,2,true)] [TestCase(1662.77f,1247.08f,2,true)] [TestCase(2199.64f,942.70f,2,true)]
        public void 出撃先スタイル交換は保存待ちも含め端切れせず取消とページ操作を維持する(float width,float height,int mode,bool blocked)
        {
            var root=new GameObject("SelectionViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("SelectionView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{});int cancels=0,choices=0;var names=Enumerable.Range(0,7).Select(n=>"テスト "+n).ToArray();var unlocked=Enumerable.Range(0,7).Select(n=>n<6).ToArray();var profile=new ClearReturnProfile{Tokens=100,Revision=7,ExchangeStock=new ExchangeStockData{RoundId="round",Offers=Enumerable.Range(0,7).Select(n=>new ExchangeOfferData{Id="offer"+n,CardId="card"+n,Name="テスト "+n,Price=3,OverflowTokens=1,Limit=ExchangeLimit.Unlimited}).ToArray()}};
                void Render(){if(mode==0)view.RenderDungeonSelection(names,unlocked,0,_=>choices++,()=>{},()=>cancels++,blocked,blocked?"ホゾンをカクニンしてください。":null,()=>{},()=>{},"キホン",()=>{},new bool[7],Enumerable.Repeat("カイホウをカクニン",7).ToArray());else if(mode==1)view.RenderStyleSelection(names,unlocked,0,_=>choices++,()=>{},()=>cancels++,blocked,blocked?"ホゾンをカクニンしてください。":null,()=>{});else view.RenderTokenExchange(profile,"offer0",blocked,blocked?"ホゾンをカクニンしてください。":null,_=>choices++,()=>{},()=>cancels++,()=>{},()=>{});}
                string prefix=mode==0?"Dungeon":mode==1?"Style":"Exchange";
                for(int repeat=0;repeat<2;repeat++){
                    Render();foreach(var button in host.GetComponentsInChildren<Button>())RequireInside(viewport,(RectTransform)button.transform);RequireSeparateButtons(host);
                    string titleName=mode==0?"DungeonSelectionTitle":mode==1?"StyleSelectionTitle":"ExchangeTitle";var title=host.GetComponentsInChildren<Text>().Single(t=>t.name==titleName);RequireInside(viewport,title.rectTransform);Assert.AreSame(font,title.font);if(width==1920)Assert.AreEqual(Vector3.one,host.transform.GetChild(0).localScale);
                    string cancelName=mode==0?"CancelDungeonSelection":mode==1?"CancelStyleSelection":"CancelTokenExchange";var cancel=host.GetComponentsInChildren<Button>().Single(t=>t.name==cancelName);Assert.AreEqual(!blocked,cancel.interactable);
                    if(!blocked){var choose=host.GetComponentsInChildren<Button>().Single(t=>t.name==(mode==2?"ExchangeOffer_offer1":prefix+"Choice_1"));choose.onClick.Invoke();cancel.onClick.Invoke();host.GetComponentsInChildren<Button>().Single(t=>t.name=="Next"+prefix+"Page").onClick.Invoke();Assert.IsTrue(host.GetComponentsInChildren<Button>().Any(t=>t.name==(mode==2?"ExchangeOffer_offer6":prefix+"Choice_6")));if(mode==2)Assert.AreEqual("2/2",host.GetComponentsInChildren<Text>().Single(t=>t.name=="ExchangePage").text);host.GetComponentsInChildren<Button>().Single(t=>t.name=="Previous"+prefix+"Page").onClick.Invoke();}
                    if(mode==2){var counter=host.GetComponentsInChildren<Text>().Single(t=>t.name=="ExchangePage");Assert.AreEqual("1/2",counter.text);RequireInside(viewport,counter.rectTransform);Assert.IsFalse(counter.raycastTarget);}
                    Assert.AreEqual(100,profile.Tokens);Assert.AreEqual(7,profile.Revision);Assert.IsTrue(profile.ExchangeStock.Offers.All(o=>o.Purchased==0));
                }
                Assert.AreEqual(blocked?0:2,cancels);Assert.AreEqual(blocked?0:2,choices);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,0)] [TestCase(1662.77f,1247.08f,0)] [TestCase(2199.64f,942.70f,0)]
        [TestCase(1920f,1080f,1)] [TestCase(1662.77f,1247.08f,1)] [TestCase(2199.64f,942.70f,1)]
        [TestCase(1920f,1080f,2)] [TestCase(1662.77f,1247.08f,2)] [TestCase(2199.64f,942.70f,2)]
        public void 長い一覧名はJikiTouraiで全文を保ち枠内余白と元の入力領域を維持する(float width,float height,int mode)
        {
            var root=new GameObject("LongChoiceCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("LongChoiceView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();
                var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");Assert.IsNotNull(font);view.Initialize(font,_=>{},()=>{},()=>{},()=>{});
                string name=string.Concat(Enumerable.Repeat("ながいテストめいしょう",mode==2?15:8));int chosen=-1;
                if(mode==0)view.RenderDungeonSelection(new[]{name,name},new[]{true,true},0,i=>chosen=i,()=>{},()=>{},false,null,()=>{},cleared:new[]{false,false});
                else if(mode==1)view.RenderStyleSelection(new[]{name+" / None",name+" / None"},new[]{true,true},0,i=>chosen=i,()=>{},()=>{},false,null,()=>{});
                else view.RenderTokenExchange(new ClearReturnProfile{Tokens=100,ExchangeStock=new ExchangeStockData{Offers=new[]{new ExchangeOfferData{Id="long",CardId="fixture",Name=name,Price=3,OverflowTokens=1,Limit=ExchangeLimit.Unlimited}}}},null,false,null,_=>chosen=0,()=>{},()=>{},()=>{},()=>{});
                Canvas.ForceUpdateCanvases();var button=host.GetComponentsInChildren<Button>().First(b=>b.name.StartsWith(mode==0?"DungeonChoice_":mode==1?"StyleChoice_":"ExchangeOffer_"));var label=button.GetComponentInChildren<Text>();var rect=(RectTransform)button.transform;
                Assert.AreEqual(mode==2?1730:1450,rect.rect.width);Assert.AreEqual(mode==2?76:78,rect.rect.height);RequireInside(viewport,rect);RequireInside(rect,label.rectTransform);
                Assert.Less(label.rectTransform.rect.width,rect.rect.width);Assert.Less(label.rectTransform.rect.height,rect.rect.height);Assert.AreSame(font,label.font);Assert.IsFalse(label.raycastTarget);
                StringAssert.Contains(name,label.text);Assert.AreEqual(label.text.Length,label.cachedTextGenerator.characterCountVisible,"内側余白で文字を欠落させない");
                button.onClick.Invoke();Assert.GreaterOrEqual(chosen,0);RequireSeparateButtons(host);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,0)] [TestCase(1662.77f,1247.08f,0)] [TestCase(2199.64f,942.70f,0)]
        [TestCase(1920f,1080f,1)] [TestCase(1662.77f,1247.08f,1)] [TestCase(2199.64f,942.70f,1)]
        [TestCase(1920f,1080f,2)] [TestCase(1662.77f,1247.08f,2)] [TestCase(2199.64f,942.70f,2)]
        [TestCase(1920f,1080f,3)] [TestCase(1662.77f,1247.08f,3)] [TestCase(2199.64f,942.70f,3)]
        [TestCase(1920f,1080f,4)] [TestCase(1662.77f,1247.08f,4)] [TestCase(2199.64f,942.70f,4)]
        [TestCase(1920f,1080f,5)] [TestCase(1662.77f,1247.08f,5)] [TestCase(2199.64f,942.70f,5)]
        public void 長い説明条件エラーは保存や選択をせず全文を読み再表示後の古い操作を拒否する(float width,float height,int mode)
        {
            var root=new GameObject("OverflowTextCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var host=new GameObject("OverflowTextView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();
                var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{});
                string condition=string.Concat(Enumerable.Repeat("ながいダンジョンをクリア / ",35))+"サイゴのジョウケン";
                string error=string.Join("\n",Enumerable.Range(0,40).Select(n=>"ホゾンにシッパイしました。ケンショウヨウのセツメイ "+n))+"\nサイゴのエラー";int calls=0;System.Action no=()=>calls++;
                void Render(){if(mode==2||mode==5)view.RenderStyleSelection(new[]{"キホン",condition},new[]{true,false},0,_=>no(),no,no,mode==2,mode==2?error:null,no);else if(mode==3)view.RenderTokenExchange(new ClearReturnProfile(),null,true,error,_=>no(),no,no,no,no);else view.RenderDungeonSelection(new[]{"テスト","テスト"},new[]{true,false},0,_=>no(),no,no,mode==1,mode==1?error:null,no,styleName:mode==0?condition:"キホン",unlockConditions:new[]{"",condition});}
                Render();string name=mode==0||mode==1?"DungeonSelectionHelpDetails":mode==2?"StyleSelectionHelpDetails":mode==3?"ExchangeBalanceDetails":mode==4?"DungeonChoice_1Details":"StyleChoice_1Details";
                var trigger=host.GetComponentsInChildren<Button>().Single(b=>b.name==name);RequireInside(viewport,(RectTransform)trigger.transform);Assert.IsTrue(trigger.interactable);
                if(mode>=4)Assert.IsFalse(trigger.transform.parent.GetComponent<Button>().interactable);
                var staleOpen=trigger.onClick;trigger.onClick.Invoke();Assert.IsTrue(view.HasMenuModal);Assert.AreEqual(0,calls);
                var scroll=host.GetComponentInChildren<ScrollRect>();var body=scroll.content.GetComponent<Text>();Assert.AreSame(font,body.font);Assert.IsFalse(body.supportRichText);Assert.IsNull(body.GetComponent<CardAttackPropertyIcons>());
                StringAssert.Contains(mode>=1&&mode<=3?"サイゴのエラー":"サイゴのジョウケン",body.text);
                viewport.sizeDelta=new Vector2(width,height);typeof(ClearReturnView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Canvas.ForceUpdateCanvases();Assert.GreaterOrEqual(scroll.content.rect.height,body.preferredHeight);
                if(mode>=1&&mode<=3)Assert.Greater(scroll.content.rect.height,scroll.viewport.rect.height);
                scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,scroll.verticalNormalizedPosition,.01f);
                var close=host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseReadonlyCardDetails");RequireInside(viewport,(RectTransform)close.transform);var staleClose=close.onClick;close.onClick.Invoke();Assert.IsFalse(view.HasMenuModal);Assert.AreEqual(0,calls);
                trigger.onClick.Invoke();Assert.IsTrue(view.HasMenuModal);Render();Assert.IsFalse(view.HasMenuModal);Assert.DoesNotThrow(()=>staleClose.Invoke());Assert.DoesNotThrow(()=>staleOpen.Invoke());Assert.IsFalse(view.HasMenuModal);Assert.AreEqual(0,calls);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f)] [TestCase(1662.77f,1247.08f)] [TestCase(2199.64f,942.70f)]
        public void 一行に収まる長めの説明も右上の補助操作と重ならず全文を保つ(float width,float height)
        {
            var root=new GameObject("HelpOverlapCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;((RectTransform)root.transform).sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("HelpOverlapView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),_=>{},()=>{},()=>{},()=>{});
                view.RenderDungeonSelection(new[]{"テスト"},new[]{true},0,_=>{},()=>{},()=>{},false,null,()=>{},styleName:string.Concat(Enumerable.Repeat("ながい",20)));
                var auxiliary=new GameObject("ExistingAuxiliary",typeof(RectTransform));auxiliary.transform.SetParent(root.transform,false);var ar=(RectTransform)auxiliary.transform;ar.anchorMin=ar.anchorMax=Vector2.one;ar.anchoredPosition=new Vector2(-310,-160);ar.sizeDelta=new Vector2(300,60);Canvas.ForceUpdateCanvases();
                Rect Bounds(RectTransform rect){var c=new Vector3[4];rect.GetWorldCorners(c);return Rect.MinMaxRect(c.Min(x=>x.x),c.Min(x=>x.y),c.Max(x=>x.x),c.Max(x=>x.y));}
                var label=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DungeonSelectionHelp");Assert.IsFalse(Bounds(label.rectTransform).Overlaps(Bounds(ar)));Assert.AreEqual(label.text.Length,label.cachedTextGenerator.characterCountVisible);
                foreach(var button in host.GetComponentsInChildren<Button>().Where(b=>b.name.EndsWith("Details")))Assert.IsFalse(Bounds((RectTransform)button.transform).Overlaps(Bounds(ar)));
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 全文入口が消えた後は外部保存でなく画面の既定選択へ戻る()
        {
            var root=new GameObject("RemovedDetailsFocus",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;((RectTransform)root.transform).sizeDelta=new Vector2(1920,1080);GameObject events=null;
            if(UnityEngine.EventSystems.EventSystem.current==null)events=new GameObject("FocusEvents",typeof(UnityEngine.EventSystems.EventSystem));
            try{
                var host=new GameObject("RemovedDetailsView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
                var outside=new GameObject("ExternalSave",typeof(RectTransform),typeof(Button));outside.transform.SetParent(root.transform,false);((RectTransform)outside.transform).anchoredPosition=new Vector2(-700,450);view.ConfigureMenuNavigation(()=>new[]{outside.GetComponent<Button>()});int calls=0;
                view.RenderStyleSelection(new[]{"キホン"},new[]{true},0,_=>calls++,()=>calls++,()=>calls++,true,string.Join("\n",Enumerable.Repeat("ホゾンエラー",40)),()=>calls++);
                host.GetComponentsInChildren<Button>().Single(b=>b.name=="StyleSelectionHelpDetails").onClick.Invoke();host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseReadonlyCardDetails").onClick.Invoke();
                view.RenderStyleSelection(new[]{"キホン"},new[]{true},0,_=>calls++,()=>calls++,()=>calls++,false,null,()=>calls++);
                var menu=(Olca.Unity.MenuPadScope)typeof(ClearReturnView).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view);Assert.AreEqual("StyleChoice_0",menu.FocusName);Assert.AreEqual(0,calls);
            }finally{Object.DestroyImmediate(root);if(events!=null)Object.DestroyImmediate(events);}
        }
        [TestCase(1920f,1080f,false)] [TestCase(1662.77f,1247.08f,false)] [TestCase(2199.64f,942.70f,false)]
        [TestCase(1920f,1080f,true)] [TestCase(1662.77f,1247.08f,true)] [TestCase(2199.64f,942.70f,true)]
        public void デッキ削除確認は長い対象名と警告を分離し取消後の古い操作を拒否する(float width,float height,bool longName)
        {
            var root=new GameObject("DeleteNameCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("DeleteNameView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),_=>{},()=>{},()=>{},()=>{});
                string name=longName?string.Concat(Enumerable.Repeat("ながいなまえ",120))+"サイゴのナマエ":"テスト";var p=new ClearReturnProfile{SavedDecks=new[]{new SavedDepartureDeck{Id="target",Name=name}}};int deletes=0;System.Action no=()=>{};
                void Render()=>view.RenderSavedDeckMenu(p,null,false,no,_=>{},_=>{},_=>{},_=>{},no,no,deleting:"target",confirmDelete:()=>deletes++);
                Render();Canvas.ForceUpdateCanvases();var warning=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DeleteDeckWarning");Assert.AreEqual(warning.text.Length,warning.cachedTextGenerator.characterCountVisible);StringAssert.Contains("このデッキを",warning.text);RequireInside(viewport,warning.rectTransform);
                var body=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DeleteDeckName");Assert.AreEqual(name,body.text);Assert.IsFalse(body.supportRichText);var scroll=host.GetComponentInChildren<ScrollRect>();Assert.AreEqual(longName,scroll!=null);if(scroll!=null){Assert.GreaterOrEqual(scroll.content.rect.height,body.preferredHeight);scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,scroll.verticalNormalizedPosition,.01f);RequireInside(viewport,scroll.viewport);}
                var confirm=host.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck");var cancel=host.GetComponentsInChildren<Button>().Single(b=>b.name=="CancelDeleteDeck");RequireInside(viewport,(RectTransform)confirm.transform);RequireInside(viewport,(RectTransform)cancel.transform);var oldConfirm=confirm.onClick;var oldCancel=cancel.onClick;cancel.onClick.Invoke();Assert.AreEqual(0,deletes);Assert.AreEqual(name,p.SavedDecks.Single().Name);Assert.IsFalse(view.HasMenuModal);var current=host.transform.GetChild(0);oldConfirm.Invoke();oldCancel.Invoke();Assert.AreSame(current,host.transform.GetChild(0));Assert.AreEqual(0,deletes);
                Render();var pending=host.GetComponentsInChildren<Button>().Single(b=>b.name=="ConfirmDeleteDeck").onClick;view.RenderStyleSelection(new[]{"キホン"},new[]{true},0,_=>{},no,no,false,null,no);pending.Invoke();Assert.AreEqual(0,deletes);Assert.IsNull(host.GetComponentInChildren<ScrollRect>());
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,false,false)] [TestCase(1662.77f,1247.08f,false,false)] [TestCase(2199.64f,942.70f,false,false)]
        [TestCase(1920f,1080f,true,false)] [TestCase(1662.77f,1247.08f,true,false)] [TestCase(2199.64f,942.70f,true,false)]
        [TestCase(1920f,1080f,false,true)] [TestCase(1662.77f,1247.08f,false,true)] [TestCase(2199.64f,942.70f,false,true)]
        [TestCase(1920f,1080f,true,true)] [TestCase(1662.77f,1247.08f,true,true)] [TestCase(2199.64f,942.70f,true,true)]
        public void 保存デッキの長文エラーは比率変更後も全文を読め保存操作を起こさない(float width,float height,bool pending,bool empty)
        {
            var root=new GameObject("SavedErrorCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var host=new GameObject("SavedErrorView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{});
                var auxiliary=new System.Collections.Generic.List<Button>();
                foreach(bool leftSide in new[]{true,false}){var o=new GameObject(leftSide?"ExistingNewGame":"ExistingCollection",typeof(RectTransform),typeof(Button));o.transform.SetParent(root.transform,false);var ar=(RectTransform)o.transform;ar.anchorMin=ar.anchorMax=new Vector2(leftSide?0:1,1);ar.anchoredPosition=new Vector2(leftSide?580:-310,-85);ar.sizeDelta=new Vector2(leftSide?280:300,60);auxiliary.Add(o.GetComponent<Button>());}view.ConfigureMenuNavigation(()=>auxiliary);
                var p=new ClearReturnProfile{SavedDecks=empty?new SavedDepartureDeck[0]:new[]{new SavedDepartureDeck{Id="fixture",Name="テスト"}}};int calls=0;System.Action no=()=>calls++;
                string error=string.Join("\n",Enumerable.Repeat("ホゾンエラーのながいセツメイ",40))+"\nサイゴのエラー";
                void Render(string message)=>view.RenderSavedDeckMenu(p,message,pending,no,_=>no(),_=>no(),_=>no(),_=>no(),no,no,reload:no);
                Render(error);var trigger=host.GetComponentsInChildren<Button>().Single(b=>b.name=="SavedDeckErrorDetails");var staleOpen=trigger.onClick;trigger.onClick.Invoke();Assert.IsTrue(view.HasMenuModal);
                var scroll=host.GetComponentInChildren<ScrollRect>();var body=scroll.content.GetComponent<Text>();StringAssert.EndsWith("サイゴのエラー",body.text);Assert.AreSame(font,body.font);Assert.IsFalse(body.supportRichText);Assert.IsNull(body.GetComponent<CardAttackPropertyIcons>());
                viewport.sizeDelta=new Vector2(width,height);typeof(ClearReturnView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Canvas.ForceUpdateCanvases();Assert.GreaterOrEqual(scroll.content.rect.height,body.preferredHeight);scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,scroll.verticalNormalizedPosition,.01f);
                var close=host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseReadonlyCardDetails");RequireInside(viewport,(RectTransform)close.transform);var staleClose=close.onClick;close.onClick.Invoke();RequireInside(viewport,(RectTransform)trigger.transform);
                var title=host.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckTitle");RequireInside(viewport,title.rectTransform);Assert.AreEqual(title.text.Length,title.cachedTextGenerator.characterCountVisible);Assert.AreEqual(36,title.fontSize);Assert.IsFalse(title.resizeTextForBestFit);
                var titlePoints=title.cachedTextGenerator.verts.Select(v=>viewport.InverseTransformPoint(title.rectTransform.TransformPoint(v.position/title.pixelsPerUnit))).ToArray();
                foreach(var button in auxiliary){var corners=new Vector3[4];((RectTransform)button.transform).GetWorldCorners(corners);var points=corners.Select(viewport.InverseTransformPoint).ToArray();Assert.IsTrue(titlePoints.Max(v=>v.x)<points.Min(v=>v.x)||titlePoints.Min(v=>v.x)>points.Max(v=>v.x),"タイトルと既存補助操作の描画非重複");}
                var hint=host.GetComponentsInChildren<Text>().SingleOrDefault(t=>t.name=="SavedDeckEmpty");Assert.AreEqual(empty,hint!=null);if(hint!=null){StringAssert.Contains("まだありません",hint.text);Assert.AreEqual(hint.text.Length,hint.cachedTextGenerator.characterCountVisible);RequireInside(viewport,hint.rectTransform);}
                Render(null);Canvas.ForceUpdateCanvases();var help=host.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckHelp");Assert.AreEqual(help.text.Length,help.cachedTextGenerator.characterCountVisible);RequireInside(viewport,help.rectTransform);var drawnRight=help.cachedTextGenerator.verts.Max(v=>viewport.InverseTransformPoint(help.rectTransform.TransformPoint(v.position/help.pixelsPerUnit)).x);Assert.LessOrEqual(drawnRight,viewport.rect.xMax-400,"空白のRectでなく描画文字が右上操作を避ける");staleOpen.Invoke();staleClose.Invoke();Assert.IsFalse(view.HasMenuModal);Assert.IsFalse(host.GetComponentsInChildren<Button>().Any(b=>b.name=="SavedDeckErrorDetails"));Assert.AreEqual(0,calls);Assert.AreEqual(empty?0:1,p.SavedDecks.Length);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(.3333333f)] [TestCase(2f)]
        public void 保存デッキの説明は起動時倍率によらず比率変更後も補助操作へ重ならない(float scale)
        {
            var root=new GameObject("SavedHelpScaleCanvas",typeof(RectTransform),typeof(Canvas));var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;canvas.scaleFactor=scale;var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var host=new GameObject("SavedHelpScaleView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),_=>{},()=>{},()=>{},()=>{});
                view.RenderSavedDeckMenu(new ClearReturnProfile(),null,false,()=>{},_=>{},_=>{},_=>{},_=>{},()=>{},()=>{});
                viewport.sizeDelta=new Vector2(1662.77f,1247.08f);canvas.scaleFactor=.8660254f;typeof(ClearReturnView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Canvas.ForceUpdateCanvases();
                var help=host.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckHelp");Assert.AreEqual(help.text.Length,help.cachedTextGenerator.characterCountVisible);var drawnRight=help.cachedTextGenerator.verts.Max(v=>viewport.InverseTransformPoint(help.rectTransform.TransformPoint(v.position/help.pixelsPerUnit)).x);Assert.Less(drawnRight,viewport.rect.xMax-460,"右上装備操作の左端より手前へ描画する");Assert.IsFalse(host.GetComponentsInChildren<Button>().Any(b=>b.name=="SavedDeckHelpDetails"));
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 空の保存デッキは消えた行から新規へ戻り有効な操作と再試行の優先を保つ()
        {
            var root=new GameObject("SavedFocusCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;((RectTransform)root.transform).sizeDelta=new Vector2(1920,1080);GameObject events=null;if(UnityEngine.EventSystems.EventSystem.current==null){events=new GameObject("SavedEvents",typeof(UnityEngine.EventSystems.EventSystem));var es=events.GetComponent<UnityEngine.EventSystems.EventSystem>();typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnEnable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(es,null);UnityEngine.EventSystems.EventSystem.current=es;}
            try{
                var host=new GameObject("SavedFocusView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});int calls=0;System.Action no=()=>calls++;
                var outside=new GameObject("ExternalSavedAction",typeof(RectTransform),typeof(Button));outside.transform.SetParent(root.transform,false);view.ConfigureMenuNavigation(()=>new[]{outside.GetComponent<Button>()});
                void Render(bool empty,bool pending=false,string error=null)=>view.RenderSavedDeckMenu(new ClearReturnProfile{SavedDecks=empty?new SavedDepartureDeck[0]:new[]{new SavedDepartureDeck{Id="fixture",Name="テスト"}}},error,pending,no,_=>no(),_=>no(),_=>no(),_=>no(),no,no,reload:no);
                string Focus()=>((Olca.Unity.MenuPadScope)typeof(ClearReturnView).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(view)).FocusName;
                Render(false);host.GetComponentsInChildren<Button>().Single(b=>b.name=="EditDeck_fixture").Select();Render(true);Assert.AreEqual("NewSavedDeck",Focus());
                host.GetComponentsInChildren<Button>().Single(b=>b.name=="ReloadSavedDecks").Select();Assert.AreEqual("ReloadSavedDecks",UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject?.name,"再表示前の選択状態");Render(true);Assert.AreEqual("ReloadSavedDecks",Focus());outside.GetComponent<Button>().Select();Render(true);Assert.AreEqual("ExternalSavedAction",Focus());
                Render(true,true,string.Join("\n",Enumerable.Repeat("ホゾンエラー",40)));Assert.AreEqual("RetrySavedDeckSave",Focus());host.GetComponentsInChildren<Button>().Single(b=>b.name=="SavedDeckErrorDetails").onClick.Invoke();host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseReadonlyCardDetails").onClick.Invoke();Render(true);Assert.AreEqual("NewSavedDeck",Focus());Assert.AreEqual(0,calls);
            }finally{Object.DestroyImmediate(root);if(events!=null){typeof(UnityEngine.EventSystems.EventSystem).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(events.GetComponent<UnityEngine.EventSystems.EventSystem>(),null);Object.DestroyImmediate(events);}}
        }
        [TestCase(1920f,1080f,false)] [TestCase(1662.77f,1247.08f,false)] [TestCase(2199.64f,942.70f,false)]
        [TestCase(1920f,1080f,true)] [TestCase(1662.77f,1247.08f,true)] [TestCase(2199.64f,942.70f,true)]
        public void 長い保存デッキ名は業務操作せず全文を読み閉じた後の操作を拒否する(float width,float height,bool pending)
        {
            var root=new GameObject("SavedNameCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("SavedNameView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var v=host.AddComponent<ClearReturnView>();var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");v.Initialize(font,_=>{},()=>{},()=>{},()=>{});int calls=0;System.Action no=()=>calls++;var name=string.Concat(Enumerable.Repeat("ながいデッキのなまえ",50))+"サイゴのナマエ";var p=new ClearReturnProfile{SavedDecks=new[]{new SavedDepartureDeck{Id="long",Name=name},new SavedDepartureDeck{Id="short",Name="テスト"}}};
                void Render()=>v.RenderSavedDeckMenu(p,null,pending,no,_=>no(),_=>no(),_=>no(),_=>no(),no,no);
                Render();var trigger=host.GetComponentsInChildren<Button>().Single(b=>b.name=="SavedDeckName_longDetails");Assert.IsFalse(host.GetComponentsInChildren<Button>().Any(b=>b.name=="SavedDeckName_shortDetails"));RequireSeparateButtons(host);RequireInside(viewport,(RectTransform)trigger.transform);var staleOpen=trigger.onClick;trigger.onClick.Invoke();
                var scroll=host.GetComponentInChildren<ScrollRect>();var body=scroll.content.GetComponent<Text>();StringAssert.Contains(name,body.text);Assert.AreSame(font,body.font);Assert.IsFalse(body.supportRichText);Assert.GreaterOrEqual(scroll.content.rect.height,body.preferredHeight);scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,scroll.verticalNormalizedPosition,.01f);
                var close=host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseReadonlyCardDetails");var staleClose=close.onClick;close.onClick.Invoke();Render();staleOpen.Invoke();staleClose.Invoke();Assert.IsFalse(v.HasMenuModal);Assert.AreEqual(0,calls);Assert.AreEqual(name,p.SavedDecks[0].Name);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 保存デッキのページ端は反対のページ操作へ戻り古い操作を再実行しない()
        {
            var root=new GameObject("SavedPageCanvas",typeof(RectTransform));((RectTransform)root.transform).sizeDelta=new Vector2(1920,1080);
            try{
                var v=root.AddComponent<ClearReturnView>();v.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),_=>{},()=>{},()=>{},()=>{});int calls=0;System.Action no=()=>calls++;v.RenderSavedDeckMenu(new ClearReturnProfile{SavedDecks=Enumerable.Range(0,11).Select(i=>new SavedDepartureDeck{Id="d"+i,Name=i==0?string.Concat(Enumerable.Repeat("ながいなまえ",60)):"テスト "+i}).ToArray()},null,false,no,_=>no(),_=>no(),_=>no(),_=>no(),no,no);
                Button Find(string name)=>root.GetComponentsInChildren<Button>().Single(b=>b.name==name);string Focus()=>((Olca.Unity.MenuPadScope)typeof(ClearReturnView).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(v)).FocusName;string Page()=>root.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckPage").text;
                var modalPage=Find("NextSavedDeckPage").onClick;Find("SavedDeckName_d0Details").onClick.Invoke();modalPage.Invoke();Assert.IsTrue(v.HasMenuModal);Assert.AreEqual("1/3",Page());Find("CloseReadonlyCardDetails").onClick.Invoke();
                for(int repeat=0;repeat<2;repeat++){
                    var stale=Find("NextSavedDeckPage").onClick;stale.Invoke();Assert.AreEqual("2/3",Page());Assert.AreEqual("NextSavedDeckPage",Focus());stale.Invoke();Assert.AreEqual("2/3",Page());Find("NextSavedDeckPage").onClick.Invoke();Assert.AreEqual("3/3",Page());Assert.AreEqual("PreviousSavedDeckPage",Focus());Find("PreviousSavedDeckPage").onClick.Invoke();Assert.AreEqual("2/3",Page());Find("PreviousSavedDeckPage").onClick.Invoke();Assert.AreEqual("1/3",Page());Assert.AreEqual("NextSavedDeckPage",Focus());
                }
                Assert.AreEqual(0,calls);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,false)] [TestCase(1662.77f,1247.08f,false)] [TestCase(2199.64f,942.70f,false)]
        [TestCase(1920f,1080f,true)] [TestCase(1662.77f,1247.08f,true)] [TestCase(2199.64f,942.70f,true)]
        public void 長いデッキ名の入力は記号を保持し取消再表示後の古い操作を拒否する(float width,float height,bool pending)
        {
            var root=new GameObject("NameEditCanvas",typeof(RectTransform),typeof(Canvas));root.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("NameEditView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var v=host.AddComponent<ClearReturnView>();var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");v.Initialize(font,_=>{},()=>{},()=>{},()=>{});string original="<b>"+string.Concat(Enumerable.Repeat("ながいデッキのなまえ",25))+"サイゴ</b>",draft=original;int renamed=0,actions=0;System.Action no=()=>actions++;
                var extras=new System.Collections.Generic.List<Button>();foreach(bool left in new[]{true,false}){var o=new GameObject("Toolbar",typeof(RectTransform),typeof(Button));o.transform.SetParent(root.transform,false);var ar=(RectTransform)o.transform;ar.anchorMin=ar.anchorMax=new Vector2(left?0:1,1);ar.anchoredPosition=new Vector2(left?580:-310,-85);ar.sizeDelta=new Vector2(left?280:300,60);extras.Add(o.GetComponent<Button>());}v.ConfigureMenuNavigation(()=>extras);
                void Render(bool discard)=>v.RenderOwnedDeparture(new ClearReturnProfile(),new Card[0],new Card[0],null,true,(_,__)=>no(),no,_=>no(),no,discard,save:no,deckName:draft,rename:value=>{draft=value;renamed++;},pending:pending,retrySave:no);
                Render(false);Canvas.ForceUpdateCanvases();var input=host.GetComponentInChildren<InputField>();Assert.AreEqual(original,input.text);Assert.IsFalse(input.textComponent.supportRichText);Assert.AreSame(font,input.textComponent.font);Assert.IsNotNull(input.GetComponent<RectMask2D>());RequireInside(viewport,(RectTransform)input.transform);RequireInside((RectTransform)input.transform,input.textComponent.rectTransform);
                var title=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DepartureTitle");Assert.AreEqual(title.text.Length,title.cachedTextGenerator.characterCountVisible);RequireInside(viewport,title.rectTransform);
                var oldName=input.onValueChanged;var oldSave=host.GetComponentsInChildren<Button>().Single(b=>b.name=="SaveNamedDeck").onClick;oldName.Invoke("<b>ひらがなABC</b>");Assert.AreEqual(pending?original:"<b>ひらがなABC</b>",draft);Assert.AreEqual(pending?0:1,renamed);
                Render(true);var oldResume=host.GetComponentsInChildren<Button>().Single(b=>b.name=="KeepEditingDeparture").onClick;oldName.Invoke("フルイナマエ");oldSave.Invoke();Assert.AreEqual(pending?0:1,renamed);Assert.AreEqual(0,actions);
                draft=original;Render(false);oldName.Invoke("フルイナマエ");oldSave.Invoke();oldResume.Invoke();Assert.AreEqual(original,draft);Assert.AreEqual(original,host.GetComponentInChildren<InputField>().text);Assert.AreEqual(0,actions);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 名前変更callback未指定でも入力と破棄確認の寿命を保つ()
        {
            var root=new GameObject("NullableRename",typeof(RectTransform));((RectTransform)root.transform).sizeDelta=new Vector2(1920,1080);
            try{var v=root.AddComponent<ClearReturnView>();v.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});int calls=0;System.Action no=()=>calls++;void Render(bool discard)=>v.RenderOwnedDeparture(new ClearReturnProfile(),new Card[0],new Card[0],null,true,(_,__)=>{},no,_=>no(),no,discard,save:no,deckName:"テスト");Render(false);Assert.DoesNotThrow(()=>root.GetComponentInChildren<InputField>().text="なまえ");Render(true);var stale=root.GetComponentsInChildren<Button>().Single(b=>b.name=="DiscardDepartureDraft").onClick;Render(false);stale.Invoke();Assert.AreEqual(0,calls);}finally{Object.DestroyImmediate(root);}
        }
        private static void RequireSeparateButtons(GameObject host)
        {
            var buttons=host.GetComponentsInChildren<Button>();
            Rect Area(RectTransform r){var points=new Vector3[4];r.GetWorldCorners(points);return Rect.MinMaxRect(points.Min(p=>p.x),points.Min(p=>p.y),points.Max(p=>p.x),points.Max(p=>p.y));}
            for(int i=0;i<buttons.Length;i++)for(int j=i+1;j<buttons.Length;j++)Assert.IsFalse(Area((RectTransform)buttons[i].transform).Overlaps(Area((RectTransform)buttons[j].transform)),buttons[i].name+" / "+buttons[j].name);
        }
        [TestCase(1920f,1080f,0)] [TestCase(1662.77f,1247.08f,0)] [TestCase(2199.64f,942.70f,0)]
        [TestCase(1920f,1080f,1)] [TestCase(1662.77f,1247.08f,1)] [TestCase(2199.64f,942.70f,1)]
        [TestCase(1920f,1080f,2)] [TestCase(1662.77f,1247.08f,2)] [TestCase(2199.64f,942.70f,2)]
        [TestCase(1920f,1080f,3)] [TestCase(1662.77f,1247.08f,3)] [TestCase(2199.64f,942.70f,3)]
        [TestCase(1920f,1080f,4)] [TestCase(1662.77f,1247.08f,4)] [TestCase(2199.64f,942.70f,4)]
        public void 帰還の各段階は基準配置を保ち幅と高さの境界へ等比で収まる(float width,float height,int mode)
        {
            var root=new GameObject("ReturnViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("ReturnView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{},()=>{});
                var profile=new ClearReturnProfile{Owned=Enumerable.Range(0,8).Select(n=>new OwnedCardCount{Id="card"+n,Name="テスト",Count=4}).ToArray()};var flow=new ClearReturnData{RunId="layout",Stage=mode==1?ClearReturnStage.Choosing:mode==2?ClearReturnStage.Awarded:ClearReturnStage.Returned,Candidates=Enumerable.Range(0,8).Select(n=>new ReturnCardData{Id="card"+n,Name="テスト"}).ToArray(),Selected=new[]{0,1,2}};
                for(int repeat=0;repeat<2;repeat++){
                    view.Render(profile,flow,mode==4,mode==4?"ホゾンをカクニンしてください。":null,mode==3);
                    foreach(var button in host.GetComponentsInChildren<Button>())RequireInside(viewport,(RectTransform)button.transform);RequireSeparateButtons(host);
                    var title=host.GetComponentsInChildren<Text>().Single(t=>t.name=="Title");RequireInside(viewport,title.rectTransform);Assert.AreSame(font,title.font);
                    var contents=host.transform.Find("ReturnContents");Assert.AreEqual(contents.localScale.x,contents.localScale.y);Assert.AreEqual(1,contents.localScale.z);if(width==1920){Assert.AreEqual(Vector3.one,contents.localScale);Assert.AreEqual(455,viewport.InverseTransformPoint(title.transform.position).y,.1);}
                }
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,0)] [TestCase(1662.77f,1247.08f,0)] [TestCase(2199.64f,942.70f,0)]
        [TestCase(1920f,1080f,1)] [TestCase(1662.77f,1247.08f,1)] [TestCase(2199.64f,942.70f,1)]
        [TestCase(1920f,1080f,2)] [TestCase(1662.77f,1247.08f,2)] [TestCase(2199.64f,942.70f,2)]
        public void 編成は確認と保存待ちを含め操作部が欠けず名前欄と説明が重ならない(float width,float height,int mode)
        {
            var root=new GameObject("DepartureViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("DepartureView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{});int closed=0;
                var profile=new ClearReturnProfile{Owned=Enumerable.Range(0,7).Select(n=>new OwnedCardCount{Id="card"+n,Name="テスト",Count=4}).ToArray()};var options=profile.Owned.Select(c=>new Card(c.Id,c.Name,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,"")).ToArray();
                for(int repeat=0;repeat<2;repeat++){
                    view.RenderOwnedDeparture(profile,options,options,null,true,(_,__)=>{},()=>{},_=>closed++,()=>{},mode==2,save:()=>{},deckName:"ひらがなテスト",rename:_=>{},pending:mode==1,retrySave:()=>{});
                    foreach(var button in host.GetComponentsInChildren<Button>())RequireInside(viewport,(RectTransform)button.transform);RequireSeparateButtons(host);
                    var title=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DepartureTitle");RequireInside(viewport,title.rectTransform);Assert.AreSame(font,title.font);
                    if(mode!=2){var input=host.GetComponentInChildren<InputField>();var rules=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DepartureRules");var valid=host.GetComponentsInChildren<Text>().Single(t=>t.name=="DepartureValidation");float scale=host.transform.Find("DepartureContents").localScale.y;Assert.Greater(title.transform.position.y-title.rectTransform.rect.height*scale/2,input.transform.position.y+((RectTransform)input.transform).rect.height*scale/2);Assert.Greater(input.transform.position.y-((RectTransform)input.transform).rect.height*scale/2,rules.transform.position.y+rules.rectTransform.rect.height*scale/2);Assert.Greater(rules.transform.position.y-rules.rectTransform.rect.height*scale/2,valid.transform.position.y+valid.rectTransform.rect.height*scale/2);Assert.AreEqual("ひらがなテスト",input.text);Assert.AreEqual(mode!=1,input.interactable);}
                    if(mode==0)host.GetComponentsInChildren<Button>().Single(b=>b.name=="CloseOwnedDeparture").onClick.Invoke();
                    if(width==1920)Assert.AreEqual(Vector3.one,host.transform.Find("DepartureContents").localScale);
                }
                Assert.AreEqual(mode==0?2:0,closed);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 帰還編成の等比補正はリサイズと他画面への切替で持ち越さない()
        {
            var root=new GameObject("ResizeViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(1920,1080);
            try{
                var host=new GameObject("ResizeView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});var profile=new ClearReturnProfile();var flow=new ClearReturnData{Stage=ClearReturnStage.Returned};view.Render(profile,flow,false,null);
                var update=typeof(ClearReturnView).GetMethod("LateUpdate",BindingFlags.Instance|BindingFlags.NonPublic);
                viewport.sizeDelta=new Vector2(2199.64f,942.70f);update.Invoke(view,null);foreach(var b in host.GetComponentsInChildren<Button>())RequireInside(viewport,(RectTransform)b.transform);Assert.Less(host.transform.Find("ReturnContents").localScale.x,1);
                view.RenderSavedDeckMenu(profile,null,false,()=>{},_=>{},_=>{},_=>{},_=>{},()=>{},()=>{});update.Invoke(view,null);Assert.AreEqual(Vector3.one,host.transform.Find("SavedDeckContents").localScale);
                viewport.sizeDelta=new Vector2(1920,1080);view.Render(profile,flow,false,null);update.Invoke(view,null);Assert.AreEqual(Vector3.one,host.transform.Find("ReturnContents").localScale);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,false)][TestCase(1662.77f,1247.08f,false)][TestCase(2199.64f,942.70f,false)]
        [TestCase(1920f,1080f,true)][TestCase(1662.77f,1247.08f,true)][TestCase(2199.64f,942.70f,true)]
        public void 保存デッキ画面は再表示と保存待ちでも操作部と見出しが画面内に収まる(float width,float height,bool pending)
        {
            var root=new GameObject("SavedDeckViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try{
                var host=new GameObject("SavedDeckView",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");view.Initialize(font,_=>{},()=>{},()=>{},()=>{});
                var profile=new ClearReturnProfile{SavedDecks=Enumerable.Range(0,6).Select(i=>new SavedDepartureDeck{Id="deck"+i,Name="テスト"+i}).ToArray(),EquippedDeckId="deck0"};int closes=0;
                for(int repeat=0;repeat<2;repeat++){
                    view.RenderSavedDeckMenu(profile,repeat==0?null:"ホゾンをカクニンしてください。",pending,()=>{},_=>{},_=>{},_=>{},_=>{},()=>closes++,()=>{},reload:()=>{});Canvas.ForceUpdateCanvases();
                    foreach(var button in host.GetComponentsInChildren<Button>())RequireInside(viewport,(RectTransform)button.transform);
                    var title=host.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckTitle");RequireInside(viewport,title.rectTransform);Assert.AreSame(font,title.font);
                    var help=host.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckHelp");RequireInside(viewport,help.rectTransform);Assert.Less(help.rectTransform.position.y+help.rectTransform.rect.height/2,title.rectTransform.position.y-title.rectTransform.rect.height/2);var error=host.GetComponentsInChildren<Text>().Single(t=>t.name=="SavedDeckError");RequireInside(viewport,error.rectTransform);Assert.AreEqual(repeat==0,string.IsNullOrEmpty(error.text));Assert.AreEqual(repeat!=0,string.IsNullOrEmpty(help.text));

                    var close=host.GetComponentsInChildren<Button>().Single(t=>t.name=="CloseSavedDeckMenu");Assert.AreEqual(!pending,close.interactable);if(!pending)close.onClick.Invoke();
                    var footer=host.GetComponentsInChildren<Button>().Single(t=>t.name==(pending?"RetrySavedDeckSave":"ReloadSavedDecks"));if(width==1920)Assert.AreEqual(-493,viewport.InverseTransformPoint(footer.transform.position).y,.1);
                }
                Assert.AreEqual(pending?0:2,closes);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f,false)][TestCase(1662.77f,1247.08f,false)][TestCase(2199.64f,942.70f,false)]
        [TestCase(1920f,1080f,true)][TestCase(1662.77f,1247.08f,true)][TestCase(2199.64f,942.70f,true)]
        public void 店と削除画面は基準画面の余白を保ち四対三と横長でも操作部が切れない(float width,float height,bool removal)
        {
            var root=new GameObject("Viewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try
            {
                var host=new GameObject("Visit",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<ClearReturnView>();
                view.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),_=>{},()=>{},()=>{},()=>{});
                for(int repeat=0;repeat<2;repeat++)
                {
                    if(removal)view.RenderVisitRemoval("テスト",Enumerable.Repeat("カードをエラんでください",6).ToArray(),new bool[6],2,20,false,false,_=>{},()=>{},()=>{},()=>{});
                    else view.RenderDungeonVisit("テスト",Enumerable.Repeat("テストカード / 10 ツウカ",3).ToArray(),new[]{true,true,true},-1,false,false,_=>{},()=>{},()=>{},()=>{},()=>{});
                    Canvas.ForceUpdateCanvases();
                    var buttons=host.GetComponentsInChildren<Button>();
                    foreach(var button in buttons)RequireInside(viewport,(RectTransform)button.transform);
                    Rect Area(RectTransform rect){var corners=new Vector3[4];rect.GetWorldCorners(corners);return Rect.MinMaxRect(corners.Min(c=>c.x),corners.Min(c=>c.y),corners.Max(c=>c.x),corners.Max(c=>c.y));}
                    var help=host.GetComponentsInChildren<Text>().Single(t=>t.name==(removal?"RemovalHelp":"VisitHelp"));
                    foreach(var button in buttons)Assert.IsFalse(Area(help.rectTransform).Overlaps(Area((RectTransform)button.transform)),"説明と操作部: "+button.name);
                    for(int i=0;i<buttons.Length;i++)for(int j=i+1;j<buttons.Length;j++)Assert.IsFalse(Area((RectTransform)buttons[i].transform).Overlaps(Area((RectTransform)buttons[j].transform)),buttons[i].name+" / "+buttons[j].name);
                    if(width==1920){var pause=host.GetComponentsInChildren<Button>().Single(b=>b.name=="PauseVisit");var point=viewport.InverseTransformPoint(pause.transform.position);Assert.AreEqual(780,point.x,.1);Assert.AreEqual(480,point.y,.1);}
                }
            }
            finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1920f,1080f)][TestCase(1662.77f,1247.08f)][TestCase(2199.64f,942.70f)]
        public void 会話のポーズと既存操作は画面内に収まり基準画面の位置とフォントを保持する(float width,float height)
        {
            var root=new GameObject("DialogueViewport",typeof(RectTransform));var viewport=(RectTransform)root.transform;viewport.sizeDelta=new Vector2(width,height);
            try
            {
                var host=new GameObject("Dialogue",typeof(RectTransform));host.transform.SetParent(root.transform,false);var view=host.AddComponent<DialogueView>();var font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");int pauses=0;view.Initialize(font,()=>pauses++);
                view.Bind(new DialogueSession(new DialogueScript("layout",new[]{new DialogueLine("テスト")}),DialogueTextSpeed.Instant));Canvas.ForceUpdateCanvases();
                foreach(var button in host.GetComponentsInChildren<Button>())RequireInside(viewport,(RectTransform)button.transform);
                var pause=host.GetComponentsInChildren<Button>().Single(b=>b.name=="DialoguePause");pause.onClick.Invoke();Assert.AreEqual(1,pauses);Assert.AreSame(font,pause.GetComponentInChildren<Text>().font);
                if(width==1920){var point=viewport.InverseTransformPoint(pause.transform.position);Assert.AreEqual(790,point.x,.1);Assert.AreEqual(455,point.y,.1);}
            }
            finally{Object.DestroyImmediate(root);}
        }
        [TestCase(false)][TestCase(true)]
        public void 報酬の長い名前は元フォントと通常文字サイズを保ちながら名前欄へ収まる(bool longName)
        {
            var root=new GameObject("RewardName",typeof(RectTransform));((RectTransform)root.transform).sizeDelta=new Vector2(280,360);
            try
            {
                var view=root.AddComponent<CardSelectionItemView>();var obj=new GameObject("Name",typeof(RectTransform),typeof(Text));obj.transform.SetParent(root.transform,false);var text=obj.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=22;text.rectTransform.sizeDelta=new Vector2(280,40);var font=text.font;
                typeof(CardSelectionItemView).GetField("cardNameText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,text);
                string name=longName?string.Concat(Enumerable.Repeat("テストカード",8)):"ソード";
                for(int repeat=0;repeat<2;repeat++){view.Setup(new Card("layout",name,1,CardRarity.Normal,CardCategory.Attack,ElementType.None,CardSeries.Sword,1,"テスト"));text.cachedTextGenerator.Populate(text.text,text.GetGenerationSettings(text.rectTransform.rect.size));Assert.GreaterOrEqual(text.cachedTextGenerator.characterCountVisible,name.Length);Assert.AreEqual(252,text.rectTransform.rect.width,.1);RequireInside((RectTransform)root.transform,text.rectTransform);Assert.AreSame(font,text.font);Assert.AreEqual(name,text.text);Assert.AreEqual(22,text.resizeTextMaxSize);if(!longName)Assert.AreEqual(22,text.cachedTextGenerator.fontSizeUsedForBestFit);}
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 店の削除案内は現在枚数の漢字だけをカナにして助詞を保持する()
            =>Assert.AreEqual("カードサクジョ 2マイ（ゲンザイマイスウまで）",CustomDisplay.Katakana("カード削除 2枚（現在枚数まで）"));

        [TestCase("？？？　未入手のカードA 1/2", "？？？　ミニュウシュのカードA 1/2")]
        [TestCase("未公開レシピはありません", "ミコウカイレシピはありません")]
        public void 図鑑の未登録表示は漢字だけをカナにして元の文字を保持する(string original,string expected)
        {
            Assert.AreEqual(expected,CustomDisplay.Katakana(original));
            var card=Create("unknown",CardSeries.Sword);var profile=new ClearReturnProfile();
            Assert.AreEqual("？？？　ミニュウシュ",CustomDisplay.Katakana(CollectionDisplay.CardText(profile,card)));
            Assert.IsFalse(CollectionDisplay.IsKnown(profile,card));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void 再初期化は共通ボタンの濃色文字と既存フォント操作を保持する(bool visible)
        {
            var root=new GameObject("ReusedCustomScreen",typeof(RectTransform));
            try
            {
                var view=root.AddComponent<CustomScreenView>();
                var confirmObject=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));confirmObject.transform.SetParent(root.transform,false);
                typeof(CustomScreenView).GetField("confirmButton",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,confirmObject.GetComponent<Button>());
                var buttonObject=new GameObject("Inspection",typeof(RectTransform),typeof(Image),typeof(Button));
                buttonObject.transform.SetParent(root.transform,false);
                var labelObject=new GameObject("Label",typeof(RectTransform),typeof(Text));labelObject.transform.SetParent(buttonObject.transform,false);
                var label=labelObject.GetComponent<Text>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.text="ジブン";
                var button=buttonObject.GetComponent<Button>();int clicks=0;button.onClick.AddListener(()=>clicks++);UiLook.Button(button);
                var originalFont=label.font;buttonObject.SetActive(visible);
                var detailObject=new GameObject("Description",typeof(RectTransform),typeof(Text));detailObject.transform.SetParent(root.transform,false);
                var detail=detailObject.GetComponent<Text>();UiLook.Label(detail);
                for(int i=0;i<2;i++)
                {
                    view.Initialize(new CustomScreenSystem(new Deck(new List<Card>())));
                    Assert.AreEqual(UiLook.Ink,label.color,"淡い共通ボタン上の文字を白へ戻さない");
                    Assert.AreSame(originalFont,label.font);Assert.AreEqual("ジブン",label.text);
                    Assert.AreEqual(Color.white,detail.color,"暗い背景上の本文は従来の白文字を維持する");
                    Assert.AreEqual(visible,buttonObject.activeSelf);button.onClick.Invoke();
                }
                Assert.AreEqual(2,clicks);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void カード詳細は全補正と複数効果をカナ表示し無敵削り値や敵補正を公開しない()
        {
            var card=Create("テスト",CardSeries.Sword);card.Modifier.BonusDamage=-5;card.Modifier.CostAdjustment=2;card.Modifier.DamageMultiplier=1.5f;card.Modifier.ElementOverride=ElementType.Fire;
            foreach(var type in System.Enum.GetValues(typeof(AdditionalEffectType)).Cast<AdditionalEffectType>())card.Modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=type,Power=2,Duration=1.25f,HitCount=4,Trigger=DerivativeTrigger.OnDamage,PoisonIntervalFrames=30,TargetOffset=new Olca.Core.Domain.Vector2Int(-2,1),DelayTime=.5f,CancelOnHole=true});
            var before=card.GetFinalPower();var text=CustomDisplay.CardDetails(card);StringAssert.Contains("コスト: 5 / イリョク: "+before,text);StringAssert.Contains("コスト +2",text);StringAssert.Contains("イリョク -5",text);StringAssert.Contains("バイリツ 1.5",text);StringAssert.Contains("ゾクセイ ヘンコウ ヒ",text);StringAssert.Contains("タダンヒット",text);StringAssert.Contains("4 ヒット",text);StringAssert.Contains("チエン 0.5",text);StringAssert.Contains("イチ -2,1",text);StringAssert.Contains("30 フレーム",text);Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text,"[一-鿿]"));StringAssert.DoesNotContain("ムテキ",text);StringAssert.DoesNotContain("キョウジンド",text);StringAssert.DoesNotContain("ヨソク",text);StringAssert.DoesNotContain("Normal",text);StringAssert.Contains("ツウジョウ",text);Assert.AreEqual(before,card.GetFinalPower());
        }
        [Test]
        public void カード詳細は相殺する調律も現在段階ごとに残し表示で履歴を変更しない()
        {
            var target=Create("テスト",CardSeries.Sword);var table=new Dictionary<int,CardModifier>{{1,new CardModifier{BonusDamage=5}},{2,new CardModifier{BonusDamage=10}}};
            var first=Olca.Core.Logic.Battle.TuningEvaluator.Evaluate(target,new TuningGrantDefinition("a",new[]{new TuningEffectDefinition("a",2,table)},fixedCostAdjustment:1));Assert.IsTrue(first.IsSuccess);
            var second=Olca.Core.Logic.Battle.TuningEvaluator.Evaluate(first.ResultCard,new TuningGrantDefinition("b",new[]{new TuningEffectDefinition("b",1,new Dictionary<int,CardModifier>{{1,new CardModifier{BonusDamage=-10}}})},fixedCostAdjustment:-1));Assert.IsTrue(second.IsSuccess);
            string text=CustomDisplay.CardDetails(second.ResultCard);StringAssert.Contains("ダンカイ 2",text);StringAssert.Contains("イリョク +10",text);StringAssert.Contains("イリョク -10",text);StringAssert.Contains("コスト +1",text);StringAssert.Contains("コスト -1",text);Assert.AreEqual(2,second.ResultCard.TuningCount);
        }
        [TestCase(-100,false,1)] [TestCase(100,true,0)] [TestCase(0,false,3)]
        public void カード詳細の実効コストは最低値と専用ゼロ効果に従う(int change,bool zero,int expected)
        {var card=Create("テスト",CardSeries.Sword);card.Modifier.CostAdjustment=change;card.Modifier.ZeroCost=zero;StringAssert.Contains("コスト: "+expected,CustomDisplay.CardDetails(card));}
        [Test]
        public void カード詳細は数値の表現範囲外でも画面を壊さず明示し桁と小数は文化に依存しない()
        {
            var card=Create("テスト",CardSeries.Sword);card.Modifier.CostAdjustment=int.MaxValue;card.Modifier.DamageMultiplier=float.MaxValue;var old=System.Globalization.CultureInfo.CurrentCulture;
            try{System.Globalization.CultureInfo.CurrentCulture=new System.Globalization.CultureInfo("fr-FR");var text=CustomDisplay.CardDetails(card);StringAssert.Contains("コスト: ハンイガイ / イリョク: ハンイガイ",text);card.Modifier.DamageMultiplier=1.25f;StringAssert.Contains("バイリツ 1.25",CustomDisplay.CardDetails(card));}finally{System.Globalization.CultureInfo.CurrentCulture=old;}
        }
        [Test]
        public void 長い調律詳細は既存枠内でスクロールしfocus切替で先頭へ戻る()
        {
            var root=new GameObject("DetailOverflow",typeof(RectTransform),typeof(Canvas));
            try{
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;var view=root.AddComponent<CustomScreenView>();var obj=new GameObject("Description",typeof(RectTransform),typeof(Text));obj.transform.SetParent(root.transform,false);var text=obj.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=28;text.rectTransform.sizeDelta=new Vector2(300,100);
                typeof(CustomScreenView).GetField("selectedCardDescription",flags).SetValue(view,text);typeof(CustomScreenView).GetMethod("EnsureCardDescriptionScroll",flags).Invoke(view,null);
                var a=Create("テストA",CardSeries.Sword);for(int i=0;i<50;i++)a.Modifier.AdditionalEffects.Add(new AdditionalEffect{EffectType=AdditionalEffectType.Poison,Power=i+1,Duration=2});var b=Create("テストB",CardSeries.Sword);typeof(CustomScreenView).GetField("inputHand",flags).SetValue(view,new List<Card>{a,b});
                typeof(CustomScreenView).GetMethod("UpdateCursorCardDisplay",flags).Invoke(view,new object[]{0});typeof(CustomScreenView).GetMethod("RefreshKana",flags).Invoke(view,null);var scroll=obj.GetComponentInParent<ScrollRect>();Assert.AreEqual(new Vector2(300,100),scroll.viewport.sizeDelta);Assert.Greater(text.rectTransform.rect.height,100);StringAssert.Contains("イリョク 50",text.text);Assert.IsFalse(text.resizeTextForBestFit);scroll.verticalNormalizedPosition=0;
                typeof(CustomScreenView).GetMethod("UpdateCursorCardDisplay",flags).Invoke(view,new object[]{1});typeof(CustomScreenView).GetMethod("RefreshKana",flags).Invoke(view,null);StringAssert.StartsWith("テストB",text.text);Assert.AreEqual(1,scroll.verticalNormalizedPosition,.01);StringAssert.DoesNotContain("イリョク 50",text.text);
            }finally{Object.DestroyImmediate(root);}
        }
        [TestCase(1f)] [TestCase(1.956f)] [TestCase(2f)]
        public void 長文末尾はCanvas倍率変更でも文字生成から欠落せず詳細の高さを再計算する(float scale)
        {
            var root=new GameObject("ScaledDetail",typeof(RectTransform),typeof(Canvas));
            try{var canvas=root.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.scaleFactor=scale;var area=new GameObject("Area",typeof(RectTransform),typeof(ScrollRect),typeof(RectMask2D));area.transform.SetParent(root.transform,false);var rect=(RectTransform)area.transform;rect.sizeDelta=new Vector2(380,270);var obj=new GameObject("Text",typeof(RectTransform),typeof(Text));obj.transform.SetParent(area.transform,false);var text=obj.GetComponent<Text>();text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");text.fontSize=28;text.rectTransform.anchorMin=new Vector2(0,1);text.rectTransform.anchorMax=Vector2.one;text.rectTransform.pivot=new Vector2(.5f,1);text.rectTransform.sizeDelta=Vector2.zero;text.text=string.Join("\n",Enumerable.Range(0,50).Select(i=>"テスト "+i+" / イリョク 123 / 1.25 ビョウ"))+"\nサイゴノコウカ";
                var scroll=area.GetComponent<ScrollRect>();scroll.viewport=rect;scroll.content=text.rectTransform;typeof(CustomScreenView).GetMethod("FitDetailContent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{text,scroll});Canvas.ForceUpdateCanvases();typeof(CustomScreenView).GetMethod("FitDetailContent",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{text,scroll});
                Assert.AreEqual(VerticalWrapMode.Overflow,text.verticalOverflow);Assert.GreaterOrEqual(text.rectTransform.rect.height,Mathf.Ceil(text.preferredHeight)+text.fontSize*2);scroll.verticalNormalizedPosition=0;Assert.AreEqual(0,scroll.verticalNormalizedPosition,.01);Assert.Greater(text.rectTransform.rect.height,rect.rect.height);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 敵詳細は初遭遇でも全耐性を読み技や行動説明を含めず状態を変更しない()
        {
            var enemy=new Enemy("unseen","テストテキ",80,ElementType.Fire,new Olca.Core.Domain.Vector2Int(3,0),resistances:new ElementResistances(fire:-50,water:100),statusResistances:new TimedStatusResistances(poison:25));
            enemy.SetBarrier(12);enemy.Guard.Set(true,false);enemy.ApplyStatus(new StatusEffect(StatusEffectType.Blind,2000));
            var before=enemy.GetStatus(StatusEffectType.Blind).RemainingFrames;var first=CustomDisplay.CombatantDetails(enemy);var second=CustomDisplay.CombatantDetails(enemy);
            Assert.AreEqual(first,second);StringAssert.Contains("ヒ -50",first);StringAssert.Contains("ミズ 100",first);StringAssert.Contains("ドク 25",first);StringAssert.Contains("モウモク",first);StringAssert.Contains("バリア HP 12",first);StringAssert.Contains("ガード",first);
            StringAssert.DoesNotContain("パターン",first);StringAssert.DoesNotContain("クールダウン",first);Assert.AreEqual(before,enemy.GetStatus(StatusEffectType.Blind).RemainingFrames);Assert.AreEqual(80,enemy.CurrentHp);
        }
        [Test]
        public void 自分詳細は変身中の実耐性と集中防御を表示し未確定毒の予約を確定しない()
        {
            var p=new PlayerCharacter(100);p.SetElementResistances(new ElementResistances(fire:25));p.BeginTransformationDefense(new ElementResistances(water:-75),ElementType.Fire);p.SetConcentrateBuff(2);p.SetBarrier(9);
            p.ApplyPoison("fixture",new StatusEffect(StatusEffectType.Poison,2000,3,poisonKind:"fixture",poisonIntervalFrames:30),30);
            Assert.IsEmpty(p.InspectStatuses());var text=CustomDisplay.CombatantDetails(p);Assert.IsEmpty(p.InspectStatuses(),"表示は保存Captureや毒の確定処理を呼ばない");
            StringAssert.Contains("ミズ -75",text);StringAssert.Contains("シュウチュウ",text);StringAssert.Contains("2 バイ",text);StringAssert.Contains("バリア HP 9",text);Assert.AreEqual(100,p.CurrentHp);Assert.AreEqual(2,p.NextCardMultiplier);
        }
        [Test]
        public void 状態詳細スナップショットは独立し複数毒と他状態を保持して読取専用で返る()
        {
            var p=new PlayerCharacter(100);var e=new Enemy("e","テキ",100,ElementType.None,new Olca.Core.Domain.Vector2Int(4,0));
            var b=new BattleSystem(p,new List<Enemy>{e},new Deck(new List<Card>()));
            foreach(var kind in new[]{"b","a"}){p.ApplyPoison(kind,new StatusEffect(StatusEffectType.Poison,2000,1,poisonKind:kind,poisonIntervalFrames:60),60);e.ApplyStatus(new StatusEffect(StatusEffectType.Poison,2000,1,poisonKind:kind,poisonIntervalFrames:60));}
            p.ApplyParalysis(120);e.ApplyStatus(new StatusEffect(StatusEffectType.Confusion,2000));b.TickFrame(16);
            var own=p.InspectStatuses();var other=e.InspectStatuses();Assert.AreEqual(3,own.Count);Assert.AreEqual(3,other.Count);Assert.AreEqual("a",own[0].Kind);Assert.AreEqual("b",own[1].Kind);
            int frames=own[0].RemainingFrames;long frame=b.LogicalFrame;Assert.Throws<System.NotSupportedException>(()=>((IList<StatusEffectSnapshot>)own).Clear());
            p.InspectStatuses();e.InspectStatuses();Assert.AreEqual(frame,b.LogicalFrame);Assert.AreEqual(frames,p.InspectStatuses()[0].RemainingFrames);
            b.TickFrame(16);Assert.AreEqual(frames,own[0].RemainingFrames);Assert.Less(p.InspectStatuses()[0].RemainingFrames,frames);b.Transformations.Dispose();
        }
        [Test]
        public void 非表示切替と同一フレームの論理確定は復帰後もカスタムを閉じない()
        {
            var root=new GameObject("VisibilityInputBoundary",typeof(RectTransform),typeof(Canvas));var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var b=new BattleSystem(new PlayerCharacter(100),new List<Enemy>{new Enemy("test","テスト",50,ElementType.None,new Olca.Core.Domain.Vector2Int(3,0))},new Deck(new List<Card>()));
            try {
                Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,b.CustomScreen);b.CustomScreen.Open(new Card[0],new MockRandomProvider());
                System.Action queued=null;view.ConfigureLogicalConfirmation((_,action)=>queued=action,()=>0L);
                typeof(CustomScreenView).GetMethod("OnConfirmClicked",flags).Invoke(view,null);Assert.IsNotNull(queued);

                view.ToggleCustomPresentation();Assert.IsTrue(view.IsCustomPresentationHidden);view.ToggleCustomPresentation();Assert.IsFalse(view.IsCustomPresentationHidden);
                queued();Assert.IsFalse(b.CustomScreen.IsConfirming,"切替前のポインター確定も持ち越さない");
                view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput{MenuConfirmButtonDown=true});
                Assert.IsTrue(b.CustomScreen.IsOpen,"表示復帰と同じ描画フレームの古い確定を通さない");Assert.IsFalse(b.CustomScreen.IsConfirming,"閉じる準備も開始しない");Assert.AreEqual(0,b.LogicalFrame);
            }finally{typeof(CustomScreenView).GetMethod("OnDestroy",flags).Invoke(view,null);Object.DestroyImmediate(root);b.Transformations.Dispose();}
        }

        [TestCase(false)] [TestCase(true)]
        public void 非表示中の敵巡回は同じマスでも自分を含まず停止を保持し解除で復元する(bool paused)
        {
            var root=new GameObject("CombatInspection",typeof(RectTransform),typeof(Canvas));var flags=BindingFlags.NonPublic|BindingFlags.Instance;
            var p=new PlayerCharacter(100);var e1=new Enemy("first","テキイチ",50,ElementType.Fire,new Olca.Core.Domain.Vector2Int(3,0));var e2=new Enemy("second","テキニ",70,ElementType.Water,new Olca.Core.Domain.Vector2Int(3,0));
            var b=new BattleSystem(p,new List<Enemy>{e1,e2},new Deck(new List<Card>()));CustomScreenView view=null;
            try {
                Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                view=root.AddComponent<CustomScreenView>();var obj=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));obj.transform.SetParent(root.transform,false);var label=new GameObject("Label",typeof(RectTransform),typeof(Text));label.transform.SetParent(obj.transform,false);label.GetComponent<Text>().font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");
                typeof(CustomScreenView).GetField("confirmButton",flags).SetValue(view,obj.GetComponent<Button>());typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,b.CustomScreen);
                b.CustomScreen.Open(new Card[0],new MockRandomProvider());view.BindCombatants(b);long frame=b.LogicalFrame;
                if(!paused){view.ToggleCustomPresentation();view.OpenCombatantDetails(1);var firstNext=root.GetComponentsInChildren<Button>().Single(x=>x.name=="NextInspection");Assert.IsTrue(firstNext.targetGraphic.enabled);Assert.IsTrue(firstNext.targetGraphic.raycastTarget);Assert.IsTrue(root.GetComponentsInChildren<Button>().Single(x=>x.name=="CloseCustomPadDetails").targetGraphic.enabled);view.ToggleCustomPresentation();}

                view.OpenCombatantDetails(0);var details=root.GetComponentsInChildren<Text>().Single(t=>t.name=="CustomPadDetailsText");StringAssert.Contains("ジブン",details.text);
                typeof(CustomScreenView).GetMethod("ApplyCustomPad",flags).Invoke(view,new object[]{Olca.Unity.CustomPadCommand.Right});StringAssert.Contains("ジブン",details.text,"自分を敵巡回へ混ぜない");
                view.OpenCombatantDetails(1);StringAssert.Contains("ジブン",details.text,"敵詳細は非表示モードだけ");
                typeof(CustomScreenView).GetMethod("ClosePadDetails",flags).Invoke(view,null);view.ToggleCustomPresentation();Assert.IsTrue(view.IsCustomPresentationHidden);Assert.IsFalse(obj.GetComponent<Image>().enabled);Assert.IsFalse(obj.GetComponent<Image>().raycastTarget);
                view.OpenCombatantDetails(1);StringAssert.Contains("テキイチ",details.text);
                if(paused){Olca.Unity.GameplayPause.SetPaused(true);view.OpenCombatantDetails(0);view.ToggleCustomPresentation();StringAssert.Contains("テキイチ",details.text);Assert.IsTrue(view.IsCustomPresentationHidden);Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();}
                typeof(CustomScreenView).GetMethod("ApplyCustomPad",flags).Invoke(view,new object[]{Olca.Unity.CustomPadCommand.Right});StringAssert.Contains("テキニ",details.text);
                root.GetComponentsInChildren<Button>().Single(x=>x.name=="NextInspection").onClick.Invoke();StringAssert.Contains("テキイチ",details.text);
                typeof(CustomScreenView).GetMethod("ApplyCustomPad",flags).Invoke(view,new object[]{Olca.Unity.CustomPadCommand.PageDown});StringAssert.Contains("ジブン",details.text);Assert.IsFalse(root.transform.Find("CustomPadDetails/NextInspection").gameObject.activeSelf);
                Assert.AreEqual(frame,b.LogicalFrame);Assert.IsTrue(b.CustomScreen.IsOpen);Assert.IsEmpty(b.CustomScreen.SelectedCards);
                view.BindCombatants(null);Assert.IsFalse(root.transform.Find("CustomPadDetails").gameObject.activeSelf);Assert.IsFalse(view.IsCustomPresentationHidden);Assert.IsTrue(obj.GetComponent<Image>().enabled);Assert.IsTrue(obj.GetComponent<Image>().raycastTarget);
            } finally {if(view!=null)typeof(CustomScreenView).GetMethod("OnDestroy",flags).Invoke(view,null);Object.DestroyImmediate(root);b.Transformations.Dispose();Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();}
        }

        [Test]
        public void 出撃前情報は全階層の実定義を読むだけで敵や地形を変えず独立した一覧を返す()
        {
            var cells=Enumerable.Range(0,8).Select(i=>new MapCell(i)).ToList();
            var fire=new Enemy("fire","テスト",100,ElementType.Fire,new Olca.Core.Domain.Vector2Int(3,0));
            cells[3]=new MapCell(3,fire);cells[4]=new MapCell(4,new Enemy("summon","テスト",10,ElementType.Earth,new Olca.Core.Domain.Vector2Int(4,0),true));
            var terrain=new[]{new ExplorationTerrainCell(0,PanelType.Grass),new ExplorationTerrainCell(1,PanelType.Grass),new ExplorationTerrainCell(2,PanelType.Grass),new ExplorationTerrainCell(5,PanelType.Ice)};
            var a=new DungeonConfig("a","テスト",8,cells,explorationTerrain:terrain);
            var bCells=Enumerable.Range(0,4).Select(i=>new MapCell(i)).ToList();bCells[3]=new MapCell(3,new Enemy("water","テスト",100,ElementType.Water,new Olca.Core.Domain.Vector2Int(3,0)));
            var b=new DungeonConfig("b","テスト",4,bCells,explorationTerrain:new[]{new ExplorationTerrainCell(0,PanelType.Magma),new ExplorationTerrainCell(1,PanelType.Magma),new ExplorationTerrainCell(2,PanelType.Ice),new ExplorationTerrainCell(3,PanelType.Ice)});
            var info=new DungeonDeparturePreview(new[]{a,b});
            CollectionAssert.AreEqual(new[]{ElementType.Fire,ElementType.Water},info.EnemyElements);
            CollectionAssert.AreEqual(new[]{PanelType.Normal,PanelType.Ice,PanelType.Grass,PanelType.Magma},info.TerrainTypes);
            Assert.AreEqual(100,fire.CurrentHp);CollectionAssert.AreEqual(terrain,a.ExplorationTerrain);
            a.MapCells[3]=new MapCell(3);CollectionAssert.AreEqual(new[]{ElementType.Fire,ElementType.Water},info.EnemyElements);
            Assert.Throws<System.NotSupportedException>(()=>((IList<ElementType>)info.EnemyElements).Add(ElementType.Wind));
            var text=ClearReturnView.FormatDungeonInformation(info);StringAssert.Contains("ヒ / ミズ",text);StringAssert.Contains("ノーマル / コオリ / クサムラ / マグマ",text);Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(text,"[一-鿿]"));
        }
        [Test]
        public void 敵や地形の情報がない場合に属性や推奨レベルを捏造しない()
        {
            var info=new DungeonDeparturePreview(new[]{new DungeonConfig("empty","テスト",0,new List<MapCell>())});
            Assert.IsEmpty(info.EnemyElements);Assert.IsEmpty(info.TerrainTypes);
            Assert.AreEqual("テキゾクセイ: ジョウホウなし　チケイ: ジョウホウなし",CustomDisplay.Katakana(ClearReturnView.FormatDungeonInformation(info)));
            Assert.Throws<System.ArgumentNullException>(()=>new DungeonDeparturePreview(null));
            Assert.Throws<System.ArgumentException>(()=>new DungeonDeparturePreview(new DungeonConfig[0]));
            Assert.Throws<System.ArgumentException>(()=>new DungeonDeparturePreview(new DungeonConfig[]{null}));
        }
        [Test]
        public void 未解放名と詳細はページを変えても露出せず解放条件とクリア履歴を保持する()
        {
            var root=new GameObject("DungeonSelectionPreview",typeof(RectTransform));ClearReturnView view=null;
            try {
                view=root.AddComponent<ClearReturnView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),null,null,null,null);
                var names=Enumerable.Range(0,7).Select(i=>"secret-name-"+i).ToArray();var unlocked=new[]{true,false,false,false,false,false,false};var cleared=new[]{true,false,false,false,false,false,false};
                var conditions=Enumerable.Repeat("ヒトツマエノダンジョンヲクリア",7).ToArray();var descriptions=Enumerable.Range(0,7).Select(i=>"secret-info-"+i).ToArray();
                view.RenderDungeonSelection(names,unlocked,0,_=>{},()=>{},()=>{},false,null,()=>{},cleared:cleared,unlockConditions:conditions,descriptions:descriptions);
                var first=root.GetComponentsInChildren<Button>().Single(b=>b.name=="DungeonChoice_0");Canvas.ForceUpdateCanvases();StringAssert.Contains("クリアズみ",first.GetComponentInChildren<Text>().text);
                var locked=root.GetComponentsInChildren<Button>().Single(b=>b.name=="DungeonChoice_1");Assert.IsFalse(locked.interactable);StringAssert.Contains("？？？",locked.GetComponentInChildren<Text>().text);StringAssert.DoesNotContain(names[1],locked.GetComponentInChildren<Text>().text);
                root.GetComponentsInChildren<Button>().Single(b=>b.name=="NextDungeonPage").onClick.Invoke();
                var all=string.Join("\n",root.GetComponentsInChildren<Text>().Select(t=>t.text));StringAssert.DoesNotContain(names[5],all);StringAssert.DoesNotContain(names[6],all);StringAssert.Contains(conditions[6],all);
                view.RenderDungeonSelection(names,unlocked,6,_=>{},()=>{},()=>{},false,null,()=>{},cleared:cleared,unlockConditions:conditions,descriptions:descriptions);
                all=string.Join("\n",root.GetComponentsInChildren<Text>().Select(t=>t.text));StringAssert.DoesNotContain(descriptions[6],all);
            }finally{if(view!=null)typeof(ClearReturnView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Object.DestroyImmediate(root);}
        }

        [Test]
        public void 非アクティブな親でも手札初期化と選択がAwakeに依存せず再表示で寿命を作り直さない()
        {
            var root=new GameObject("InactiveCustom",typeof(RectTransform));root.SetActive(false);
            try {
                var parent=root.AddComponent<CustomScreenView>();
                var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab");
                var obj=Object.Instantiate(prefab,root.transform);var hand=obj.GetComponent<HandCardView>();
                var card=Create("sword",CardSeries.Sword);
                hand.Initialize(card,parent);
                var field=typeof(HandCardView).GetField("destroyCts",BindingFlags.Instance|BindingFlags.NonPublic);
                var lifetime=field.GetValue(hand);Assert.IsNotNull(lifetime);
                Assert.DoesNotThrow(()=>hand.UpdateSelection(false));
                Assert.DoesNotThrow(()=>hand.UpdateHighlight(CardHighlightType.None));
                root.SetActive(true);Assert.AreSame(lifetime,field.GetValue(hand));
                root.SetActive(false);hand.Initialize(card,parent);root.SetActive(true);
                Assert.AreSame(lifetime,field.GetValue(hand));Assert.AreSame(card,hand.Card);
            } finally {Object.DestroyImmediate(root);}
        }

        [TestCase("close")] [TestCase("disable")] [TestCase("destroy")] [TestCase("reinitialize")]
        public async System.Threading.Tasks.Task 手札射出中に閉じる無効化破棄しても後続カードを生成しない(string ending)
        {
            Olca.Unity.GameplayPause.SetPaused(false);
            var root=new GameObject("CustomLifetime",typeof(RectTransform));
            try {
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));confirm.transform.SetParent(root.transform,false);
                var container=new GameObject("Hand",typeof(RectTransform));container.transform.SetParent(root.transform,false);
                typeof(CustomScreenView).GetField("confirmButton",flags).SetValue(view,confirm.GetComponent<Button>());
                typeof(CustomScreenView).GetField("handCardContainer",flags).SetValue(view,container.transform);
                typeof(CustomScreenView).GetField("handCardPrefab",flags).SetValue(view,UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab"));
                typeof(CustomScreenView).GetField("cardShootInterval",flags).SetValue(view,.15f);
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("a",CardSeries.Sword),Create("b",CardSeries.Sword),Create("c",CardSeries.Sword)}));
                view.Initialize(model);model.Open(new Card[0],new MockRandomProvider(0));
                var cards=(List<HandCardView>)typeof(CustomScreenView).GetField("handCardViews",flags).GetValue(view);
                Assert.AreEqual(1,cards.Count,"最初のカードの射出中に終了させる");
                if(ending=="close")model.Close();
                else if(ending=="disable"){
                    // EditModeでは通常のMonoBehaviour通知をUnityが実行しないため明示する。
                    typeof(CustomScreenView).GetMethod("OnDisable",flags).Invoke(view,null);root.SetActive(false);
                }
                else if(ending=="reinitialize")view.Initialize(new CustomScreenSystem(new Deck(new List<Card>())));
                else {typeof(CustomScreenView).GetMethod("OnDestroy",flags).Invoke(view,null);Object.DestroyImmediate(root);root=null;}
                await System.Threading.Tasks.Task.Delay(600);
                Assert.AreEqual(1,cards.Count,"旧タスクが画面終了後もカードを追加してはならない");
                if(root!=null)Assert.IsFalse((bool)typeof(CustomScreenView).GetField("handAnimating",flags).GetValue(view));
            } finally {if(root!=null){typeof(CustomScreenView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(root.GetComponent<CustomScreenView>(),null);Object.DestroyImmediate(root);}Olca.Unity.BattleVisualClock.Refresh();}
        }

        [Test]
        public void 左上の本絵はマウスと共通カーソル移動で切替わり未登録時はミニを流用しない()
        {
            Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            var root=new GameObject("IllustrationBinding",typeof(RectTransform));var texture=new Texture2D(2,1);
            var a=Sprite.Create(texture,new Rect(0,0,1,1),Vector2.one*.5f);var b=Sprite.Create(texture,new Rect(1,0,1,1),Vector2.one*.5f);
            try {
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("sword",CardSeries.Sword),Create("unregistered-art",CardSeries.Sword)}));model.Open(new Card[0],new MockRandomProvider(0));
                var hand=model.CurrentHand.ToList();typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,model);typeof(CustomScreenView).GetField("inputHand",flags).SetValue(view,hand);
                var imageObject=new GameObject("FocusedCardIllustration",typeof(RectTransform),typeof(Image));imageObject.transform.SetParent(root.transform,false);var image=imageObject.GetComponent<Image>();image.rectTransform.sizeDelta=new Vector2(280,280);
                typeof(CustomScreenView).GetField("selectedCardImage",flags).SetValue(view,image);
                var cursor=new CustomScreenCursor();typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                cursor.OnCursorMoved+=index=>typeof(CustomScreenView).GetMethod("OnCursorMoved",flags).Invoke(view,new object[]{index});cursor.Initialize(2);
                view.ConfigureCardVisuals(new[]{new CardVisualEntry{CardId=hand[0].Id,Illustration=a},new CardVisualEntry{CardId=hand[1].Id,Illustration=b}});
                Assert.AreSame(a,image.sprite);Assert.AreEqual(Color.white,image.color);Assert.IsTrue(image.isActiveAndEnabled);Assert.IsTrue(image.preserveAspect);
                cursor.MoveRight();Assert.AreSame(b,image.sprite);
                var handViews=(List<HandCardView>)typeof(CustomScreenView).GetField("handCardViews",flags).GetValue(view);
                for(int i=0;i<2;i++){var obj=new GameObject("Hand"+i);obj.transform.SetParent(root.transform,false);var h=obj.AddComponent<HandCardView>();typeof(HandCardView).GetField("parentView",flags).SetValue(h,view);typeof(HandCardView).GetField("<Card>k__BackingField",flags).SetValue(h,hand[i]);handViews.Add(h);}
                handViews[0].OnPointerEnter(null);Assert.AreSame(a,image.sprite);Assert.AreEqual(0,cursor.CurrentIndex);
                handViews[0].OnPointerExit(null);Assert.AreSame(a,image.sprite);Assert.AreEqual(0,cursor.CurrentIndex,"退出や領域クリックで確定へ移さない");
                cursor.MoveToConfirmButton();Assert.IsNull(image.sprite);cursor.MoveToCard(1);Assert.AreSame(b,image.sprite);
                view.ConfigureCardVisuals(null);Assert.IsNull(image.sprite);Assert.AreEqual(new Vector2(280,280),image.rectTransform.sizeDelta);
            }finally{Object.DestroyImmediate(root);Object.DestroyImmediate(a);Object.DestroyImmediate(b);Object.DestroyImmediate(texture);}
        }
        [Test]
        public void 操作した機器だけでガイドが切替わり離し入力や微小スティック揺れでは奪い返さない()
        {
            var keyboard=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();var pad=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Gamepad>();var mouse=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            Olca.Unity.InputGuideActivity.EnsureInitialized();
            try {
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.A));UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.KeyboardMouse,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.West));UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.Gamepad,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,new UnityEngine.InputSystem.LowLevel.KeyboardState());UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.Gamepad,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse,new UnityEngine.InputSystem.LowLevel.MouseState{delta=Vector2.right,position=new Vector2(10,20)});UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.KeyboardMouse,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState{leftStick=new Vector2(.01f,0)});UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.KeyboardMouse,Olca.Unity.InputGuideActivity.Current);
                UnityEngine.InputSystem.InputSystem.QueueStateEvent(pad,new UnityEngine.InputSystem.LowLevel.GamepadState{leftStick=new Vector2(.6f,0)});UnityEngine.InputSystem.InputSystem.Update();Assert.AreEqual(Olca.Unity.InputGuideDevice.Gamepad,Olca.Unity.InputGuideActivity.Current);
            } finally {UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);UnityEngine.InputSystem.InputSystem.RemoveDevice(pad);UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);typeof(Olca.Unity.InputGuideActivity).GetMethod("Reset",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);}
        }
        [TestCase(false)] [TestCase(true)]
        public void ガイドは最後の機器だけを表示し既存の再割当とカナ表記を維持する(bool battle)
        {
            var profile=Olca.Unity.UnityInputProvider.CreateDefaultKeyboardProfile();var phase=battle?Olca.Core.Logic.Integration.GamePhase.Battle:Olca.Core.Logic.Integration.GamePhase.Dungeon;
            var keyboard=GameFieldView.FormatGameplayInstructionsForDevice(profile,phase,Olca.Unity.InputGuideDevice.KeyboardMouse);
            var pad=GameFieldView.FormatGameplayInstructionsForDevice(profile,phase,Olca.Unity.InputGuideDevice.Gamepad);
            StringAssert.Contains("Escape",keyboard);StringAssert.DoesNotContain("West",keyboard);StringAssert.DoesNotContain("South",keyboard);StringAssert.DoesNotContain("Escape",pad);
            StringAssert.Contains(battle?"West":"South",pad);
            var edit=profile.BeginEdit();Assert.IsTrue(edit.TryAssign(("keyboard",battle?"battle":"exploration",battle?"AttackPrimary":"JumpPrimary"),"J",out _));
            StringAssert.Contains("J",GameFieldView.FormatGameplayInstructionsForDevice(edit.Preview(),phase,Olca.Unity.InputGuideDevice.KeyboardMouse));
            var rendered=CustomDisplay.Katakana(pad);Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(rendered,"[一-鿿]"));
        }

        [Test]
        public async System.Threading.Tasks.Task カスタム停止中もHUD退場遷移を完了してカスタム画面の登場を妨げない()
        {
            var root=new GameObject("HudTransition");var owner=new object();
            using(var cancel=new System.Threading.CancellationTokenSource()){
                try {
                    Olca.Unity.GameplayPause.SetPaused(false);
                    var group=root.AddComponent<CanvasGroup>();var field=root.AddComponent<GameFieldView>();
                    typeof(GameFieldView).GetField("dungeonHudCanvasGroup",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(field,group);
                    Olca.Unity.BattleVisualClock.SetPaused(owner,true);
                    var task=field.HideHUDAsync(.001f,cancel.Token).AsTask();
                    Assert.AreSame(task,await System.Threading.Tasks.Task.WhenAny(task,System.Threading.Tasks.Task.Delay(3000)));
                    await task;Assert.AreEqual(0f,group.alpha);Assert.IsTrue(Olca.Unity.BattleVisualClock.IsPaused);
                } finally {cancel.Cancel();Olca.Unity.BattleVisualClock.SetPaused(owner,false);Object.DestroyImmediate(root);}
            }
        }

        [Test]
        public void 描画前のカナ変換は文字データだけでなく同一フレームの文字メッシュにも反映する()
        {
            var root=new GameObject("KanaCanvas",typeof(RectTransform),typeof(Canvas));
            try {
                root.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
                var child=new GameObject("KanaText",typeof(RectTransform),typeof(Text));child.transform.SetParent(root.transform,false);
                var label=child.GetComponent<Text>();label.font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");label.fontSize=28;
                label.rectTransform.sizeDelta=new Vector2(400,100);label.horizontalOverflow=HorizontalWrapMode.Overflow;
                label.text="カナ";Canvas.ForceUpdateCanvases();UiLook.Label(label);
                var kana=label.GetComponent<UiKanaLabel>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(UiKanaLabel).GetMethod("OnDisable",flags).Invoke(kana,null);
                typeof(UiKanaLabel).GetMethod("OnEnable",flags).Invoke(kana,null);
                label.text="通貨";Canvas.ForceUpdateCanvases();
                Assert.AreEqual("ツウカ",label.text);Assert.AreEqual(3,label.cachedTextGenerator.characterCountVisible);
            } finally {foreach(var kana in root.GetComponentsInChildren<UiKanaLabel>(true))typeof(UiKanaLabel).GetMethod("OnDisable",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(kana,null);Object.DestroyImmediate(root);}
        }

        [Test]
        public void 変身の登録名称とアイコンは一覧とHUDへ連携し未登録素材を作らない()
        {
            var root=new GameObject("TransformationMetadata",typeof(RectTransform));
            var texture=new Texture2D(1,1);var sprite=Sprite.Create(texture,new Rect(0,0,1,1),Vector2.one*.5f);
            var player=new PlayerCharacter(100,new Olca.Core.Domain.Vector2Int(0,0));
            var battle=new BattleSystem(player,new List<Enemy>{new Enemy("fixtureEnemy","fixtureEnemy",50,ElementType.None,new Olca.Core.Domain.Vector2Int(4,0))},new Deck(new List<Card>{Create("sword",CardSeries.Sword)}));
            try {
                var container=new GameObject("Custom",typeof(RectTransform));container.transform.SetParent(root.transform,false);
                var view=container.AddComponent<CustomScreenView>();
                var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Image),typeof(Button));confirm.transform.SetParent(container.transform,false);
                var text=new GameObject("Label",typeof(RectTransform),typeof(Text));text.transform.SetParent(confirm.transform,false);
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(CustomScreenView).GetField("confirmButton",flags).SetValue(view,confirm.GetComponent<Button>());
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,battle.CustomScreen);
                var entry=new TransformationVisualEntry{Id="fixture",DisplayNameKana="テストヘンシン",Icon=sprite};
                view.ConfigureTransformationVisuals(new[]{entry});entry.DisplayNameKana="カキカエ";
                Assert.AreEqual("テストヘンシン",view.GetTransformationName("fixture"));Assert.IsNull(view.GetTransformationIcon("unregistered"));
                Assert.Throws<System.ArgumentException>(()=>view.ConfigureTransformationVisuals(new[]{new TransformationVisualEntry{Id="fixture",DisplayNameKana="漢字"}}));
                battle.Transformations.Configure(new[]{new TransformationDefenseDefinition("fixture",ElementResistances.Neutral,ElementType.Fire)});
                battle.SetFirstStrike();battle.CustomScreen.Open(new Card[0],new MockRandomProvider(0));view.BindTransformations(battle.Transformations);
                var choice=container.transform.Find("TransformationChoice0");Assert.IsNotNull(choice);
                StringAssert.Contains("テストヘンシン",choice.GetComponentInChildren<Text>().text);Assert.AreSame(sprite,choice.Find("TransformationIcon").GetComponent<Image>().sprite);
                Assert.IsFalse(choice.Find("TransformationIcon").GetComponent<Image>().raycastTarget);
                Assert.IsTrue(battle.Transformations.TryPreview("fixture"));battle.CustomScreen.Confirm();
                var hud=root.AddComponent<Olca.Presentation.Battle.BattleView>();
                typeof(Olca.Presentation.Battle.BattleView).GetField("customScreenContainer",flags).SetValue(hud,container.GetComponent<RectTransform>());
                typeof(Olca.Presentation.Battle.BattleView).GetField("battleSystem",flags).SetValue(hud,battle);
                typeof(Olca.Presentation.Battle.BattleView).GetMethod("UpdateTransformationVisual",flags).Invoke(hud,null);
                var status=root.GetComponentsInChildren<Text>(true).Single(t=>t.name=="TransformationHud");StringAssert.Contains("テストヘンシン",status.text);
                Assert.AreSame(sprite,root.GetComponentsInChildren<Image>(true).Single(i=>i.name=="TransformationHudIcon").sprite);
                view.ConfigureTransformationVisuals(null);
                typeof(Olca.Presentation.Battle.BattleView).GetMethod("UpdateTransformationVisual",flags).Invoke(hud,null);
                Assert.IsFalse(root.GetComponentsInChildren<Image>(true).Single(i=>i.name=="TransformationHudIcon").gameObject.activeSelf);
            } finally {battle.Transformations.Dispose();Object.DestroyImmediate(root);Object.DestroyImmediate(sprite);Object.DestroyImmediate(texture);}
        }

        [Test]
        public void 閉じた画面の内部クリーンアップは末尾解除を拒否せず仮選択を戻せる()
        {
            var card=Create("sword",CardSeries.Sword);var model=new CustomScreenSystem(new Deck(new List<Card>{card}));
            model.Open(new Card[0],new MockRandomProvider(0));Assert.IsTrue(model.TrySelectCard(card));model.CompleteClose();
            Assert.IsFalse(model.IsOpen);Assert.IsTrue(model.TryDeselectCard(card));Assert.IsEmpty(model.SelectedCards);
            Assert.Contains(card,model.CurrentHand.ToList());
        }

        [Test]
        public void 選択済み領域は位置に依存せず末尾一枚を解除し空き部分と未選択時にも順序とフォーカスを保つ()
        {
            Olca.Unity.GameplayPause.SetPaused(false);Olca.Unity.GameplayPause.UpdateInputBarrier(false);
            Olca.Unity.GameplayPause.AdvanceLogicalFrame();Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            var root=new GameObject("SelectedSlots",typeof(RectTransform));
            try {
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("sword",CardSeries.Sword),Create("sword",CardSeries.Sword),Create("heal",CardSeries.Sword)}));
                model.Open(new Card[0],new MockRandomProvider(0));var hand=model.CurrentHand.ToList();
                foreach(var card in hand)Assert.IsTrue(model.TrySelectCard(card));
                Assert.IsFalse(model.TryDeselectCard(hand[1]),"既存の取消は末尾だけを解除する契約を保つ");
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,model);
                typeof(CustomScreenView).GetField("inputHand",flags).SetValue(view,hand);
                var cursor=new CustomScreenCursor();cursor.Initialize(hand.Count);typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                var cost=new GameObject("Cost",typeof(RectTransform),typeof(Text));cost.transform.SetParent(root.transform,false);
                typeof(CustomScreenView).GetField("costDisplay",flags).SetValue(view,cost.GetComponent<Text>());
                var indicator=new GameObject("SelectionIndicator",typeof(RectTransform));indicator.transform.SetParent(root.transform,false);
                var slots=new List<Image>();for(int i=0;i<3;i++){var slot=new GameObject("Slot"+i,typeof(RectTransform),typeof(Image));slot.transform.SetParent(indicator.transform,false);((RectTransform)slot.transform).anchoredPosition=new Vector2(50*i,0);slots.Add(slot.GetComponent<Image>());}
                typeof(CustomScreenView).GetMethod("UpdateSelectedCardDisplay",flags).Invoke(view,null);
                for(int i=0;i<3;i++)Assert.AreSame(view.GetCardIconForCard(hand[i]),slots[i].sprite);
                var area=indicator.GetComponent<Button>();Assert.IsNotNull(area);Assert.IsTrue(indicator.GetComponent<Image>().raycastTarget);
                Assert.IsTrue(slots.All(s=>!s.raycastTarget),"各アイコンは行全体の入力を遮らない");
                area.onClick.Invoke();CollectionAssert.AreEqual(new[]{hand[0],hand[1]},model.SelectedCards);
                Assert.AreSame(view.GetCardIconForCard(hand[1]),slots[1].sprite);Assert.IsNull(slots[2].sprite);Assert.AreEqual(0,cursor.CurrentIndex);
                area.onClick.Invoke();CollectionAssert.AreEqual(new[]{hand[0]},model.SelectedCards);
                area.onClick.Invoke();Assert.IsEmpty(model.SelectedCards);area.onClick.Invoke();Assert.IsEmpty(model.SelectedCards);
                Assert.AreEqual(0,cursor.CurrentIndex);Assert.IsFalse(model.IsConfirming);

            } finally {Object.DestroyImmediate(root);}
        }
        [TestCase("ひらがなはそのまま ABC 123 ! カタカナ", "ひらがなはそのまま ABC 123 ! カタカナ")]
        [TestCase("<b>選択</b>して戻る（A+1）", "<b>センタク</b>してモドる（A+1）")]
        [TestCase("持ち帰り・同じ・速い・続きから", "モちカエり・オナじ・ハヤい・ツヅきから")]
        [TestCase("前方一マス / 一つ前", "ゼンポウイチマス / ヒトつマエ")]
        [TestCase("受け取らずに進む / 支払い", "ウけトらずにススむ / シハラい")]
        [TestCase("探索中は画面のボタンで操作", "タンサクチュウはガメンのボタンでソウサ")]
        public void 漢字以外の元文字と送り仮名と助詞を一字も変えない(string source,string expected)
            =>Assert.AreEqual(expected,CustomDisplay.Katakana(source));
        [Test]
        public void 現行製品とフォールバックの全カードに本絵があり実性能と明示素材を優先する()
        {
            var root = new GameObject("AllCardArt");
            var texture = new Texture2D(2, 2); var assigned = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            try {
                var view = root.AddComponent<CustomScreenView>(); var factory = new Olca.Application.Battle.SimpleCardFactory();
                foreach (var card in factory.CollectionCards) { Assert.IsNotNull(view.GetCardIllustrationForCard(card), card.Id + "/" + card.Name); Assert.IsNotNull(view.GetCardIconForCard(card), card.Id + "/mini"); }
                foreach (var id in new[] { "heal", "shield" }) {
                    var fallback = Olca.Core.Logic.Dungeon.CardRewardSystem.CreateFallbackCard(id, CardRarity.Normal);
                    Assert.AreSame(view.GetCardIllustration("sword"), view.GetCardIllustrationForCard(fallback));
                    Assert.AreSame(view.GetCardIcon("sword"), view.GetCardIconForCard(fallback));
                    Assert.AreSame(view.GetCardIllustration(id), view.GetCardIllustrationForCard(factory.CreateById(id)));
                }
                view.ConfigureCardVisuals(new[] { new CardVisualEntry { CardId = "heal", Illustration = assigned } });
                Assert.AreSame(assigned, view.GetCardIllustrationForCard(Olca.Core.Logic.Dungeon.CardRewardSystem.CreateFallbackCard("heal", CardRarity.Normal)));
            } finally { Object.DestroyImmediate(root); Object.DestroyImmediate(assigned); Object.DestroyImmediate(texture); }
        }

        [TestCase("sword")][TestCase("fire_sword")][TestCase("bomb")][TestCase("heal")][TestCase("shield")][TestCase("tuning")][TestCase("fire")][TestCase("ice")]
        public void 登録仮本絵は正方形の専用画像でミニアイコンとは別素材である(string id)
        {
            var root=new GameObject("CardArt");try{var v=root.AddComponent<CustomScreenView>();var art=v.GetCardIllustration(id);Assert.IsNotNull(art);Assert.AreEqual(art.rect.width,art.rect.height);Assert.Greater(art.rect.width,256);Assert.AreNotSame(art,v.GetCardIcon(id));StringAssert.StartsWith("OlcaCardArt_",art.name);}finally{Object.DestroyImmediate(root);}
        }

        [Test]
        public void フォーカス枠は背景より前で手札アイコンより後ろに描きポインター入力を遮らない()
        {
            var root=new GameObject("FocusOrder",typeof(RectTransform));
            try {var background=new GameObject("BgCustom",typeof(RectTransform),typeof(Image));background.transform.SetParent(root.transform,false);
                var cards=new GameObject("Cards",typeof(RectTransform));cards.transform.SetParent(root.transform,false);
                var focus=new GameObject("Focus",typeof(RectTransform),typeof(Image));focus.transform.SetParent(root.transform,false);
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                typeof(CustomScreenView).GetField("handCardContainer",flags).SetValue(view,cards.transform);
                typeof(CustomScreenView).GetField("cursorIndicator",flags).SetValue(view,focus);
                for(int i=0;i<3;i++)typeof(CustomScreenView).GetMethod("UpdateCursorIndicator",flags).Invoke(view,new object[]{-1});
                Assert.Less(background.transform.GetSiblingIndex(),focus.transform.GetSiblingIndex());Assert.Less(focus.transform.GetSiblingIndex(),cards.transform.GetSiblingIndex());Assert.IsFalse(focus.GetComponent<Image>().raycastTarget);
            } finally {Object.DestroyImmediate(root);}
        }
        [Test]
        public void カスタム停止は戦闘Animatorの元速度を復元しUIアニメと入力を止めない()
        {
            var world=new GameObject("BattleCharacter",typeof(Animator));var ui=new GameObject("Ui",typeof(Canvas),typeof(Animator));var a=new object();var b=new object();
            try {world.GetComponent<Animator>().speed=.55f;ui.GetComponent<Animator>().speed=.8f;
                bool inputBlocked=Olca.Unity.GameplayPause.InputBlocked;
                Olca.Unity.BattleVisualClock.SetPaused(a,true);Olca.Unity.BattleVisualClock.SetPaused(b,true);
                Assert.AreEqual(0f,world.GetComponent<Animator>().speed);Assert.AreEqual(.8f,ui.GetComponent<Animator>().speed);Assert.AreEqual(0f,Olca.Unity.BattleVisualClock.DeltaTime);Assert.AreEqual(inputBlocked,Olca.Unity.GameplayPause.InputBlocked);
                Olca.Unity.BattleVisualClock.SetPaused(a,false);Assert.AreEqual(0f,world.GetComponent<Animator>().speed);
                Olca.Unity.BattleVisualClock.SetPaused(b,false);Assert.AreEqual(.55f,world.GetComponent<Animator>().speed);
            } finally {Olca.Unity.BattleVisualClock.SetPaused(a,false);Olca.Unity.BattleVisualClock.SetPaused(b,false);Object.DestroyImmediate(world);Object.DestroyImmediate(ui);}
        }
        [TestCase("選択したカードを確定します","センタクしたカードをカクテイします")]
        [TestCase("前方1マス炎属性攻撃","ゼンポウ1マスホノオゾクセイコウゲキ")]
        [TestCase("使用済み / 手札上限","シヨウズみ / テフダジョウゲン")]
        [TestCase("十字左 / 獲得: 3枚 / 通貨: 0","ジュウジヒダリ / カクトク: 3マイ / ツウカ: 0")]
        [TestCase("続きから","ツヅきから")]
        [TestCase("変身演出：通常 / 短縮","ヘンシンエンシュツ：ツウジョウ / タンシュク")]
        [TestCase("音量設定","オンリョウセッテイ")]
        [TestCase("文字表示：標準 / ゆっくり / 速い / 即時","モジヒョウジ：ヒョウジュン / ゆっくり / ハヤい / ソクジ")]
        [TestCase("演出設定 保存失敗（再試行）","エンシュツセッテイ ホゾンシッパイ（サイシコウ）")]
        public void 指定フォントを交換せず既存カスタム文言をカナで表示する(string source,string expected)
            =>Assert.AreEqual(expected,CustomDisplay.Katakana(source));

        [Test]
        public void タッチ短押しはクリックを残し長押しは一度だけ詳細を開いて解放クリックを拒否する()
        {
            WithTouchHold((hold,screen,group,opened,valid)=>{
                var tap=Touch(screen,1);hold.OnPointerDown(tap);hold.OnPointerUp(tap);
                Assert.IsTrue(tap.eligibleForClick);Assert.AreEqual(0,opened());
                var press=Touch(screen,2);hold.OnPointerDown(press);AdvanceHold(hold);AdvanceHold(hold);
                Assert.AreEqual(1,opened());Assert.IsFalse(press.eligibleForClick);hold.OnPointerUp(press);Assert.AreEqual(1,opened());
                var mouse=new UnityEngine.InputSystem.UI.ExtendedPointerEventData(null){pointerId=-1,eligibleForClick=true};
                hold.OnPointerDown(mouse);AdvanceHold(hold);hold.OnPointerUp(mouse);Assert.IsTrue(mouse.eligibleForClick);Assert.AreEqual(1,opened());
            });
        }
        [Test]
        public void 長押しの離脱ドラッグ取消無効化とOS取消は詳細もクリックも発生させない()
        {
            WithTouchHold((hold,screen,group,opened,valid)=>{
                for(int i=0;i<8;i++){
                    var press=Touch(screen,10+i);hold.OnPointerDown(press);
                    if(i==0)hold.OnPointerExit(press);
                    else if(i==1){press.position=new Vector2(100,100);hold.OnPointerMove(press);}
                    else if(i==2)hold.OnCancel(null);
                    else if(i==3){valid(false);}
                    else if(i==4)hold.enabled=false;
                    else if(i==5)hold.GetComponent<Button>().enabled=false;
                    else if(i==6)hold.GetComponent<Button>().interactable=false;
                    else {var guard=hold.GetComponent<Olca.Unity.TouchUiCancelGuard>();guard.OnPointerDown(press);Olca.Unity.TouchUiCancelGuard.CancelTouch(screen.deviceId,press.touchId);}
                    AdvanceHold(hold);hold.OnPointerUp(press);Assert.IsFalse(press.eligibleForClick,"case "+i);Assert.AreEqual(0,opened());hold.enabled=true;hold.GetComponent<Button>().enabled=true;hold.GetComponent<Button>().interactable=true;valid(true);
                }
                valid(false);var invalid=Touch(screen,20);hold.OnPointerDown(invalid);Assert.IsFalse(invalid.eligibleForClick);AdvanceHold(hold);Assert.AreEqual(0,opened());
            });
        }
        [Test]
        public void 複数指は両方の長押しとクリックを抑え全解放後の新規押下だけを受け付ける()
        {
            WithTouchHold((hold,screen,group,opened,valid)=>{
                var other=new GameObject("SecondTouchDetail",typeof(RectTransform),typeof(Image),typeof(Button));
                try{var second=other.AddComponent<CustomDetailHold>();second.Configure(other.GetComponent<Button>(),()=>true,()=>Assert.Fail("second finger opened"),group);
                    var a=Touch(screen,1);var b=Touch(screen,2);hold.OnPointerDown(a);second.OnPointerDown(b);AdvanceHold(hold);AdvanceHold(second);
                    Assert.IsFalse(a.eligibleForClick);Assert.IsFalse(b.eligibleForClick);Assert.AreEqual(0,opened());
                    hold.OnPointerUp(a);second.OnPointerUp(b);var fresh=Touch(screen,3);hold.OnPointerDown(fresh);AdvanceHold(hold);Assert.AreEqual(1,opened());hold.OnPointerUp(fresh);
                }finally{Object.DestroyImmediate(other);}
            });
        }
        private static UnityEngine.InputSystem.UI.ExtendedPointerEventData Touch(UnityEngine.InputSystem.Touchscreen screen,int id)=>
            new UnityEngine.InputSystem.UI.ExtendedPointerEventData(null){device=screen,touchId=id,pointerId=id,eligibleForClick=true};
        private static void AdvanceHold(CustomDetailHold hold)=>typeof(CustomDetailHold).GetMethod("Advance",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(hold,new object[]{double.MaxValue});
        private static void WithTouchHold(System.Action<CustomDetailHold,UnityEngine.InputSystem.Touchscreen,HashSet<CustomDetailHold>,System.Func<int>,System.Action<bool>> action)
        {
            var screen=UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Touchscreen>();
            var root=new GameObject("CustomTouchDetails",typeof(RectTransform),typeof(Image),typeof(Button));
            try{int count=0;bool valid=true;var group=new HashSet<CustomDetailHold>();var hold=root.AddComponent<CustomDetailHold>();
                hold.Configure(root.GetComponent<Button>(),()=>valid,()=>count++,group);action(hold,screen,group,()=>count,v=>valid=v);
            }finally{Object.DestroyImmediate(root);UnityEngine.InputSystem.InputSystem.RemoveDevice(screen);}
        }

        [Test]
        public void 共通UI素材は指定フォントとウェイトサイズ自動縮小とマテリアルを置き換えない()
        {
            var root=new GameObject("AssignedTypeface",typeof(RectTransform),typeof(Text));
            try{var text=root.GetComponent<Text>();var font=UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf");Assert.IsNotNull(font);
                text.font=font;text.fontSize=31;text.fontStyle=FontStyle.Bold;text.resizeTextForBestFit=true;text.resizeTextMinSize=16;text.resizeTextMaxSize=35;var material=text.material;
                UiLook.Label(text);Assert.AreSame(font,text.font);Assert.AreEqual(FontStyle.Bold,text.fontStyle);Assert.AreEqual(31,text.fontSize);Assert.IsTrue(text.resizeTextForBestFit);Assert.AreEqual(16,text.resizeTextMinSize);Assert.AreEqual(35,text.resizeTextMaxSize);Assert.AreSame(material,text.material);
                text.font=null;UiLook.Label(text);Assert.AreSame(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"),text.font);
            }finally{Object.DestroyImmediate(root);}
        }
        [Test]
        public void 手札コストの元フォント参照を共通UIの都合で別書体へ差し替えない()
        {
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/HandCardViewPrefab.prefab");
            var cost=prefab.GetComponentInChildren<Text>(true);Assert.IsNotNull(cost);Assert.AreEqual("Assets/Font/JikiTourai.ttf",UnityEditor.AssetDatabase.GetAssetPath(cost.font));
        }

        [Test]
        public void 共通ボタン素材は専用カードアイコンを上書きしない()
        {
            var root=new GameObject("DedicatedCardButton",typeof(RectTransform),typeof(Image),typeof(Button));
            var texture=new Texture2D(2,2);var icon=Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f);icon.name="dedicated-card-mini";
            try {var image=root.GetComponent<Image>();image.sprite=icon;var button=root.GetComponent<Button>();button.targetGraphic=image;
                Olca.Presentation.Dungeon.UiLook.Button(button);Assert.AreSame(icon,image.sprite);Assert.AreNotEqual(Selectable.Transition.SpriteSwap,button.transition);
            } finally {Object.DestroyImmediate(root);Object.DestroyImmediate(icon);Object.DestroyImmediate(texture);}
        }
        [Test]
        public void 表示生成前の決定は先頭手札を選び再選択と後続カーソルを演出完了で変えない()
        {
            var root=new GameObject("EarlyCustomInput",typeof(RectTransform));
            try {
                var model=new CustomScreenSystem(new Deck(new List<Card>{Create("a",CardSeries.Sword),Create("b",CardSeries.Sword),Create("c",CardSeries.Sword)}));
                model.Open(new Card[0],new MockRandomProvider(0));var order=new List<Card>(model.CurrentHand);
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,model);
                typeof(CustomScreenView).GetField("costDisplay",flags).SetValue(view,root.AddComponent<Text>());
                var cursor=new CustomScreenCursor();typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                view.ConfigureLogicalConfirmation((time,action)=>action(),()=>0);
                typeof(CustomScreenView).GetMethod("InitializeInputHand",flags).Invoke(view,new object[]{order});
                typeof(CustomScreenView).GetField("handAnimating",flags).SetValue(view,true);
                var decide=new Olca.Core.Logic.Integration.LogicalInput{MenuDecideButtonDown=true};
                view.HandleLogicalConfirmation(decide);Assert.IsTrue(model.IsOpen);Assert.IsFalse(model.IsConfirming);Assert.AreSame(order[0],model.SelectedCards[0]);
                view.HandleLogicalConfirmation(decide);Assert.IsEmpty(model.SelectedCards,"同じ位置の再決定は同じカードを取り消す");
                cursor.MoveToCard(1);view.HandleLogicalConfirmation(decide);Assert.AreSame(order[1],model.SelectedCards[0]);
                typeof(CustomScreenView).GetMethod("ShowCursorAfterAnimation",flags).Invoke(view,null);Assert.AreEqual(1,cursor.CurrentIndex);
                view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput{MenuConfirmButtonDown=true});Assert.IsTrue(model.IsConfirming);Assert.AreSame(order[1],model.AvailableCards[0]);
            } finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void 手札演出中の通過ホバーはキー選択位置を確定へ移さず空手札は明示確定できる()
        {
            var root=new GameObject("CustomHover",typeof(RectTransform));
            try {
                var view=root.AddComponent<CustomScreenView>();var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var cursor=new CustomScreenCursor();cursor.Initialize(1);typeof(CustomScreenView).GetField("cursor",flags).SetValue(view,cursor);
                var cardObject=new GameObject("MovingCard");cardObject.transform.SetParent(root.transform);var moving=cardObject.AddComponent<HandCardView>();
                ((List<HandCardView>)typeof(CustomScreenView).GetField("handCardViews",flags).GetValue(view)).Add(moving);
                typeof(CustomScreenView).GetField("handAnimating",flags).SetValue(view,true);view.OnCardPointerExited(moving);Assert.AreEqual(0,cursor.CurrentIndex);
                var empty=new CustomScreenSystem(new Deck(new List<Card>()));empty.Open(new Card[0],new MockRandomProvider(0));
                typeof(CustomScreenView).GetField("customScreen",flags).SetValue(view,empty);
                typeof(CustomScreenView).GetMethod("InitializeInputHand",flags).Invoke(view,new object[]{new List<Card>()});Assert.IsTrue(cursor.IsOnConfirmButton);
                view.ConfigureLogicalConfirmation((time,action)=>action(),()=>0);view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput{MenuDecideButtonDown=true});Assert.IsTrue(empty.IsConfirming);
            } finally { Object.DestroyImmediate(root); }
        }

        [TestCase(0, false)]
        [TestCase(2, true)]
        public void キー移動後の静止ポインタークリックは同じ個体へカーソルと次の決定を揃える(int target, bool selected)
        {
            var root = new GameObject("PointerKeyboardSelection", typeof(RectTransform));
            try {
                // 同じIDでも異なるカード個体。選択によるCurrentHandの詰め直しに依存しない。
                var cards = new List<Card> { Create("same", CardSeries.Sword), Create("same", CardSeries.Sword), Create("same", CardSeries.Sword) };
                var model = new CustomScreenSystem(new Deck(cards));
                model.Open(new Card[0], new MockRandomProvider(0));
                var order = new List<Card>(model.CurrentHand);
                var view = root.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, root.AddComponent<Text>());
                var cursor = new CustomScreenCursor();
                typeof(CustomScreenView).GetField("cursor", flags).SetValue(view, cursor);
                typeof(CustomScreenView).GetMethod("InitializeInputHand", flags).Invoke(view, new object[] { order });
                view.ConfigureLogicalConfirmation((time, action) => action(), () => 0);
                if (selected) view.OnCardClicked(order[target]);
                cursor.MoveToCard((target + 1) % order.Count);

                // 再入場のPointerEnterがないクリックでも操作位置を更新する。
                view.OnCardClicked(order[target]);
                Assert.AreEqual(target, cursor.CurrentIndex);
                Assert.AreEqual(!selected, model.SelectedCards.Contains(order[target]));
                view.HandleLogicalConfirmation(new Olca.Core.Logic.Integration.LogicalInput { MenuDecideButtonDown = true });
                Assert.AreEqual(selected, model.SelectedCards.Contains(order[target]));
                foreach (var card in order)
                    if (!ReferenceEquals(card, order[target])) Assert.IsFalse(model.SelectedCards.Contains(card));
            } finally { Object.DestroyImmediate(root); }
        }

        [TestCase(5, true, 5, 2)]
        [TestCase(8, false, 6, 2)]
        [TestCase(9, false, 9, 3)]
        public void カードクリック_選択可否と表示合計と確定内容が一致する(int limit, bool consecutive, int total, int count)
        {
            var cards = new List<Card> {
                Create("a", CardSeries.Sword),
                Create("b", consecutive ? CardSeries.Sword : CardSeries.Bomb),
                Create("c", CardSeries.Sword)
            };
            var model = new CustomScreenSystem(new Deck(cards), maxCost: limit);
            model.Open(new Card[0], new MockRandomProvider(0));
            var go = new GameObject("CostDisplayTest", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, text);
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                view.OnCardClicked(cards[0]);
                view.OnCardClicked(cards[1]);
                if (!consecutive) view.OnCardClicked(cards[2]);
                Assert.AreEqual(total, model.GetTotalUsedCost());
                Assert.AreEqual(limit - total, model.RemainingCost);
                Assert.AreEqual($"コスト: {total} / {limit}", text.text);
                Assert.AreEqual(Color.white, text.color);
                Assert.AreEqual(count, model.SelectedCards.Count);
                var expected = new List<Card>(model.SelectedCards);
                model.Confirm();
                model.Close();
                CollectionAssert.AreEqual(expected, model.AvailableCards);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void 中間解除は表示不変で末尾解除と再選択だけ反映する()
        {
            var cards = new List<Card> { Create("a", CardSeries.Sword), Create("b", CardSeries.Bomb), Create("c", CardSeries.Sword) };
            var model = new CustomScreenSystem(new Deck(cards));
            model.Open(new Card[0], new MockRandomProvider(0));
            var go = new GameObject("CostDisplayTest", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, text);
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                foreach (var card in cards) view.OnCardClicked(card);
                Assert.AreEqual("コスト: 9 / 10", text.text);
                view.OnCardClicked(cards[1]);
                Assert.AreEqual("コスト: 9 / 10", text.text);
                CollectionAssert.AreEqual(cards, model.SelectedCards);
                view.OnCardClicked(cards[2]);
                Assert.AreEqual("コスト: 6 / 10", text.text);
                view.OnCardClicked(cards[2]);
                Assert.AreEqual("コスト: 9 / 10", text.text);
                CollectionAssert.AreEqual(cards, model.SelectedCards);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void 中間解除拒否_上限内表示と確定ボタンを維持する()
        {
            var a = Create("a", CardSeries.Sword);
            var b = new Card("b", "B", 0, CardRarity.Normal, CardCategory.Attack, ElementType.None, CardSeries.Bomb, 1, "テスト");
            var c = Create("c", CardSeries.Bomb);
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, b, c }), maxCost: 5);
            model.Open(new Card[0], new MockRandomProvider(0));
            var go = new GameObject("CostDisplayTest", typeof(RectTransform));
            try
            {
                var text = go.AddComponent<Text>();
                var button = go.AddComponent<Button>();
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("costDisplay", flags).SetValue(view, text);
                typeof(CustomScreenView).GetField("confirmButton", flags).SetValue(view, button);
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                foreach (var card in new[] { a, b, c }) view.OnCardClicked(card);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(b);
                Assert.AreEqual("コスト: 5 / 5", text.text);
                Assert.AreEqual(Color.white, text.color);
                Assert.IsTrue(button.interactable);
                view.OnCardClicked(c);
                Assert.AreEqual("コスト: 3 / 5", text.text);
                Assert.IsTrue(button.interactable);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        private static Card Create(string id, CardSeries series) =>
            new Card(id, id, 3, CardRarity.Normal, CardCategory.Attack, ElementType.None, series, 1, "テスト");

        [TestCase(16f / 9f)]
        [TestCase(4f / 3f)]
        [TestCase(691f / 327f)]
        [TestCase(9f / 16f)]
        public void 共通カメラは画面比率にかかわらず横七マスでスケールを変えない(float aspect)
        {
            var go = new GameObject("SevenCellCamera");
            var viewObject = new GameObject("FieldView");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.orthographic = true;
                camera.aspect = aspect;
                camera.transform.localScale = new Vector3(1f, 1f, 1f);
                var view = viewObject.AddComponent<GameFieldView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GameFieldView).GetField("fieldCamera", flags).SetValue(view, camera);
                var refresh = typeof(GameFieldView).GetMethod("UpdateCameraFraming", flags);
                refresh.Invoke(view, null);
                Assert.AreEqual(7f, camera.orthographicSize * 2f * camera.aspect, 0.0001f);
                Assert.AreEqual(0.25f, camera.WorldToViewportPoint(new Vector3(0f, -0.25f, 0f)).y, 0.0001f);
                Assert.AreEqual(Vector3.one, camera.transform.localScale);
                camera.aspect = 2f;
                refresh.Invoke(view, null);
                Assert.AreEqual(1.75f, camera.orthographicSize, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(viewObject);
                Object.DestroyImmediate(go);
            }
        }

        [TestCase(CardRarity.Normal,"スタンダード")]
        [TestCase(CardRarity.Limited,"リミテッド")]
        [TestCase(CardRarity.Ultimate,"アルティメット")]
        public void カード報酬のレギュレーションは識別子を変えず確定したカナ名称を表示する(CardRarity value,string expected)
        {
            var root=new GameObject("RegulationFixture",typeof(RectTransform));
            try {
                var view=root.AddComponent<CardSelectionItemView>();var label=new GameObject("CardRarity",typeof(RectTransform),typeof(Text));label.transform.SetParent(root.transform,false);
                typeof(CardSelectionItemView).GetField("cardRarityText",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(view,label.GetComponent<Text>());
                var card=new Card("fixture","テスト",1,value,CardCategory.Attack,ElementType.None,CardSeries.None,1,"");view.Setup(card);
                Assert.AreEqual(expected,label.GetComponent<Text>().text);Assert.AreEqual(value,card.Rarity);Assert.AreEqual("fixture",card.Id);
                Assert.AreEqual(expected,CustomDisplay.Regulation(value));
            }finally{Object.DestroyImmediate(root);}
        }

        [TestCase(RunReturnOutcome.Cleared)]
        [TestCase(RunReturnOutcome.Defeated)]
        [TestCase(RunReturnOutcome.Retreated)]
        public void 帰還ホームの報酬まとめはクリアだけに表示し敗北撤退の現在能力と再出撃は保つ(RunReturnOutcome outcome)
        {
            var root=new GameObject("ReturnSummaryFixture",typeof(RectTransform));ClearReturnView view=null;int departures=0;
            var profile=new ClearReturnProfile{Level=2,Experience=100,Tokens=7,ReturnedHp=105,Revision=4};
            var flow=new ClearReturnData{RunId="fixture",Stage=ClearReturnStage.Returned,Outcome=outcome,ReturnedMaxHp=105,ReturnedAttack=11};
            try {
                view=root.AddComponent<ClearReturnView>();view.Initialize(UnityEditor.AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/JikiTourai.ttf"),null,null,null,null,()=>departures++);
                view.Render(profile,flow,false,null);
                var home=CustomDisplay.Katakana(root.GetComponentsInChildren<Text>().Single(t=>t.name=="HomeStats").text);
                StringAssert.Contains("HP 105/105",home);StringAssert.Contains("11",home);
                if(outcome==RunReturnOutcome.Cleared)StringAssert.Contains("ホゾンズみ",home);
                else {StringAssert.DoesNotContain("+0",home);StringAssert.DoesNotContain("ウシナ",home);StringAssert.DoesNotContain("ホゾンズみ",home);}
                Assert.IsFalse(root.GetComponentsInChildren<Text>().Any(t=>t.name=="AwardSummary"||t.name=="AwardCards"));
                root.GetComponentsInChildren<Button>().Single(b=>b.name=="NextDeparture").onClick.Invoke();Assert.AreEqual(1,departures);
                Assert.AreEqual(100,profile.Experience);Assert.AreEqual(7,profile.Tokens);Assert.AreEqual(4,profile.Revision);Assert.IsEmpty(profile.Owned);
            }finally{if(view!=null)typeof(ClearReturnView).GetMethod("OnDestroy",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,null);Object.DestroyImmediate(root);}
        }

        [TestCase("敗北して帰還しました","ハイボクしてキカンしました")]
        [TestCase("総経験値 0 / 永続所持カード / 装備 / トークン交換所 / カード図鑑","ソウケイケンチ 0 / エイゾクショジカード / ソウビ / トークンコウカンジョ / カードズカン")]
        [TestCase("基本 / 無属性 / 再出撃 / 前へ / 次へ","キホン / ムゾクセイ / サイシュツゲキ / マエへ / ツギへ")]
        [TestCase("持ち帰りカードを選択","モちカエりカードをセンタク")]
        [TestCase("同じカード1枚まで・★★★不可","オナじカード1マイまで・★★★フカ")]
        [TestCase("経験値予定 +100 / 経験値反映","ケイケンチヨテイ +100 / ケイケンチハンエイ")]
        [TestCase("経験値 +100（反映済み） / 超過変換 +0 トークン","ケイケンチ +100（ハンエイズみ） / チョウカヘンカン +0 トークン")]
        [TestCase("帰還後：HP 100/100 / 攻略中の状態を解除して全回復します","キカンゴ：HP 100/100 / コウリャクチュウのジョウタイをカイジョしてゼンカイフクします")]
        [TestCase("持ち帰れる獲得カードはありません","モちカエれるカクトクカードはありません")]
        [TestCase("カード・経験値は保存済みです","カード・ケイケンチはホゾンズみです")]
        public void 帰還画面の既存用語をフォント変更せずカナで表示する(string source,string expected)
            =>Assert.AreEqual(expected,CustomDisplay.Katakana(source));

        [TestCase(0L,"ゲキハタイム: 00:00.00")]
        [TestCase(1L,"ゲキハタイム: 00:00.01")]
        [TestCase(59L,"ゲキハタイム: 00:00.98")]
        [TestCase(60L,"ゲキハタイム: 00:01.00")]
        [TestCase(3599L,"ゲキハタイム: 00:59.98")]
        [TestCase(3600L,"ゲキハタイム: 01:00.00")]
        public void 報酬の撃破時間は整数論理フレームから表示する(long frames,string expected)
            =>Assert.AreEqual(expected,CardSelectionView.FormatBattleTime(frames));
        [Test]
        public void 未計測の撃破時間は捏造せず負の時刻を拒否する()
        {
            Assert.AreEqual("",CardSelectionView.FormatBattleTime(null));
            Assert.Throws<System.ArgumentOutOfRangeException>(()=>CardSelectionView.FormatBattleTime(-1));
            StringAssert.StartsWith("ゲキハタイム: ",CardSelectionView.FormatBattleTime(long.MaxValue));
        }

        [TestCase(false)] [TestCase(true)]
        public async System.Threading.Tasks.Task 報酬の受取と明示スキップは最初の入力だけで確定し再表示でもボタンを増やさない(bool skipFirst)
        {
            var root = new GameObject("RewardSkip", typeof(RectTransform));
            var headingObject=new GameObject("TitleText",typeof(RectTransform),typeof(Text));headingObject.transform.SetParent(root.transform,false);
            var heading=headingObject.GetComponent<Text>();heading.text="カードを1枚獲得";
            heading.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");var originalFont=heading.font;
            try {
                var view = root.AddComponent<CardSelectionView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CardSelectionView).GetField("containerRoot", flags).SetValue(view, root);
                var countObject=new GameObject("DeckCount",typeof(RectTransform),typeof(Text));countObject.transform.SetParent(root.transform,false);
                var countText=countObject.GetComponent<Text>();typeof(CardSelectionView).GetField("deckCountText",flags).SetValue(view,countText);
                typeof(CardSelectionView).GetField("cardItems", flags).SetValue(view, new CardSelectionItemView[0]);
                var card = Create("reward", CardSeries.Sword);
                for (int repeat=0; repeat<2; repeat++) {
                    var task = view.ShowAsync(new[] { card }, 6, battleLogicalFrames:repeat==0?(long?)61:null).AsTask();
                    var time=root.transform.Find("BattleTimeText").GetComponent<Text>();
                    Assert.AreEqual(repeat==0,time.gameObject.activeSelf);
                    Assert.AreEqual(repeat==0?"ゲキハタイム: 00:01.01":"",time.text);
                    Assert.AreSame(countText.font,time.font);Assert.IsFalse(time.raycastTarget);
                    Assert.AreEqual(1,root.GetComponentsInChildren<Text>(true).Count(t=>t.name=="BattleTimeText"));
                    var button = root.GetComponentInChildren<Button>(true);
                    Assert.AreEqual("SkipCardReward", button.name);
                    Assert.AreEqual("カードを1マイカクトク",heading.text);Assert.AreSame(originalFont,heading.font);Assert.AreEqual(Color.white,countText.color);
                    Canvas.ForceUpdateCanvases();Assert.AreEqual("ウけトらずにススむ",button.GetComponentInChildren<Text>(true).text);
                    Assert.AreEqual(1, root.GetComponentsInChildren<Button>(true).Length);
                    // ポーズ中は明示スキップでも報酬を確定しない。
                    Olca.Unity.GameplayPause.SetPaused(true); button.onClick.Invoke(); Assert.IsFalse(task.IsCompleted);
                    Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                    Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
                    if (skipFirst) button.onClick.Invoke();
                    typeof(CardSelectionView).GetMethod("OnItemClicked", flags).Invoke(view, new object[] { card });
                    button.onClick.Invoke();
                    Assert.AreSame(skipFirst ? null : card, await task); Assert.IsFalse(root.activeSelf);
                }
            } finally {
                Object.DestroyImmediate(root); Olca.Unity.GameplayPause.SetPaused(false); Olca.Unity.GameplayPause.UpdateInputBarrier(false);
                Olca.Unity.GameplayPause.AdvanceLogicalFrame(); Olca.Unity.GameplayPause.AdvanceLogicalFrame();
            }
        }

        [TestCase(691, 327, 3)]
        [TestCase(1280, 720, 3)]
        [TestCase(640, 480, 1)]
        public void 報酬カードは画面内へ等比配置される(int width, int height, int count)
        {
            var root = new GameObject("RewardLayout", typeof(RectTransform));
            try
            {
                var parent = (RectTransform)root.transform;
                parent.sizeDelta = new Vector2(width, height);
                var view = root.AddComponent<CardSelectionView>();
                var items = new CardSelectionItemView[count];
                for (int i = 0; i < count; i++)
                {
                    var child = new GameObject("Card", typeof(RectTransform));
                    child.transform.SetParent(root.transform, false);
                    ((RectTransform)child.transform).sizeDelta = new Vector2(280, 380);
                    items[i] = child.AddComponent<CardSelectionItemView>();
                }
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CardSelectionView).GetField("cardItems", flags).SetValue(view, items);
                typeof(CardSelectionView).GetMethod("UpdateChoiceLayout", flags).Invoke(view, null);
                foreach (var item in items)
                {
                    var rect = (RectTransform)item.transform;
                    Assert.AreEqual(rect.localScale.x, rect.localScale.y);
                    Assert.LessOrEqual(Mathf.Abs(rect.anchoredPosition.x) + rect.rect.width * rect.localScale.x / 2f, width / 2f - 16f + 0.01f);
                    Assert.LessOrEqual(rect.rect.height * rect.localScale.y, height - 64f + 0.01f);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        [TestCase(false)]
        [TestCase(true)]
        public void 加工Undoボタンは二重入力と確定競合で余分に戻さない(bool confirmFirst)
        {
            var a = Create("a", CardSeries.Sword);
            var t1 = new Card("t1", "調律", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 0, "", modifier: new CardModifier { BonusDamage = 1 });
            t1 = t1.WithTuningDefinition(new TuningGrantDefinition("undo", new[] { new TuningEffectDefinition("bonus", 1,
                new Dictionary<int, CardModifier> { { 1, new CardModifier { BonusDamage = 1 } } }) }));
            var t2 = t1.Clone();
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, t1, t2 }), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            model.Open(new Card[0], new MockRandomProvider(0));
            var first = model.ExecuteTuning(a, t1).ResultCard;
            model.ExecuteTuning(first, t2);
            var root = new GameObject("UndoButtonTest", typeof(RectTransform));
            try
            {
                var confirmObject = new GameObject("Confirm", typeof(RectTransform), typeof(Image), typeof(Button));
                confirmObject.transform.SetParent(root.transform, false);
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelObject.transform.SetParent(confirmObject.transform, false);
                var view = root.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("confirmButton", flags).SetValue(view, confirmObject.GetComponent<Button>());
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetMethod("EnsureUndoButton", flags).Invoke(view, null);
                var button = (Button)typeof(CustomScreenView).GetField("undoCustomizationButton", flags).GetValue(view);
                Assert.AreEqual("加工を戻す", button.GetComponentInChildren<Text>().text);
                Assert.AreEqual(Navigation.Mode.None, button.navigation.mode);
                if (confirmFirst) model.Confirm();
                typeof(CustomScreenView).GetMethod("UpdateUndoButton", flags).Invoke(view, null);
                button.onClick.Invoke();
                button.onClick.Invoke();
                Assert.AreEqual(confirmFirst ? 2 : 1, model.CustomizationUndoCount);
                Assert.IsFalse(button.interactable);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("empty")]
        [TestCase("closed")]
        [TestCase("confirming")]
        public void 加工Undoボタンは操作できない状態で無効(string state)
        {
            var a = Create("a", CardSeries.Sword);
            var t = new Card("t", "調律", 1, CardRarity.Normal, CardCategory.Tuning, ElementType.None, CardSeries.None, 0, "");
            var model = new CustomScreenSystem(new Deck(new List<Card> { a, t }), tuningSystem: new TuningSystem(Olca.Tests.Mocks.MockTuningDefinitions.FromModifier));
            model.Open(new Card[0], new MockRandomProvider(0));
            if (state != "empty") model.ExecuteTuning(a, t);
            if (state == "closed") { model.Close(); model.Open(new Card[0], new MockRandomProvider(0)); }
            if (state == "confirming") model.Confirm();
            var go = new GameObject("UndoState", typeof(RectTransform), typeof(Button));
            try
            {
                var view = go.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("undoCustomizationButton", flags).SetValue(view, go.GetComponent<Button>());
                typeof(CustomScreenView).GetMethod("UpdateUndoButton", flags).Invoke(view, null);
                Assert.IsFalse(go.GetComponent<Button>().interactable);
            }
            finally { Object.DestroyImmediate(go); }
        }
        [Test]
        public void 加工Undoボタンは閉鎖と確定中に非表示となり再開時に戻る()
        {
            var model = new CustomScreenSystem(new Deck(new List<Card>()));
            var root = new GameObject("UndoVisibility", typeof(RectTransform));
            var buttonObject = new GameObject("UndoButton", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(root.transform);
            try
            {
                var view = root.AddComponent<CustomScreenView>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CustomScreenView).GetField("customScreen", flags).SetValue(view, model);
                typeof(CustomScreenView).GetField("undoCustomizationButton", flags).SetValue(view, buttonObject.GetComponent<Button>());
                var refresh = typeof(CustomScreenView).GetMethod("UpdateUndoButton", flags);
                refresh.Invoke(view, null);
                Assert.IsFalse(buttonObject.activeSelf);
                model.Open(new Card[0], new MockRandomProvider(0));
                refresh.Invoke(view, null);
                Assert.IsTrue(buttonObject.activeSelf);
                Assert.IsFalse(buttonObject.GetComponent<Button>().interactable);
                model.Confirm();
                refresh.Invoke(view, null);
                Assert.IsFalse(buttonObject.activeSelf);
                model.Close();
                refresh.Invoke(view, null);
                Assert.IsFalse(buttonObject.activeSelf);
                model.Open(new Card[0], new MockRandomProvider(0));
                refresh.Invoke(view, null);
                Assert.IsTrue(buttonObject.activeSelf);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void 共通床は二対一の切り出しを等比描画し戦闘前後で接地面を保つ()
        {
            var go = new GameObject("SharedFloor");
            var texture = new Texture2D(64, 64);
            var source = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64f);
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = go.AddComponent<WorldBattleGridView>();
                typeof(WorldBattleGridView).GetField("panelSprite", flags).SetValue(grid, source);
                typeof(WorldBattleGridView).GetField("panelScale", flags).SetValue(grid, 1f);
                typeof(WorldBattleGridView).GetMethod("Awake", flags).Invoke(grid, null);
                var floor = go.GetComponentsInChildren<SpriteRenderer>();
                foreach (var renderer in floor)
                {
                    Assert.AreEqual(64f, renderer.sprite.rect.width);
                    Assert.AreEqual(32f, renderer.sprite.rect.height);
                    Assert.AreEqual(renderer.transform.localScale.x, renderer.transform.localScale.y);
                    Assert.AreEqual(1.6f, renderer.bounds.size.x, 0.0001f);
                    Assert.AreEqual(0.8f, renderer.bounds.size.y, 0.0001f);
                    if (renderer.enabled) Assert.AreEqual(-0.3f, renderer.bounds.max.y, 0.0001f);
                }
                grid.Show(new BattleField(), 0f);
                var occupied = new System.Collections.Generic.HashSet<int>();
                foreach (var renderer in floor)
                {
                    if (!renderer.enabled) continue;
                    Assert.AreEqual(-0.3f, renderer.bounds.max.y, 0.0001f);
                    Assert.IsTrue(occupied.Add(Mathf.RoundToInt(renderer.transform.position.x / grid.CellSize)), "床の二重表示");
                }
                grid.Hide();
                Assert.IsTrue(go.activeSelf);
                Assert.IsTrue(System.Array.Exists(floor, renderer => renderer.enabled));
                Assert.AreEqual(3 * 1.6f, grid.SnapWorldXToCellCenter(5.5f), 0.0001f);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(source); Object.DestroyImmediate(texture); }
        }

        [TestCase(1f)]
        [TestCase(1.6f)]
        [TestCase(1.92f)]
        [TestCase(2.56f)]
        public void 表示単位を変えても論理マス距離と共通カメラの七マス幅を保つ(float units)
        {
            var gridObject = new GameObject("GridUnits");
            var viewObject = new GameObject("FieldUnits");
            var cameraObject = new GameObject("CameraUnits");
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = gridObject.AddComponent<WorldBattleGridView>();
                typeof(WorldBattleGridView).GetField("panelSize", flags).SetValue(grid, units);
                typeof(WorldBattleGridView).GetField("originX", flags).SetValue(grid, 12.5f);
                Assert.AreEqual(units, grid.GridIndexToWorldX(4) - grid.GridIndexToWorldX(3), 0.0001f);
                Assert.AreEqual(3.75f, grid.WorldToLogical(grid.LogicalToWorld(3.75f)), 0.0001f);
                Assert.AreEqual(12.5f + 2.5f * units, grid.GridCenterWorldX, 0.0001f);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true; camera.aspect = 16f / 9f;
                var view = viewObject.AddComponent<GameFieldView>();
                typeof(GameFieldView).GetField("fieldCamera", flags).SetValue(view, camera);
                typeof(GameFieldView).GetField("battleGridView", flags).SetValue(view, grid);
                typeof(GameFieldView).GetMethod("UpdateCameraFraming", flags).Invoke(view, null);
                Assert.AreEqual(7f * units, camera.orthographicSize * 2f * camera.aspect, 0.0001f);
                Assert.AreEqual(7.5f * units, view.LogicalToWorld(7.5f), 0.0001f);
                Assert.AreEqual(Vector3.one, view.transform.localScale);
            }
            finally { Object.DestroyImmediate(gridObject); Object.DestroyImmediate(viewObject); Object.DestroyImmediate(cameraObject); }
        }
        [Test]
        public void 足元表示差分は親座標を変えず再適用でも累積しない()
        {
            var go = new GameObject("FootlineView");
            var root = new GameObject("LogicalPlayer");
            var spriteObject = new GameObject("PlayerSprite");
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var grid = go.AddComponent<WorldBattleGridView>();
                var view = go.AddComponent<GameFieldView>();
                root.transform.position = new Vector3(12f, 3f, 0f);
                spriteObject.transform.SetParent(root.transform, false);
                spriteObject.transform.localPosition = new Vector3(0f, -0.3f, 0f);
                var sprite = spriteObject.AddComponent<SpriteRenderer>();
                typeof(GameFieldView).GetField("playerTransform", flags).SetValue(view, root.transform);
                typeof(GameFieldView).GetField("playerSprite", flags).SetValue(view, sprite);
                typeof(GameFieldView).GetField("battleGridView", flags).SetValue(view, grid);
                var apply = typeof(GameFieldView).GetMethod("ApplyCharacterDisplayOffset", flags);
                apply.Invoke(view, null);
                apply.Invoke(view, null);
                Assert.AreEqual(-0.36f, view.CharacterDisplayOffsetY, 0.0001f);
                Assert.AreEqual(-0.66f, sprite.transform.localPosition.y, 0.0001f);
                Assert.AreEqual(new Vector3(12f, 3f, 0f), root.transform.position);
                Assert.AreEqual(Vector3.one, sprite.transform.localScale);
                Assert.AreEqual(1.6f, view.LogicalToWorld(1f), 0.0001f);
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(root); }
        }
        [Test]
        public void 前景は床より下を覆い等比タイルで控えめに速く流れる()
        {
            var go = new GameObject("ForegroundTest");
            var cameraObject = new GameObject("ForegroundCamera");
            var texture = new Texture2D(64, 16);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 16), new Vector2(0.5f, 0.5f), 64f);
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true; camera.aspect = 16f / 9f; camera.orthographicSize = 3.15f;
                camera.transform.position = new Vector3(0f, 1.275f, -10f);
                var grid = go.AddComponent<WorldBattleGridView>();
                var layer = go.AddComponent<ScrollingBackgroundView>();
                typeof(ScrollingBackgroundView).GetField("targetCamera", flags).SetValue(layer, camera);
                typeof(ScrollingBackgroundView).GetField("foregroundGrid", flags).SetValue(layer, grid);
                var refresh = typeof(ScrollingBackgroundView).GetMethod("UpdateForeground", flags);
                refresh.Invoke(layer, null);
                Assert.IsNull(typeof(ScrollingBackgroundView).GetField("foregroundRoot", flags).GetValue(layer), "素材なしで仮前景を出さない");
                typeof(ScrollingBackgroundView).GetField("foregroundSprite", flags).SetValue(layer, sprite);
                refresh.Invoke(layer, null);
                var root = (Transform)typeof(ScrollingBackgroundView).GetField("foregroundRoot", flags).GetValue(layer);
                var fill = root.Find("ForegroundOpaqueBase").GetComponent<SpriteRenderer>();
                Assert.AreEqual(-1.1f, fill.bounds.max.y, 0.0001f);
                Assert.AreEqual(-1.875f, fill.bounds.min.y, 0.0001f);
                Assert.GreaterOrEqual(fill.bounds.size.x, 11.2f);
                var rock = root.Find("ForegroundRock_0").GetComponent<SpriteRenderer>();
                Assert.AreEqual(rock.transform.localScale.x, rock.transform.localScale.y);
                Assert.AreEqual(-1.1f, rock.bounds.max.y, 0.0001f);
                typeof(ScrollingBackgroundView).GetField("foregroundOpaqueTop", flags).SetValue(layer, 0.25f);
                refresh.Invoke(layer, null);
                Assert.AreEqual(-1.1f, rock.bounds.min.y + rock.bounds.size.y * 0.25f, 0.0001f, "透過余白ではなく不透明部分の最高点を床下端に合わせる");
                float before = rock.transform.position.x;
                camera.transform.position += new Vector3(0.1f, 0f, 0f);
                refresh.Invoke(layer, null);
                Assert.AreEqual(-0.012f, rock.transform.position.x - before, 0.0001f);
                camera.transform.position -= new Vector3(0.1f, 0f, 0f);
                refresh.Invoke(layer, null);
                Assert.AreEqual(before, rock.transform.position.x, 0.0001f);
                // EditModeでは通常MonoBehaviourのライフサイクルが自動実行されない。
                typeof(ScrollingBackgroundView).GetMethod("OnDisable", flags).Invoke(layer, null);
                Assert.IsFalse(root.gameObject.activeSelf);
            }
            finally
            {
                var layer = go.GetComponent<ScrollingBackgroundView>();
                if (layer != null) typeof(ScrollingBackgroundView).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(layer, null);
                Object.DestroyImmediate(go); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
            }
        }
    }
}

namespace Olca.Tests
{
    [NUnit.Framework.TestFixture]
    public class EnemyTelegraphViewTests
    {
        [NUnit.Framework.Test]
        public void 予告描画は同一マスで一つ床より前キャラより後で戦闘切替時に解除する()
        {
            var root=new UnityEngine.GameObject("TelegraphViewTest");
            try{
                var grid=root.AddComponent<Olca.Presentation.Field.WorldBattleGridView>();
                typeof(Olca.Presentation.Field.WorldBattleGridView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grid,null);
                grid.Show(new Olca.Core.Logic.Battle.BattleField(),0);
                var tile=new Olca.Core.Domain.Vector2Int(1,0);grid.ShowTelegraphs(new[]{tile,tile});
                var marker=root.transform.Find("EnemyAttackTelegraph_1");NUnit.Framework.Assert.IsTrue(marker.gameObject.activeSelf);NUnit.Framework.Assert.AreEqual(5,marker.childCount);
                foreach(var renderer in marker.GetComponentsInChildren<UnityEngine.SpriteRenderer>()){NUnit.Framework.Assert.AreEqual(2,renderer.sortingOrder);NUnit.Framework.Assert.Greater(renderer.color.a,0);}
                NUnit.Framework.Assert.IsFalse(root.transform.Find("EnemyAttackTelegraph_2").gameObject.activeSelf);
                grid.Hide();NUnit.Framework.Assert.IsFalse(marker.gameObject.activeSelf);grid.Show(new Olca.Core.Logic.Battle.BattleField(),0);NUnit.Framework.Assert.IsFalse(marker.gameObject.activeSelf);
                grid.ShowTelegraphs(new[]{new Olca.Core.Domain.Vector2Int(2,0)});NUnit.Framework.Assert.IsFalse(marker.gameObject.activeSelf);NUnit.Framework.Assert.IsTrue(root.transform.Find("EnemyAttackTelegraph_2").gameObject.activeSelf);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
        [NUnit.Framework.Test]
        public void 描画直前に最新予告を読み中断した同フレームに古い枠を残さない()
        {
            var root=new UnityEngine.GameObject("TelegraphLateRead");
            try{
                var grid=root.AddComponent<Olca.Presentation.Field.WorldBattleGridView>();typeof(Olca.Presentation.Field.WorldBattleGridView).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(grid,null);
                var player=new Olca.Core.Domain.PlayerCharacter(100,new Olca.Core.Domain.Vector2Int(1,0));var enemy=new Olca.Core.Domain.Enemy("e","テスト",100,Olca.Core.Domain.ElementType.None,new Olca.Core.Domain.Vector2Int(3,0),poiseDefinition:new Olca.Core.Domain.EnemyPoiseDefinition(30,3));var battle=new Olca.Core.Logic.Battle.BattleSystem(player,new System.Collections.Generic.List<Olca.Core.Domain.Enemy>{enemy},new Olca.Core.Domain.Deck());
                var card=new Olca.Core.Domain.Card("fixture","テスト",1,Olca.Core.Domain.CardRarity.Normal,Olca.Core.Domain.CardCategory.Attack,Olca.Core.Domain.ElementType.None,Olca.Core.Domain.CardSeries.Sword,7,"",startupFrames:6,poiseDamage:0);
                battle.TrySwitchEnemyActionPattern(enemy,new System.Collections.Generic.List<Olca.Core.Logic.Battle.IEnemyAIAction>{new Olca.Core.Logic.Battle.EnemyCardAction(card,1000)});grid.Show(battle.Field,0);grid.BindTelegraphs(battle);battle.TickFrame(17);grid.Refresh();var marker=root.transform.Find("EnemyAttackTelegraph_1");NUnit.Framework.Assert.IsTrue(marker.gameObject.activeSelf);
                enemy.TakeDamage(40,Olca.Core.Domain.ElementType.None);grid.Refresh();NUnit.Framework.Assert.IsFalse(marker.gameObject.activeSelf);
            }finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
