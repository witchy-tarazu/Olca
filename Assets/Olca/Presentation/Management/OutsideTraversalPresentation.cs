using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Integration;

namespace Olca.Presentation.Exploration
{
    /// <summary>CC0横向きコマを読むAnnex限定の試用表示。時計・攻撃・保存は所有しない。</summary>
    public static class AnnexGoblinSprite
    {
        public static int FrameIndex(long frame,int windup,bool attacking,bool staggered)
            =>staggered?8+(int)(Math.Max(0,frame)/8%2):attacking&&windup>=0&&windup<=12?4+Math.Min(3,(12-windup)/3):(int)(Math.Max(0,frame)/10%4);
        public static Sprite Resolve(Olca.Unity.GeneratedSpriteAssets.Lease lease,int frame)
        {
            if(lease==null||frame<0||frame>=10)return null;
            var sprites=lease.Get<Sprite[]>("AnnexGoblin418",builder=>{
                var data=Resources.Load<TextAsset>("OlcaPickups/annex_goblin_v1");if(data==null)return null;
                var texture=builder.CreateTexture(2,2);if(!texture.LoadImage(data.bytes,true))return null;
                texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var result=new Sprite[10];for(int n=0;n<10;n++){result[n]=builder.CreateSprite(texture,new Rect(n*256,0,256,256),new Vector2(.5f,20f/256),100);result[n].name="trial_annex_goblin_"+n;}return result;
            });return sprites==null?null:sprites[frame];
        }
    }
    public sealed partial class OutsideExplorationView
    {
        private Button mapDoor;private Image mapDoorMarker;private Action travelMap;private Func<bool> canTravelMap;private Func<string> mapDoorMessage;
        public void ConfigureMapDoor(Action travel,Func<bool> canUse,Func<string> message)
        {
            travelMap=travel;canTravelMap=canUse;mapDoorMessage=message;
            if(mapDoor==null){var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,gaugeLabel.font,null);mapDoor=factory.Button("OutsideMapDoor","シヨウ マップ イドウ（カリ）",transform,new Vector2(.5f,1),new Vector2(0,-170));Olca.Presentation.Dungeon.UiLook.MenuButton(mapDoor);mapDoor.navigation=new Navigation{mode=Navigation.Mode.None};var text=mapDoor.GetComponentInChildren<Text>();text.resizeTextForBestFit=true;text.resizeTextMinSize=18;text.resizeTextMaxSize=24;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.raycastTarget=false;}
            if(mapDoorMarker==null){var go=new GameObject("OutsideMapDoorMarker",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);mapDoorMarker=go.GetComponent<Image>();mapDoorMarker.sprite=trialDoor.sprite;mapDoorMarker.type=Image.Type.Sliced;mapDoorMarker.color=new Color(.15f,.45f,.55f,.6f);mapDoorMarker.raycastTarget=false;var rect=mapDoorMarker.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.21f);rect.pivot=new Vector2(.5f,0);rect.sizeDelta=new Vector2(90,190);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());}
            var captured=context;var lease=presentation;mapDoor.onClick=new Button.ButtonClickedEvent();mapDoor.onClick.AddListener(()=>{
                if(this==null||!IsOpen||!CanPlay||captured==null||!ReferenceEquals(context,captured)||!captured.IsCurrent||lease==null||!ReferenceEquals(presentation,lease)||!lease.IsCurrent||!mapDoor.isActiveAndEnabled||!mapDoor.interactable||canTravelMap?.Invoke()!=true)return;
                var selected=UnityEngine.EventSystems.EventSystem.current;if(selected!=null&&selected.currentSelectedGameObject==mapDoor.gameObject)selected.SetSelectedGameObject(null);travelMap?.Invoke();
            });
        }
        private void RenderMapDoor(float x,float verticalOffset)
        {
            if(mapDoor==null)return;var core=presentation.Core;bool registered=travelMap!=null&&OutsideTrialRoute.KnownMap(core.TrialMapId)&&core.Definition.Id=="validation-landing-32";
            mapDoorMarker.gameObject.SetActive(registered);mapDoorMarker.rectTransform.anchoredPosition=new Vector2(x,-verticalOffset*pixelsPerWorldUnit);
            mapDoor.gameObject.SetActive(registered&&Math.Abs(core.Position-OutsideTrialRoute.DoorPosition(core))<=3*OutsideMovementDefinition.UnitsPerWorldUnit);
            mapDoor.GetComponent<RectTransform>().sizeDelta=new Vector2(Mathf.Clamp(((RectTransform)transform).rect.width-1440,180,380),90);mapDoor.interactable=CanPlay&&canTravelMap?.Invoke()==true;mapDoor.GetComponentInChildren<Text>().text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(mapDoorMessage?.Invoke()??"シヨウ マップ イドウ（カリ）");
        }
        private void ClearMapDoor(){travelMap=null;canTravelMap=null;mapDoorMessage=null;if(mapDoor!=null){mapDoor.onClick.RemoveAllListeners();mapDoor.interactable=false;mapDoor.gameObject.SetActive(false);}if(mapDoorMarker!=null)mapDoorMarker.gameObject.SetActive(false);}
        private Func<long> miniMapRevision;private Func<OutsideTrialMapOverview[]> miniMapSource;
        private Func<OutsideTrialNavigation> navigation;private Image miniMapTarget;private Text navigationLabel,miniMapTargetText;
        public void ConfigureNavigation(Func<OutsideTrialNavigation> source){navigation=source;}
        private void RenderNavigation(string mapId,double playerX,double playerY)
        {
            var target=navigation?.Invoke()?.Resolve(mapId);
            if(navigationLabel==null){var go=new GameObject("OutsideNavigation",typeof(RectTransform),typeof(Text));go.transform.SetParent(miniMapPanel,false);navigationLabel=go.GetComponent<Text>();navigationLabel.font=gaugeLabel.font;navigationLabel.fontSize=14;navigationLabel.color=new Color(1,.94f,.5f);navigationLabel.alignment=TextAnchor.MiddleCenter;navigationLabel.raycastTarget=false;navigationLabel.rectTransform.anchoredPosition=new Vector2(0,35);navigationLabel.rectTransform.sizeDelta=new Vector2(420,24);}
            navigationLabel.gameObject.SetActive(target!=null);miniMapPanel.Find("MiniMapTitle").gameObject.SetActive(target==null);miniMapTarget.gameObject.SetActive(target!=null);
            if(target==null)return;
            double x=target.GuidePosition/(double)OutsideMovementDefinition.UnitsPerWorldUnit;
            string direction=Math.Abs(x-playerX)<.5&&Math.Abs(playerY)<.5?"ココ":x>playerX?"→":x<playerX?"←":playerY>0?"↓":"↑";
            navigationLabel.text=direction+" "+target.Title+(target.ViaExit?" / イドウグチ":"")+"（カリ）";
            miniMapTarget.rectTransform.anchoredPosition=new Vector2(Mathf.Clamp(-200+(float)(x/4-miniMapLeft)*50,-192,192),Mathf.Clamp(-14-miniMapBottom*16,-22,22));
            // 同地点のP/Rを覆わない。目的地点自体は動かさず記号だけ横へ逃がす。
            var at=miniMapTarget.rectTransform.anchoredPosition;bool overlap=(at-miniMapPlayer.rectTransform.anchoredPosition).sqrMagnitude<24*24||(miniMapRecovery.gameObject.activeSelf&&(at-miniMapRecovery.rectTransform.anchoredPosition).sqrMagnitude<24*24);
            miniMapTarget.color=overlap?Color.clear:new Color(.6f,.3f,.02f,1);miniMapTargetText.color=new Color(1,.94f,.5f);miniMapTargetText.rectTransform.anchoredPosition=overlap?new Vector2(at.x>176?-18:18,0):Vector2.zero;
        }
        private OutsideTrialMapOverview miniMapData;private long miniMapShownRevision=long.MinValue;private string miniMapShownId;
        private RectTransform miniMapPanel;private Image[] miniMapCells;private Image miniMapPlayer,miniMapRecovery;private int miniMapLeft,miniMapBottom;private int miniMapDrawLeft=int.MinValue,miniMapDrawBottom;private long miniMapDrawRevision=long.MinValue;private string miniMapDrawId;
        // 全体マップと同じ確定済み開示だけを読む。表示は保存・発見・時計を進めない。
        public void ConfigureMiniMap(Func<long> revision,Func<OutsideTrialMapOverview[]> maps)
        {
            ClearMiniMap();miniMapRevision=revision;miniMapSource=maps;
        }
        private void RenderMiniMap()
        {
            if(miniMapSource==null||!IsOpen){if(miniMapPanel!=null)miniMapPanel.gameObject.SetActive(false);return;}
            var core=presentation.Core;string id=core.TrialMapId??OutsideMapRevealTrial.HomeMapId;
            if(!OutsideMapRevealTrial.KnownMap(id)){if(miniMapPanel!=null)miniMapPanel.gameObject.SetActive(false);return;}
            long revision=miniMapRevision?.Invoke()??-1;
            if(miniMapData==null||miniMapShownRevision!=revision||miniMapShownId!=id){miniMapData=miniMapSource()?.FirstOrDefault(m=>m.MapId==id);miniMapShownRevision=revision;miniMapShownId=id;}
            if(miniMapData==null){if(miniMapPanel!=null)miniMapPanel.gameObject.SetActive(false);return;}
            if(miniMapPanel==null)CreateMiniMap();miniMapPanel.gameObject.SetActive(true);
            double x=core.Position/(double)OutsideMapRevealTrial.CellSize,y=core.Height/(double)OutsideMapRevealTrial.CellSize;
            int columns=(int)Math.Ceiling(core.Definition.Width/(double)OutsideMapRevealTrial.CellSize);
            miniMapLeft=Math.Max(0,Math.Min(Math.Max(0,columns-8),(int)Math.Floor(x)-4));miniMapBottom=(int)Math.Floor(y)-1;
            if(miniMapDrawRevision!=revision||miniMapDrawId!=id||miniMapDrawLeft!=miniMapLeft||miniMapDrawBottom!=miniMapBottom){
            miniMapDrawRevision=revision;miniMapDrawId=id;miniMapDrawLeft=miniMapLeft;miniMapDrawBottom=miniMapBottom;
            for(int row=0;row<3;row++)for(int column=0;column<8;column++){
                int cx=miniMapLeft+column,cy=miniMapBottom+row;bool revealed=miniMapData.ItemRevealed||miniMapData.Cells.Any(c=>c.X==cx&&c.Y==cy);
                miniMapCells[row*8+column].color=revealed?new Color(.1f,.42f,.56f,1):new Color(.025f,.075f,.1f,1);
            }
            }
            miniMapPlayer.rectTransform.anchoredPosition=new Vector2(-200+(float)(x-miniMapLeft)*50,-24+(float)(y-miniMapBottom)*16);
            bool recovery=miniMapData.RecoveryVisible&&miniMapBottom<=0&&miniMapBottom+3>0&&miniMapLeft<=1&&miniMapLeft+8>1;
            miniMapRecovery.gameObject.SetActive(recovery);if(recovery){miniMapRecovery.rectTransform.anchoredPosition=new Vector2(-200+(1.5f-miniMapLeft)*50,-8-miniMapBottom*16);miniMapRecovery.color=miniMapData.RecoveryKnown?new Color(.02f,.4f,.42f,1):new Color(.22f,.27f,.32f,1);}
            RenderNavigation(id,core.Position/(double)OutsideMovementDefinition.UnitsPerWorldUnit,core.Height/(double)OutsideMovementDefinition.UnitsPerWorldUnit);
        }
        private void CreateMiniMap()
        {
            var go=new GameObject("OutsideMiniMap",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);miniMapPanel=(RectTransform)go.transform;miniMapPanel.anchorMin=miniMapPanel.anchorMax=new Vector2(0,1);miniMapPanel.anchoredPosition=new Vector2(255,-348);miniMapPanel.sizeDelta=new Vector2(430,96);var panel=go.GetComponent<Image>();panel.color=new Color(.005f,.03f,.045f,.97f);panel.raycastTarget=false;
            Text Label(string name,string text,Vector2 at,Vector2 size,int points){var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(go.transform,false);var t=obj.GetComponent<Text>();t.font=gaugeLabel.font;t.text=text;t.fontSize=points;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.rectTransform.anchoredPosition=at;t.rectTransform.sizeDelta=size;return t;}
            Label("MiniMapTitle","ミニマップ（シヨウ）",new Vector2(0,35),new Vector2(410,24),14);Label("MiniMapLegend","P: ゲンザイチ / R: カイフク / !: アンナイ",new Vector2(0,-36),new Vector2(420,20),12);
            Image Tile(string name,Vector2 at,Vector2 size){var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(go.transform,false);var image=obj.GetComponent<Image>();image.raycastTarget=false;image.rectTransform.anchoredPosition=at;image.rectTransform.sizeDelta=size;return image;}
            miniMapCells=new Image[24];for(int row=0;row<3;row++)for(int column=0;column<8;column++)miniMapCells[row*8+column]=Tile("MiniMapCell_"+column+"_"+row,new Vector2(-175+column*50,-16+row*16),new Vector2(48,14));
            Image Marker(string name,string text,Color color){var im=Tile(name,Vector2.zero,new Vector2(16,16));im.color=color;var label=Label(name+"Label",text,Vector2.zero,new Vector2(16,20),12);label.transform.SetParent(im.transform,false);return im;}
            miniMapRecovery=Marker("MiniMapRecovery","R",Color.gray);miniMapTarget=Marker("MiniMapTarget","!",new Color(.6f,.3f,.02f,1));miniMapTargetText=miniMapTarget.GetComponentInChildren<Text>();miniMapPlayer=Marker("MiniMapPlayer","P",new Color(.6f,.34f,.08f,1));
        }
        private void ClearMiniMap(){navigation=null;miniMapRevision=null;miniMapSource=null;miniMapData=null;miniMapShownRevision=long.MinValue;miniMapShownId=miniMapDrawId=null;miniMapDrawRevision=long.MinValue;miniMapDrawLeft=int.MinValue;if(miniMapPanel!=null)miniMapPanel.gameObject.SetActive(false);}
        private Text mapNameLabel;
        private void RenderMapName()
        {
            double opacity=presentation?.MapNameOpacity??0;
            if(opacity>0&&mapNameLabel==null){
                var go=new GameObject("OutsideMapName",typeof(RectTransform),typeof(Text),typeof(Shadow));go.transform.SetParent(transform,false);mapNameLabel=go.GetComponent<Text>();mapNameLabel.font=gaugeLabel.font;mapNameLabel.fontSize=44;mapNameLabel.alignment=TextAnchor.MiddleCenter;mapNameLabel.raycastTarget=false;mapNameLabel.horizontalOverflow=HorizontalWrapMode.Wrap;mapNameLabel.verticalOverflow=VerticalWrapMode.Overflow;
                var rect=mapNameLabel.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(0,90);rect.sizeDelta=new Vector2(1000,90);var shadow=go.GetComponent<Shadow>();shadow.effectColor=new Color(.005f,.025f,.04f,.95f);shadow.effectDistance=new Vector2(2,-2);
            }
            if(mapNameLabel==null)return;mapNameLabel.gameObject.SetActive(opacity>0);if(opacity<=0)return;mapNameLabel.text=presentation.MapName;mapNameLabel.color=new Color(.8f,.98f,1,(float)opacity);mapNameLabel.transform.SetAsLastSibling();
        }
        private Image outsideKamui;
        private void RenderOutsideKamui()
        {
            if(!Olca.Presentation.Effects.TrialFireKamuiVisual.IsActive(presentation?.Player)||player==null||!player.isActiveAndEnabled){ClearOutsideKamui();return;}
            var sprite=outsideKamui!=null?outsideKamui.sprite:Olca.Presentation.Effects.TrialFireKamuiVisual.LoadSprite();if(sprite==null){ClearOutsideKamui();return;}
            if(outsideKamui==null){var go=new GameObject("OutsideTrialFireKamui",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);outsideKamui=go.GetComponent<Image>();outsideKamui.raycastTarget=false;}
            var r=outsideKamui.rectTransform;r.anchorMin=player.rectTransform.anchorMin;r.anchorMax=player.rectTransform.anchorMax;r.pivot=sprite.pivot/sprite.rect.size;
            r.anchoredPosition=player.rectTransform.anchoredPosition;r.sizeDelta=Vector2.one*Olca.Presentation.Effects.TrialFireKamuiVisual.SizeWorld*pixelsPerWorldUnit;r.localScale=player.rectTransform.localScale;
            outsideKamui.sprite=sprite;outsideKamui.color=new Color(1,1,1,Olca.Presentation.Effects.TrialFireKamuiVisual.Opacity);outsideKamui.enabled=true;
            var target=outsideDefensiveField!=null?outsideDefensiveField.transform:player.transform;int before=target.GetSiblingIndex();
            if(outsideKamui.transform.GetSiblingIndex()<before)before--; // 自身を除いた挿入先。連続Renderでも前後を往復させない。
            outsideKamui.transform.SetSiblingIndex(before);
        }
        private void ClearOutsideKamui(){if(outsideKamui!=null)outsideKamui.enabled=false;}
        private Image outsideDefensiveField;
        private Olca.Unity.GeneratedSpriteAssets.Lease outsideDefensiveAssets;
        // バトルと同じ外周素材/校正を使う。表示だけを所有し、防御状態や寿命は変更しない。
        private void RenderOutsideDefensiveField(bool barrier,bool aura)
        {
            if(player==null||!player.isActiveAndEnabled||player.sprite==null||barrier==aura){ClearOutsideDefensiveField();return;}
            if(outsideDefensiveField==null){var go=new GameObject("OutsideDefensiveField",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);outsideDefensiveField=go.GetComponent<Image>();outsideDefensiveField.raycastTarget=false;go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());}
            var kind=barrier?Olca.Presentation.Effects.DefensiveFieldKind.Barrier:Olca.Presentation.Effects.DefensiveFieldKind.Aura;
            var sprite=Olca.Presentation.Effects.DefensiveFieldVisual.GetSprite(kind,outsideDefensiveAssets??(outsideDefensiveAssets=Olca.Unity.GeneratedSpriteAssets.Acquire()));
            if(sprite==null){ClearOutsideDefensiveField();return;}outsideDefensiveAssets.BindNativeOwner(outsideDefensiveField);
            var bounds=Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(player.sprite);var rect=outsideDefensiveField.rectTransform;rect.anchorMin=player.rectTransform.anchorMin;rect.anchorMax=player.rectTransform.anchorMax;rect.pivot=new Vector2(.5f,.5f);
            rect.anchoredPosition=player.rectTransform.anchoredPosition+new Vector2(bounds.center.x*player.rectTransform.localScale.x,bounds.center.y+bounds.size.y*.1f)*pixelsPerWorldUnit;
            rect.sizeDelta=new Vector2(Mathf.Max(bounds.size.x*1.15f,bounds.size.y*.62f),bounds.size.y*1.15f)*pixelsPerWorldUnit;outsideDefensiveField.sprite=sprite;outsideDefensiveField.color=new Color(1,1,1,.55f);outsideDefensiveField.enabled=true;
        }
        private void ClearOutsideDefensiveField(){if(outsideDefensiveField!=null){outsideDefensiveField.enabled=false;outsideDefensiveField.sprite=null;}outsideDefensiveAssets?.Dispose();outsideDefensiveAssets=null;}
        private Olca.Presentation.Effects.TrialFireBodyMotion trialFireBody;
        private Sprite ResolveBodyCostume(Sprite sprite)=>(trialFireBody??(trialFireBody=new Olca.Presentation.Effects.TrialFireBodyMotion())).Resolve(sprite,presentation?.Player);
        private Sprite[] trialComboSprites,trialAirDownSprites;
        private TrialGroundComboMotion trialGroundCombo;
        private TrialChargeMotion trialChargeMotion;
        private TrialBombThrowMotion trialBombMotion;
        private TrialSwordSwingMotion trialSwordMotion;
        private TrialShotBodyMotion trialShotBody;
        private TrialSpearBodyMotion trialSpearBody;
        private TrialWideSwordEffect trialWideSwordEffect;
        private TrialSupportBodyMotion trialSupportBody;private long supportPulseSeen,supportPulseSuppressed;private int supportPulseFacing;private string supportPulseStyle;
        private void PaintSupportFeedback(){healingCanvas?.Paint(Olca.Unity.BattleVisualClock.DeltaTime,Time.frameCount);shieldActivationCanvas?.Paint(Olca.Unity.BattleVisualClock.DeltaTime,Time.frameCount);}
        private Sprite TrialSupportSprite()
        {
            long heal=healingCanvas?.PulseOrder??0,shield=shieldActivationCanvas?.PulseOrder??0,order=Math.Max(heal,shield);if(order==0||order<supportPulseSeen||order<=supportPulseSuppressed)return null;
            var actor=presentation?.Player;var core=presentation?.Core;if(order>supportPulseSeen){supportPulseSeen=order;supportPulseFacing=core?.Facing??1;supportPulseStyle=actor?.DepartureStyle.Id;}
            bool costume=actor!=null&&(actor.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.BasicId||actor.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id);
            bool bomb=presentation?.InstantBombReleasedAt>=0&&core!=null&&core.Frame-presentation.InstantBombReleasedAt<TrialBombThrowMotion.InstantReleaseVisualFrames;
            if(presentation?.IsCurrent!=true||actor==null||core==null||!costume||actor.IsDead||actor.IsStaggered||actor.IsParalyzed||!core.IsGrounded||core.IsMoving||core.IsDashing||presentation.IsDeckEditingLocation||presentation.HasAttackAction||presentation.NormalKickKind.HasValue||presentation.IsCharging||bomb||core.Facing!=supportPulseFacing||actor.DepartureStyle.Id!=supportPulseStyle){supportPulseSuppressed=Math.Max(supportPulseSuppressed,order);return null;}
            bool isShield=shield>heal;float age=isShield?shieldActivationCanvas.AnimationAge:healingCanvas.AnimationAge;return (trialSupportBody??(trialSupportBody=new TrialSupportBodyMotion())).Resolve(isShield,age,actor.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id);
        }

        private Sprite TrialShotSprite()
        {
            if(presentation?.IsFiringShot!=true||presentation.Player==null||presentation.Player.IsDead||presentation.Player.IsStaggered||presentation.Player.IsParalyzed||presentation.Player.DepartureStyle.Id!=Olca.Core.Domain.DepartureStyleDefinition.BasicId&&presentation.Player.DepartureStyle.Id!=Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id)return null;
            return (trialShotBody??(trialShotBody=new TrialShotBodyMotion())).Resolve(presentation.ShotFiringElapsed,presentation.ShotFiringStartup,presentation.ShotFiringRecovery,presentation.Player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id,presentation.Core.IsGrounded);
        }
        private Sprite TrialSwordSprite()
        {
            if(presentation?.IsSwingingSword!=true||presentation.Player==null||presentation.Player.IsDead||presentation.Player.IsStaggered||presentation.Player.IsParalyzed||presentation.Player.DepartureStyle.Id!=Olca.Core.Domain.DepartureStyleDefinition.BasicId&&presentation.Player.DepartureStyle.Id!=Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id)return null;
            // スピアだけ水平な構え・突き・復帰へ接続し、欠損時は既存の表示を保つ。
            if(presentation.SwordSwingCardId==Olca.Core.Domain.TrialInitialCardDefinitions.Spear){var spear=(trialSpearBody??(trialSpearBody=new TrialSpearBodyMotion())).Resolve(presentation.SwordSwingElapsed,presentation.SwordSwingStartup,presentation.SwordSwingRecovery,presentation.Player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id,presentation.Core.IsGrounded);if(spear!=null)return spear;}
            // 地上/空中と衣装に合う専用6コマを表示し、欠損時だけ既存の試用姿勢へ戻す。
            var dedicated=(trialSwordMotion??(trialSwordMotion=new TrialSwordSwingMotion())).Resolve(presentation.SwordSwingElapsed,presentation.SwordSwingStartup,presentation.SwordSwingRecovery,presentation.Player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id,presentation.Core.IsGrounded);if(dedicated!=null)return dedicated;
            return (trialBombMotion??(trialBombMotion=new TrialBombThrowMotion())).Resolve(presentation.SwordSwingElapsed,presentation.SwordSwingStartup,presentation.SwordSwingRecovery,presentation.Core.IsGrounded,presentation.Player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id);
        }
        private Sprite TrialBombSprite()
        {
            // 試用の地上/空中投擲。登録済みBasicとカエンだけを借用する。
            if(presentation?.IsCurrent!=true||presentation.Player==null||
                presentation.Player.IsDead||presentation.Player.IsStaggered||presentation.Player.IsParalyzed||
                presentation.Player.DepartureStyle.Id!=Olca.Core.Domain.DepartureStyleDefinition.BasicId&&presentation.Player.DepartureStyle.Id!=Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id)return null;
            if(presentation.IsThrowingBomb)return (trialBombMotion??(trialBombMotion=new TrialBombThrowMotion())).Resolve(
                presentation.BombThrowElapsedFrames,presentation.BombThrowStartupFrames,presentation.BombThrowRecoveryFrames,presentation.Core.IsGrounded,presentation.Player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id);
            // 既存の0/0定義は実発射と同時の解放→復帰だけを短く描く。行動/移動を拘束しない。
            if(presentation.InstantBombReleasedAt<0||presentation.HasAttackAction||presentation.IsCharging||
                presentation.IsDeckEditingLocation||presentation.Core.IsGrounded&&presentation.Core.IsMoving||presentation.Core.IsGrounded==presentation.InstantBombWasAirborne||presentation.Core.Facing!=presentation.BombThrowFacing)return null;
            return (trialBombMotion??(trialBombMotion=new TrialBombThrowMotion())).Resolve(
                presentation.Core.Frame-presentation.InstantBombReleasedAt,0,TrialBombThrowMotion.InstantReleaseVisualFrames,presentation.Core.IsGrounded,presentation.Player.DepartureStyle.Id==Olca.Core.Domain.DepartureStyleDefinition.TrialFire.Id);
        }
        private Sprite trialKickPrepare,trialKickContact,trialAirForwardContact,trialDashKickContact;
        private Sprite TrialKickSprite()
        {
            // 専用の試用4姿勢。Sessionの論理phaseだけを読み、表示から時刻・位置を変更しない。
            if(presentation?.Player==null||presentation.Player.IsDead||presentation.Player.IsStaggered||presentation.Player.IsParalyzed)return null;
            // 保持中は待機・移動・ジャンプの本来の動作を続け、溜めは別レイヤーの演出で示す。
            if(presentation.IsChargedKick){
                bool ground=presentation.NormalKickKind<=OutsideKickKind.GroundThird;
                var pose=(trialChargeMotion??(trialChargeMotion=new TrialChargeMotion())).Resolve(ground,false,
                    presentation.ChargeFrames>=presentation.ChargeRequiredFrames,presentation.KickElapsedFrames,presentation.KickTiming,
                    Olca.Presentation.Effects.TrialFireKamuiVisual.IsActive(presentation.Player));
                if(pose!=null)return pose;
            }
            var kind=presentation?.NormalKickKind;
            if(!kind.HasValue||presentation.Player==null||presentation.Player.IsDead||presentation.Player.IsStaggered||presentation.Player.IsParalyzed)return null;
            if(kind.Value<=OutsideKickKind.GroundThird){
                var frame=(trialGroundCombo??(trialGroundCombo=new TrialGroundComboMotion())).Resolve(kind.Value,presentation.KickElapsedFrames,presentation.KickTiming,Olca.Presentation.Effects.TrialFireKamuiVisual.IsActive(presentation.Player));
                if(frame!=null)return frame;
            }
            if(kind.Value==OutsideKickKind.Dash){
                if(trialDashKickContact==null)trialDashKickContact=Resources.Load<Sprite>("OlcaCharacterMotion/trial_dash_kick_contact");
                return presentation.IsKickHitWindow&&trialDashKickContact!=null?trialDashKickContact:null;
            }
            if(kind.Value==OutsideKickKind.AirForward){
                if(trialAirForwardContact==null)trialAirForwardContact=Resources.Load<Sprite>("OlcaCharacterMotion/trial_air_forward_kick_contact");
                // 前後隙は既存の上昇姿勢を暫定利用。時刻や物理位置は表示側で進めない。
                return trialAirForwardContact!=null&&riseSprite!=null?(presentation.IsKickHitWindow?trialAirForwardContact:riseSprite):null;
            }
            if(kind.Value==OutsideKickKind.AirDown){
                if(trialAirDownSprites==null)trialAirDownSprites=new[]{Resources.Load<Sprite>("OlcaCharacterMotion/trial_air_down_contact"),Resources.Load<Sprite>("OlcaCharacterMotion/trial_air_down_recovery")};
                return riseSprite!=null&&trialAirDownSprites[0]!=null&&trialAirDownSprites[1]!=null?(presentation.IsKickHitWindow?trialAirDownSprites[0]:presentation.IsKickRecovery?trialAirDownSprites[1]:riseSprite):null;
            }
            if(kind.Value==OutsideKickKind.GroundSecond||kind.Value==OutsideKickKind.GroundThird){
                if(trialComboSprites==null)trialComboSprites=new[]{Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_second_prepare"),Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_second_contact"),Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_third_prepare"),Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_third_contact")};
                int index=kind.Value==OutsideKickKind.GroundSecond?0:2;
                return trialComboSprites[index]!=null&&trialComboSprites[index+1]!=null?trialComboSprites[index+(presentation.IsKickHitWindow?1:0)]:null;
            }
            if(kind.Value>OutsideKickKind.GroundThird)return null;
            if(trialKickPrepare==null)trialKickPrepare=Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_kick_prepare");
            if(trialKickContact==null)trialKickContact=Resources.Load<Sprite>("OlcaCharacterMotion/trial_ground_kick_contact");
            if(trialKickPrepare==null||trialKickContact==null)return null;
            return presentation.IsKickHitWindow?trialKickContact:trialKickPrepare;
        }
        private Sprite traversalDashSprite;
        private Sprite[] wallSlideFrames;
        private Olca.Unity.ResourceManager traversalResources;
        private OutsideTraversalProfile renderedTraversal;
        private readonly List<Image> wallImages=new List<Image>();
        private readonly List<Image> gapImages=new List<Image>();
        private readonly Dictionary<Image,List<Image>> waterSurfaces=new Dictionary<Image,List<Image>>();private Button[] waterOccluders;private readonly Vector3[] waterCorners=new Vector3[4];private readonly List<Vector2> waterBlocked=new List<Vector2>();private readonly List<Vector2> waterVisible=new List<Vector2>();
        private readonly List<Image> waterImages=new List<Image>();private Image breathPanel,breathFill;private Text breathLabel;
        private void DestroyWaterImages(){foreach(var image in waterImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}waterImages.Clear();waterSurfaces.Clear();waterOccluders=null;}
        private void RenderWaterSurface(Image area)
        {
            // 水位は動かさず、既存POIの読取面だけで高輝度の線を遮る。背景の透明感は保つ。
            float width=area.rectTransform.rect.width,top=area.rectTransform.rect.height;waterBlocked.Clear();waterVisible.Clear();
            if(waterOccluders==null)waterOccluders=GetComponentsInChildren<Button>(true);
            foreach(var button in waterOccluders){if(button==null||!button.gameObject.activeInHierarchy)continue;var rect=button.transform as RectTransform;if(rect==null)continue;rect.GetWorldCorners(waterCorners);float left=float.PositiveInfinity,right=float.NegativeInfinity,bottom=float.PositiveInfinity,high=float.NegativeInfinity;foreach(var corner in waterCorners){var local=area.rectTransform.InverseTransformPoint(corner);left=Mathf.Min(left,local.x);right=Mathf.Max(right,local.x);bottom=Mathf.Min(bottom,local.y);high=Mathf.Max(high,local.y);}if(high<top-2||bottom>top||right<0||left>width)continue;waterBlocked.Add(new Vector2(Mathf.Max(0,left-2),Mathf.Min(width,right+2)));}
            waterBlocked.Sort((a,b)=>a.x.CompareTo(b.x));float cursor=0;foreach(var block in waterBlocked){if(block.x>cursor)waterVisible.Add(new Vector2(cursor,block.x));cursor=Mathf.Max(cursor,block.y);}if(cursor<width)waterVisible.Add(new Vector2(cursor,width));
            var lines=waterSurfaces[area];for(int i=0;i<waterVisible.Count;i++){if(i>=lines.Count){var go=new GameObject("WaterSurface"+i,typeof(RectTransform),typeof(Image));go.transform.SetParent(area.transform,false);var line=go.GetComponent<Image>();line.color=new Color(.45f,.95f,1,.9f);line.raycastTarget=false;line.rectTransform.anchorMin=line.rectTransform.anchorMax=new Vector2(0,1);line.rectTransform.pivot=new Vector2(0,1);lines.Add(line);}var image=lines[i];image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(waterVisible[i].x,0);image.rectTransform.sizeDelta=new Vector2(waterVisible[i].y-waterVisible[i].x,2);}for(int i=waterVisible.Count;i<lines.Count;i++)lines[i].gameObject.SetActive(false);
        }
        private void RenderBreath()
        {
            bool visible=presentation!=null&&presentation.ShouldShowBreath;
            if(visible&&breathPanel==null){
                var go=new GameObject("OutsideBreathGauge",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);breathPanel=go.GetComponent<Image>();breathPanel.color=new Color(.015f,.055f,.08f,.95f);breathPanel.raycastTarget=false;var rect=breathPanel.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,1);rect.pivot=new Vector2(.5f,1);rect.anchoredPosition=new Vector2(0,-150);rect.sizeDelta=new Vector2(220,14);
                var fill=new GameObject("BreathRemaining",typeof(RectTransform),typeof(Image));fill.transform.SetParent(go.transform,false);breathFill=fill.GetComponent<Image>();breathFill.color=new Color(.3f,.9f,1,1);breathFill.raycastTarget=false;breathFill.rectTransform.anchorMin=breathFill.rectTransform.anchorMax=new Vector2(0,.5f);breathFill.rectTransform.pivot=new Vector2(0,.5f);breathFill.rectTransform.anchoredPosition=new Vector2(2,0);breathFill.rectTransform.sizeDelta=new Vector2(216,10);
                var text=new GameObject("OutsideBreathLabel",typeof(RectTransform),typeof(Text));text.transform.SetParent(go.transform,false);breathLabel=text.GetComponent<Text>();breathLabel.font=movementGuideText.font;breathLabel.fontSize=18;breathLabel.verticalOverflow=VerticalWrapMode.Overflow;breathLabel.color=Color.white;breathLabel.alignment=TextAnchor.LowerCenter;breathLabel.raycastTarget=false;var tr=text.GetComponent<RectTransform>();tr.anchorMin=new Vector2(0,1);tr.anchorMax=Vector2.one;tr.pivot=new Vector2(.5f,0);tr.anchoredPosition=new Vector2(0,4);tr.sizeDelta=new Vector2(0,26);
            }
            if(breathPanel==null)return;breathPanel.gameObject.SetActive(visible);if(!visible)return;
            float fraction=(float)presentation.BreathFramesRemaining/presentation.BreathMaximumFrames;breathFill.rectTransform.sizeDelta=new Vector2(216*Mathf.Clamp01(fraction),10);breathLabel.text="コキュウ（カリ） "+(Math.Ceiling(presentation.BreathFramesRemaining/6d)/10d).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
        }
        private void DestroyGapImages(){foreach(var image in gapImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}gapImages.Clear();}
        private readonly Image[] afterimageImages=new Image[4];
        private void InitializeTraversal(Olca.Unity.ResourceManager resources,Olca.Presentation.Field.GameFieldView field)
        {
            traversalResources=resources;traversalDashSprite=field.AuthoredDashPlayerSprite;
            for(int i=0;i<afterimageImages.Length;i++){
                var go=new GameObject("OutsideDashAfterimage"+i,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);
                var image=afterimageImages[i]=go.GetComponent<Image>();image.raycastTarget=false;image.enabled=false;
                image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());
            }
        }
        private Sprite TraversalSprite(OutsideExplorationCore core)
        {
            if(presentation?.IsDashAttacking==true&&core.IsMoving)return traversalDashSprite; // 接触素材欠損時も既存ダッシュ姿勢へ戻す
            if(core.IsWallSliding){
                if(wallSlideFrames==null)wallSlideFrames=new[]{traversalResources.GetShared<Sprite>("OlcaTraversal/WallSlide_01"),traversalResources.GetShared<Sprite>("OlcaTraversal/WallSlide_02")};
                return wallSlideFrames[(int)(core.WallSlideFrame/6%wallSlideFrames.Length)];
            }
            // 壁蹴り中も通常ジャンプの上昇姿勢を使う。専用PNGは保持するが表示には接続しない。
            if(core.IsWallKicking)return riseSprite;
            // 地形で実際に停止している間は立ち姿。ダッシュの残時間や再開条件は変えない。
            return core.IsDashing&&core.IsMoving?traversalDashSprite:null;
        }
        private float ResolveVerticalViewOffset(OutsideExplorationCore core)
        {
            float Top(Sprite sprite)=>sprite!=null?sprite.bounds.max.y:0;
            float actorTop=Mathf.Max(Top(idleSprite),Mathf.Max(Top(riseSprite),Mathf.Max(Top(apexSprite),Mathf.Max(Top(fallSprite),Top(traversalDashSprite)))));
            // 壁4枚は共通256px/PPU100/pivotY14。未ロード時も同じ上端でView再生成の跳ねを防ぐ。
            if(core.TraversalProfile?.Walls.Count>0)actorTop=Mathf.Max(actorTop,2.42f);
            float height=((RectTransform)transform).rect.height;
            return presentation.Camera.ResolveVerticalOffset(Math.Max(0,core.Height),height/pixelsPerWorldUnit,height*.21f/pixelsPerWorldUnit,144/pixelsPerWorldUnit,actorTop);
        }
        private void RenderTraversal(OutsideExplorationCore core,float camera,float viewport,float verticalOffset)
        {
            const float unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float Origin(long x)=>(x/unit-camera+viewport*.5f)*pixelsPerWorldUnit;
            var profile=core.TraversalProfile;
            if(!ReferenceEquals(renderedTraversal,profile)){
                foreach(var image in wallImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}wallImages.Clear();DestroyGapImages();DestroyWaterImages();renderedTraversal=profile;
                if(profile!=null)foreach(var wall in profile.Walls){
                    var go=new GameObject("OutsideValidationWall-"+wall.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=terrain.sprite;image.type=Image.Type.Tiled;image.color=new Color(.6f,.85f,.94f,.8f);image.raycastTarget=false;
                    image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=new Vector2(.5f,0);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());wallImages.Add(image);
                }
                if(profile!=null)foreach(var gap in profile.FloorGaps){
                    var go=new GameObject("OutsideTrialFloorGap-"+gap.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.color=new Color(.015f,.035f,.055f,1);image.raycastTarget=false;image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=new Vector2(0,1);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());gapImages.Add(image);
                    for(int side=0;side<2;side++){var edge=new GameObject("VoidEdge"+side,typeof(RectTransform),typeof(Image));edge.transform.SetParent(go.transform,false);var line=edge.GetComponent<Image>();line.color=new Color(.3f,.85f,.9f,.85f);line.raycastTarget=false;line.rectTransform.anchorMin=line.rectTransform.anchorMax=new Vector2(side,1);line.rectTransform.pivot=new Vector2(side,1);line.rectTransform.sizeDelta=new Vector2(2,8);}
                }
                if(profile!=null)foreach(var area in profile.Waters){var go=new GameObject("OutsideTrialWater-"+area.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.color=new Color(.08f,.55f,.75f,.26f);image.raycastTarget=false;image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=Vector2.zero;go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());waterImages.Add(image);waterSurfaces[image]=new List<Image>();}

                var title=transform.Find("OutsideExplorationTitle")?.GetComponent<Text>();
                if(title!=null)title.text=profile!=null&&profile.Walls.Count>0?"ガイカイ タンサク / ケンショウ カベ（"+(profile.WallActionsUnlocked?"ノウリョク カリカイホウ":"ノウリョク ミカイホウ")+"）":"ガイカイ タンサク  /  ケンショウ チケイ";
            }
            for(int i=0;i<wallImages.Count;i++){
                var wall=profile.Walls[i];var image=wallImages[i];image.gameObject.SetActive(true);
                float height=((RectTransform)transform).rect.height,ground=height*.21f/pixelsPerWorldUnit;
                float bottom=Math.Max(wall.Bottom/unit,verticalOffset-ground),top=Math.Min(wall.Top/unit,verticalOffset+height/pixelsPerWorldUnit-ground);
                image.enabled=top>bottom;
                float tile=image.sprite.rect.height/(image.pixelsPerUnit*image.pixelsPerUnitMultiplier);
                // 見える区間だけを生成し、壁のタイル位相はworldへ固定する。
                if(tile>0)bottom-=Mathf.Repeat((bottom-wall.Bottom/unit)*pixelsPerWorldUnit,tile)/pixelsPerWorldUnit;
                image.rectTransform.anchoredPosition=new Vector2(Origin(wall.X),(bottom-verticalOffset)*pixelsPerWorldUnit);image.rectTransform.sizeDelta=new Vector2(18,Math.Max(0,top-bottom)*pixelsPerWorldUnit);
            }
            for(int i=0;i<gapImages.Count;i++){
                var gap=profile.FloorGaps[i];var image=gapImages[i];image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(Origin(gap.Left),-verticalOffset*pixelsPerWorldUnit);image.rectTransform.sizeDelta=new Vector2((gap.Right-gap.Left)/unit*pixelsPerWorldUnit,profile.VoidDepth/unit*pixelsPerWorldUnit+8);
            }
            for(int i=0;i<waterImages.Count;i++){var area=profile.Waters[i];var image=waterImages[i];image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(Origin(area.Left),(area.Bottom/unit-verticalOffset)*pixelsPerWorldUnit);image.rectTransform.sizeDelta=new Vector2((area.Right-area.Left)/unit*pixelsPerWorldUnit,(area.Top-area.Bottom)/unit*pixelsPerWorldUnit);RenderWaterSurface(image);}
            RenderBreath();
            var samples=core.Afterimages;
            for(int i=0;i<afterimageImages.Length;i++){
                var image=afterimageImages[i];if(i>=samples.Count){image.enabled=false;image.sprite=null;continue;}
                var sample=samples[i];var sprite=sample.Pose==OutsideAfterimagePose.Dash?traversalDashSprite:sample.Pose==OutsideAfterimagePose.JumpRise?riseSprite:sample.Pose==OutsideAfterimagePose.JumpApex?apexSprite:fallSprite;
                sprite=ResolveBodyCostume(sprite);
                image.sprite=sprite;image.enabled=sprite!=null;if(sprite==null)continue;
                image.color=new Color(.65f,.9f,1,.25f*(12-(core.Frame-sample.Frame))/12f);
                var rect=image.rectTransform;rect.pivot=sprite.pivot/sprite.rect.size;rect.sizeDelta=new Vector2(sprite.bounds.size.x,sprite.bounds.size.y)*pixelsPerWorldUnit;
                rect.anchoredPosition=new Vector2(Origin(sample.X),(sample.Y/unit-verticalOffset)*pixelsPerWorldUnit);rect.localScale=new Vector3(-sample.Facing,1,1);
            }
            if(movementGuideText.text.Length>0){
                if(profile!=null&&profile.WallActionsUnlocked&&profile.Walls.Count>0)movementGuideText.text+="\nタイオウするカベ: ホウコウホジでスベル / ジャンプでケる";
                if(profile?.DoubleJumpUnlocked==true)movementGuideText.text+="\nニダンジャンプ: クウチュウでオシナオシ（カリ カイホウ）";
                if(profile?.Water!=null)movementGuideText.text+="\nアナのサラにヒダリ: ミズ（カリ） / コキュウ0でフッキ";
                if(profile?.FloorGaps.Count>0)movementGuideText.text+="\nカイフクのヒダリ: ラッカ フッキのシヨウアナ（カリ）";
                if(core.DashCooldownRemaining>0)movementGuideText.text+="\nダッシュ マチ: "+(Math.Ceiling(core.DashCooldownRemaining/6d)/10d).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
            }
        }
        private void ClearTraversalDisplay()
        {foreach(var image in afterimageImages)if(image!=null){image.enabled=false;image.sprite=null;}foreach(var image in wallImages)if(image!=null)image.gameObject.SetActive(false);foreach(var image in gapImages)if(image!=null)image.gameObject.SetActive(false);foreach(var image in waterImages)if(image!=null)image.gameObject.SetActive(false);if(breathPanel!=null)breathPanel.gameObject.SetActive(false);}
        private void ReleaseTraversalReferences(){trialWideSwordEffect?.Dispose();trialWideSwordEffect=null;trialSpearBody?.Dispose();trialSpearBody=null;trialShotBody?.Dispose();trialShotBody=null;initialShotMotion?.Dispose();initialShotMotion=null;trialSwordMotion?.Dispose();trialSwordMotion=null;trialBombMotion?.Dispose();trialBombMotion=null;slashSprites?.Dispose();slashSprites=null;trialChargeMotion?.Dispose();trialChargeMotion=null;trialGroundCombo?.Dispose();trialGroundCombo=null;trialFireBody?.Dispose();trialFireBody=null;ClearTraversalDisplay();DestroyGapImages();DestroyWaterImages();if(breathPanel!=null){if(UnityEngine.Application.isPlaying)Destroy(breathPanel.gameObject);else DestroyImmediate(breathPanel.gameObject);}breathPanel=breathFill=null;breathLabel=null;wallSlideFrames=null;traversalDashSprite=null;trialKickPrepare=trialKickContact=trialAirForwardContact=trialDashKickContact=null;trialComboSprites=trialAirDownSprites=null;traversalResources=null;renderedTraversal=null;wallImages.Clear();}
    }
}

namespace Olca.Presentation.Management
{
    // 通常状態だけの製品入口。神依定義/効果/解放順をここで仮登録しない。
    public sealed class OutsideKamuiWheelDialog : UnityEngine.MonoBehaviour
    {
        private DialogManager dialogs;
        private IDialogOwner owner;
        private System.Func<bool> parentAlive,canConfirm;
        private System.Action<bool> pause;
        private UnityEngine.UI.Button confirm,cancel;
        private UnityEngine.UI.Text status,hint;
        private OutsideKamuiWheelRing ring;
        private bool open,confirmHeld,pauseOwned;private long epoch;
        private UnityEngine.UI.Button alternate;private UnityEngine.UI.Text normalLabel,alternateLabel;private UnityEngine.Font choiceFont;
        private string[] choices={"ツウジョウ ジョウタイ"};private int selectedChoice=-1,currentChoice=-1;private System.Func<int,bool> tryChoice;private string choiceError;
        public int SelectedChoice=>selectedChoice;
        public void ConfigureChoices(string[] names,int current,System.Func<int,bool> apply)
        {
            if(open||names==null||names.Length<1||names.Length>2||System.Array.Exists(names,string.IsNullOrWhiteSpace))throw new System.ArgumentException("閉鎖中の試用ホイールへ1〜2項目を指定してください");
            choices=(string[])names.Clone();currentChoice=current;tryChoice=apply;choiceError=null;
            if(choices.Length==2&&alternate==null){var go=new UnityEngine.GameObject("KamuiWheelAlternate",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(transform,false);alternate=go.GetComponent<UnityEngine.UI.Button>();alternate.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};Olca.Presentation.Dungeon.UiLook.MenuButton(alternate);go.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;alternateLabel=Text("KamuiWheelAlternateLabel","",choiceFont,UnityEngine.Vector2.zero,new UnityEngine.Vector2(340,100),24,go.transform);}
            ((UnityEngine.RectTransform)confirm.transform).anchoredPosition=new UnityEngine.Vector2(0,choices.Length==1?0:75);((UnityEngine.RectTransform)confirm.transform).sizeDelta=choices.Length==1?new UnityEngine.Vector2(250,90):new UnityEngine.Vector2(350,110);normalLabel.rectTransform.sizeDelta=choices.Length==1?new UnityEngine.Vector2(240,80):new UnityEngine.Vector2(340,100);normalLabel.text=choices[0]+(current==0?"\n[ゲンザイ]":"");
            if(alternate!=null){alternate.gameObject.SetActive(choices.Length==2);((UnityEngine.RectTransform)alternate.transform).anchoredPosition=new UnityEngine.Vector2(0,-75);((UnityEngine.RectTransform)alternate.transform).sizeDelta=new UnityEngine.Vector2(350,110);if(choices.Length==2)alternateLabel.text=choices[1]+(current==1?"\n[ゲンザイ]":"");}
        }
        private void TryConfirmChoice()
        {
            if(!open||!HasSelection||!confirm.interactable||owner?.IsCurrent!=true||parentAlive?.Invoke()!=true)return;
            if(tryChoice!=null&&!tryChoice(selectedChoice)){choiceError="キリカエ デキマセン / サイカクテイ マタハ トジル";status.text=choiceError;return;}
            confirm.onClick.Invoke();
        }

        public bool IsOpen=>open;
        public bool HasSelection{get;private set;}
        public DialogResult? LastResult{get;private set;}
        public void Initialize(UnityEngine.Font font)
        {
            choiceFont=font;var rect=(UnityEngine.RectTransform)transform;rect.anchorMin=UnityEngine.Vector2.zero;rect.anchorMax=UnityEngine.Vector2.one;rect.offsetMin=rect.offsetMax=UnityEngine.Vector2.zero;
            var overlay=gameObject.AddComponent<UnityEngine.UI.Image>();overlay.color=new UnityEngine.Color(.015f,.04f,.06f,.75f);
            var face=new UnityEngine.GameObject("KamuiWheelSurface",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));face.transform.SetParent(transform,false);
            var fr=(UnityEngine.RectTransform)face.transform;fr.sizeDelta=new UnityEngine.Vector2(800,670);var image=face.GetComponent<UnityEngine.UI.Image>();image.sprite=UnityEngine.Resources.Load<UnityEngine.Sprite>("OlcaUI/description");image.type=UnityEngine.UI.Image.Type.Sliced;image.color=image.sprite!=null?UnityEngine.Color.white:new UnityEngine.Color(.025f,.075f,.105f,.98f);image.raycastTarget=false;
            Text("KamuiWheelTitle","カムイ ホイール",font,new UnityEngine.Vector2(0,255),new UnityEngine.Vector2(700,60),32);
            var circle=new UnityEngine.GameObject("KamuiWheelRing",typeof(UnityEngine.RectTransform),typeof(OutsideKamuiWheelRing));circle.transform.SetParent(transform,false);((UnityEngine.RectTransform)circle.transform).sizeDelta=new UnityEngine.Vector2(300,300);ring=circle.GetComponent<OutsideKamuiWheelRing>();ring.raycastTarget=false;
            var item=new UnityEngine.GameObject("KamuiWheelNormal",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));item.transform.SetParent(transform,false);((UnityEngine.RectTransform)item.transform).sizeDelta=new UnityEngine.Vector2(250,90);confirm=item.GetComponent<UnityEngine.UI.Button>();confirm.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};Olca.Presentation.Dungeon.UiLook.MenuButton(confirm);item.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            normalLabel=Text("KamuiWheelNormalLabel","ツウジョウ ジョウタイ",font,UnityEngine.Vector2.zero,new UnityEngine.Vector2(240,80),24,item.transform);
            status=Text("KamuiWheelSelection","ミセンタク",font,new UnityEngine.Vector2(0,-195),new UnityEngine.Vector2(700,50),24);
            hint=Text("KamuiWheelHint","ヒダリ スティック: センタク / ケッテイ ボタン: カクテイ\nヒラク ボタンヲ ハナス: トジル",font,new UnityEngine.Vector2(0,-270),new UnityEngine.Vector2(740,85),22);
            var hidden=new UnityEngine.GameObject("KamuiWheelRelease",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Button));hidden.transform.SetParent(transform,false);((UnityEngine.RectTransform)hidden.transform).sizeDelta=UnityEngine.Vector2.zero;cancel=hidden.GetComponent<UnityEngine.UI.Button>();cancel.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};
            gameObject.SetActive(false);
        }
        private UnityEngine.UI.Text Text(string name,string value,UnityEngine.Font font,UnityEngine.Vector2 at,UnityEngine.Vector2 size,int points,UnityEngine.Transform parent=null)
        {
            var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(parent??transform,false);var r=(UnityEngine.RectTransform)go.transform;r.sizeDelta=size;r.anchoredPosition=at;var text=go.GetComponent<UnityEngine.UI.Text>();text.font=font;text.fontSize=points;text.alignment=UnityEngine.TextAnchor.MiddleCenter;text.text=value;Olca.Presentation.Dungeon.UiLook.Label(text);text.color=UnityEngine.Color.white;text.raycastTarget=false;return text;
        }
        public bool Open(DialogManager manager,IDialogOwner current,System.Func<bool> alive,System.Func<bool> allowed,System.Action<bool> paused,bool wasConfirmHeld,UnityEngine.Vector2 direction,string openLabel="LB",string confirmLabel="South")
        {
            if(open||manager==null||manager.ActiveCount!=0||current?.IsCurrent!=true||alive==null||!alive())return false;
            hint.text="ヒダリ スティック: センタク / "+confirmLabel+": カクテイ\n"+openLabel+" ヲ ハナス: トジル";
            dialogs=manager;owner=current;parentAlive=alive;canConfirm=allowed;pause=paused;confirmHeld=wasConfirmHeld;open=true;LastResult=null;confirm.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();cancel.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();Select(direction);
            long ticket=++epoch;
            try{var task=manager.Show(current,gameObject,confirm,cancel,result=>Finish(ticket,result),manageMenu:false,inputAllowed:()=>open&&this!=null&&parentAlive?.Invoke()==true);if(task.IsCompleted){Finish(ticket,task.Result);return false;}if(!open)return false;pauseOwned=true;pause?.Invoke(true);return true;}
            catch{Abort();throw;}
        }
        /// <summary>Pause経由は既存のメニュー決定/取消で操作し、LB保持を要求しない。</summary>
        public bool OpenMenu(DialogManager manager,IDialogOwner current,System.Func<bool> alive,System.Func<bool> allowed,System.Action<bool> paused,string guide)
        {
            if(open||manager==null||manager.ActiveCount!=0||current?.IsCurrent!=true||alive==null||!alive())return false;
            dialogs=manager;owner=current;parentAlive=alive;canConfirm=allowed;pause=paused;open=true;LastResult=null;long ticket=++epoch;
            confirm.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();cancel.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();
            confirm.onClick.AddListener(()=>{if(open&&ticket==epoch)SelectChoice(0);});
            if(alternate!=null){alternate.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();alternate.onClick.AddListener(()=>{if(!open||ticket!=epoch||owner?.IsCurrent!=true||parentAlive?.Invoke()!=true)return;SelectChoice(1);menuAlternate=true;try{confirm.onClick.Invoke();}finally{menuAlternate=false;}});}
            menuMode=true;SetMenuSurface(true);hint.text=guide;SelectChoice(-1);confirm.interactable=true;
            try{var task=manager.ShowPersistent(current,gameObject,confirm,cancel,()=>{if(menuAlternate)SelectChoice(1);if(parentAlive?.Invoke()!=true||canConfirm?.Invoke()!=true)return false;if(tryChoice!=null&&!tryChoice(selectedChoice)){choiceError="キリカエ デキマセン / サイカクテイ マタハ モドる";status.text=choiceError;return false;}return true;},result=>Finish(ticket,result));if(task.IsCompleted){Finish(ticket,task.Result);return false;}pauseOwned=true;pause?.Invoke(true);return true;}
            catch{Abort();throw;}
        }
        private bool menuMode,menuAlternate;
        private void SetMenuSurface(bool value)
        {
            confirm.GetComponent<UnityEngine.UI.Image>().raycastTarget=value;if(alternate!=null)alternate.GetComponent<UnityEngine.UI.Image>().raycastTarget=value;
            var rect=(UnityEngine.RectTransform)cancel.transform;rect.sizeDelta=value?new UnityEngine.Vector2(280,60):UnityEngine.Vector2.zero;rect.anchoredPosition=new UnityEngine.Vector2(0,-230);
            if(value&&cancel.GetComponent<UnityEngine.UI.Image>()==null){cancel.targetGraphic=cancel.gameObject.AddComponent<UnityEngine.UI.Image>();Olca.Presentation.Dungeon.UiLook.MenuButton(cancel);Text("KamuiMenuBack","モドる",choiceFont,UnityEngine.Vector2.zero,new UnityEngine.Vector2(260,55),24,cancel.transform);}
            cancel.transform.Find("KamuiMenuBack")?.gameObject.SetActive(value);
            status.rectTransform.anchoredPosition=new UnityEngine.Vector2(0,value?-170:-195);hint.rectTransform.anchoredPosition=new UnityEngine.Vector2(0,value?-295:-270);
        }
        public void RefreshMenu()
        {
            if(!open||!menuMode)return;if(owner?.IsCurrent!=true||parentAlive?.Invoke()!=true){Abort();return;}
            var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            normalLabel.color=selected==confirm.gameObject?new UnityEngine.Color(.6f,1,1):UnityEngine.Color.white;
            if(alternateLabel!=null)alternateLabel.color=selected==alternate.gameObject?new UnityEngine.Color(.6f,1,1):UnityEngine.Color.white;
            confirm.interactable=canConfirm?.Invoke()==true;if(alternate!=null)alternate.interactable=confirm.interactable;
        }
        private void Select(UnityEngine.Vector2 direction)
        {
            SelectChoice(direction.sqrMagnitude>=.19f*.19f?(choices.Length==2&&direction.y<0?1:0):-1);
        }
        private void SelectChoice(int index)
        {
            if(selectedChoice!=index)choiceError=null;selectedChoice=index;HasSelection=index>=0;confirm.interactable=HasSelection&&(canConfirm?.Invoke()??false);ring.Selected=HasSelection;status.text=choiceError??(HasSelection?"センタク: "+choices[index]:"ミセンタク");
            normalLabel.color=index==0?new UnityEngine.Color(.6f,1,1):UnityEngine.Color.white;if(alternateLabel!=null)alternateLabel.color=index==1?new UnityEngine.Color(.6f,1,1):UnityEngine.Color.white;
        }
        public void Tick(bool held,bool decide,UnityEngine.Vector2 direction)
        {
            if(!open)return;if(owner?.IsCurrent!=true||parentAlive?.Invoke()!=true){Abort();return;}
            if(!held){cancel.onClick.Invoke();return;}
            bool edge=decide&&!confirmHeld;confirmHeld=decide;Select(direction);
            // 標準UI SubmitやB取消は所有しない。開始時A保持を新しい確定へ合成しない。
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if(edge&&confirm.interactable)TryConfirmChoice();
        }
        /// <summary>PCはトグル開閉。表示/所有/PauseはPadと同じDialogへ集め、入力方式だけを分ける。</summary>
        public bool OpenPc(DialogManager manager,IDialogOwner current,System.Func<bool> alive,System.Func<bool> allowed,System.Action<bool> paused,Olca.Unity.UnityInputProvider.OutsidePcWheelInput input,string openLabel="Tab")
        {
            bool result=Open(manager,current,alive,allowed,paused,input.EnterHeld||input.ClickHeld,UnityEngine.Vector2.zero,openLabel,"Enter");if(!result)return false;
            hint.text="マウス / WASD: センタク\nEnter / ヒダリクリック: カクテイ  /  "+openLabel+": トジル";
            SelectMouse(input.Pointer,input.PointerAvailable);
            // 開く前から方向を保持していた入力も初回sampleとして表示する。以後は実イベント順だけで切り替える。
            if(input.Direction!=UnityEngine.Vector2.zero)Select(input.Direction);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);return true;
        }
        private void SelectMouse(UnityEngine.Vector2 pointer,bool available)
        {int index=-1;if(available){if(UnityEngine.RectTransformUtility.RectangleContainsScreenPoint((UnityEngine.RectTransform)confirm.transform,pointer,null))index=0;else if(choices.Length==2&&alternate!=null&&UnityEngine.RectTransformUtility.RectangleContainsScreenPoint((UnityEngine.RectTransform)alternate.transform,pointer,null))index=1;}SelectChoice(index);}
        public void TickPc(Olca.Unity.UnityInputProvider.OutsidePcWheelInput input)
        {
            if(!open)return;if(owner?.IsCurrent!=true||parentAlive?.Invoke()!=true||!input.KeyboardAvailable){Abort();return;}
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if(input.Events!=null&&input.Events.Count>0){
                foreach(var step in input.Events){
                    if(!open)return;if(owner?.IsCurrent!=true||parentAlive?.Invoke()!=true){Abort();return;}
                    switch(step.Kind){
                        case Olca.Unity.UnityInputProvider.OutsidePcWheelEventKind.Direction:Select(step.Value);break;
                        case Olca.Unity.UnityInputProvider.OutsidePcWheelEventKind.Pointer:SelectMouse(step.Value,true);break;
                        case Olca.Unity.UnityInputProvider.OutsidePcWheelEventKind.Toggle:cancel.onClick.Invoke();return;
                        case Olca.Unity.UnityInputProvider.OutsidePcWheelEventKind.Click:SelectMouse(step.Value,true);ConfirmPcChoice();break;
                        case Olca.Unity.UnityInputProvider.OutsidePcWheelEventKind.Confirm:ConfirmPcChoice();break;
                    }
                }
                return;
            }
            if(input.ToggleDown){cancel.onClick.Invoke();return;}
            if(input.KeyboardChanged&&(!input.PointerChanged||input.KeyboardOrder>=input.PointerOrder))Select(input.Direction);
            else if(input.PointerChanged)SelectMouse(input.Pointer,input.PointerAvailable);
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            // クリックは新しいマウス操作。静止hoverとは区別し、その位置の項目を選んで確定する。
            if(input.ClickDown)SelectMouse(input.Pointer,input.PointerAvailable);
            // 選択入力が静止していても、現在の切替可否を確定直前に読み直す。
            if(input.EnterDown||input.ClickDown)ConfirmPcChoice();else confirm.interactable=HasSelection&&(canConfirm?.Invoke()??false);
        }
        private void ConfirmPcChoice(){confirm.interactable=HasSelection&&(canConfirm?.Invoke()??false);if(confirm.interactable)TryConfirmChoice();}
        private void Finish(long ticket,DialogResult result)
        {
            if(ticket!=epoch)return;if(!open){if(LastResult==DialogResult.ParentClosed&&result!=DialogResult.ParentClosed)LastResult=result;return;}open=false;if(menuMode){SetMenuSurface(false);menuMode=false;}HasSelection=false;selectedChoice=-1;tryChoice=null;choiceError=null;LastResult=result;var callback=pause;pause=null;parentAlive=canConfirm=null;owner=null;dialogs=null;bool release=pauseOwned;pauseOwned=false;if(release)callback?.Invoke(false);
        }
        public void Abort(){if(!open)return;var manager=dialogs;var current=owner;manager?.CancelActiveOwner(current);Finish(epoch,DialogResult.ParentClosed);if(this!=null&&gameObject.activeSelf)gameObject.SetActive(false);}
        private void OnDisable(){Abort();}
        private void OnDestroy(){Abort();confirm=cancel=null;status=null;ring=null;}
    }
    // 生成Texture/Spriteを持たない、単一項目ホイールの既存UI上の輪郭。
    [UnityEngine.RequireComponent(typeof(UnityEngine.CanvasRenderer))]
    public sealed class OutsideKamuiWheelRing : UnityEngine.UI.MaskableGraphic
    {
        private bool selected;
        public bool Selected{get=>selected;set{if(selected==value)return;selected=value;SetVerticesDirty();}}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();const int count=64;float outer=UnityEngine.Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f,inner=outer-8;
            var c=selected?new UnityEngine.Color(.55f,.95f,1,1):new UnityEngine.Color(.25f,.6f,.7f,1);
            for(int n=0;n<count;n++){float a=n*UnityEngine.Mathf.PI*2/count,b=(n+1)*UnityEngine.Mathf.PI*2/count;int i=vh.currentVertCount;vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*inner,UnityEngine.Mathf.Sin(a)*inner),c,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*outer,UnityEngine.Mathf.Sin(a)*outer),c,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*outer,UnityEngine.Mathf.Sin(b)*outer),c,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*inner,UnityEngine.Mathf.Sin(b)*inner),c,UnityEngine.Vector2.zero);vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);}
        }
    }
}

namespace Olca.Presentation.Management
{
    /// <summary>保存済みの試用目的を読む。正式依頼・受注・報酬の状態は生成しない。</summary>
    public sealed class OutsideObjectivesView : MonoBehaviour
    {
        private Func<OutsideTrialNavigation> navigation;private Button clearNavigation,toggleNavigation;
        public void ConfigureNavigation(Func<OutsideTrialNavigation> source)
        {
            navigation=source;
            Button Make(string name,float x,string text){var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(transform,false);var b=go.GetComponent<Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(b);b.navigation=new Navigation{mode=Navigation.Mode.None};var rr=(RectTransform)go.transform;rr.anchoredPosition=new Vector2(x,-267);rr.sizeDelta=new Vector2(570,48);Label(name+"Label",text,Vector2.zero,new Vector2(550,44),22,go.transform);LiquidGlassMenu.Button(b);return b;}
            clearNavigation=Make("ClearOutsideNavigation",-300,"案内を解除");toggleNavigation=Make("ToggleOutsideNavigation",300,"案内表示");
        }
        private Selectable FindNavigation(Selectable from,Vector2 direction)
        {
            var buttons=rows.GetComponentsInChildren<Button>();int index=Array.FindIndex(buttons,b=>b==from);int row=index>=0?index:from==clearNavigation||from==toggleNavigation?buttons.Length:buttons.Length+1;
            if(direction.x!=0)return row==buttons.Length?(direction.x>0&&from==clearNavigation?toggleNavigation:direction.x<0&&from==toggleNavigation?clearNavigation:null):null;
            int next=row+(direction.y>0?-1:1);if(next<0||next>buttons.Length+1)return null;if(next<buttons.Length)return buttons[next];if(next==buttons.Length)return clearNavigation;return back;
        }
        private void RefreshNavigationSelection()
        {
            var state=navigation?.Invoke();if(state==null)return;
            foreach(var b in rows.GetComponentsInChildren<Button>()){bool selected=b.name=="Objective_"+state.SelectedId;var colors=b.colors;colors.normalColor=selected?new Color(.08f,.28f,.35f,1):new Color(.045f,.25f,.31f,1);b.colors=colors;var t=b.transform.Find("ObjectiveTitle").GetComponent<Text>();t.text=t.text.Replace("[案内] ","");if(selected)t.text="[案内] "+t.text;}
            toggleNavigation.GetComponentInChildren<Text>().text="案内表示: "+(state.Visible?"ON":"OFF");
        }
        private ManagedScreenContext context;private Func<bool> ownerAlive;private Func<IReadOnlyList<OutsideTrialGoalOverview>> source;private Action close;private Button back;private Text guide;private Font font;private Transform rows;private Olca.Unity.MenuPadScope menu;private long epoch;
        private bool CanOperate=>this!=null&&context?.IsCurrent==true&&!context.DialogBlocksInput&&gameObject.activeInHierarchy&&ownerAlive?.Invoke()==true;
        public void Initialize(Font value,Func<IReadOnlyList<OutsideTrialGoalOverview>> read,Action closed,Func<bool> alive)
        {
            font=value;source=read;close=closed;ownerAlive=alive;((RectTransform)transform).sizeDelta=new Vector2(1380,900);var panel=GetComponent<Image>()??gameObject.AddComponent<Image>();LiquidGlassMenu.Panel(panel);panel.raycastTarget=true;
            Label("ObjectivesTitle","目的・依頼（試用）",new Vector2(0,365),new Vector2(1260,70),34,transform);
            Label("ObjectivesScope","主な目的：試用ルートの案内\n依頼データは未登録です。受注と報酬は未実装です。",new Vector2(0,275),new Vector2(1260,100),24,transform);
            rows=new GameObject("ObjectiveRows",typeof(RectTransform)).transform;rows.SetParent(transform,false);
            var obj=new GameObject("CloseOutsideObjectives",typeof(RectTransform),typeof(Image),typeof(Button));obj.transform.SetParent(transform,false);back=obj.GetComponent<Button>();back.navigation=new Navigation{mode=Navigation.Mode.None};Olca.Presentation.Dungeon.UiLook.MenuButton(back);back.GetComponent<RectTransform>().anchoredPosition=new Vector2(0,-335);back.GetComponent<RectTransform>().sizeDelta=new Vector2(480,80);Label("ObjectivesBackLabel","ポーズへ戻る",Vector2.zero,new Vector2(450,70),26,obj.transform);LiquidGlassMenu.Button(back);
            guide=Label("ObjectivesInputGuide","",new Vector2(0,-407),new Vector2(1260,50),22,transform);
        }
        private Text Label(string name,string value,Vector2 position,Vector2 size,int points,Transform parent)
        {
            var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(parent,false);var text=obj.GetComponent<Text>();text.font=font;text.text=value;text.fontSize=points;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.rectTransform.anchoredPosition=position;text.rectTransform.sizeDelta=size;LiquidGlassMenu.ProtectText(text);return text;
        }
        public void Open(ManagedScreenContext owner)
        {
            Hide();context=owner;long token=++epoch;for(int n=rows.childCount-1;n>=0;n--){var child=rows.GetChild(n);child.gameObject.SetActive(false);if(Application.isPlaying)Destroy(child.gameObject);else DestroyImmediate(child.gameObject);}
            var goals=source?.Invoke()??Array.Empty<OutsideTrialGoalOverview>();for(int i=0;i<goals.Count;i++){
                var goal=goals[i];var obj=new GameObject("Objective_"+goal.Id,typeof(RectTransform),typeof(Image));obj.transform.SetParent(rows,false);var rr=(RectTransform)obj.transform;rr.anchoredPosition=new Vector2(0,145-i*155);rr.sizeDelta=new Vector2(1260,140);var bg=obj.GetComponent<Image>();bg.color=new Color(.035f,.14f,.19f,1);bg.raycastTarget=false;
                if(navigation!=null){bg.raycastTarget=true;var button=obj.AddComponent<Button>();button.targetGraphic=bg;button.navigation=new Navigation{mode=Navigation.Mode.None};Olca.Presentation.Dungeon.UiLook.MenuButton(button);button.onClick.AddListener(()=>{if(CanOperate&&epoch==token&&ReferenceEquals(context,owner)){navigation().Select(goal.Id);RefreshNavigationSelection();}});}
                var title=Label("ObjectiveTitle",(goal.Completed?"[達成] ":"[未達成] ")+goal.Title,new Vector2(0,35),new Vector2(1220,50),26,obj.transform);title.color=goal.Completed?new Color(.55f,1,1):Color.white;
                Label("ObjectiveDetail",goal.Detail,new Vector2(0,-24),new Vector2(1210,68),22,obj.transform);if(obj.TryGetComponent<Button>(out var rowButton))LiquidGlassMenu.Button(rowButton);
            }
            if(navigation!=null){clearNavigation.onClick=new Button.ButtonClickedEvent();clearNavigation.onClick.AddListener(()=>{if(CanOperate&&epoch==token){clearNavigation.GetComponent<LiquidGlassCoordinates>().PlayConfirmation();navigation().Select(null);RefreshNavigationSelection();}});toggleNavigation.onClick=new Button.ButtonClickedEvent();toggleNavigation.onClick.AddListener(()=>{if(CanOperate&&epoch==token){toggleNavigation.GetComponent<LiquidGlassCoordinates>().PlayConfirmation();navigation().ToggleVisibility();RefreshNavigationSelection();}});RefreshNavigationSelection();}
            back.onClick=new Button.ButtonClickedEvent();back.onClick.AddListener(()=>{if(CanOperate&&epoch==token&&ReferenceEquals(context,owner))close?.Invoke();});gameObject.SetActive(true);transform.SetAsLastSibling();menu=new Olca.Unity.MenuPadScope(()=>CanOperate);if(navigation!=null)menu.NavigationResolver=FindNavigation;menu.Bind(transform,navigation==null?back.name:"Objective_"+(navigation().SelectedId??goals.FirstOrDefault()?.Id),()=>{if(CanOperate&&epoch==token)close?.Invoke();});RefreshGuide();
        }
        private void RefreshGuide(){guide.text=GameplayPauseView.InputGuideText(Olca.Unity.UnityInputProvider.ActiveGamepadProfile,Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad)+(navigation==null?" / ポーズへ戻る":" / モクテキを エラんで アンナイ / サイキドウで カイジョ（カリ）");}
        private void LateUpdate(){if(context!=null&&(context.IsCurrent!=true||ownerAlive?.Invoke()!=true)){Hide();return;}menu?.Tick();if(context!=null)RefreshGuide();}
        public void Hide(){epoch++;menu?.Dispose();menu=null;var prior=context;context=null;if(this!=null)gameObject.SetActive(false);prior?.NotifyHidden();}
        private void OnDisable(){epoch++;menu?.Dispose();menu=null;var prior=context;context=null;prior?.NotifyHidden();}
        private void OnDestroy(){OnDisable();source=null;close=null;ownerAlive=null;navigation=null;}
    }
    /// <summary>試用地図と登録済み地点の移動確認。保存と時間停止はController/Sessionが所有する。</summary>
    public sealed class OutsideFastTravelView : MonoBehaviour
    {
        private Func<OutsideTrialNavigation> navigation;
        public void ConfigureNavigation(Func<OutsideTrialNavigation> source){navigation=source;}
        private ManagedScreenContext context;private Olca.Unity.MenuPadScope menu;private Button confirm;private Text message;private Func<bool> discovered,ownerAlive;private Action travel,close;private bool busy;private long renderEpoch;private readonly Dictionary<Button,Action> actions=new Dictionary<Button,Action>();
        private bool CanOperate=>this!=null&&isActiveAndEnabled&&context?.IsCurrent==true&&!context.DialogBlocksInput&&!busy&&ownerAlive?.Invoke()==true;
        public void Initialize(Font font,Func<bool> known,Action execute,Action closed,Func<bool> alive=null,Func<OutsideTrialMapOverview[]> maps=null,Action<string> acquire=null)
        {
            mapAcquire=acquire;mapSource=maps;mapFont=font;discovered=known;travel=execute;close=closed;ownerAlive=alive??(()=>true);var rect=(RectTransform)transform;rect.sizeDelta=new Vector2(1240,680);LiquidGlassMenu.Panel(gameObject.AddComponent<Image>());
            Text Label(string name,string value,float y,int size){var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(transform,false);var t=go.GetComponent<Text>();t.font=font;t.text=value;t.color=Color.white;t.fontSize=size;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.rectTransform.anchoredPosition=new Vector2(0,y);t.rectTransform.sizeDelta=new Vector2(1140,150);LiquidGlassMenu.ProtectText(t);return t;}
            Label("FastTravelTitle","ファストトラベル / シヨウ カイフクチテン",230,36);message=Label("FastTravelMessage","",50,28);
            Button Make(string name,string text,float x,Action action){var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(transform,false);var r=(RectTransform)go.transform;r.anchoredPosition=new Vector2(x,-200);r.sizeDelta=new Vector2(480,90);var b=go.GetComponent<Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(b);var t=Label(name+"Label",text,0,28);t.transform.SetParent(go.transform,false);t.rectTransform.anchoredPosition=Vector2.zero;t.rectTransform.sizeDelta=new Vector2(450,80);actions.Add(b,action);LiquidGlassMenu.Button(b);return b;}
            confirm=Make("ConfirmOutsideFastTravel","イドウする",-280,()=>{if(discovered?.Invoke()==true&&(mapSource==null||SelectedMap?.MapId==OutsideMapRevealTrial.HomeMapId))travel?.Invoke();});Make("CancelOutsideFastTravel","戻る",280,()=>close?.Invoke());
            if(mapSource!=null){
                rect.sizeDelta=new Vector2(1380,930);var title=transform.Find("FastTravelTitle").GetComponent<Text>();title.text="全体マップ（試用）";title.rectTransform.anchoredPosition=new Vector2(0,390);title.rectTransform.sizeDelta=new Vector2(1260,70);
                message.rectTransform.anchoredPosition=new Vector2(0,-245);message.rectTransform.sizeDelta=new Vector2(1240,110);message.fontSize=22;
                foreach(var b in actions.Keys)((RectTransform)b.transform).anchoredPosition=new Vector2(((RectTransform)b.transform).anchoredPosition.x,-350);
                confirm.GetComponentInChildren<Text>().text="回復地点へ移動";
                mapRate=Label("MapExplorationRate","",245,30);mapRate.rectTransform.sizeDelta=new Vector2(1240,60);
                var guide=Label("MapInputGuide","",-423,20);guide.rectTransform.sizeDelta=new Vector2(1250,40);mapGuide=guide;
                for(int index=0;index<2;index++){int captured=index;var b=Make("MapChoice_"+index,"",0,()=>SelectMap(captured));((RectTransform)b.transform).anchoredPosition=new Vector2(index==0?-300:300,310);((RectTransform)b.transform).sizeDelta=new Vector2(560,62);b.GetComponentInChildren<Text>().fontSize=22;mapChoices.Add(b);}
                if(mapAcquire!=null){acquireButton=Make("AcquireTrialMapItem","",0,()=>{var map=SelectedMap;if(map!=null&&!map.ItemRevealed)mapAcquire(map.MapId);});((RectTransform)acquireButton.transform).anchoredPosition=new Vector2(0,-150);((RectTransform)acquireButton.transform).sizeDelta=new Vector2(720,62);var label=acquireButton.GetComponentInChildren<Text>();label.fontSize=22;label.rectTransform.sizeDelta=new Vector2(700,56);}

            }
        }
        public void Open(ManagedScreenContext owner){context=owner;if(mapSource!=null){mapData=mapSource()??Array.Empty<OutsideTrialMapOverview>();selectedMap=Math.Max(0,Array.FindIndex(mapData,m=>m.IsCurrent));RenderMap();}long epoch=++renderEpoch;foreach(var pair in actions){var action=pair.Value;pair.Key.onClick=new Button.ButtonClickedEvent();pair.Key.onClick.AddListener(()=>{if(CanOperate&&renderEpoch==epoch&&ReferenceEquals(context,owner)){pair.Key.GetComponent<LiquidGlassCoordinates>().PlayConfirmation();action();}});}gameObject.SetActive(true);transform.SetAsLastSibling();SetBusy(false,null);menu?.Dispose();menu=new Olca.Unity.MenuPadScope(()=>CanOperate);menu.Bind(transform,confirm.interactable?confirm.name:"CancelOutsideFastTravel",()=>close?.Invoke());}
        public void SetBusy(bool value,string error){busy=value;bool known=discovered?.Invoke()==true&&(mapSource==null||SelectedMap?.MapId==OutsideMapRevealTrial.HomeMapId);confirm.interactable=known&&!busy;foreach(var button in mapChoices)button.interactable=!busy;if(acquireButton!=null){acquireButton.interactable=!busy&&SelectedMap!=null&&!SelectedMap.ItemRevealed;acquireButton.GetComponentInChildren<Text>().text=SelectedMap?.ItemRevealed==true?"マップ シュトクずみ / ゼンイキ カイジ":"シヨウ マップアイテムを ウケトル（ムリョウ）";}message.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(error??(busy?"ホゾン・イドウチュウ":mapSource!=null&&SelectedMap?.MapId!=OutsideMapRevealTrial.HomeMapId?"このチズの イドウサキは ミトウロク / モドるで ポーズへ":known?"ヒダリの カイフクチテン（カリ）へ ムリョウで イドウ\nHP・ジョウタイ カイフク / 30マイ サイソウテン・ホゾン":"ミハッケン / カイフクチテンへ ジッサイに ホウモンしてください"));}
        private Action<string> mapAcquire;private Button acquireButton;
        public void RefreshMapSnapshot(string message)
        {
            if(this==null||context?.IsCurrent!=true||ownerAlive?.Invoke()!=true)return;
            string selected=SelectedMap?.MapId;mapData=mapSource?.Invoke()??Array.Empty<OutsideTrialMapOverview>();selectedMap=Math.Max(0,Array.FindIndex(mapData,m=>m.MapId==selected));RenderMap();SetBusy(false,message);
        }
        private Func<OutsideTrialMapOverview[]> mapSource;private Font mapFont;private Text mapRate,mapGuide;private Transform mapGrid;
        private OutsideTrialMapOverview[] mapData=Array.Empty<OutsideTrialMapOverview>();private readonly List<Button> mapChoices=new List<Button>();private int selectedMap;
        private OutsideTrialMapOverview SelectedMap=>selectedMap>=0&&selectedMap<mapData.Length?mapData[selectedMap]:null;
        private void SelectMap(int index){if(index<0||index>=mapData.Length||busy)return;selectedMap=index;RenderMap();SetBusy(false,null);}
        private void RenderMap()
        {
            if(mapSource==null)return;for(int i=0;i<mapChoices.Count;i++){var b=mapChoices[i];b.gameObject.SetActive(i<mapData.Length);if(i<mapData.Length){b.GetComponentInChildren<Text>().text=(i==selectedMap?"> ":"")+mapData[i].Name;((RectTransform)b.transform).anchoredPosition=new Vector2(mapData.Length==1?0:i==0?-300:300,310);}}
            var map=SelectedMap;mapRate.text=map==null?"チズ ミトウロク":"シヨウ タンサクリツ  "+(map.ExplorationPercent.HasValue?map.ExplorationPercent.Value+"%":"--%（タイショウ ミトウロク）");
            RefreshMapGuide();
            if(mapGrid!=null){var old=mapGrid.gameObject;old.SetActive(false);if(Application.isPlaying)Destroy(old);else DestroyImmediate(old);}
            var root=new GameObject("MapGrid",typeof(RectTransform),typeof(Image));root.transform.SetParent(transform,false);mapGrid=root.transform;var panel=root.GetComponent<Image>();panel.color=new Color(.02f,.055f,.08f,1);panel.raycastTarget=false;var area=(RectTransform)root.transform;area.sizeDelta=new Vector2(1240,mapAcquire==null?300:240);area.anchoredPosition=new Vector2(0,mapAcquire==null?55:85);if(map==null)return;
            var cells=map.Cells.Where(c=>c.X>=0&&c.X<8).ToArray();int low=Math.Min(-1,cells.Length==0?0:cells.Min(c=>c.Y)),high=Math.Max(1,cells.Length==0?0:cells.Max(c=>c.Y));if(map.IsCurrent){low=Math.Min(low,(int)Math.Floor(map.PlayerY/4));high=Math.Max(high,(int)Math.Floor(map.PlayerY/4));}
            // 形式上の大きな座標があってもUIオブジェクト数を増やさない。開示セルだけを描画する。
            double rows=high-(double)low+1;float size=(float)Math.Min(140,(mapAcquire==null?270:210)/rows);float left=-4*size,bottom=(float)(-rows*size/2);
            void Tile(string name,float x,float y,float width,float height,Color color,string label=null){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(root.transform,false);var im=go.GetComponent<Image>();im.color=color;im.raycastTarget=false;var rr=(RectTransform)go.transform;rr.anchoredPosition=new Vector2(x,y);rr.sizeDelta=new Vector2(width,height);if(label!=null){var text=new GameObject("Label",typeof(RectTransform),typeof(Text)).GetComponent<Text>();text.transform.SetParent(go.transform,false);text.font=mapFont;text.text=label;text.fontSize=24;text.color=Color.white;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.rectTransform.sizeDelta=new Vector2(35,35);}}
            var revealed=new Color(.1f,.42f,.56f,1);
            void Cell(int x,int y,string prefix)=>Tile(prefix+x+"_"+y,left+(x+.5f)*size,(float)(bottom+(y-(double)low+.5)*size),Mathf.Max(1,size-2),Mathf.Max(1,size-2),revealed);
            if(map.ItemRevealed){
                // 開示と実訪問は同じ色。高い試走壁などでもオブジェクト数を表示範囲で抑える。
                if(rows<=128)for(int y=low;y<=high;y++)for(int x=0;x<8;x++)Cell(x,y,"Revealed_");
                else Tile("FullyRevealedArea",0,0,8*size,(float)(rows*size),revealed);
            }else foreach(var cell in cells)Cell(cell.X,cell.Y,"Visited_");
            if(map.RecoveryVisible)Tile(map.RecoveryKnown?"DiscoveredRecovery":"UnvisitedRecovery",left+1.5f*size,(float)(bottom+(0-(double)low)*size)+38,32,32,map.RecoveryKnown?new Color(.02f,.4f,.42f,1):new Color(.22f,.27f,.32f,1),"R");
            if(map.IsCurrent)Tile("CurrentPosition",left+(float)(map.PlayerX/4)*size,(float)(bottom+(map.PlayerY/4-low)*size),28,28,new Color(.6f,.34f,.08f,1),"P");
            var target=navigation?.Invoke()?.Resolve(map.MapId);if(target!=null){
                string name=target.ViaExit?"NavigationExit":"NavigationDestination";Tile(name,left+(float)(target.GuidePosition/(double)OutsideMapRevealTrial.CellSize)*size,(float)(bottom+(-low+.5)*size),32,32,new Color(.6f,.3f,.02f,1),"!");
                var mark=root.transform.Find(name).GetComponent<Image>();var at=mark.rectTransform.anchoredPosition;var player=root.transform.Find("CurrentPosition") as RectTransform;var recovery=root.transform.Find(map.RecoveryKnown?"DiscoveredRecovery":"UnvisitedRecovery") as RectTransform;
                bool nearPlayer=player!=null&&(at-player.anchoredPosition).sqrMagnitude<42*42;bool nearRecovery=recovery!=null&&(at-recovery.anchoredPosition).sqrMagnitude<42*42;
                if(nearPlayer||nearRecovery){var label=mark.GetComponentInChildren<Text>();mark.color=Color.clear;float offset=nearPlayer&&at.x<=player.anchoredPosition.x?-40:40;if(Mathf.Abs(at.x+offset)>580)offset=-offset;label.rectTransform.anchoredPosition=new Vector2(offset,0);label.color=new Color(1,.94f,.5f);}
            }
            var legend=new GameObject("MapLegend",typeof(RectTransform),typeof(Text)).GetComponent<Text>();legend.transform.SetParent(root.transform,false);legend.font=mapFont;legend.fontSize=18;legend.color=new Color(.8f,.96f,1);legend.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana("P: ゲンザイチ / R: カイフク / !: アンナイ / アカルいクカク: カイジずみ\n4worldグリッド（カリ） / シヨウリツ: タカラバコ・カイフクチテンの カイホウ");legend.alignment=TextAnchor.MiddleCenter;legend.raycastTarget=false;legend.rectTransform.sizeDelta=new Vector2(1220,70);legend.rectTransform.anchoredPosition=new Vector2(0,mapAcquire==null?-185:-160);
        }
        public void Hide(){renderEpoch++;menu?.Dispose();menu=null;var prior=context;context=null;if(this!=null)gameObject.SetActive(false);prior?.NotifyHidden();}
        private void RefreshMapGuide(){if(mapGuide!=null)mapGuide.text=GameplayPauseView.InputGuideText(Olca.Unity.UnityInputProvider.ActiveGamepadProfile,Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad)+" / ポーズへ戻る";}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}RefreshMapGuide();menu?.Tick();}
        private void OnDisable(){renderEpoch++;menu?.Dispose();menu=null;context?.NotifyHidden();context=null;}
        private void OnDestroy(){OnDisable();discovered=ownerAlive=null;travel=close=null;mapAcquire=null;mapSource=null;mapData=Array.Empty<OutsideTrialMapOverview>();actions.Clear();}
    }
    public sealed class OutsideItemRegistrationView : UnityEngine.MonoBehaviour
    {
        private ManagedScreenContext context;
        private Olca.Core.Domain.ClearReturnSession session;
        private Olca.Core.Domain.OutsideItemDisplayDefinition[] definitions;
        private System.Action close;
        private Olca.Unity.MenuPadScope menu;
        private UnityEngine.Font font;
        private UnityEngine.Transform content;
        private int selectedSlot,page;private long renderEpoch;
        private System.Func<string,string> useItem;private System.Func<string,int> cooldown;private string useMessage;
        public void ConfigureUse(System.Func<string,string> use,System.Func<string,int> remaining){useItem=use;cooldown=remaining;}
        private bool CanOperate=>this!=null&&isActiveAndEnabled&&context?.IsCurrent==true&&!context.DialogBlocksInput;
        public void Initialize(UnityEngine.Font value,Olca.Core.Domain.ClearReturnSession source,Olca.Core.Domain.OutsideItemDisplayDefinition[] catalog,System.Action closed){font=value;session=source;definitions=catalog.ToArray();close=closed;}
        public void Open(ManagedScreenContext owner){context=owner;useMessage=null;selectedSlot=0;gameObject.SetActive(true);transform.SetAsLastSibling();Render();}
        private UnityEngine.UI.Text Text(string name,string value,float x,float y,float width,float height,int size){var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(content,false);var label=go.GetComponent<UnityEngine.UI.Text>();label.font=font;label.text=value;label.fontSize=size;label.alignment=UnityEngine.TextAnchor.MiddleCenter;label.color=UnityEngine.Color.white;label.raycastTarget=false;var r=label.rectTransform;r.anchoredPosition=new UnityEngine.Vector2(x,y);r.sizeDelta=new UnityEngine.Vector2(width,height);LiquidGlassMenu.ProtectText(label);return label;}
        private UnityEngine.UI.Button Button(string name,string text,float x,float y,float width,System.Action action){var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(content,false);var r=(UnityEngine.RectTransform)go.transform;r.anchoredPosition=new UnityEngine.Vector2(x,y);r.sizeDelta=new UnityEngine.Vector2(width,74);var b=go.GetComponent<UnityEngine.UI.Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(b);var label=Text(name+"Label",text,0,0,width-20,68,25);label.transform.SetParent(go.transform,false);label.rectTransform.anchoredPosition=UnityEngine.Vector2.zero;label.resizeTextForBestFit=true;label.resizeTextMinSize=18;label.resizeTextMaxSize=25;LiquidGlassMenu.Button(b);var captured=context;long capturedRender=renderEpoch;b.onClick.AddListener(()=>{if(b!=null&&b.isActiveAndEnabled&&CanOperate&&capturedRender==renderEpoch&&ReferenceEquals(context,captured))action();});return b;}
        private void Render()
        {
            renderEpoch++;menu?.Dispose();menu=null;
            if(content!=null){content.gameObject.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content.gameObject);else DestroyImmediate(content.gameObject);}
            var root=new UnityEngine.GameObject("OutsideItemContents",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));root.transform.SetParent(transform,false);content=root.transform;((UnityEngine.RectTransform)content).sizeDelta=new UnityEngine.Vector2(1540,920);LiquidGlassMenu.Panel(root.GetComponent<UnityEngine.UI.Image>());
            var data=session.OutsideInventorySnapshot;bool pending=session.HasPendingSave;
            Text("OutsideItemsTitle","探索アイテム / ショートカット",0,385,1400,65,40);
            Text("OutsideWallet","所持金："+data.Currency+"  / 超過カードのトークンとは別",0,310,1400,50,25);
            for(int n=0;n<4;n++){
                int index=n;string id=data.Shortcuts[n];var item=definitions.SingleOrDefault(v=>v.Id==id);int count=data.Items.FirstOrDefault(v=>v.Id==id)?.Count??0;
                var button=Button("OutsideItemSlot_"+n,new[]{"1 / ウエ","2 / ミギ","3 / シタ","4 / ヒダリ"}[n]+"\n"+(id==null?"ミトウロク":item?.Name??"ミトウロクID")+"  "+count,-555+n*370,195,340,()=>{selectedSlot=index;Render();});button.interactable=!pending;if(index==selectedSlot)button.GetComponent<UnityEngine.UI.Image>().color=Olca.Presentation.Dungeon.UiLook.Chosen;
            }
            Text("OutsideItemsHelp","枠を選んで、ショジ アイテムをトウロク。ジュウフクはイレカエ。",0,100,1420,55,25);
            var known=data.Items.Where(v=>definitions.Any(d=>d.Id==v.Id)).ToArray();
            if(known.Length==0)Text("OutsideNoItems","登録できるアイテムはありません。",0,0,1400,60,28);
            // 1ページを限定し、長い所持一覧で下部操作を押し出さない。
            int pages=System.Math.Max(1,(known.Length+1)/2);page=System.Math.Min(page,pages-1);var rows=known.Skip(page*2).Take(2).ToArray();
            for(int i=0;i<rows.Length;i++){string id=rows[i].Id;var definition=definitions.Single(d=>d.Id==id);var button=Button("RegisterOutsideItem_"+i,definition.Name+"  / ショジ: "+rows[i].Count,0,-i*90,1240,()=>{session.TryAssignOutsideItem(selectedSlot,id,definitions);Render();});button.interactable=!pending;}
            if(pages>1){Button("OutsideItemPrevious","マエ",-350,-185,270,()=>{page--;Render();}).interactable=!pending&&page>0;Text("OutsideItemPage",(page+1)+" / "+pages,0,-185,300,55,25);Button("OutsideItemNext","ツギ",350,-185,270,()=>{page++;Render();}).interactable=!pending&&page+1<pages;}
            int unknown=data.Items.Count(v=>!definitions.Any(d=>d.Id==v.Id));
            Text("OutsideItemsStatus",useMessage??session.Error??(unknown>0?"ミトウロク ID "+unknown+"ケンは ホゾンのままホジしています。":"ショジスウ0でも トウロクをホジします。"),0,-260,1400,44,25);
            string selectedId=data.Shortcuts[selectedSlot];int remaining=cooldown?.Invoke(selectedId)??0;
            Button("UseOutsideItem","登録アイテムを使う"+(remaining>0?" / "+((remaining+59L)/60)+"ビョウ":""),0,-330,1240,()=>{useMessage=useItem?.Invoke(selectedId);Render();}).interactable=!pending&&useItem!=null&&selectedId!=null&&remaining==0;
            Text("OutsideItemsScope","カリ: HP+25 / ドクケシ / HP+25とドクケシ / HP25%。CD: 3/2/3/3ビョウ。フクゴウ・25%はショップ。",0,55,1440,30,20);

            Button("ClearOutsideItemSlot","枠を空にする",-480,-410,420,()=>{session.TryAssignOutsideItem(selectedSlot,null,definitions);Render();}).interactable=!pending;
            Button("RetryOutsideItemsSave","保存を再試行",0,-410,420,()=>{session.Retry();Render();}).interactable=pending;
            Button("CloseOutsideItems","戻る",480,-410,420,()=>close?.Invoke());
            menu=new Olca.Unity.MenuPadScope(()=>CanOperate);menu.Bind(content,"OutsideItemSlot_"+selectedSlot,()=>close?.Invoke());
        }
        public void Hide(){renderEpoch++;menu?.Dispose();menu=null;var prior=context;context=null;if(this!=null)gameObject.SetActive(false);prior?.NotifyHidden();}
        private void LateUpdate(){if(context!=null&&!context.IsCurrent){Hide();return;}menu?.Tick();}
        private void OnDisable(){renderEpoch++;menu?.Dispose();menu=null;context?.NotifyHidden();context=null;}
        private void OnDestroy(){menu?.Dispose();menu=null;context=null;session=null;definitions=null;close=null;}
    }
}

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private System.Func<Olca.Core.Domain.OutsideInventoryData> itemInventory;
        private System.Func<long> itemRevision;
        private Olca.Core.Domain.OutsideItemDisplayDefinition[] itemDefinitions;
        private readonly UnityEngine.UI.Text[] itemMarks=new UnityEngine.UI.Text[4],itemCounts=new UnityEngine.UI.Text[4];
        private long shownItemRevision=long.MinValue;
        public void ConfigureItemInventory(System.Func<Olca.Core.Domain.OutsideInventoryData> read,System.Func<long> revision,Olca.Core.Domain.OutsideItemDisplayDefinition[] definitions){itemInventory=read;itemRevision=revision;itemDefinitions=definitions;shownItemRevision=long.MinValue;RefreshItemHud();}
        private System.Action<int> queueItemUse;private System.Func<string,int> readItemCooldown,readItemDuration;private System.Func<int,string> readItemKey;
        private Olca.Core.Domain.OutsideInventoryData cachedItems;
        private readonly UnityEngine.UI.Button[] itemButtons=new UnityEngine.UI.Button[4];
        private readonly OutsideItemPressGuard[] itemPresses=new OutsideItemPressGuard[4];
        private readonly UnityEngine.UI.Text[] itemSeconds=new UnityEngine.UI.Text[4],itemKeys=new UnityEngine.UI.Text[4];
        private readonly OutsideItemCooldownRing[] itemRings=new OutsideItemCooldownRing[4];
        private readonly UnityEngine.UI.Image[] itemImages=new UnityEngine.UI.Image[4];
        private Olca.Unity.GeneratedSpriteAssets.Lease itemIconAssets;
        private void RefreshItemGraphic(int slot,string id)
        {
            string path=id==Olca.Core.Domain.OutsideRecoveryItemDefinition.PoisonProvisional.Id?"OlcaPickups/antidote_vial_v1":
                id==Olca.Core.Domain.OutsideRecoveryItemDefinition.Provisional.Id?"OlcaPickups/heal_vial":null;
            UnityEngine.Sprite sprite=null;
            if(path!=null){
                var source=UnityEngine.Resources.Load<UnityEngine.TextAsset>(path);
                if(source!=null)sprite=(itemIconAssets??(itemIconAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this))).Get<UnityEngine.Sprite>(path,builder=>{
                    var texture=builder.CreateTexture(2,2);if(!UnityEngine.ImageConversion.LoadImage(texture,source.bytes,true))return null;
                    texture.filterMode=UnityEngine.FilterMode.Point;texture.wrapMode=UnityEngine.TextureWrapMode.Clamp;
                    return builder.CreateSprite(texture,new UnityEngine.Rect(0,0,texture.width,texture.height),new UnityEngine.Vector2(.5f,.5f));
                });
            }
            if(itemImages[slot]==null&&sprite!=null){
                var go=new UnityEngine.GameObject("OutsideItemImage_"+slot,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));
                go.transform.SetParent(itemMarks[slot].transform,false);var image=itemImages[slot]=go.GetComponent<UnityEngine.UI.Image>();
                image.raycastTarget=false;image.preserveAspect=true;image.rectTransform.sizeDelta=new UnityEngine.Vector2(56,56);
            }
            if(itemImages[slot]!=null){itemImages[slot].sprite=sprite;itemImages[slot].enabled=sprite!=null;itemImages[slot].color=itemMarks[slot].color;}
            if(sprite!=null)itemMarks[slot].text="";
        }
        private UnityEngine.UI.Text itemFeedback;
        public void ConfigureItemUse(System.Action<int> queue,System.Func<string,int> remaining,System.Func<int,string> key)=>ConfigureItemUse(queue,remaining,key,null);
        public void ConfigureItemUse(System.Action<int> queue,System.Func<string,int> remaining,System.Func<int,string> key,System.Func<string,int> duration){queueItemUse=queue;readItemCooldown=remaining;readItemKey=key;readItemDuration=duration;RefreshItemHud();}
        public void ShowItemFeedback(string message){if(itemFeedback!=null)itemFeedback.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(message??"");}
        private void BindItemHudUse(Olca.Presentation.Management.ManagedScreenContext owner,OutsideExplorationSession.PresentationLease lease)
        {
            for(int i=0;i<4;i++){int slot=i;var button=itemButtons[i];if(button==null)continue;button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();itemPresses[i].BeginAllowed=()=>this!=null&&button!=null&&button.IsInteractable()&&ReferenceEquals(context,owner)&&owner.IsCurrent&&lease.IsCurrent&&CanPlay;button.onClick.AddListener(()=>{
                if(itemPresses[slot].Consume()&&this!=null&&button!=null&&button.isActiveAndEnabled&&button.IsInteractable()&&ReferenceEquals(context,owner)&&owner.IsCurrent&&ReferenceEquals(presentation,lease)&&lease.IsCurrent&&CanPlay)queueItemUse?.Invoke(slot);
            });}
        }
        private void InitializeItemHud(Olca.Presentation.Management.GameplayPanelFactory factory)
        {
            for(int i=0;i<4;i++){
                float x=80+i*110;var go=new UnityEngine.GameObject("OutsideItemHud_"+i,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(transform,false);var image=go.GetComponent<UnityEngine.UI.Image>();image.color=new UnityEngine.Color(.04f,.18f,.23f,.9f);image.raycastTarget=true;itemButtons[i]=go.AddComponent<UnityEngine.UI.Button>();itemPresses[i]=go.AddComponent<OutsideItemPressGuard>();go.AddComponent<Olca.Unity.TouchUiCancelGuard>();var rect=(UnityEngine.RectTransform)go.transform;rect.anchorMin=rect.anchorMax=UnityEngine.Vector2.zero;rect.anchoredPosition=new UnityEngine.Vector2(x,84);rect.sizeDelta=new UnityEngine.Vector2(88,88);
                var ringGo=new UnityEngine.GameObject("OutsideItemCooldownRing_"+i,typeof(UnityEngine.RectTransform),typeof(OutsideItemCooldownRing));ringGo.transform.SetParent(go.transform,false);var ring=itemRings[i]=ringGo.GetComponent<OutsideItemCooldownRing>();ring.raycastTarget=false;ring.rectTransform.sizeDelta=new UnityEngine.Vector2(88,88);ring.enabled=false;
                var seconds=itemSeconds[i]=factory.Label("OutsideItemCooldownSeconds_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,142.5f),new UnityEngine.Vector2(90,25));seconds.fontSize=16;seconds.color=new UnityEngine.Color(.65f,1,1,1);seconds.raycastTarget=false;seconds.text="";
                var mark=itemMarks[i]=factory.Label("OutsideItemMark_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,96),new UnityEngine.Vector2(84,55));mark.fontSize=24;mark.raycastTarget=false;
                var count=itemCounts[i]=factory.Label("OutsideItemCount_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,53),new UnityEngine.Vector2(84,28));count.fontSize=22;count.resizeTextForBestFit=true;count.resizeTextMinSize=12;count.resizeTextMaxSize=22;count.raycastTarget=false;
                var key=itemKeys[i]=factory.Label("OutsideItemKey_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,130),new UnityEngine.Vector2(90,32));key.text=new[]{"1 / ↑","2 / →","3 / ↓","4 / ←"}[i];key.fontSize=20;key.resizeTextForBestFit=true;key.resizeTextMinSize=12;key.resizeTextMaxSize=20;key.color=UnityEngine.Color.white;key.raycastTarget=false;
            }
            var hint=itemFeedback=factory.Label("OutsideItemsUnconnected",transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(250,17),new UnityEngine.Vector2(470,26));hint.text="アイテム: 1〜4 / ジュウジキー / タップ";hint.resizeTextForBestFit=true;hint.resizeTextMinSize=12;hint.resizeTextMaxSize=18;hint.fontSize=18;hint.color=new UnityEngine.Color(.65f,.82f,.86f,1);hint.raycastTarget=false;
            RefreshItemHud();
        }
        private void RefreshItemHud()
        {
            if(itemMarks[0]==null)return;long revision=itemRevision?.Invoke()??-1;
            if(cachedItems==null||revision!=shownItemRevision){if(cachedItems!=null)ClearItemPresses();shownItemRevision=revision;cachedItems=itemInventory?.Invoke()??new Olca.Core.Domain.OutsideInventoryData();}
            for(int i=0;i<4;i++){
                string id=cachedItems.Shortcuts[i];var definition=itemDefinitions?.FirstOrDefault(v=>v.Id==id);int count=cachedItems.Items.FirstOrDefault(v=>v.Id==id)?.Count??0;int remaining=readItemCooldown?.Invoke(id)??0;
                itemMarks[i].text=id==null?"—":definition?.Mark??"?";itemMarks[i].color=count==0||remaining>0?new UnityEngine.Color(.45f,.58f,.62f,1):UnityEngine.Color.white;itemCounts[i].text=id==null?"":count.ToString();itemCounts[i].color=UnityEngine.Color.white;
                RefreshItemGraphic(i,id);
                itemButtons[i].interactable=CanPlay&&queueItemUse!=null&&definition!=null&&count>0&&remaining==0;
                itemKeys[i].text=readItemKey?.Invoke(i)??new[]{"1 / ↑","2 / →","3 / ↓","4 / ←"}[i];itemKeys[i].enabled=remaining==0;
                itemSeconds[i].text=remaining>0?((remaining+59L)/60).ToString():"";itemSeconds[i].enabled=remaining>0;
                itemRings[i].Fraction=remaining<=0?0:UnityEngine.Mathf.Clamp01((float)remaining/Mathf.Max(1,readItemDuration?.Invoke(id)??Olca.Core.Domain.OutsideRecoveryItemDefinition.Provisional.CooldownFrames));itemRings[i].enabled=remaining>0;
            }
        }
        private void ClearItemPresses(){foreach(var press in itemPresses)if(press!=null)press.Clear();}
        private void ClearItemProviders(){foreach(var image in itemImages)if(image!=null){image.sprite=null;image.enabled=false;}itemIconAssets?.Dispose();itemIconAssets=null;ClearItemPresses();foreach(var press in itemPresses)if(press!=null)press.BeginAllowed=null;itemInventory=null;itemRevision=null;itemDefinitions=null;queueItemUse=null;readItemCooldown=readItemDuration=null;readItemKey=null;cachedItems=null;shownItemRevision=long.MinValue;foreach(var button in itemButtons)if(button!=null){button.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();button.interactable=false;}}
    }

    // 使用可になるまで保持した入力を、解放時のクリックとして後から受理しない。
    public sealed class OutsideItemPressGuard : UnityEngine.MonoBehaviour,UnityEngine.EventSystems.IPointerDownHandler,UnityEngine.EventSystems.IPointerUpHandler
    {
        public System.Func<bool> BeginAllowed;
        private readonly System.Collections.Generic.Dictionary<int,bool> presses=new System.Collections.Generic.Dictionary<int,bool>();
        private bool released;private int releaseFrame=-1;
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData data){if(data==null)return;released=false;presses[data.pointerId]=data.button==UnityEngine.EventSystems.PointerEventData.InputButton.Left&&BeginAllowed?.Invoke()==true;}
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData data){if(data==null)return;released=presses.TryGetValue(data.pointerId,out var allowed)&&allowed;presses.Remove(data.pointerId);releaseFrame=UnityEngine.Time.frameCount;}
        public bool Consume(){bool result=released&&releaseFrame==UnityEngine.Time.frameCount;released=false;return result;}
        public void Clear(){presses.Clear();released=false;releaseFrame=-1;}
        private void OnDisable()=>Clear();
        private void OnDestroy(){Clear();BeginAllowed=null;}
    }
    [UnityEngine.RequireComponent(typeof(UnityEngine.CanvasRenderer))]
    public sealed class OutsideItemCooldownRing : UnityEngine.UI.MaskableGraphic
    {
        private float fraction;
        public float Fraction{get=>fraction;set{value=UnityEngine.Mathf.Clamp01(value);if(fraction==value)return;fraction=value;SetVerticesDirty();}}
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();if(fraction<=0)return;const int segments=64;float outer=UnityEngine.Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.5f,inner=outer-3;var tint=new UnityEngine.Color(.55f,.95f,1,1);
            for(int n=0;n<segments;n++){float start=(float)n/segments;if(start>=fraction)break;float end=UnityEngine.Mathf.Min((n+1f)/segments,fraction);float a=(.25f-start)*UnityEngine.Mathf.PI*2,b=(.25f-end)*UnityEngine.Mathf.PI*2;int v=vh.currentVertCount;vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*inner,UnityEngine.Mathf.Sin(a)*inner),tint,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(a)*outer,UnityEngine.Mathf.Sin(a)*outer),tint,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*outer,UnityEngine.Mathf.Sin(b)*outer),tint,UnityEngine.Vector2.zero);vh.AddVert(new UnityEngine.Vector3(UnityEngine.Mathf.Cos(b)*inner,UnityEngine.Mathf.Sin(b)*inner),tint,UnityEngine.Vector2.zero);vh.AddTriangle(v,v+1,v+2);vh.AddTriangle(v,v+2,v+3);}
        }
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>Overlay座標だけを適応する数値View。既存World用の書体/色/軌道を借用する。</summary>
    public sealed class OutsideCombatFeedbackCanvas:MonoBehaviour
    {
        private sealed class Entry{public Text Text;public Vector2 World;public float Age,Lift,OverlayDrop;public bool Active,Placed;}
        private readonly List<Entry> entries=new List<Entry>();
        private Olca.Core.Domain.PlayerCharacter actor;private Olca.Core.Domain.Enemy enemy;private Olca.Presentation.Effects.CombatNumberStyle style;
        private Func<bool> live;private Func<Vector2> origin;private float pixelsPerWorldUnit;private int lastFrame=-1;
        public int ActiveCount{get;private set;}
        public void Bind(Olca.Core.Domain.PlayerCharacter value,Olca.Presentation.Effects.CombatNumberStyle visual,Func<bool> isLive,Func<Vector2> anchor,float scale)
        {Unbind();if(value==null||visual==null)return;actor=value;style=visual;live=isLive;origin=anchor;pixelsPerWorldUnit=scale;actor.OnCombatFeedback+=OnFeedback;}
        public void BindEnemy(Olca.Core.Domain.Enemy value,Olca.Presentation.Effects.CombatNumberStyle visual,Func<bool> isLive,Func<Vector2> anchor,float scale){Unbind();if(value==null||visual==null)return;enemy=value;style=visual;live=isLive;origin=anchor;pixelsPerWorldUnit=scale;enemy.OnCombatFeedback+=OnFeedback;}
        private void OnFeedback(Olca.Core.Domain.CombatFeedback feedback)
        {
            if(actor==null&&enemy==null||style==null||live?.Invoke()!=true||origin==null)return;
            var entry=entries.FirstOrDefault(e=>!e.Active);
            if(entry==null){var go=new GameObject("OutsideCombatNumber_"+entries.Count,typeof(RectTransform),typeof(Text),typeof(Outline));go.transform.SetParent(transform,false);var text=go.GetComponent<Text>();text.raycastTarget=false;text.alignment=TextAnchor.MiddleCenter;text.horizontalOverflow=HorizontalWrapMode.Overflow;text.verticalOverflow=VerticalWrapMode.Overflow;text.supportRichText=false;text.lineSpacing=1;entry=new Entry{Text=text};entries.Add(entry);}
            entry.Text.font=style.Font;entry.Text.fontStyle=style.FontStyle;entry.Text.fontSize=Mathf.Max(1,Mathf.RoundToInt(style.FontWorldUnits*pixelsPerWorldUnit));entry.Text.text=Olca.Presentation.Effects.CombatNumberPresentation.Format(feedback);entry.Text.color=style.Color;
            var outline=entry.Text.GetComponent<Outline>();outline.effectColor=new Color(.02f,.04f,.06f,.9f);outline.effectDistance=Vector2.one*(style.OutlineWorldUnits*pixelsPerWorldUnit);outline.useGraphicAlpha=true;
            entry.World=origin();entry.Age=entry.Lift=entry.OverlayDrop=0;entry.Placed=false;entry.Active=true;entry.Text.enabled=true;ActiveCount++;entry.Text.gameObject.SetActive(true);entry.Text.transform.SetAsLastSibling();
        }
        public void Paint(Func<Vector2,Vector2> project,IReadOnlyList<RectTransform> obstacles,RectTransform protectedOverlay=null)
        {
            if(style==null||ActiveCount==0)return;if(live?.Invoke()!=true){Unbind();return;}
            bool advance=lastFrame!=Time.frameCount;lastFrame=Time.frameCount;var root=(RectTransform)transform;var occupied=new List<Rect>();
            if(obstacles!=null)foreach(var obstacle in obstacles)if(obstacle!=null&&obstacle.gameObject.activeInHierarchy){var corners=new Vector3[4];obstacle.GetWorldCorners(corners);var local=corners.Select(p=>root.InverseTransformPoint(p)).ToArray();occupied.Add(Rect.MinMaxRect(local.Min(p=>p.x),local.Min(p=>p.y),local.Max(p=>p.x),local.Max(p=>p.y)));}
            var visible=new List<(Entry Entry,Vector2 Position,Vector2 Size)>();float top=float.NegativeInfinity;
            foreach(var entry in entries){if(!entry.Active)continue;if(advance)entry.Age+=Olca.Unity.BattleVisualClock.DeltaTime;if(entry.Age>=style.Duration){entry.Active=false;ActiveCount--;entry.Text.gameObject.SetActive(false);continue;}
                var position=project(entry.World+Vector2.up*(Olca.Presentation.Effects.CombatNumberPresentation.Rise(style.FloatSpeed,style.Duration,entry.Age)+entry.Lift));
                var size=new Vector2(Mathf.Max(1,entry.Text.preferredWidth+4),Mathf.Max(1,entry.Text.preferredHeight));entry.Text.rectTransform.sizeDelta=size;
                // 地点案内と先行の数値の上へ積む。World poolと同じく過去の効果を捨てない。
                float beforeLift=position.y;
                if(!entry.Placed)for(int pass=0;pass<=occupied.Count;pass++){var box=new Rect(position-size*.5f,size);bool moved=false;foreach(var area in occupied)if(box.Overlaps(area)){position.y=area.yMax+size.y*.5f+6;box=new Rect(position-size*.5f,size);moved=true;}if(!moved)break;}
                if(!entry.Placed){entry.Lift+=(position.y-beforeLift)/pixelsPerWorldUnit;entry.Placed=true;}
                occupied.Add(new Rect(position-size*.5f,size));visible.Add((entry,position,size));top=Mathf.Max(top,position.y+size.y*.5f);
            }
            float shift=Mathf.Max(0,top-(root.rect.yMax-144));
            // 上端補正後も表示中の地名を保護する。補正を保持し、地名の消失で数値を戻さない。
            bool protect=protectedOverlay!=null&&protectedOverlay.gameObject.activeInHierarchy;
            var finalOccupied=new List<Rect>();
            if(protect&&obstacles!=null)foreach(var obstacle in obstacles)if(obstacle!=null&&obstacle.gameObject.activeInHierarchy){var corners=new Vector3[4];obstacle.GetWorldCorners(corners);var local=corners.Select(p=>root.InverseTransformPoint(p)).ToArray();finalOccupied.Add(Rect.MinMaxRect(local.Min(p=>p.x),local.Min(p=>p.y),local.Max(p=>p.x),local.Max(p=>p.y)));}
            bool adjusted=protect||visible.Any(row=>row.Entry.OverlayDrop>0);
            foreach(var row in adjusted?visible.OrderByDescending(row=>row.Position.y).ToList():visible){var position=row.Position-Vector2.up*(shift+row.Entry.OverlayDrop);
                if(adjusted){float before=position.y;for(int pass=0;pass<=finalOccupied.Count;pass++){var box=new Rect(position-row.Size*.5f,row.Size);bool moved=false;foreach(var area in finalOccupied)if(box.Overlaps(area)){position.y=area.yMin-row.Size.y*.5f-6;box=new Rect(position-row.Size*.5f,row.Size);moved=true;}if(!moved)break;}row.Entry.OverlayDrop+=before-position.y;finalOccupied.Add(new Rect(position-row.Size*.5f,row.Size));}
                row.Entry.Text.rectTransform.anchoredPosition=position;var color=style.Color;color.a=Olca.Presentation.Effects.CombatNumberPresentation.Alpha(style.Duration,row.Entry.Age);row.Entry.Text.color=color;row.Entry.Text.enabled=position.y-row.Size.y*.5f>=root.rect.yMin+160;}
        }
        public void Unbind(){if(actor!=null)actor.OnCombatFeedback-=OnFeedback;if(enemy!=null)enemy.OnCombatFeedback-=OnFeedback;actor=null;enemy=null;style=null;live=null;origin=null;lastFrame=-1;ActiveCount=0;foreach(var entry in entries){entry.Active=false;entry.Age=entry.OverlayDrop=0;if(entry.Text!=null){entry.Text.text="";entry.Text.font=null;entry.Text.gameObject.SetActive(false);}}}
        private void OnDisable()=>Unbind();
        private void OnDestroy(){Unbind();entries.Clear();}
    }
    public sealed partial class OutsideExplorationView
    {
        private OutsideCombatFeedbackCanvas feedbackCanvas;
        private OutsideHealingSpriteCanvas healingCanvas;
        private OutsideShieldHitSpriteCanvas shieldHitCanvas;
        private OutsideShieldActivationSpriteCanvas shieldActivationCanvas;
        private readonly List<RectTransform> feedbackObstacles=new List<RectTransform>();
        public void ConfigureCombatFeedback(Olca.Presentation.Effects.CombatNumberStyle style)
        {
            trialNumberStyle=style;BindTrialTargetFeedback();
            if(healingCanvas==null)healingCanvas=gameObject.AddComponent<OutsideHealingSpriteCanvas>();var healingOwner=context;var healingLease=presentation;healingCanvas.Bind(healingLease?.Player,()=>this!=null&&ReferenceEquals(context,healingOwner)&&healingOwner?.IsCurrent==true&&ReferenceEquals(presentation,healingLease)&&healingLease.IsCurrent,player,pixelsPerWorldUnit);
            if(shieldHitCanvas==null)shieldHitCanvas=gameObject.AddComponent<OutsideShieldHitSpriteCanvas>();shieldHitCanvas.Bind(healingLease?.Player,()=>this!=null&&ReferenceEquals(context,healingOwner)&&healingOwner?.IsCurrent==true&&ReferenceEquals(presentation,healingLease)&&healingLease.IsCurrent,player,pixelsPerWorldUnit);
            if(shieldActivationCanvas==null)shieldActivationCanvas=gameObject.AddComponent<OutsideShieldActivationSpriteCanvas>();shieldActivationCanvas.Bind(healingLease?.Player,()=>this!=null&&ReferenceEquals(context,healingOwner)&&healingOwner?.IsCurrent==true&&ReferenceEquals(presentation,healingLease)&&healingLease.IsCurrent,player,pixelsPerWorldUnit);
            if(feedbackCanvas==null&&style!=null)feedbackCanvas=gameObject.AddComponent<OutsideCombatFeedbackCanvas>();if(feedbackCanvas==null)return;var owner=context;var lease=presentation;
            feedbackCanvas.Bind(lease?.Player,style,()=>this!=null&&ReferenceEquals(context,owner)&&owner?.IsCurrent==true&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)lease.Core.Position/OutsideMovementDefinition.UnitsPerWorldUnit,(float)lease.Core.Height/OutsideMovementDefinition.UnitsPerWorldUnit+player.rectTransform.rect.height/pixelsPerWorldUnit+.2f),pixelsPerWorldUnit);
        }
        private void ClearCombatFeedback(){trialSupportBody?.Dispose();trialSupportBody=null;supportPulseSeen=supportPulseSuppressed=0;supportPulseStyle=null;shieldActivationCanvas?.Unbind();shieldHitCanvas?.Unbind();healingCanvas?.Unbind();feedbackCanvas?.Unbind();feedbackObstacles.Clear();}
        private void RenderCombatFeedback(float camera,float verticalOffset)
        {
            shieldActivationCanvas?.Paint(Olca.Unity.BattleVisualClock.DeltaTime,Time.frameCount);healingCanvas?.Paint(Olca.Unity.BattleVisualClock.DeltaTime,Time.frameCount);shieldHitCanvas?.Paint(Olca.Unity.BattleVisualClock.DeltaTime,Time.frameCount);
            if(feedbackCanvas==null||feedbackCanvas.ActiveCount==0)return;feedbackObstacles.Clear();if(mapNameLabel!=null&&mapNameLabel.isActiveAndEnabled&&mapNameLabel.color.a>0)feedbackObstacles.Add(mapNameLabel.rectTransform);if(recoveryPoint!=null)feedbackObstacles.Add((RectTransform)recoveryPoint.transform);if(reloadPoint!=null&&reloadPoint.gameObject.activeInHierarchy)feedbackObstacles.Add(reloadPoint);if(entrance!=null)feedbackObstacles.Add((RectTransform)entrance.transform);foreach(var slot in supplySlots)if(slot?.Button!=null)feedbackObstacles.Add((RectTransform)slot.Button.transform);
            if(trialTargetName!=null)feedbackObstacles.Add(trialTargetPlate);if(trialTargetGauge!=null&&trialTargetGauge.enabled)feedbackObstacles.Add(trialTargetGauge.rectTransform);if(secondTrialGauge!=null&&secondTrialGauge.enabled)feedbackObstacles.Add(secondTrialGauge.rectTransform);float floor=-((RectTransform)transform).rect.height*.29f;feedbackCanvas.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,floor+(world.y-verticalOffset)*pixelsPerWorldUnit),feedbackObstacles,mapNameLabel!=null&&mapNameLabel.isActiveAndEnabled&&mapNameLabel.color.a>0?mapNameLabel.rectTransform:null);
        }
    }
}

namespace Olca.Presentation.Exploration
{
    // World EnemyViewと同じ円/外周半円の配色と5度分割をOverlayへ適応する。生成Textureは不要。
    /// <summary>既存の即時効果を遅らせず、接触から12論理frameだけ振り抜く暫定表示。</summary>
    public static class OutsideSwordSwingVisual
    {
        // スピアは前方への突進軌跡を使う。判定・本体の時計や保存形式は変更しない。
        public static int TrailRow(string cardId)=>cardId==Olca.Core.Domain.TrialInitialCardDefinitions.Spear?3:1;
        public static int FrameIndex(long relativeToHit)=>relativeToHit< -2||relativeToHit>=12?-1:relativeToHit<0?0:relativeToHit<3?1:relativeToHit<7?2:3;
        // 共有の下振り原画(row1)は左向き。発生位置/判定のfacingとは別に画像だけ反転する。
        public static int TrailScale(string spriteName,int facing)=>spriteName!=null&&spriteName.StartsWith("trial_slash_1_",StringComparison.Ordinal)?-facing:facing;
        // 振り抜き後、構えへ戻る末尾だけ残光を薄くする。判定と連番の時計は変えない。
        public static float Opacity(string cardId,long relativeToHit)=>FrameIndex(relativeToHit)<0?0:cardId!="sword"||relativeToHit<=7?1:(12-relativeToHit)/5f;
    }
    public sealed class OutsideSwordTrailGraphic:Graphic
    {
        private float angle,alpha,radius;
        public static bool TrySample(long age,int facing,out float angle,out float alpha){if(facing!=1&&facing!=-1)throw new ArgumentOutOfRangeException(nameof(facing));angle=alpha=0;if(age<0||age>=12)return false;angle=-14f*Math.Min((int)age,5)*facing;alpha=age<=6?1:(12-age)/6f;return true;}
        public void SetPose(float rightAngle,float opacity,float size){if(float.IsNaN(rightAngle)||float.IsInfinity(rightAngle)||float.IsNaN(opacity)||float.IsInfinity(opacity)||float.IsNaN(size)||float.IsInfinity(size)||size<=0)throw new ArgumentOutOfRangeException();angle=rightAngle;alpha=Mathf.Clamp01(opacity);radius=size;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh){mesh.Clear();if(alpha<=0||radius<=0)return;const int count=12;for(int n=0;n<count;n++){float a=(angle+8+60f*n/count)*Mathf.Deg2Rad,b=(angle+8+60f*(n+1)/count)*Mathf.Deg2Rad;int first=mesh.currentVertCount;var ca=new Color(.65f,.95f,1,alpha*.45f*(1-n/(float)count));var cb=new Color(.65f,.95f,1,alpha*.45f*(1-(n+1)/(float)count));mesh.AddVert(new Vector3(Mathf.Cos(a),Mathf.Sin(a))*radius,ca,Vector2.zero);mesh.AddVert(new Vector3(Mathf.Cos(a),Mathf.Sin(a))*(radius-8),ca,Vector2.zero);mesh.AddVert(new Vector3(Mathf.Cos(b),Mathf.Sin(b))*(radius-8),cb,Vector2.zero);mesh.AddVert(new Vector3(Mathf.Cos(b),Mathf.Sin(b))*radius,cb,Vector2.zero);mesh.AddTriangle(first,first+1,first+2);mesh.AddTriangle(first,first+2,first+3);}}
    }
    public sealed class OutsideTrialHpArc:Graphic
    {
        private float fraction=1;public void SetFraction(float value){if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(nameof(value));value=Mathf.Clamp01(value);if(value==fraction)return;fraction=value;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper mesh){mesh.Clear();Arc(mesh,29.7f,4.752f,360,new Color(.035f,.1f,.13f,1));Arc(mesh,29.7f,3.267f,360*fraction,new Color(.42f,.85f,.87f,1));Arc(mesh,38.016f,2.673f,180,new Color(.12f,.13f,.08f,1));}
        private static void Arc(VertexHelper mesh,float radius,float width,float degrees,Color color){if(degrees<=0)return;int steps=Mathf.Max(1,Mathf.CeilToInt(degrees/5));for(int i=0;i<steps;i++){float a=(90-degrees*i/steps)*Mathf.Deg2Rad,b=(90-degrees*(i+1)/steps)*Mathf.Deg2Rad;Vector2 pa=new Vector2(Mathf.Cos(a),Mathf.Sin(a)),pb=new Vector2(Mathf.Cos(b),Mathf.Sin(b));int n=mesh.currentVertCount;mesh.AddVert(pa*(radius-width*.5f),color,Vector2.zero);mesh.AddVert(pa*(radius+width*.5f),color,Vector2.zero);mesh.AddVert(pb*(radius+width*.5f),color,Vector2.zero);mesh.AddVert(pb*(radius-width*.5f),color,Vector2.zero);mesh.AddTriangle(n,n+1,n+2);mesh.AddTriangle(n,n+2,n+3);}}
    }
    public sealed partial class OutsideExplorationView
    {
        private Sprite trialOriginalEnemy;private Image secondTrialImage;private Text secondTrialHp;private OutsideTrialHpArc secondTrialGauge;private OutsideCombatFeedbackCanvas secondTrialFeedback;private Olca.Core.Domain.Enemy secondFeedbackTarget;
        private OutsideSwordTrailGraphic trialSwordTrail;private RectTransform trialTargetPlate;private Image trialTargetImage,trialSwordImage;private Text trialTargetName,trialTargetHp;private OutsideTrialHpArc trialTargetGauge;
        private Olca.Unity.GeneratedSpriteAssets.Lease trialSwordAssets;private OutsideCombatFeedbackCanvas trialTargetFeedback;private Olca.Core.Domain.Enemy feedbackTarget;
        private Olca.Presentation.Effects.CombatNumberStyle trialNumberStyle;private OutsideExplorationSession.PresentationLease feedbackLease;
        private Button trialEnemyAttackButton,trialHitInvincibilityButton;private Text trialHitInvincibilityLabel;
        private Button goblinApproachButton;private Text goblinApproachLabel;private Image goblinRange;private OutsideSwordTrailGraphic goblinSwing;
        private Button slimeHopButton;private Text slimeHopLabel;private OutsideSlimeLandingGraphic slimeLanding;
        private Olca.Unity.GeneratedSpriteAssets.Lease authoredVfxAssets;private Image authoredSwordLight;private OutsideSwordWallClip authoredSwordLightClip;
        private sealed class AuthoredHitPulse {public Olca.Core.Domain.Enemy Enemy;public Action<Olca.Core.Domain.CombatFeedback> Handler;public Image Image;public long Frame=-1;public long X,Y,SourceX,SourceY;public bool Sword;public int Facing;public OutsideSwordWallClip Clip;}
        private readonly List<AuthoredHitPulse> authoredHits=new List<AuthoredHitPulse>();
        private void WarmAuthoredVfx(){if(authoredVfxAssets!=null)return;authoredVfxAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);for(int kind=0;kind<3;kind++)Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(authoredVfxAssets,kind,0);}
        private void BindAuthoredHit(Olca.Core.Domain.Enemy enemy,long x){
            if(authoredHits.Any(p=>ReferenceEquals(p.Enemy,enemy)))return;var pulse=new AuthoredHitPulse{Enemy=enemy,X=x};var lease=presentation;
            pulse.Handler=feedback=>{if(feedback.Kind!=Olca.Core.Domain.CombatFeedbackKind.Damage||feedback.Amount<=0||this==null||!IsOpen||!ReferenceEquals(presentation,lease)||!lease.IsCurrent)return;var strike=lease.TrialStrike;if(strike==null||strike.CardId!="sword"||!strike.Hit||strike.Frame!=lease.Core.Frame)return;pulse.X=ReferenceEquals(enemy,lease.TrialTarget)?lease.FirstTargetX:lease.SecondTargetX;pulse.Y=ReferenceEquals(enemy,lease.TrialTarget)?lease.FirstTargetHeight:0;pulse.Frame=lease.Core.Frame;pulse.Sword=strike!=null&&strike.Frame==pulse.Frame&&strike.CardId=="sword";pulse.SourceX=pulse.Sword?strike.X:lease.Core.Position;pulse.SourceY=pulse.Sword?strike.Y:lease.Core.Height;pulse.Facing=pulse.Sword?strike.Facing:lease.Core.Facing;};enemy.OnCombatFeedback+=pulse.Handler;authoredHits.Add(pulse);
        }
        private void PaintAuthoredHits(float camera,float viewport,float verticalOffset){
            foreach(var pulse in authoredHits){long age=presentation.Core.Frame-pulse.Frame;bool visible=pulse.Frame>=0&&age>=0&&age<12;if(!visible){if(pulse.Image!=null)pulse.Image.enabled=false;continue;}WarmAuthoredVfx();if(pulse.Image==null){var go=new GameObject("OutsideAuthoredHitSpark",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);pulse.Image=go.GetComponent<Image>();pulse.Image.raycastTarget=false;pulse.Image.rectTransform.anchorMin=pulse.Image.rectTransform.anchorMax=new Vector2(0,.21f);}
                pulse.Image.sprite=Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(authoredVfxAssets,2,(int)age/2);pulse.Image.color=Color.white;pulse.Image.rectTransform.sizeDelta=Vector2.one*(1.2f*pixelsPerWorldUnit);pulse.Image.rectTransform.anchoredPosition=new Vector2((pulse.X/(float)OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit,(.55f+pulse.Y/(float)OutsideMovementDefinition.UnitsPerWorldUnit-verticalOffset)*pixelsPerWorldUnit);pulse.Image.enabled=true;
                var wall=pulse.Sword?presentation.FirstSwordBlockingWall(pulse.SourceX,pulse.SourceY,pulse.Facing,Math.Abs(pulse.X-pulse.SourceX)+36000):null;float boundary=wall.HasValue?((RectTransform)transform).rect.xMin+(wall.Value/(float)OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit:0;ClipSwordGraphic(pulse.Image,ref pulse.Clip,wall,pulse.Facing,boundary);
            }
        }
        private void ClearAuthoredHits(){foreach(var pulse in authoredHits){pulse.Enemy.OnCombatFeedback-=pulse.Handler;if(pulse.Image!=null){pulse.Image.enabled=false;if(Application.isPlaying)Destroy(pulse.Image.gameObject);else DestroyImmediate(pulse.Image.gameObject);}}authoredHits.Clear();}
        private void ClearAuthoredVfx(){if(authoredSwordLight!=null){authoredSwordLight.enabled=false;authoredSwordLight.sprite=null;}authoredSwordLightClip?.ClearClip();ClearAuthoredHits();authoredVfxAssets?.Dispose();authoredVfxAssets=null;}
        private void InitializeTrialSword(Font font,Olca.Presentation.Field.GameFieldView field){
            WarmAuthoredVfx();
            var body=new GameObject("OutsideTrialTarget",typeof(RectTransform),typeof(Image));body.transform.SetParent(transform,false);trialTargetImage=body.GetComponent<Image>();trialTargetImage.sprite=trialOriginalEnemy=field.AuthoredTrialEnemySprite;if(trialTargetImage.sprite==null)throw new InvalidOperationException("試用標的の既存素材がありません");trialTargetImage.raycastTarget=false;var rect=trialTargetImage.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.21f);rect.pivot=trialTargetImage.sprite.pivot/trialTargetImage.sprite.rect.size;rect.sizeDelta=trialTargetImage.sprite.bounds.size*pixelsPerWorldUnit*.8f;rect.localScale=new Vector3(-1,1,1);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);trialTargetName=factory.Label("OutsideTrialTargetName",transform,new Vector2(0,.21f),Vector2.zero,new Vector2(300,40));trialTargetName.font=field.AuthoredTrialEnemyFont??font;trialTargetName.fontSize=18;trialTargetName.alignment=TextAnchor.MiddleCenter;trialTargetName.color=Color.white;trialTargetName.raycastTarget=false;var plate=new GameObject("OutsideTrialTargetNamePlate",typeof(RectTransform),typeof(Image));plate.transform.SetParent(transform,false);var pr=(RectTransform)plate.transform;pr.anchorMin=pr.anchorMax=new Vector2(0,.21f);pr.sizeDelta=new Vector2(316,48);plate.GetComponent<Image>().color=new Color(.025f,.07f,.1f,.9f);plate.GetComponent<Image>().raycastTarget=true;trialEnemyAttackButton=plate.AddComponent<Button>();trialEnemyAttackButton.targetGraphic=plate.GetComponent<Image>();trialEnemyAttackButton.navigation=new Navigation{mode=Navigation.Mode.None};var colors=trialEnemyAttackButton.colors;colors.normalColor=new Color(.025f,.07f,.1f,.9f);colors.highlightedColor=colors.selectedColor=new Color(.06f,.2f,.25f,1);colors.pressedColor=new Color(.02f,.09f,.12f,1);colors.disabledColor=new Color(.025f,.07f,.1f,.6f);trialEnemyAttackButton.colors=colors;pr.SetSiblingIndex(trialTargetName.transform.GetSiblingIndex());trialTargetPlate=pr;
            var invulnerability=new GameObject("OutsideTrialHitInvincibilityButton",typeof(RectTransform),typeof(Image),typeof(Button));invulnerability.transform.SetParent(plate.transform,false);var ir=(RectTransform)invulnerability.transform;ir.anchorMin=ir.anchorMax=new Vector2(.5f,.5f);ir.anchoredPosition=new Vector2(326,0);ir.sizeDelta=new Vector2(316,52);var ib=invulnerability.GetComponent<Image>();ib.color=new Color(.035f,.18f,.24f,.97f);trialHitInvincibilityButton=invulnerability.GetComponent<Button>();trialHitInvincibilityButton.targetGraphic=ib;trialHitInvincibilityButton.navigation=new Navigation{mode=Navigation.Mode.None};
            trialHitInvincibilityLabel=factory.Label("OutsideTrialHitInvincibilityLabel",invulnerability.transform,new Vector2(.5f,.5f),Vector2.zero,new Vector2(308,48));trialHitInvincibilityLabel.fontSize=16;trialHitInvincibilityLabel.alignment=TextAnchor.MiddleCenter;trialHitInvincibilityLabel.color=Color.white;trialHitInvincibilityLabel.raycastTarget=false;
            goblinApproachButton=factory.Button("OutsideGoblinApproachTrial","セッキン ゴブリン（カリ）",transform,new Vector2(0,.5f),new Vector2(195,0));((RectTransform)goblinApproachButton.transform).sizeDelta=new Vector2(350,48);goblinApproachButton.navigation=new Navigation{mode=Navigation.Mode.None};goblinApproachLabel=goblinApproachButton.GetComponentInChildren<Text>();goblinApproachLabel.fontSize=17;goblinApproachLabel.raycastTarget=false;
            slimeHopButton=factory.Button("OutsideSlimeHopTrial","チョウヤク スライム / カイシ（カリ）",transform,new Vector2(0,.5f),new Vector2(195,-65));((RectTransform)slimeHopButton.transform).sizeDelta=new Vector2(350,48);slimeHopButton.navigation=new Navigation{mode=Navigation.Mode.None};slimeHopLabel=slimeHopButton.GetComponentInChildren<Text>();slimeHopLabel.fontSize=17;slimeHopLabel.raycastTarget=false;
            var marker=new GameObject("OutsideSlimeLandingMarker",typeof(RectTransform),typeof(OutsideSlimeLandingGraphic));marker.transform.SetParent(transform,false);slimeLanding=marker.GetComponent<OutsideSlimeLandingGraphic>();slimeLanding.raycastTarget=false;slimeLanding.rectTransform.anchorMin=slimeLanding.rectTransform.anchorMax=new Vector2(0,.21f);slimeLanding.enabled=false;
            var range=new GameObject("OutsideGoblinLockedRange",typeof(RectTransform),typeof(Image));range.transform.SetParent(transform,false);goblinRange=range.GetComponent<Image>();goblinRange.raycastTarget=false;goblinRange.rectTransform.anchorMin=goblinRange.rectTransform.anchorMax=new Vector2(0,.21f);goblinRange.enabled=false;
            var gs=new GameObject("OutsideGoblinDownSwing",typeof(RectTransform),typeof(OutsideSwordTrailGraphic));gs.transform.SetParent(transform,false);goblinSwing=gs.GetComponent<OutsideSwordTrailGraphic>();goblinSwing.raycastTarget=false;goblinSwing.rectTransform.anchorMin=goblinSwing.rectTransform.anchorMax=new Vector2(0,.21f);goblinSwing.rectTransform.sizeDelta=new Vector2(300,300);goblinSwing.enabled=false;
            trialTargetHp=factory.Label("OutsideTrialTargetHp",transform,new Vector2(0,.21f),Vector2.zero,new Vector2(58,42));trialTargetHp.font=field.AuthoredTrialEnemyFont??font;trialTargetHp.fontSize=22;trialTargetHp.alignment=TextAnchor.MiddleCenter;trialTargetHp.color=Color.white;trialTargetHp.raycastTarget=false;
            var gauge=new GameObject("OutsideTrialTargetGauge",typeof(RectTransform),typeof(OutsideTrialHpArc));gauge.transform.SetParent(transform,false);trialTargetGauge=gauge.GetComponent<OutsideTrialHpArc>();trialTargetGauge.raycastTarget=false;var gr=(RectTransform)gauge.transform;gr.anchorMin=gr.anchorMax=new Vector2(0,.21f);gr.sizeDelta=new Vector2(82,82);gauge.transform.SetSiblingIndex(trialTargetHp.transform.GetSiblingIndex());
            secondTrialImage=UnityEngine.Object.Instantiate(trialTargetImage.gameObject,transform).GetComponent<Image>();secondTrialImage.name="OutsideSecondTrialTarget";
            secondTrialHp=UnityEngine.Object.Instantiate(trialTargetHp.gameObject,transform).GetComponent<Text>();secondTrialHp.name="OutsideSecondTrialHp";
            secondTrialGauge=UnityEngine.Object.Instantiate(trialTargetGauge.gameObject,transform).GetComponent<OutsideTrialHpArc>();secondTrialGauge.name="OutsideSecondTrialGauge";secondTrialGauge.transform.SetSiblingIndex(secondTrialHp.transform.GetSiblingIndex());
            var sword=new GameObject("OutsideTrialSword",typeof(RectTransform),typeof(Image));sword.transform.SetParent(transform,false);trialSwordImage=sword.GetComponent<Image>();trialSwordImage.raycastTarget=false;trialSwordImage.rectTransform.anchorMin=trialSwordImage.rectTransform.anchorMax=new Vector2(0,.21f);trialSwordImage.enabled=false;trialSwordImage.transform.SetSiblingIndex(trialTargetPlate.GetSiblingIndex());
            var trail=new GameObject("OutsideSwordTrail",typeof(RectTransform),typeof(OutsideSwordTrailGraphic));trail.transform.SetParent(transform,false);trialSwordTrail=trail.GetComponent<OutsideSwordTrailGraphic>();trialSwordTrail.raycastTarget=false;trialSwordTrail.rectTransform.anchorMin=trialSwordTrail.rectTransform.anchorMax=new Vector2(0,.21f);trialSwordTrail.rectTransform.sizeDelta=new Vector2(300,300);trail.transform.SetSiblingIndex(trialSwordImage.transform.GetSiblingIndex());trialSwordTrail.enabled=false;
            var numbers=new GameObject("OutsideTargetCombatFeedback",typeof(RectTransform));numbers.transform.SetParent(transform,false);var nr=(RectTransform)numbers.transform;nr.anchorMin=Vector2.zero;nr.anchorMax=Vector2.one;nr.offsetMin=nr.offsetMax=Vector2.zero;trialTargetFeedback=numbers.AddComponent<OutsideCombatFeedbackCanvas>();
            var secondNumbers=UnityEngine.Object.Instantiate(numbers,transform);secondNumbers.name="OutsideSecondTargetCombatFeedback";secondTrialFeedback=secondNumbers.GetComponent<OutsideCombatFeedbackCanvas>();
        }
        private float TrialTargetTop()=>Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(trialOriginalEnemy).size.y*pixelsPerWorldUnit*.8f;
        private void BindTrialTargetFeedback(){if(trialTargetFeedback==null||trialNumberStyle==null||presentation?.IsCurrent!=true||!OutsideTrialRoute.KnownMap(presentation.Core.TrialMapId))return;var lease=presentation;var target=lease.TrialTarget;if(ReferenceEquals(feedbackTarget,target)&&ReferenceEquals(secondFeedbackTarget,lease.SecondTrialTarget)&&ReferenceEquals(feedbackLease,lease))return;feedbackTarget=target;feedbackLease=lease;slimeHopButton.onClick=new Button.ButtonClickedEvent();slimeHopButton.onClick.AddListener(()=>{if(this!=null&&IsOpen&&CanPlay&&ReferenceEquals(presentation,lease)&&lease.IsCurrent){lease.StartSlimeTrial();var es=UnityEngine.EventSystems.EventSystem.current;if(es!=null&&es.currentSelectedGameObject==slimeHopButton.gameObject)es.SetSelectedGameObject(null);}});goblinApproachButton.onClick=new Button.ButtonClickedEvent();goblinApproachButton.onClick.AddListener(()=>{if(this!=null&&IsOpen&&CanPlay&&ReferenceEquals(presentation,lease)&&lease.IsCurrent){lease.StartGoblinTrial();var es=UnityEngine.EventSystems.EventSystem.current;if(es!=null&&es.currentSelectedGameObject==goblinApproachButton.gameObject)es.SetSelectedGameObject(null);}});ClearAuthoredHits();BindAuthoredHit(target,presentation.FirstTargetX);BindAuthoredHit(lease.SecondTrialTarget,presentation.SecondTargetX);trialHitInvincibilityButton.onClick=new Button.ButtonClickedEvent();trialHitInvincibilityButton.onClick.AddListener(()=>{if(this!=null&&IsOpen&&CanPlay&&ReferenceEquals(presentation,lease)&&lease.IsCurrent){lease.ToggleTrialEnemyHitInvincibility();var selection=UnityEngine.EventSystems.EventSystem.current;if(selection!=null&&selection.currentSelectedGameObject==trialHitInvincibilityButton.gameObject)selection.SetSelectedGameObject(null);}});trialEnemyAttackButton.onClick=new Button.ButtonClickedEvent();trialEnemyAttackButton.onClick.AddListener(()=>{if(this!=null&&IsOpen&&CanPlay&&ReferenceEquals(presentation,lease)&&lease.IsCurrent){lease.ToggleTrialEnemyAttack();var es=UnityEngine.EventSystems.EventSystem.current;if(es!=null&&es.currentSelectedGameObject==trialEnemyAttackButton.gameObject)es.SetSelectedGameObject(null);}});secondFeedbackTarget=lease.SecondTrialTarget;secondTrialFeedback.BindEnemy(secondFeedbackTarget,trialNumberStyle,()=>this!=null&&IsOpen&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)presentation.SecondTargetX/OutsideMovementDefinition.UnitsPerWorldUnit,TrialTargetTop()/pixelsPerWorldUnit+.5f),pixelsPerWorldUnit);trialTargetFeedback.BindEnemy(target,trialNumberStyle,()=>this!=null&&IsOpen&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)presentation.FirstTargetX/OutsideMovementDefinition.UnitsPerWorldUnit,TrialTargetTop()/pixelsPerWorldUnit+.5f+(float)presentation.FirstTargetHeight/OutsideMovementDefinition.UnitsPerWorldUnit),pixelsPerWorldUnit);}
        private readonly List<Image> bombImages=new List<Image>();
        private Olca.Unity.GeneratedSpriteAssets.Lease bombAssets;
        private TrialBombBlastMotion bombBlastMotion;
        private void ClearBombVisuals(){foreach(var image in bombImages)if(image!=null){image.enabled=false;image.sprite=null;}bombBlastMotion?.Dispose();bombBlastMotion=null;bombAssets?.Dispose();bombAssets=null;}
        private Image polishedSwordTrail;
        private OutsideSwordWallClip swordPropWallClip,swordTrailWallClip,swordSpriteWallClip,kickTrailWallClip;
        private void ClipSwordGraphic(Graphic image,ref OutsideSwordWallClip clip,long? wall,int facing,float boundary)
        {
            if(image==null||!image.isActiveAndEnabled||!wall.HasValue){if(clip!=null)clip.ClearClip();return;}
            if(clip==null)clip=image.GetComponent<OutsideSwordWallClip>()??image.gameObject.AddComponent<OutsideSwordWallClip>();
            clip.SetClip(transform,facing,boundary);
        }
        private void RenderSwordWallClip(float camera,float viewport)
        {
            var core=presentation.Core;var strike=presentation.TrialStrike;bool action=presentation.IsSwingingSword;
            long expected=action?core.Frame-presentation.SwordSwingElapsed+presentation.SwordSwingStartup:-1;
            bool fired=strike!=null&&(!action||strike.Frame==expected);
            long x=fired?strike.X:core.Position,y=fired?strike.Y:core.Height;int facing=fired?strike.Facing:core.Facing;
            long reach=action?presentation.SwordSwingReach:strike?.Reach??0;
            bool visible=polishedSwordTrail!=null&&polishedSwordTrail.enabled||trialSwordImage!=null&&trialSwordImage.enabled||trialSwordTrail!=null&&trialSwordTrail.enabled;
            var wall=visible?presentation.FirstSwordBlockingWall(x,y,facing,reach):null;
            float boundary=wall.HasValue?((RectTransform)transform).rect.xMin+(wall.Value/(float)OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit:0;
            ClipSwordGraphic(polishedSwordTrail,ref swordSpriteWallClip,wall,facing,boundary);ClipSwordGraphic(authoredSwordLight,ref authoredSwordLightClip,wall,facing,boundary);ClipSwordGraphic(trialSwordImage,ref swordPropWallClip,wall,facing,boundary);ClipSwordGraphic(trialSwordTrail,ref swordTrailWallClip,wall,facing,boundary);
        }
        private bool RenderPolishedSword(float camera,float viewport,float verticalOffset)
        {
            var core=presentation.Core;var strike=presentation.TrialStrike;bool action=presentation.IsSwingingSword;
            long age=action?presentation.SwordSwingElapsed-presentation.SwordSwingStartup:strike!=null?core.Frame-strike.Frame:-1;
            bool active=action||strike?.SpriteSwing==true&&age>=0&&age<12;
            if(!active){if(polishedSwordTrail!=null)polishedSwordTrail.enabled=false;if(authoredSwordLight!=null)authoredSwordLight.enabled=false;return false;}
            trialSwordTrail.enabled=false;
            if(polishedSwordTrail==null){var go=new GameObject("OutsideSwordSpriteTrail",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);polishedSwordTrail=go.GetComponent<Image>();polishedSwordTrail.raycastTarget=false;polishedSwordTrail.rectTransform.anchorMin=polishedSwordTrail.rectTransform.anchorMax=new Vector2(0,.21f);}
            bool windup=age<0;int facing=action?core.Facing:strike.Facing;float unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float reach=(action?presentation.SwordSwingReach:strike.Reach)/unit,vertical=action?presentation.SwordSwingVerticalScale:strike.VerticalScale;
            // 専用武器とスピアの中断後の末尾には、共用の剣propを重ねない。
            bool bodyHasWeapon=trialSwordMotion?.Contains(player.sprite)==true||trialSpearBody?.Contains(player.sprite)==true||(action?presentation.SwordSwingCardId:strike.CardId)==Olca.Core.Domain.TrialInitialCardDefinitions.Spear;
            if(bodyHasWeapon)trialSwordImage.enabled=false;
            else{
            if(trialSwordAssets==null)trialSwordAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);
            if(trialSwordImage.sprite==null){if(!Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.Sword,out var sprite,trialSwordAssets))throw new InvalidOperationException("共用ソード素材がありません");trialSwordImage.sprite=sprite;trialSwordImage.rectTransform.pivot=sprite.pivot/sprite.rect.size;}
            var blade=trialSwordImage.rectTransform;blade.sizeDelta=trialSwordImage.sprite.bounds.size*pixelsPerWorldUnit;blade.anchoredPosition=new Vector2((core.Position/unit-camera+viewport*.5f+.35f*facing)*pixelsPerWorldUnit,(core.Height/unit+1.3f-verticalOffset)*pixelsPerWorldUnit);float scale=(reach-.35f)/trialSwordImage.sprite.bounds.max.x;blade.localScale=new Vector3(facing*scale,scale,1);float angle=windup?45+55*(presentation.SwordSwingElapsed/(float)Math.Max(1,presentation.SwordSwingStartup)):20-Math.Min(5,(int)age)*18;blade.localRotation=Quaternion.Euler(0,0,angle*facing);trialSwordImage.color=Color.white;trialSwordImage.enabled=true;
            }
            int frame=OutsideSwordSwingVisual.FrameIndex(age);polishedSwordTrail.enabled=frame>=0;
            if(frame>=0){string cardId=action?presentation.SwordSwingCardId:strike.CardId;WarmAuthoredVfx();polishedSwordTrail.sprite=(cardId==Olca.Core.Domain.TrialInitialCardDefinitions.WideSword?(trialWideSwordEffect??(trialWideSwordEffect=new TrialWideSwordEffect())).Resolve(age):null)??(slashSprites??(slashSprites=new Olca.Presentation.Effects.TrialSlashSprites())).Resolve(OutsideSwordSwingVisual.TrailRow(cardId),frame);polishedSwordTrail.color=new Color(1,1,1,OutsideSwordSwingVisual.Opacity(cardId,age));var rr=polishedSwordTrail.rectTransform;long expectedFired=action?core.Frame-presentation.SwordSwingElapsed+presentation.SwordSwingStartup:strike.Frame;bool useStrike=strike?.SpriteSwing==true&&strike.Frame==expectedFired;float x=useStrike?strike.X/unit:core.Position/unit,y=useStrike?strike.Y/unit:core.Height/unit;rr.anchoredPosition=new Vector2((x-camera+viewport*.5f+facing*reach*.5f)*pixelsPerWorldUnit,(y+1.2f-verticalOffset)*pixelsPerWorldUnit);rr.sizeDelta=new Vector2(reach,1.8f*vertical)*pixelsPerWorldUnit;rr.localScale=new Vector3(OutsideSwordSwingVisual.TrailScale(polishedSwordTrail.sprite?.name,facing),1,1);}
            bool light=frame>=0&&(action?presentation.SwordSwingCardId:strike.CardId)=="sword";
            if(light){if(authoredSwordLight==null){var go=new GameObject("OutsideAuthoredSwordLight",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);authoredSwordLight=go.GetComponent<Image>();authoredSwordLight.raycastTarget=false;go.transform.SetSiblingIndex(polishedSwordTrail.transform.GetSiblingIndex());}var rect=authoredSwordLight.rectTransform;rect.anchorMin=polishedSwordTrail.rectTransform.anchorMin;rect.anchorMax=polishedSwordTrail.rectTransform.anchorMax;rect.anchoredPosition=polishedSwordTrail.rectTransform.anchoredPosition;rect.sizeDelta=polishedSwordTrail.rectTransform.sizeDelta;rect.localScale=new Vector3(facing,1,1);authoredSwordLight.sprite=Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(authoredVfxAssets,1,frame);authoredSwordLight.color=new Color(.65f,.9f,1,.35f*OutsideSwordSwingVisual.Opacity("sword",age));authoredSwordLight.enabled=true;}else if(authoredSwordLight!=null)authoredSwordLight.enabled=false;
            return true;
        }
        private void RenderBombs(float camera,float viewport,float verticalOffset)
        {
            if(presentation?.IsCurrent!=true||!OutsideTrialRoute.KnownMap(presentation.Core.TrialMapId)){ClearBombVisuals();return;}
            var flights=presentation.Bombs;var blasts=presentation.BombBlasts;int used=0;long frame=presentation.Core.Frame;
            // 飛翔開始時に一度だけ用意し、着弾frameで画像decode/画素変換を行わない。
            if(bombAssets==null&&(flights.Count>0||blasts.Count>0)){bombAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,out _,bombAssets);(bombBlastMotion??(bombBlastMotion=new TrialBombBlastMotion())).Resolve(0,18);}
            Image Part(string key,float x,float y,float visibleWidth,float visibleHeight,float alpha,float angle=0,(long Left,long Right)? clip=null,Sprite animation=null)
            {
                if(used==bombImages.Count){var wrapper=new GameObject("TrialOutsideBombClip",typeof(RectTransform),typeof(RectMask2D));wrapper.transform.SetParent(transform,false);var wr=wrapper.GetComponent<RectTransform>();wr.anchorMin=wr.anchorMax=new Vector2(0,.21f);var go=new GameObject("TrialOutsideBombVisual",typeof(RectTransform),typeof(Image));go.transform.SetParent(wrapper.transform,false);var created=go.GetComponent<Image>();created.raycastTarget=false;created.rectTransform.anchorMin=created.rectTransform.anchorMax=new Vector2(.5f,.5f);bombImages.Add(created);}
                var image=bombImages[used++];Sprite sprite=animation;bool animated=sprite!=null;bool found=animated||Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(key,out sprite,bombAssets??(bombAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this)));image.sprite=found?sprite:null;image.enabled=found;image.color=new Color(1,1,1,alpha);
                if(found){float aw=animated?TrialBombBlastMotion.VisibleWidth:key==Olca.Presentation.Field.SharedAttackPropCatalog.Bomb?Olca.Presentation.Field.SharedAttackPropCatalog.BombVisibleHeight:Olca.Presentation.Field.SharedAttackPropCatalog.BlastVisibleWidth;float ah=animated?TrialBombBlastMotion.VisibleHeight:key==Olca.Presentation.Field.SharedAttackPropCatalog.Bomb?Olca.Presentation.Field.SharedAttackPropCatalog.BombVisibleHeight:Olca.Presentation.Field.SharedAttackPropCatalog.BlastVisibleHeight;image.rectTransform.sizeDelta=new Vector2(sprite.bounds.size.x*visibleWidth/aw,sprite.bounds.size.y*visibleHeight/ah)*pixelsPerWorldUnit;}
                var parent=(RectTransform)image.transform.parent;var mask=parent.GetComponent<RectMask2D>();mask.enabled=clip.HasValue;
                float half=image.rectTransform.sizeDelta.x/(2*pixelsPerWorldUnit),left=x-half,right=x+half;
                if(clip.HasValue){left=Mathf.Max(left,clip.Value.Left/(float)OutsideMovementDefinition.UnitsPerWorldUnit);right=Mathf.Min(right,clip.Value.Right/(float)OutsideMovementDefinition.UnitsPerWorldUnit);}
                image.enabled&=right>left;float center=(left+right)*.5f;
                parent.anchoredPosition=new Vector2((center-camera+viewport*.5f)*pixelsPerWorldUnit,(y-verticalOffset)*pixelsPerWorldUnit);parent.sizeDelta=new Vector2(Mathf.Max(0,right-left)*pixelsPerWorldUnit,image.rectTransform.sizeDelta.y);
                image.rectTransform.anchoredPosition=new Vector2((x-center)*pixelsPerWorldUnit,0);image.rectTransform.localRotation=Quaternion.Euler(0,0,angle);return image;
            }
            foreach(var flight in flights){float age=frame-flight.FiredAt;
                // 着弾前に爆風を描かない。実際の爆発通知だけを下のループで表示する。
                var visible=TrialBombThrowMotion.FlightPosition(flight,frame);
                Part(Olca.Presentation.Field.SharedAttackPropCatalog.Bomb,visible.x,visible.y,.5f,.5f,1f,-flight.Launch.Facing*360f*age/flight.Launch.FlightDuration);
            }
            foreach(var blast in blasts){long age=frame-blast.Frame;if(age<0||age>=blast.VisualFrames)continue;var animation=(bombBlastMotion??(bombBlastMotion=new TrialBombBlastMotion())).Resolve(age,blast.VisualFrames);float alpha=.95f*Mathf.Clamp01((blast.VisualFrames-age)/(blast.VisualFrames*.35f));var clip=OutsideBombOcclusion.HorizontalBounds(presentation.Core.TraversalProfile?.Walls,blast.SourceX,blast.X,blast.Height);if(animation!=null){WarmAuthoredVfx();animation=Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(authoredVfxAssets,0,TrialBombBlastMotion.FrameIndex(age,blast.VisualFrames));
                // 三つの細い火花ではなく、既存の連番を比率を保って一つの爆発として見せる。
                float width=2f*OutsideBombLaunchData.Radius/OutsideMovementDefinition.UnitsPerWorldUnit;
                float height=width*TrialBombBlastMotion.VisibleHeight/TrialBombBlastMotion.VisibleWidth;
                Part(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,blast.X/(float)OutsideMovementDefinition.UnitsPerWorldUnit,blast.Height/(float)OutsideMovementDefinition.UnitsPerWorldUnit+height*.5f,width,height,alpha,clip:clip,animation:animation);
            }else for(int i=-1;i<=1;i++)Part(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,blast.X/(float)OutsideMovementDefinition.UnitsPerWorldUnit+i,blast.Height/(float)OutsideMovementDefinition.UnitsPerWorldUnit+.38f,.9f,.35f,alpha,clip:clip);}
            for(int i=used;i<bombImages.Count;i++)if(bombImages[i]!=null)bombImages[i].enabled=false;
        }
        private readonly List<Image> initialShotImages=new List<Image>();
        private TrialShotFlightMotion initialShotMotion;
        private void RenderInitialShots(float camera,float viewport,float verticalOffset){
            var shots=presentation.InitialShots;
            for(int i=0;i<shots.Count;i++){
                if(i>=initialShotImages.Count){var go=new GameObject("TrialStraightShot",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var created=go.GetComponent<Image>();created.raycastTarget=false;created.color=new Color(.45f,.95f,1,.95f);created.rectTransform.anchorMin=created.rectTransform.anchorMax=new Vector2(0,.21f);initialShotImages.Add(created);}
                var shot=shots[i];var image=initialShotImages[i];image.enabled=true;
                // 飛翔時計を読むだけの独立6コマ。弾速・命中・寿命・保存には触れない。
                image.sprite=(initialShotMotion??(initialShotMotion=new TrialShotFlightMotion())).Resolve(presentation.Core.Frame-shot.StartedAt,shot.CardId);
                bool animated=image.sprite!=null;var muzzle=animated?TrialShotBodyMotion.FlightOffset(presentation.Core.Frame-shot.StartedAt):Vector2.zero;image.color=animated?Color.white:new Color(.45f,.95f,1,.95f);image.rectTransform.localScale=new Vector3(animated?shot.Facing:1,1,1);image.rectTransform.sizeDelta=(animated?Vector2.one*.8f:new Vector2(.35f,.1f))*pixelsPerWorldUnit;image.rectTransform.anchoredPosition=new Vector2(((float)shot.Position(presentation.Core.Frame)/OutsideMovementDefinition.UnitsPerWorldUnit+shot.Facing*muzzle.x-camera+viewport*.5f)*pixelsPerWorldUnit,((float)shot.Height/OutsideMovementDefinition.UnitsPerWorldUnit+(1.2f+muzzle.y)-verticalOffset)*pixelsPerWorldUnit);
            }
            for(int i=shots.Count;i<initialShotImages.Count;i++)if(initialShotImages[i]!=null)initialShotImages[i].enabled=false;
        }
        private void RenderTrialSword(float camera,float viewport,float verticalOffset){
            if(trialTargetImage==null)return;
            RenderBombs(camera,viewport,verticalOffset);PaintAuthoredHits(camera,viewport,verticalOffset);
            bool combat=OutsideTrialRoute.KnownMap(presentation.Core.TrialMapId);foreach(var graphic in new Graphic[]{trialTargetImage,trialTargetName,trialTargetHp,trialTargetGauge,secondTrialImage,secondTrialHp,secondTrialGauge})if(graphic!=null)graphic.enabled=combat;
            slimeHopButton.gameObject.SetActive(combat&&presentation.Core.TrialMapId==OutsideTrialRoute.AnnexMapId);goblinApproachButton.gameObject.SetActive(combat&&presentation.Core.TrialMapId==OutsideTrialRoute.AnnexMapId);trialTargetPlate.gameObject.SetActive(combat);if(!combat){ClearTrialSword();RenderInitialShots(camera,viewport,verticalOffset);return;}RenderInitialShots(camera,viewport,verticalOffset);BindTrialTargetFeedback();var core=presentation.Core;var target=presentation.TrialTarget;float x=((float)presentation.FirstTargetX/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit;float y=-verticalOffset*pixelsPerWorldUnit;
            var slime=presentation.SlimeTrial;float enemyHeight=presentation.FirstTargetHeight/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit;var shape=SlimeShape(slime);bool annex=core.TrialMapId==OutsideTrialRoute.AnnexMapId;Sprite goblin=null;if(annex&&slime==null){WarmAuthoredVfx();goblin=AnnexGoblinSprite.Resolve(authoredVfxAssets,GoblinFrame(presentation.GoblinTrial,core.Frame,presentation.TrialEnemyWindupFrames,presentation.TrialEnemyAttackEnabled,target.IsStaggered));}
            trialTargetImage.sprite=goblin??trialOriginalEnemy;trialTargetImage.rectTransform.pivot=trialTargetImage.sprite.pivot/trialTargetImage.sprite.rect.size;trialTargetImage.rectTransform.sizeDelta=trialTargetImage.sprite.bounds.size*pixelsPerWorldUnit*.8f;trialTargetImage.rectTransform.localScale=new Vector3(slime!=null?slime.Facing*shape.x:goblin!=null?(presentation.GoblinTrial?.Facing??(core.Position<presentation.FirstTargetX?-1:1)):-1,shape.y,1);
            trialTargetImage.rectTransform.anchoredPosition=new Vector2(x,y+enemyHeight-(goblin!=null?0:Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(trialTargetImage.sprite).min.y*pixelsPerWorldUnit*.8f*shape.y));trialTargetImage.enabled=!target.IsDead;
            float top=TrialTargetTop()+44.55f+y;trialTargetHp.rectTransform.anchoredPosition=new Vector2(x,top+enemyHeight);trialTargetHp.text=target.CurrentHp.ToString();trialTargetHp.enabled=!target.IsDead;trialTargetGauge.rectTransform.anchoredPosition=new Vector2(x,top+enemyHeight);trialTargetGauge.SetFraction(target.CurrentHp/(float)target.MaxHp);trialTargetGauge.enabled=!target.IsDead;trialTargetName.rectTransform.anchoredPosition=new Vector2(x+210,top+50);
            trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;
            var second=presentation.SecondTrialTarget;float sx=((float)presentation.SecondTargetX/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit;
            secondTrialImage.rectTransform.anchoredPosition=new Vector2(sx,y-Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(secondTrialImage.sprite).min.y*pixelsPerWorldUnit*.8f);secondTrialImage.enabled=!second.IsDead;secondTrialHp.rectTransform.anchoredPosition=new Vector2(sx,top);secondTrialHp.text=second.CurrentHp.ToString();secondTrialHp.enabled=!second.IsDead;secondTrialGauge.rectTransform.anchoredPosition=new Vector2(sx,top);secondTrialGauge.SetFraction(second.CurrentHp/(float)second.MaxHp);secondTrialGauge.enabled=!second.IsDead;
            bool twoLineTargetName=presentation.TrialContactDamageEnabled||core.TrialMapId==OutsideTrialRoute.AnnexMapId;
            trialTargetName.lineSpacing=twoLineTargetName?1.5f:1f;
            trialTargetName.rectTransform.sizeDelta=new Vector2(300,twoLineTargetName?60:40);trialTargetPlate.sizeDelta=new Vector2(316,twoLineTargetName?68:48);
            trialTargetName.text=presentation.Core.TrialMapId!=null?((target.IsDead&&second.IsDead)?"マップノ デイリデ サイセイ":((target.IsDead||second.IsDead)?"1タイ / セッショク10（カリ）\n":"シヨウテキ / セッショク10（カリ）\n")+(target.IsDead?"AIコウゲキOFF":presentation.TrialEnemyAttackEnabled?"AIコウゲキON / クリック":"AIコウゲキOFF / クリック")):presentation.TrialContactDamageEnabled?((target.IsDead&&second.IsDead)?"カイフクデ サイセイ":((target.IsDead||second.IsDead)?"1タイ":"2タイ")+" / セッショク10（カリ）\n"+(target.IsDead?"AIコウゲキOFF":presentation.TrialEnemyAttackEnabled?"AIコウゲキON / クリック":"AIコウゲキOFF / クリック")):target.IsDead?"カイフクデ サイセイ":presentation.TrialEnemyAttackEnabled?"コウゲキON / クリック（カリ）":"コウゲキOFF / クリック（カリ）";trialEnemyAttackButton.interactable=CanPlay&&OutsideTrialRoute.KnownMap(presentation.Core.TrialMapId)&&!target.IsDead;trialHitInvincibilityButton.interactable=trialEnemyAttackButton.interactable;trialHitInvincibilityButton.gameObject.SetActive(combat&&!target.IsDead);trialHitInvincibilityLabel.text="ムテキ"+Olca.Core.Domain.OutsideHitInvincibilityData.TrialDurationMs+"ms（カリ）\nV3 "+(presentation.TrialEnemyHitInvincibilityEnabled?"ON":"OFF")+" / クリック";trialTargetImage.color=presentation.TrialEnemyAttackEnabled&&presentation.TrialEnemyWindupFrames<=12?new Color(1,.45f,.45f):Color.white;// 既存の赤色予告12論理frameと同じ区間に文字を添える。攻撃予定や判定は変更しない。
            if(presentation.TrialEnemyAttackEnabled&&!target.IsDead&&presentation.TrialEnemyWindupFrames<=12)trialTargetName.text=trialTargetName.text.Replace("AIコウゲキON / クリック","コウゲキ チョクゼン / OFFデ トメル").Replace("コウゲキON / クリック（カリ）","コウゲキ チョクゼン / OFFデ トメル");
            trialTargetName.rectTransform.anchoredPosition=new Vector2(x+(slime!=null?320:210),top+100);trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;PlaceTrialTargetName();PlaceTrialHitInvincibilityButton();RenderGoblinTrial(x,y);RenderSlimeTrial(x,y);
            if(!RenderPolishedSword(camera,viewport,verticalOffset)){
            var strike=presentation.TrialStrike;float swordAngle=0,swordAlpha=0;bool show=strike!=null&&OutsideSwordTrailGraphic.TrySample(core.Frame>=strike.Frame?core.Frame-strike.Frame:-1,strike.Facing,out swordAngle,out swordAlpha);trialSwordImage.enabled=show;trialSwordTrail.enabled=show;if(show){if(trialSwordAssets==null)trialSwordAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);if(trialSwordImage.sprite==null){if(!Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.Sword,out var sprite,trialSwordAssets))throw new InvalidOperationException("共用ソード素材がありません");trialSwordImage.sprite=sprite;trialSwordImage.rectTransform.pivot=sprite.pivot/sprite.rect.size;trialSwordImage.rectTransform.sizeDelta=sprite.bounds.size*pixelsPerWorldUnit;}trialSwordImage.rectTransform.anchoredPosition=new Vector2(((float)strike.X/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f+.35f*strike.Facing)*pixelsPerWorldUnit,((float)strike.Y/OutsideMovementDefinition.UnitsPerWorldUnit+1.2f-verticalOffset)*pixelsPerWorldUnit);float tip=(float)(strike.Reach>0?strike.Reach:OutsideExplorationSession.TrialSwordRange)/OutsideMovementDefinition.UnitsPerWorldUnit-.35f;float scale=tip/trialSwordImage.sprite.bounds.max.x;trialSwordImage.rectTransform.localScale=new Vector3(strike.Facing*scale,scale*(strike.VerticalScale>0?strike.VerticalScale:1),1);trialSwordImage.rectTransform.localRotation=Quaternion.Euler(0,0,swordAngle);trialSwordImage.color=new Color(1,1,1,swordAlpha);trialSwordTrail.rectTransform.anchoredPosition=trialSwordImage.rectTransform.anchoredPosition;trialSwordTrail.rectTransform.localScale=new Vector3(strike.Facing,strike.VerticalScale>0?strike.VerticalScale:1,1);trialSwordTrail.SetPose(swordAngle*strike.Facing,swordAlpha,tip*pixelsPerWorldUnit);}
            }
            RenderSwordWallClip(camera,viewport);
            PaintSecondTrialFeedback(camera,verticalOffset);
            if(trialTargetFeedback.ActiveCount>0){var obstacles=TrialNumberObstacles();trialTargetFeedback.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,-((RectTransform)transform).rect.height*.29f+(world.y-verticalOffset)*pixelsPerWorldUnit),obstacles,TrialNumberProtectedOverlay());}
        }
        public static int GoblinFrame(OutsideGoblinTrialData g,long frame,int windup,bool attacking,bool staggered)
        {
            if(g==null)return AnnexGoblinSprite.FrameIndex(frame,windup,attacking,staggered);
            if(staggered)return 8+(int)(Math.Max(0,frame)/8%2);
            if(g.Phase==OutsideGoblinPhase.Windup)return 4+Math.Min(1,g.Age/12);
            if(g.Phase==OutsideGoblinPhase.Strike)return 6+Math.Min(1,g.Age/3);
            if(g.Phase==OutsideGoblinPhase.Recovery&&g.Age<16)return 7;
            return (int)(Math.Max(0,frame)/10%4);
        }
        private void RenderGoblinTrial(float x,float y)
        {
            var g=presentation.GoblinTrial;bool active=g!=null;goblinApproachButton.interactable=CanPlay&&!active&&presentation.SlimeTrial==null&&!presentation.TrialTarget.IsDead;
            goblinApproachLabel.text=active?"ゴブリン セッキン ON（カリ）":presentation.SlimeTrial!=null?"スライム シヨウチュウ\nマップ デイリデ キリカエ":"セッキン ゴブリン / カイシ（カリ）";
            goblinRange.enabled=active&&(g.Phase==OutsideGoblinPhase.Windup||g.Phase==OutsideGoblinPhase.Strike)&&!presentation.TrialTarget.IsDead;
            goblinSwing.enabled=active&&g.Phase==OutsideGoblinPhase.Strike&&!presentation.TrialTarget.IsDead;
            if(!active)return;
            trialEnemyAttackButton.interactable=false;trialHitInvincibilityButton.gameObject.SetActive(false);
            string phase=g.Phase==OutsideGoblinPhase.Approach?"セッキン":g.Phase==OutsideGoblinPhase.Windup?"フリカブリ / ヨケル":g.Phase==OutsideGoblinPhase.Strike?"フリオロシ":g.Phase==OutsideGoblinPhase.Recovery?"スキ / ハンゲキ":"マップノ デイリデ サイセイ";
            trialTargetName.text="ゴブリン（カリ） / "+phase;
            trialTargetImage.color=g.Phase==OutsideGoblinPhase.Windup?new Color(1,.6f,.5f):Color.white;
            if(goblinRange.enabled){float reach=OutsideGoblinTrialData.Reach/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit;goblinRange.rectTransform.sizeDelta=new Vector2(reach,OutsideGoblinTrialData.Height/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit);goblinRange.rectTransform.pivot=new Vector2(g.Facing==1?0:1,0);goblinRange.rectTransform.anchoredPosition=new Vector2(x,y);goblinRange.color=new Color(1,.18f,.1f,g.Phase==OutsideGoblinPhase.Windup?.13f:.3f);}
            if(goblinSwing.enabled){goblinSwing.rectTransform.anchoredPosition=new Vector2(x,y+.75f*pixelsPerWorldUnit);goblinSwing.rectTransform.localScale=new Vector3(g.Facing,1,1);goblinSwing.SetPose(65-g.Age*25,.8f,OutsideGoblinTrialData.Reach/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit);}
        }
        public static Vector2 SlimeShape(OutsideSlimeTrialData s)
        {
            if(s==null)return Vector2.one;
            if(s.Phase==OutsideSlimePhase.Squash){float t=s.Age/(float)OutsideSlimeTrialData.SquashFrames;return new Vector2(1+.2f*t,1-.35f*t);}
            if(s.Phase==OutsideSlimePhase.Flight)return new Vector2(.85f,1.12f);
            if(s.Phase==OutsideSlimePhase.Recovery&&s.Age<12){float t=1-s.Age/12f;return new Vector2(1+.18f*t,1-.25f*t);}
            return Vector2.one;
        }
        private void RenderSlimeTrial(float x,float y)
        {
            var s=presentation.SlimeTrial;bool active=s!=null;slimeHopButton.interactable=CanPlay&&!active&&presentation.GoblinTrial==null&&!presentation.TrialTarget.IsDead;
            slimeHopLabel.text=active?"スライム チョウヤク ON（カリ）":presentation.GoblinTrial!=null?"ゴブリン シヨウチュウ\nマップ デイリデ キリカエ":"チョウヤク スライム / カイシ（カリ）";
            slimeLanding.enabled=active&&(s.Phase==OutsideSlimePhase.Squash||s.Phase==OutsideSlimePhase.Flight||s.Phase==OutsideSlimePhase.Recovery&&s.Age<12)&&!presentation.TrialTarget.IsDead;
            if(!active)return;
            trialEnemyAttackButton.interactable=false;trialHitInvincibilityButton.gameObject.SetActive(false);
            string phase=s.Phase==OutsideSlimePhase.Idle?"タイキ":s.Phase==OutsideSlimePhase.Squash?"チヂム / チャクチ ヨテイ":s.Phase==OutsideSlimePhase.Flight?"チャクチ ヨテイ / ヨケル":s.Phase==OutsideSlimePhase.Recovery?"チャクチ / ハンゲキ":"マップノ デイリデ サイセイ";
            trialTargetName.text="スライム（カリ） / "+phase;trialTargetImage.color=Color.white;
            if(slimeLanding.enabled){float offset=(s.LandingX-s.X)/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit;float t=s.Phase==OutsideSlimePhase.Recovery?s.Age/12f:0;slimeLanding.rectTransform.anchoredPosition=new Vector2(x+offset,y+4);float radius=OutsideSlimeTrialData.LandingRadius/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit;slimeLanding.rectTransform.sizeDelta=new Vector2(radius*2*(1+t*.2f),Mathf.Max(10,radius*.25f)*(1+t*.2f));slimeLanding.SetLanding(s.Phase==OutsideSlimePhase.Recovery,t);}
        }
        // 試用標的の名札だけを横へ避け、主人公の頭と体を隠さない。標的・HP・判定は動かさない。
        private void PlaceTrialTargetName()
        {
            float shift=EntranceTitleOffset(BoundsInView(trialTargetPlate),BoundsInView(player.rectTransform),((RectTransform)transform).rect);
            trialTargetName.rectTransform.anchoredPosition+=new Vector2(shift,0);
            trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;
        }
        private void PlaceTrialHitInvincibilityButton()
        {
            if(trialHitInvincibilityButton==null||!trialHitInvincibilityButton.gameObject.activeInHierarchy)return;
            // 既存の名札は動かさず、新しい試用ボタンだけを横に配置する。
            var rect=(RectTransform)trialHitInvincibilityButton.transform;var viewport=((RectTransform)transform).rect;rect.anchoredPosition=new Vector2(326,0);var bounds=BoundsInView(rect);
            if(bounds.xMax>viewport.xMax-16){rect.anchoredPosition=new Vector2(-326,0);bounds=BoundsInView(rect);}
            float shift=EntranceTitleOffset(bounds,BoundsInView(player.rectTransform),viewport);shift=Mathf.Clamp(shift,viewport.xMin+16-bounds.xMin,viewport.xMax-16-bounds.xMax);rect.anchoredPosition+=new Vector2(shift,0);
        }
        private List<RectTransform> TrialNumberObstacles()=>new List<RectTransform>{slimeHopButton!=null&&slimeHopButton.gameObject.activeInHierarchy?(RectTransform)slimeHopButton.transform:null,goblinApproachButton!=null&&goblinApproachButton.gameObject.activeInHierarchy?(RectTransform)goblinApproachButton.transform:null,trialTargetPlate,trialHitInvincibilityButton!=null&&trialHitInvincibilityButton.gameObject.activeInHierarchy?(RectTransform)trialHitInvincibilityButton.transform:null,trialTargetGauge.rectTransform,secondTrialGauge.rectTransform,entrance!=null?(RectTransform)entrance.transform:null,reloadPoint,mapNameLabel!=null?mapNameLabel.rectTransform:null};
        private RectTransform TrialNumberProtectedOverlay()=>mapNameLabel!=null&&mapNameLabel.isActiveAndEnabled&&mapNameLabel.color.a>0?mapNameLabel.rectTransform:null;
        private void PaintSecondTrialFeedback(float camera,float verticalOffset){if(secondTrialFeedback?.ActiveCount>0)secondTrialFeedback.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,-((RectTransform)transform).rect.height*.29f+(world.y-verticalOffset)*pixelsPerWorldUnit),TrialNumberObstacles(),TrialNumberProtectedOverlay());}
        private void ClearTrialSword(){if(slimeHopButton!=null){slimeHopButton.onClick.RemoveAllListeners();slimeHopButton.gameObject.SetActive(false);}if(slimeLanding!=null)slimeLanding.enabled=false;if(goblinApproachButton!=null){goblinApproachButton.onClick.RemoveAllListeners();goblinApproachButton.gameObject.SetActive(false);}if(goblinRange!=null)goblinRange.enabled=false;if(goblinSwing!=null)goblinSwing.enabled=false;ClearAuthoredVfx();if(trialHitInvincibilityButton!=null){trialHitInvincibilityButton.onClick.RemoveAllListeners();trialHitInvincibilityButton.interactable=false;}swordPropWallClip?.ClearClip();swordTrailWallClip?.ClearClip();swordSpriteWallClip?.ClearClip();if(polishedSwordTrail!=null){polishedSwordTrail.enabled=false;polishedSwordTrail.sprite=null;}ClearBombVisuals();foreach(var image in initialShotImages)if(image!=null){image.enabled=false;image.sprite=null;}if(trialEnemyAttackButton!=null){trialEnemyAttackButton.onClick.RemoveAllListeners();trialEnemyAttackButton.interactable=false;}if(trialSwordTrail!=null)trialSwordTrail.enabled=false;trialTargetFeedback?.Unbind();secondTrialFeedback?.Unbind();secondFeedbackTarget=null;feedbackTarget=null;feedbackLease=null;trialNumberStyle=null;if(trialSwordImage!=null){trialSwordImage.enabled=false;trialSwordImage.sprite=null;}trialSwordAssets?.Dispose();trialSwordAssets=null;}
    }
}

namespace Olca.Presentation.Management
{
    /// <summary>外界店の表示・選択・操作寿命。取引の原子保存はClearReturnSessionへ委譲する。</summary>
    public sealed class OutsideShopView:UnityEngine.MonoBehaviour
    {
        private UnityEngine.Font font;private Olca.Core.Domain.ClearReturnSession session;private Olca.Core.Domain.OutsideShopDefinition shop;private System.Action closed;
        private ManagedScreenContext context;private UnityEngine.Transform content;private Olca.Unity.MenuPadScope menu;private string selected,message;private int page;private const int PageSize=7;private long renderEpoch;private int attemptFrame=-1;private System.Func<int> frame;private System.Func<bool> alive;private UnityEngine.UI.Text guide;
        private bool CanOperate=>this!=null&&isActiveAndEnabled&&context?.IsCurrent==true&&!context.DialogBlocksInput&&(alive?.Invoke()??true);
        public void Initialize(UnityEngine.Font value,Olca.Core.Domain.ClearReturnSession source,Olca.Core.Domain.OutsideShopDefinition definition,System.Action close,System.Func<int> readFrame=null,System.Func<bool> isAlive=null){if(definition==null)throw new System.ArgumentException("ショップ定義が必要です");alive=isAlive;font=value;session=source;shop=definition;closed=close;frame=readFrame??(()=>UnityEngine.Time.frameCount);gameObject.SetActive(false);}
        public void RebindDefinition(Olca.Core.Domain.OutsideShopDefinition definition){if(CanOperate)throw new System.InvalidOperationException("開いているショップ定義は変更できません");if(definition==null)throw new System.ArgumentException("ショップ定義が必要です");shop=definition;page=0;}
        public void Open(ManagedScreenContext owner){context=owner;selected=message=null;page=0;gameObject.SetActive(true);transform.SetAsLastSibling();Render();}
        private UnityEngine.UI.Text Text(string name,string value,float x,float y,float width,float height,int size)
        {
            var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(content,false);var text=go.GetComponent<UnityEngine.UI.Text>();text.font=font;text.text=value;text.fontSize=size;text.alignment=UnityEngine.TextAnchor.MiddleCenter;text.color=UnityEngine.Color.white;text.raycastTarget=false;text.rectTransform.anchoredPosition=new UnityEngine.Vector2(x,y);text.rectTransform.sizeDelta=new UnityEngine.Vector2(width,height);LiquidGlassMenu.ProtectText(text);return text;
        }
        private UnityEngine.UI.Button Button(string name,string text,float x,float y,float width,System.Action action)
        {
            var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(content,false);var rect=(UnityEngine.RectTransform)go.transform;rect.anchoredPosition=new UnityEngine.Vector2(x,y);rect.sizeDelta=new UnityEngine.Vector2(width,90);var button=go.GetComponent<UnityEngine.UI.Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(button);LiquidGlassMenu.Button(button);var label=Text(name+"Label",text,0,0,width-30,84,26);label.transform.SetParent(go.transform,false);if(!name.StartsWith("OutsideShopOffer_",System.StringComparison.Ordinal))LiquidGlassMenu.UseButtonFont(label);var owner=context;long epoch=renderEpoch;button.onClick.AddListener(()=>{if(button!=null&&button.isActiveAndEnabled&&button.IsInteractable()&&CanOperate&&ReferenceEquals(context,owner)&&epoch==renderEpoch)action();});return button;
        }
        private void Attempt(bool funds)
        {
            if(attemptFrame==frame())return;attemptFrame=frame();
            var chosen=shop.Offers.SingleOrDefault(v=>v.Id==selected);bool overflow=chosen?.CardDefinition!=null&&session.OutsideShopOwnedCount(chosen)>=4;
            bool success=funds?session.TryClaimOutsideShopTrialFunds(out message):session.TryPurchaseOutsideShop(shop,selected,out message);if(success)message=funds?"試用資金"+Olca.Core.Domain.OutsideShopDefinition.TrialFunds+"を受け取りました":(chosen.CardDefinition!=null?(overflow?"カードの所持上限に達したため、"+chosen.OverflowTokens+"トークンに変換":"カードを1枚購入しました。編成地点で編成できます。"):shop.Offers.Single(v=>v.Id==selected).AccessoryId!=null?"装飾品を購入しました。「装備・パーツ」で装備できます。":shop.Offers.Single(v=>v.Id==selected).PartId!=null?"パーツを購入しました。「装備・パーツ」で配置できます。":chosen.UnlockItemId!=null?"解放アイテムを購入しました。入口の「マス解放」で使えます。":"1個購入しました");Render();
        }
        private void ChangePage(int delta){page=System.Math.Max(0,System.Math.Min((shop.Offers.Count-1)/PageSize,page+delta));selected=message=null;Render();}
        private void Render(string pressedOffer = null)
        {
            bool paged=shop.Offers.Count>PageSize;var offers=shop.Offers.Skip(page*PageSize).Take(PageSize).ToArray();int count=offers.Length;
            renderEpoch++;menu?.Dispose();menu=null;if(content!=null){content.gameObject.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content.gameObject);else DestroyImmediate(content.gameObject);}
            var go=new UnityEngine.GameObject("OutsideShopContents",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(transform,false);content=go.transform;((UnityEngine.RectTransform)content).sizeDelta=new UnityEngine.Vector2(1480,paged?980:850);LiquidGlassMenu.Panel(go.GetComponent<UnityEngine.UI.Image>());
            var inventory=session.OutsideInventorySnapshot;Text("OutsideShopTitle","試用ショップ",0,340,1400,65,38);Text("OutsideShopWallet","所持金："+inventory.Currency.ToString(),0,270,1380,55,25);
            bool many=paged||count>6,three=!paged&&count==3,four=paged||count>=4;int offerColumns=paged||count>=5?3:2;float offerWidth=paged||three||count>=5?440:640;
            for(int i=0;i<count;i++){var offer=offers[i];int ownedCount=session.OutsideShopOwnedCount(offer);string price=(offer.CardDefinition!=null?"カード1枚 ":offer.AccessoryId!=null?"装飾品 ":offer.PartId!=null?"パーツ1個 ":offer.UnlockItemId!=null?"解放アイテム1個 ":"1個 ")+offer.Price+"通貨";string owned="所持 "+ownedCount+(!offer.IsEquipment?" / "+offer.Maximum:"");string stock=offer.PurchaseLimit==0?"在庫無制限":"在庫 "+session.OutsideShopRemaining(shop,offer.Id)+" / "+offer.PurchaseLimit+"（補充なし）";var button=Button("OutsideShopOffer_"+offer.Id,offer.Name+(three||four?"\n":" / ")+price+"\n"+owned+"\n"+stock,three?(i-1)*475:four?(i%offerColumns-(offerColumns-1)*.5f)*(offerColumns==3?475:700):i==0?-350:350,paged?135-(i/offerColumns)*120:many?200-(i/offerColumns)*120:four?170-(i/offerColumns)*140:155,offerWidth,()=>{selected=offer.Id;message=null;Render(offer.Id);});((UnityEngine.RectTransform)button.transform).sizeDelta=new UnityEngine.Vector2(offerWidth,many?112:four?132:three?136:118);var label=button.GetComponentInChildren<UnityEngine.UI.Text>();label.fontSize=many?18:three||four?20:23;label.rectTransform.sizeDelta=new UnityEngine.Vector2(offerWidth-30,many?106:four?126:three?130:112);if(selected==offer.Id)button.GetComponent<UnityEngine.UI.Image>().color=Olca.Presentation.Dungeon.UiLook.Chosen;if(pressedOffer==offer.Id)button.GetComponent<LiquidGlassCoordinates>().PlayConfirmation();}
            if(paged){var previous=Button("OutsideShopPrevious","前へ",-560,220,220,()=>ChangePage(-1));previous.interactable=page>0;var next=Button("OutsideShopNext","次へ",560,220,220,()=>ChangePage(1));next.interactable=(page+1)*PageSize<shop.Offers.Count;foreach(var control in new[]{previous,next})((UnityEngine.RectTransform)control.transform).sizeDelta=new UnityEngine.Vector2(220,44);Text("OutsideShopPage",$"商品 {page+1} / {(shop.Offers.Count+PageSize-1)/PageSize}",0,220,600,40,23);}
            bool canBuy=session.CanPurchaseOutsideShop(shop,selected,out var error);var chosen=shop.Offers.SingleOrDefault(v=>v.Id==selected);Text("OutsideShopSelection",chosen==null?"商品を選んでください":chosen.Name+" / 購入後 "+(canBuy?(inventory.Currency-chosen.Price).ToString():"—")+"通貨",0,paged?-205:many?-130:four?-78:45,1380,many?45:four?55:60,27);
            Text("OutsideShopStatus",message??(chosen==null?"購入時に所持金と商品を保存します":canBuy?(chosen.CardDefinition!=null?"カードの所持上限は4枚。超過分は"+chosen.OverflowTokens+"トークンに変換":"一度の購入につき1個。価格と配置は試用です。"):error),0,paged?-250:many?-180:four?-137:-20,1380,many?45:four?55:65,four?22:24);
            var buy=Button("OutsideShopBuy","購入する",0,paged?-305:many?-240:four?-210:-105,520,()=>Attempt(false));buy.interactable=canBuy;if(four)((UnityEngine.RectTransform)buy.transform).sizeDelta=new UnityEngine.Vector2(520,70);
            var fundsButton=Button("OutsideShopFunds","試用資金"+Olca.Core.Domain.OutsideShopDefinition.TrialFunds+"（1回限り）",-350,paged?-380:many?-330:four?-305:-245,640,()=>Attempt(true));fundsButton.interactable=!session.HasClaimedOutsideSupply(Olca.Core.Domain.OutsideShopDefinition.TrialFundsId)&&!session.HasPendingSave;
            var backButton=Button("OutsideShopBack","戻る（購入内容は保存済み）",350,paged?-380:many?-330:four?-305:-245,640,()=>closed?.Invoke());
            if(paged)foreach(var control in new[]{fundsButton,backButton})((UnityEngine.RectTransform)control.transform).sizeDelta=new UnityEngine.Vector2(640,70);
            guide=Text("OutsideShopGuide","",0,paged?-445:many?-402:four?-390:-350,1380,four?40:55,23);RefreshGuide();menu=new Olca.Unity.MenuPadScope(()=>CanOperate&&Olca.Unity.MenuPadScope.IsTopmost(transform));menu.NavigationResolver=FindShopNavigation;menu.Bind(transform,selected==null?"OutsideShopBack":"OutsideShopOffer_"+selected,()=>closed?.Invoke());
        }
        // 商品の2/3列と下段の2列を明示し、斜めに近い購入ボタンへ横移動が吸われないようにする。
        private UnityEngine.UI.Selectable FindShopNavigation(UnityEngine.UI.Selectable from,UnityEngine.Vector2 direction)
        {
            bool paged=shop.Offers.Count>PageSize;var offers=shop.Offers.Skip(page*PageSize).Take(PageSize).ToArray();int columns=paged||offers.Length>=5?3:offers.Length==4?2:offers.Length;int rows=paged?4:(offers.Length+columns-1)/columns;var cells=offers.Select((offer,i)=>(Name:"OutsideShopOffer_"+offer.Id,Row:(paged?1:0)+i/columns,First:i%columns,Last:i%columns)).ToList();if(paged){cells.Add(("OutsideShopPrevious",0,0,0));cells.Add(("OutsideShopNext",0,2,2));}cells.Add(("OutsideShopBuy",rows,0,columns-1));cells.Add(("OutsideShopFunds",rows+1,0,0));cells.Add(("OutsideShopBack",rows+1,1,System.Math.Max(1,columns-1)));
            var buttons=content.GetComponentsInChildren<UnityEngine.UI.Button>();var origin=cells.FirstOrDefault(v=>v.Name==from.name);if(origin.Name==null)return null;var enabled=cells.Where(v=>v.Name!=null&&buttons.Any(b=>b.name==v.Name&&b.IsInteractable()));
            var target=direction.x!=0?enabled.Where(v=>v.Row==origin.Row&&(direction.x>0?v.First>origin.Last:v.Last<origin.First)).OrderBy(v=>direction.x>0?v.First-origin.Last:origin.First-v.Last).FirstOrDefault():enabled.Where(v=>v.First<=origin.Last&&v.Last>=origin.First&&(direction.y>0?v.Row<origin.Row:v.Row>origin.Row)).OrderBy(v=>System.Math.Abs(v.Row-origin.Row)).ThenBy(v=>v.First).FirstOrDefault();return target.Name==null?null:buttons.First(v=>v.name==target.Name);
        }
        private void RefreshGuide(){if(guide!=null)guide.text=GameplayPauseView.InputGuideText(Olca.Unity.UnityInputProvider.ActiveGamepadProfile,Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad);}
        private void Update(){if(CanOperate)menu?.Tick();RefreshGuide();}
        public void Hide(){renderEpoch++;context=null;menu?.Dispose();menu=null;gameObject.SetActive(false);}
        private void OnDisable(){menu?.Dispose();menu=null;}
        private void OnDestroy(){menu?.Dispose();menu=null;context=null;closed=null;session=null;}
    }
}

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private Olca.Presentation.Effects.ProvisionalCombatGraphic chargeRing;
        private Image kickTrail;
        private Olca.Presentation.Effects.TrialSlashSprites slashSprites;
        private RectTransform kickCaptionPlate;private Text kickCaption;private int kickCaptionKey=-1;
        private readonly Olca.Presentation.Effects.ProvisionalCombatGraphic[] targetBursts=new Olca.Presentation.Effects.ProvisionalCombatGraphic[2];
        private readonly Olca.Core.Domain.Enemy[] observedBurstTargets=new Olca.Core.Domain.Enemy[2];
        private readonly bool[] observedDead=new bool[2];private readonly long[] targetDeathAt={-1,-1};
        private OutsideExplorationSession.PresentationLease burstLease;
        public static string ProvisionalKickCaption(OutsideKickKind kind,bool hit,bool recovery)
        {
            string name=kind==OutsideKickKind.GroundFirst?"1ダンメ":kind==OutsideKickKind.GroundSecond?"2ダンメ":kind==OutsideKickKind.GroundThird?"3ダンメ":kind==OutsideKickKind.AirUp?"ウエキック":kind==OutsideKickKind.AirDown?"シタキック":kind==OutsideKickKind.Dash?"ダッシュ":"マエキック";
            return name+"（カリ） / "+(hit?"ハツドウ":recovery?"フッキ":"ジュンビ");
        }
        private Olca.Presentation.Effects.ProvisionalCombatGraphic PixelGraphic(string name)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(Olca.Presentation.Effects.ProvisionalCombatGraphic));go.transform.SetParent(transform,false);var graphic=go.GetComponent<Olca.Presentation.Effects.ProvisionalCombatGraphic>();graphic.raycastTarget=false;graphic.rectTransform.anchorMin=graphic.rectTransform.anchorMax=new Vector2(0,.21f);graphic.enabled=false;return graphic;
        }
        private void InitializeKickEffects(Font font)
        {
            chargeRing=PixelGraphic("OutsideProvisionalChargeRing");chargeRing.transform.SetSiblingIndex(player.transform.GetSiblingIndex()+1);
            var slash=new GameObject("OutsideProvisionalKickTrail",typeof(RectTransform),typeof(Image));slash.transform.SetParent(transform,false);kickTrail=slash.GetComponent<Image>();kickTrail.raycastTarget=false;kickTrail.enabled=false;kickTrail.rectTransform.anchorMin=kickTrail.rectTransform.anchorMax=new Vector2(0,.21f);kickTrail.rectTransform.pivot=Vector2.one*.5f;kickTrail.transform.SetSiblingIndex(player.transform.GetSiblingIndex()+1);
            var plate=new GameObject("OutsideProvisionalKickCaption",typeof(RectTransform),typeof(Image));plate.transform.SetParent(transform,false);kickCaptionPlate=(RectTransform)plate.transform;kickCaptionPlate.anchorMin=kickCaptionPlate.anchorMax=new Vector2(0,.21f);kickCaptionPlate.sizeDelta=new Vector2(360,36);plate.GetComponent<Image>().color=new Color(.01f,.055f,.08f,.9f);plate.GetComponent<Image>().raycastTarget=false;
            var text=new GameObject("OutsideProvisionalKickCaptionText",typeof(RectTransform),typeof(Text));text.transform.SetParent(plate.transform,false);kickCaption=text.GetComponent<Text>();kickCaption.font=Olca.Presentation.Dungeon.UiLook.MenuFont(font);kickCaption.fontSize=22;kickCaption.color=Color.white;kickCaption.alignment=TextAnchor.MiddleCenter;kickCaption.raycastTarget=false;kickCaption.rectTransform.anchorMin=Vector2.zero;kickCaption.rectTransform.anchorMax=Vector2.one;kickCaption.rectTransform.offsetMin=kickCaption.rectTransform.offsetMax=Vector2.zero;plate.SetActive(false);
            for(int i=0;i<2;i++){targetBursts[i]=PixelGraphic("OutsideProvisionalTargetDeath_"+i);targetBursts[i].transform.SetSiblingIndex(trialTargetHp.transform.GetSiblingIndex());}
        }
        private void RenderKickEffects(float camera,float viewport,float verticalOffset)
        {
            var kind=presentation.NormalKickKind;var timing=presentation.KickTiming;
            if(!presentation.IsCharging)chargeRing.ClearChargeParticles();
            if(presentation.IsCharging){
                kickTrailWallClip?.ClearClip();
                var chargeCore=presentation.Core;float chargeUnit=OutsideMovementDefinition.UnitsPerWorldUnit;
                float chargeX=(chargeCore.Position/chargeUnit-camera+viewport*.5f)*pixelsPerWorldUnit;
                float chargeY=(chargeCore.Height/chargeUnit-verticalOffset+1.15f)*pixelsPerWorldUnit;
                float fraction=Mathf.Clamp01(presentation.ChargeFrames/(float)Mathf.Max(1,presentation.ChargeRequiredFrames));bool full=fraction>=1;
                chargeRing.rectTransform.anchoredPosition=new Vector2(chargeX,chargeY);chargeRing.rectTransform.sizeDelta=Vector2.one*1.9f*pixelsPerWorldUnit;
                chargeRing.ShowChargeParticles(chargeCore.Frame,presentation.ChargeFrames,presentation.ChargeRequiredFrames);kickTrail.enabled=false;
                kickCaption.text=full?"カンリョウ / ハナス（カリ）":"タメチュウ（カリ）";kickCaptionKey=-1;kickCaptionPlate.gameObject.SetActive(true);
                float chargeWidth=((RectTransform)transform).rect.width;kickCaptionPlate.anchoredPosition=new Vector2(Mathf.Clamp(chargeX,182,Mathf.Max(182,chargeWidth-182)),(chargeCore.Height/chargeUnit-verticalOffset)*pixelsPerWorldUnit-30);return;
            }
            if(!kind.HasValue||timing==null||presentation.Player==null){kickTrailWallClip?.ClearClip();kickTrail.enabled=false;kickCaptionPlate.gameObject.SetActive(false);kickCaptionKey=-1;return;}
            var core=presentation.Core;bool hit=presentation.IsKickHitWindow,recovery=presentation.IsKickRecovery;int elapsed=presentation.KickElapsedFrames;long low,high;if(presentation.IsChargedKick)OutsideChargeKickData.Bounds(kind.Value<=OutsideKickKind.GroundThird,out low,out high);else OutsideKickTrialDefinition.Bounds(kind.Value,out low,out high);
            float unit=OutsideMovementDefinition.UnitsPerWorldUnit,range=(kind.Value==OutsideKickKind.Dash?OutsideKickTrialDefinition.DashRange:OutsideKickTrialDefinition.Range)/unit;int facing=presentation.NormalKickFacing??core.Facing;
            float center=(core.Position/unit+facing*range*.5f-camera+viewport*.5f)*pixelsPerWorldUnit,y=(core.Height/unit+(low+high)*.5f/unit-verticalOffset)*pixelsPerWorldUnit;
            kickTrail.rectTransform.anchoredPosition=new Vector2(center,y);kickTrail.rectTransform.sizeDelta=new Vector2(range*pixelsPerWorldUnit,(high-low)/unit*pixelsPerWorldUnit);
            float alpha=hit?1:recovery?.4f*Mathf.Clamp01(1-(elapsed-timing.StartupFrames-timing.ActiveFrames)/(float)Mathf.Max(1,timing.RecoveryFrames)):0;
            int frame=Olca.Presentation.Effects.TrialSlashSprites.FrameIndex(hit?elapsed-timing.StartupFrames:-1,timing.ActiveFrames,recovery?elapsed-timing.StartupFrames-timing.ActiveFrames:-1,timing.RecoveryFrames);
            int row=presentation.IsChargedKick?3:kind.Value==OutsideKickKind.GroundSecond?1:kind.Value==OutsideKickKind.GroundThird?2:0;
            kickTrail.sprite=(slashSprites??(slashSprites=new Olca.Presentation.Effects.TrialSlashSprites())).Resolve(row,frame);kickTrail.color=new Color(1,1,1,hit?1:alpha);kickTrail.rectTransform.localScale=new Vector3(facing,1,1);kickTrail.enabled=alpha>0&&kickTrail.sprite!=null;
            // 元の寸法・反転・UVを保ち、命中と同じ試用壁の先だけを除く。
            var wall=kickTrail.enabled?presentation.FirstSwordBlockingWall(core.Position,core.Height,facing,kind.Value==OutsideKickKind.Dash?OutsideKickTrialDefinition.DashRange:OutsideKickTrialDefinition.Range):null;
            float boundary=wall.HasValue?((RectTransform)transform).rect.xMin+(wall.Value/unit-camera+viewport*.5f)*pixelsPerWorldUnit:0;
            ClipSwordGraphic(kickTrail,ref kickTrailWallClip,wall,facing,boundary);
            int key=(presentation.IsChargedKick?100:0)+(int)kind.Value*3+(hit?1:recovery?2:0);if(kickCaptionKey!=key){kickCaptionKey=key;kickCaption.text=presentation.IsChargedKick?"タメ（カリ） / "+(hit?"ハツドウ":recovery?"フッキ":"ジュンビ"):ProvisionalKickCaption(kind.Value,hit,recovery);}kickCaptionPlate.gameObject.SetActive(true);
            float width=((RectTransform)transform).rect.width;float x=(core.Position/unit-camera+viewport*.5f)*pixelsPerWorldUnit;kickCaptionPlate.anchoredPosition=new Vector2(Mathf.Clamp(x,182,Mathf.Max(182,width-182)),(core.Height/unit-verticalOffset)*pixelsPerWorldUnit-30);
        }
        private void RenderTargetBursts(float camera,float viewport,float verticalOffset)
        {
            bool visible=presentation?.IsCurrent==true&&OutsideTrialRoute.KnownMap(presentation.Core.TrialMapId);
            if(!visible){ClearTargetBursts();return;}var core=presentation.Core;bool newLease=!ReferenceEquals(burstLease,presentation);burstLease=presentation;
            for(int i=0;i<2;i++){
                var target=i==0?presentation.TrialTarget:presentation.SecondTrialTarget;bool dead=target.IsDead;
                if(newLease||!ReferenceEquals(observedBurstTargets[i],target)){observedBurstTargets[i]=target;observedDead[i]=dead;targetDeathAt[i]=-1;}
                else if(!observedDead[i]&&dead)targetDeathAt[i]=presentation.Player?.IsBlind==true?-1:core.Frame;
                observedDead[i]=dead;if(presentation.Player?.IsBlind==true||!dead)targetDeathAt[i]=-1;
                long age=targetDeathAt[i]<0?-1:core.Frame-targetDeathAt[i];var effect=targetBursts[i];effect.enabled=age>=0&&age<14;if(!effect.enabled)continue;
                long position=i==0?presentation.FirstTargetX:presentation.SecondTargetX;float x=(position/(float)OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit,top=TrialTargetTop();effect.rectTransform.anchoredPosition=new Vector2(x,top*.5f-verticalOffset*pixelsPerWorldUnit+(i==0?presentation.FirstTargetHeight/(float)OutsideMovementDefinition.UnitsPerWorldUnit*pixelsPerWorldUnit:0));effect.rectTransform.sizeDelta=Vector2.one*Mathf.Max(48,top*1.2f);effect.Burst(age/14f,Olca.Presentation.Effects.ProvisionalPixelPattern.Tint(false));
            }
        }
        private void ClearTargetBursts(){burstLease=null;for(int i=0;i<2;i++){observedBurstTargets[i]=null;observedDead[i]=false;targetDeathAt[i]=-1;if(targetBursts[i]!=null)targetBursts[i].enabled=false;}}
        private void ClearKickEffects(){kickTrailWallClip?.ClearClip();if(chargeRing!=null)chargeRing.ClearChargeParticles();if(kickTrail!=null){kickTrail.enabled=false;kickTrail.sprite=null;}if(kickCaptionPlate!=null)kickCaptionPlate.gameObject.SetActive(false);kickCaptionKey=-1;ClearTargetBursts();}
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>地上三段の試用連番。Sessionの論理時刻を読み、判定・移動・保存は変更しない。</summary>
    /// <summary>探索タメ専用の試用本体。共有Spriteを借り、破棄・ゲーム更新は行わない。</summary>
    /// <summary>地上ボムの試用4姿勢。Sessionの前後隙を表示へ写すだけで、独自時計を持たない。</summary>
    /// <summary>直進弾の試用6コマ。既存の60論理フレーム寿命と発射時刻を表示へ写す。</summary>
    public sealed class TrialShotFlightMotion : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;private Sprite[] frames,airFrames;private bool tried,airTried,disposed;
        public TrialShotFlightMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(long age)=>age<0||age>=60?-1:(int)(age/2%6);
        public Sprite Resolve(long age,string cardId=null)
        {
            int frame=FrameIndex(age);if(disposed||frame<0)return null;
            // 保存済みCardIdだけで見分ける。欠損時は既存ショットへ戻し、属性や飛翔計算を変えない。
            if(cardId==Olca.Core.Domain.TrialInitialCardDefinitions.AirShot){if(!airTried){airTried=true;airFrames=ReadFrames("trial_air_shot_flight");}if(airFrames!=null)return airFrames[frame];}
            if(!tried){tried=true;frames=ReadFrames("trial_shot_flight");}
            return frames?[frame];
        }
        private Sprite[] ReadFrames(string stem)
        {
            var all=load("OlcaCharacterMotion/"+stem+"_sheet");if(all==null||all.Length!=6)return null;var ordered=new Sprite[6];for(int i=0;i<6;i++){var found=Array.FindAll(all,s=>s!=null&&s.name==stem+"_"+i);if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f||found[0].pivot!=new Vector2(128,128))return null;ordered[i]=found[0];}return ordered;
        }
        public void Dispose(){disposed=true;frames=airFrames=null;}
    }
    /// <summary>実着弾の既存表示期間へ6コマを配分する。飛翔・命中・保存は所有しない。</summary>
    public sealed class TrialBombBlastMotion : System.IDisposable
    {
        public const float VisibleWidth=230f/100f,VisibleHeight=90f/100f;
        private readonly System.Func<string,Sprite[]> load;private Sprite[] frames;private bool tried,disposed;
        public TrialBombBlastMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(long age,int duration)=>duration<=0||age<0||age>=duration?-1:(int)(age*6/duration);
        public Sprite Resolve(long age,int duration)
        {
            int frame=FrameIndex(age,duration);if(disposed||frame<0)return null;
            if(!tried){tried=true;var all=load("OlcaCharacterMotion/trial_bomb_blast_sheet");if(all!=null&&all.Length==6){var ordered=new Sprite[6];for(int i=0;i<6;i++){var found=Array.FindAll(all,s=>s!=null&&s.name=="trial_bomb_blast_"+i);if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f||found[0].pivot!=new Vector2(128,128))return null;ordered[i]=found[0];}frames=ordered;}}
            return frames?[frame];
        }
        public void Dispose(){disposed=true;frames=null;}
    }
    /// <summary>地上/空中射撃の試用姿勢。既存の行動時計だけを読む。</summary>
    public sealed class TrialShotBodyMotion : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;
        private readonly Sprite[][] clips=new Sprite[2][];
        private readonly bool[] tried=new bool[2];
        private bool disposed;
        public TrialShotBodyMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        // 掌からの発射直後だけ補正し、12論理フレームで既存射線へ戻す。判定/保存には触れない。
        public static Vector2 FlightOffset(long age)=>new Vector2(.55f,.5f)*Mathf.Clamp01(1-age/12f);
        // 前隙は構え、発射後は反動→復帰。表示だけで既存6/12フレームを分割する。
        public static int FrameIndex(long elapsed,int startup,int recovery,bool grounded=true){if(elapsed<0||startup<0||recovery<0||elapsed>=startup+(long)recovery)return -1;return (grounded?0:3)+(elapsed<startup?0:elapsed-startup<Math.Max(1,recovery/2)?1:2);}
        public Sprite Resolve(long elapsed,int startup,int recovery,bool fire=false,bool grounded=true)
        {
            int n=FrameIndex(elapsed,startup,recovery,grounded);if(disposed||n<0)return null;int key=fire?1:0;
            if(!tried[key]){tried[key]=true;string stem=fire?"trial_fire_shot_body":"trial_shot_body";var all=load("OlcaCharacterMotion/"+stem+"_sheet");if(all!=null&&all.Length==6){var found=new Sprite[6];for(int k=0;k<6;k++){var matches=Array.FindAll(all,v=>v!=null&&v.name==stem+"_"+k);if(matches.Length!=1||matches[0].rect.width!=256||matches[0].rect.height!=256||Mathf.Abs(matches[0].pixelsPerUnit-100)>.001f||Vector2.Distance(matches[0].pivot,new Vector2(128,14))>.001f)return null;found[k]=matches[0];}clips[key]=found;}}
            return clips[key]?[n];
        }
        public bool Contains(Sprite sprite)=>!disposed&&sprite!=null&&Array.Exists(clips,c=>c!=null&&Array.IndexOf(c,sprite)>=0);
        public void Dispose(){disposed=true;Array.Clear(clips,0,clips.Length);}
    }
    /// <summary>回復・防御の成立後の試用身振り。既存エフェクトの時計を読むだけで行動を拘束しない。</summary>
    public sealed class TrialSupportBodyMotion:System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;private readonly Sprite[][] clips=new Sprite[2][];private readonly bool[] tried=new bool[2];private bool disposed;
        public TrialSupportBodyMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(bool shield,float age){float duration=shield?OutsideShieldActivationSpriteCanvas.Duration:OutsideHealingSpriteCanvas.Duration;return age<0||age>=duration?-1:(shield?3:0)+Mathf.Min(2,Mathf.FloorToInt(age*3/duration));}
        public Sprite Resolve(bool shield,float age,bool fire=false)
        {
            int frame=FrameIndex(shield,age);if(disposed||frame<0)return null;int key=fire?1:0;if(!tried[key]){tried[key]=true;string stem=fire?"trial_fire_support_body":"trial_support_body";var all=load("OlcaCharacterMotion/"+stem+"_sheet");if(all!=null&&all.Length==6){var ordered=new Sprite[6];for(int n=0;n<6;n++){var found=Array.FindAll(all,x=>x!=null&&x.name==stem+"_"+n);if(found.Length!=1||found[0].rect.size!=new Vector2(256,256)||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f||found[0].pivot!=new Vector2(128,14))return null;ordered[n]=found[0];}clips[key]=ordered;}}return clips[key]?[frame];
        }
        public bool Contains(Sprite sprite)=>!disposed&&sprite!=null&&Array.Exists(clips,c=>c!=null&&Array.IndexOf(c,sprite)>=0);
        public void Dispose(){disposed=true;Array.Clear(clips,0,clips.Length);}
    }
    /// <summary>ワイドソードの試用6コマ。従来の予兆2/命中後12フレームと表示領域を維持する。</summary>
    public sealed class TrialWideSwordEffect : System.IDisposable
    {
        private readonly Func<string,Sprite[]> load;private Sprite[] frames;private bool tried,disposed;
        public TrialWideSwordEffect(Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(long age)=>age< -2||age>=12?-1:age<0?0:age<2?1:age<4?2:age<7?3:age<10?4:5;
        public Sprite Resolve(long age)
        {
            int n=FrameIndex(age);if(disposed||n<0)return null;
            if(!tried){tried=true;var all=load("OlcaCharacterMotion/trial_wide_sword_effect_sheet");if(all!=null&&all.Length==6){var ordered=new Sprite[6];for(int k=0;k<6;k++){var matches=Array.FindAll(all,v=>v!=null&&v.name=="trial_wide_sword_effect_"+k);if(matches.Length!=1||matches[0].rect.width!=256||matches[0].rect.height!=256||Mathf.Abs(matches[0].pixelsPerUnit-100)>.001f||Vector2.Distance(matches[0].pivot,new Vector2(128,128))>.001f)return null;ordered[k]=matches[0];}frames=ordered;}}
            return frames?[n];
        }
        public void Dispose(){disposed=true;frames=null;}
    }
    /// <summary>スピアの試用3姿勢×地上/空中。行動時計を参照し、命中・保存を所有しない。</summary>
    public sealed class TrialSpearBodyMotion : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;
        private readonly Sprite[][] clips=new Sprite[2][];
        private readonly bool[] tried=new bool[2];private bool disposed;
        public TrialSpearBodyMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        // 構えは前隙全体、突きは既存の命中開始から後隙前半、後半で復帰する。
        public static int FrameIndex(long elapsed,int startup,int recovery,bool grounded=true){if(elapsed<0||startup<0||recovery<0||elapsed>=startup+(long)recovery)return -1;return (grounded?0:3)+(elapsed<startup?0:elapsed-startup<Math.Max(1,recovery/2)?1:2);}
        public Sprite Resolve(long elapsed,int startup,int recovery,bool fire=false,bool grounded=true)
        {
            int n=FrameIndex(elapsed,startup,recovery,grounded);if(disposed||n<0)return null;int key=fire?1:0;
            if(!tried[key]){tried[key]=true;string stem=fire?"trial_fire_spear_body":"trial_spear_body";var all=load("OlcaCharacterMotion/"+stem+"_sheet");if(all!=null&&all.Length==6){var found=new Sprite[6];for(int k=0;k<6;k++){var matches=Array.FindAll(all,v=>v!=null&&v.name==stem+"_"+k);if(matches.Length!=1||matches[0].rect.width!=256||matches[0].rect.height!=256||Mathf.Abs(matches[0].pixelsPerUnit-100)>.001f||Vector2.Distance(matches[0].pivot,new Vector2(128,14))>.001f)return null;found[k]=matches[0];}clips[key]=found;}}
            return clips[key]?[n];
        }
        public bool Contains(Sprite sprite)=>!disposed&&sprite!=null&&Array.Exists(clips,c=>c!=null&&Array.IndexOf(c,sprite)>=0);
        public void Dispose(){disposed=true;Array.Clear(clips,0,clips.Length);}
    }
    public sealed class TrialSwordSwingMotion : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;
        private readonly Sprite[][] clips=new Sprite[4][];
        private readonly bool[] tried=new bool[4];
        private bool disposed;
        public TrialSwordSwingMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(long elapsed,int startup,int recovery){if(elapsed<0||startup<0||recovery<0||elapsed>=startup+(long)recovery)return -1;return elapsed<startup?(int)(elapsed*3/startup):3+Math.Min(2,(int)((elapsed-startup)*3/Math.Max(1,recovery)));}
        public Sprite Resolve(long elapsed,int startup,int recovery,bool isFire=false,bool grounded=true)
        {
            int n=FrameIndex(elapsed,startup,recovery);if(disposed||n<0)return null;
            int key=(grounded?0:2)+(isFire?1:0);
            if(!tried[key]){tried[key]=true;clips[key]=Read(isFire,grounded);}
            return clips[key]?[n];
        }
        private Sprite[] Read(bool isFire,bool grounded)
        {
            string stem=(isFire?"trial_fire_":"trial_")+(grounded?"sword_swing":"air_sword_swing");
            var all=load("OlcaCharacterMotion/"+stem+"_sheet");if(all==null||all.Length!=6)return null;
            var frames=new Sprite[6];for(int n=0;n<6;n++){var found=Array.FindAll(all,s=>s!=null&&s.name==stem+"_"+n);if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f||Mathf.Abs(found[0].pivot.x-128)>.001f||Mathf.Abs(found[0].pivot.y-14)>.001f)return null;frames[n]=found[0];}return frames;
        }
        public bool Contains(Sprite sprite)=>!disposed&&sprite!=null&&Array.Exists(clips,c=>c!=null&&Array.IndexOf(c,sprite)>=0);
        public void Dispose(){disposed=true;Array.Clear(clips,0,clips.Length);}
    }
    public sealed class TrialBombThrowMotion : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;
        private Sprite[] frames,variants;private bool attempted,attemptedVariants,disposed;
        public const int InstantReleaseVisualFrames=12; // 表示だけの仮値。入力予約や実行硬直を延ばさない。
        public TrialBombThrowMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(long elapsed,int startup,int recovery)
        {
            if(elapsed<0||startup<0||recovery<0||elapsed>=startup+(long)recovery)return -1;
            if(elapsed<startup)return elapsed<Math.Max(1,startup/2)?0:1;
            return elapsed-startup<Math.Max(1,recovery/2)?2:3;
        }
        public Sprite Resolve(long elapsed,int startup,int recovery,bool grounded=true,bool isFire=false)
        {
            int index=FrameIndex(elapsed,startup,recovery);if(disposed||index<0)return null;
            if(!grounded||isFire){if(!attemptedVariants){attemptedVariants=true;variants=LoadVariants();}return variants==null?null:variants[(isFire?(grounded?1:2):0)*4+index];}
            if(!attempted){attempted=true;var all=load("OlcaCharacterMotion/trial_bomb_throw_sheet");
                if(all!=null&&all.Length==4){var candidate=new Sprite[4];for(int n=0;n<4;n++){
                    var found=System.Array.FindAll(all,s=>s!=null&&s.name=="trial_bomb_throw_"+n);
                    if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f)return null;
                    candidate[n]=found[0];
                }frames=candidate;}
            }
            return frames==null?null:frames[index];
        }
        private Sprite[] LoadVariants()
        {
            var all=load("OlcaCharacterMotion/trial_bomb_variants_sheet");if(all==null||all.Length!=12)return null;
            var result=new Sprite[12];for(int n=0;n<12;n++){
                var found=System.Array.FindAll(all,s=>s!=null&&s.name=="trial_bomb_variant_"+n);
                if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f)return null;result[n]=found[0];
            }return result;
        }
        /// <summary>試用の手先から既存衝突終点へ収束する表示だけの補間。live actorや保存計画を変更しない。</summary>
        public static Vector2 FlightPosition(OutsideBombFlightData flight,long frame)
        {
            float unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float age=Mathf.Clamp(frame-flight.FiredAt,0,flight.Launch.Duration);
            float remaining=1-age/Mathf.Max(1,flight.Launch.Duration);
            float start=Mathf.Clamp(flight.Launch.X/unit+flight.Launch.Facing*.68f,Mathf.Min(flight.Launch.X,flight.Launch.ImpactX)/unit,Mathf.Max(flight.Launch.X,flight.Launch.ImpactX)/unit);
            float x=Mathf.Lerp(flight.Launch.ImpactX/unit,start,remaining);
            return new Vector2(x,flight.Height(frame)/unit+.85f*remaining);
        }
        public bool Contains(Sprite sprite)=>!disposed&&sprite!=null&&(frames!=null&&System.Array.IndexOf(frames,sprite)>=0||variants!=null&&System.Array.IndexOf(variants,sprite)>=0);
        public void Dispose(){disposed=true;frames=variants=null;}
    }
    public sealed class TrialChargeMotion : System.IDisposable
    {
        private readonly System.Func<string,Sprite[]> load;
        private Sprite[] basic,fire;private bool attemptedBasic,attemptedFire,disposed;
        public TrialChargeMotion(System.Func<string,Sprite[]> loader=null){load=loader??(key=>Resources.LoadAll<Sprite>(key));}
        public static int FrameIndex(bool charging,bool full,int elapsed,OutsideKickTiming timing)
        {
            if(charging)return full?1:0;
            if(timing==null||elapsed<0||elapsed>=timing.StartupFrames+(long)timing.ActiveFrames+timing.RecoveryFrames)return -1;
            return elapsed<timing.StartupFrames?1:elapsed<timing.StartupFrames+timing.ActiveFrames?2:3;
        }
        public Sprite Resolve(bool grounded,bool charging,bool full,int elapsed,OutsideKickTiming timing,bool isFire)
        {
            if(disposed)return null;int index=FrameIndex(charging,full,elapsed,timing);if(index<0)return null;
            if(isFire&&!attemptedFire){attemptedFire=true;fire=Load(true);}else if(!isFire&&!attemptedBasic){attemptedBasic=true;basic=Load(false);}
            var sprites=isFire?fire:basic;return sprites==null?null:sprites[(grounded?0:4)+index];
        }
        private Sprite[] Load(bool isFire)
        {
            string prefix=isFire?"trial_fire_charge_":"trial_charge_";
            var all=load("OlcaCharacterMotion/"+(isFire?"trial_fire_charge_sheet":"trial_charge_sheet"));if(all==null||all.Length!=8)return null;
            var frames=new Sprite[8];for(int row=0;row<2;row++)for(int frame=0;frame<4;frame++){
                string name=prefix+(row==0?"ground_":"air_")+frame;var found=System.Array.FindAll(all,s=>s!=null&&s.name==name);
                if(found.Length!=1||Mathf.Abs(found[0].rect.width-256)>.01f||Mathf.Abs(found[0].rect.height-256)>.01f||Mathf.Abs(found[0].pixelsPerUnit-100)>.001f)return null;
                frames[row*4+frame]=found[0];
            }return frames;
        }
        public bool Contains(Sprite sprite)=>!disposed&&sprite!=null&&(basic!=null&&System.Array.IndexOf(basic,sprite)>=0||fire!=null&&System.Array.IndexOf(fire,sprite)>=0);
        public void Dispose(){disposed=true;basic=fire=null;}
    }
    public sealed class TrialGroundComboMotion : System.IDisposable
    {
        private readonly System.Func<string,UnityEngine.Sprite[]> load;
        private UnityEngine.Sprite[] basic,fire;
        private bool attemptedBasic,attemptedFire,disposed;
        public TrialGroundComboMotion(System.Func<string,UnityEngine.Sprite[]> loader=null){load=loader??(key=>UnityEngine.Resources.LoadAll<UnityEngine.Sprite>(key));}
        public static int FrameIndex(Olca.Core.Logic.Integration.OutsideKickKind kind,int elapsed,Olca.Core.Logic.Integration.OutsideKickTiming timing)
        {
            if(kind<Olca.Core.Logic.Integration.OutsideKickKind.GroundFirst||kind>Olca.Core.Logic.Integration.OutsideKickKind.GroundThird||timing==null||elapsed<0||elapsed>=timing.StartupFrames+(long)timing.ActiveFrames+timing.RecoveryFrames)return -1;
            if(elapsed<timing.StartupFrames)return elapsed<System.Math.Max(1,timing.StartupFrames/3)?0:1;
            if(elapsed<timing.StartupFrames+timing.ActiveFrames)return 2;
            // 接触後は脚を引いてから立ち姿へ戻る。復帰の時計を別途進めない。
            return elapsed-timing.StartupFrames-timing.ActiveFrames<System.Math.Max(1,timing.RecoveryFrames/2)?3:0;
        }
        public UnityEngine.Sprite Resolve(Olca.Core.Logic.Integration.OutsideKickKind kind,int elapsed,Olca.Core.Logic.Integration.OutsideKickTiming timing,bool isFire)
        {
            if(disposed)return null;int frame=FrameIndex(kind,elapsed,timing);if(frame<0)return null;
            if(isFire&&!attemptedFire){attemptedFire=true;fire=Load(true);}else if(!isFire&&!attemptedBasic){attemptedBasic=true;basic=Load(false);}
            var sprites=isFire?fire:basic;return sprites==null?null:sprites[(int)kind*4+frame];
        }
        private UnityEngine.Sprite[] Load(bool isFire)
        {
            string prefix=isFire?"trial_fire_combo_":"trial_combo_";var all=load("OlcaCharacterMotion/"+(isFire?"trial_fire_ground_combo_sheet":"trial_ground_combo_sheet"));
            if(all==null||all.Length!=12)return null;var frames=new UnityEngine.Sprite[12];
            for(int stage=0;stage<3;stage++)for(int frame=0;frame<4;frame++){
                string name=prefix+(stage+1)+"_"+frame;var found=System.Array.FindAll(all,s=>s!=null&&s.name==name);
                if(found.Length!=1||found[0].rect.width!=256||found[0].rect.height!=256||UnityEngine.Mathf.Abs(found[0].pixelsPerUnit-100)>.001f)return null;frames[stage*4+frame]=found[0];
            }
            return frames;
        }
        // 新atlasは右向き原画。既存の左向き本体と反転基準を混同しない。
        public bool Contains(UnityEngine.Sprite sprite)=>!disposed&&sprite!=null&&
            (basic!=null&&System.Array.IndexOf(basic,sprite)>=0||fire!=null&&System.Array.IndexOf(fire,sprite)>=0);
        public void Dispose(){disposed=true;basic=fire=null;}
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>確定済み表示通知を六枚の借用Spriteへ写す。回復と吸収の寿命は別instanceで所有する。</summary>
    public abstract class OutsideFeedbackSpriteCanvas:MonoBehaviour
    {
        protected abstract Olca.Core.Domain.CombatFeedbackKind Trigger{get;}
        protected abstract string SheetStem{get;}
        protected abstract string EffectName{get;}
        protected abstract float PulseDuration{get;}
        private Olca.Core.Domain.PlayerCharacter actor;private Func<bool> live;private Image body,effect;private Sprite[] frames;private float age,scale;private int lastDraw=-1;
        public bool IsAnimating=>effect!=null&&effect.enabled;
        private static long nextPulseOrder;private long pulseOrder;
        public long PulseOrder=>IsAnimating?pulseOrder:0;
        public float AnimationAge=>IsAnimating?age:-1;

        protected static int ResolveFrame(float elapsed,float duration)=>elapsed<0||elapsed>=duration?-1:Mathf.Min(5,Mathf.FloorToInt(elapsed*(6f/duration)));
        public void Bind(Olca.Core.Domain.PlayerCharacter value,Func<bool> isLive,Image anchor,float pixelsPerUnit)
        {Unbind();if(value==null||isLive==null||anchor==null||pixelsPerUnit<=0)return;actor=value;live=isLive;body=anchor;scale=pixelsPerUnit;Subscribe(actor);}
        protected virtual void Subscribe(Olca.Core.Domain.PlayerCharacter value){value.OnCombatFeedback+=OnFeedback;}
        protected virtual void Unsubscribe(Olca.Core.Domain.PlayerCharacter value){value.OnCombatFeedback-=OnFeedback;}
        protected void OnFeedback(Olca.Core.Domain.CombatFeedback value)
        {
            if(value.Kind!=Trigger||value.Amount<=0||live?.Invoke()!=true||body==null||!body.isActiveAndEnabled)return;
            if(frames==null){var source=Resources.LoadAll<Sprite>("OlcaCharacterMotion/"+SheetStem+"_sheet");frames=Enumerable.Range(0,6).Select(n=>source.FirstOrDefault(s=>s.name==SheetStem+"_"+n&&s.rect.size==new Vector2(256,256)&&Mathf.Approximately(s.pixelsPerUnit,100)&&s.pivot==new Vector2(128,128))).ToArray();}
            if(frames.Any(s=>s==null))return;
            if(effect==null){var go=new GameObject(EffectName,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);effect=go.GetComponent<Image>();effect.raycastTarget=false;}
            age=0;lastDraw=-1;pulseOrder=System.Threading.Interlocked.Increment(ref nextPulseOrder);effect.sprite=frames[0];effect.color=Color.white;effect.enabled=true;Place();
        }
        private void Place()
        {
            var rect=effect.rectTransform;rect.anchorMin=body.rectTransform.anchorMin;rect.anchorMax=body.rectTransform.anchorMax;rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=body.rectTransform.anchoredPosition+Vector2.up*(1.25f*scale);rect.sizeDelta=Vector2.one*(3*scale);rect.localScale=Vector3.one;
            // 胴体と顔の前へ重ねず、左右向きでも同じ空間へ追従させる。
            int before=body.transform.GetSiblingIndex();if(effect.transform.GetSiblingIndex()<before)before--;effect.transform.SetSiblingIndex(before);
        }
        public void Paint(float delta,int drawFrame)
        {
            if(live?.Invoke()!=true||body==null||!body.isActiveAndEnabled){Unbind();return;}if(!IsAnimating)return;
            if(lastDraw!=drawFrame){if(lastDraw!=-1)age+=Mathf.Max(0,delta);lastDraw=drawFrame;}
            int n=ResolveFrame(age,PulseDuration);if(n<0){effect.enabled=false;effect.sprite=null;return;}effect.sprite=frames[n];Place();
        }
        public void Unbind(){if(actor!=null)Unsubscribe(actor);actor=null;live=null;body=null;frames=null;age=0;pulseOrder=0;lastDraw=-1;if(effect!=null){effect.enabled=false;effect.sprite=null;}}
        protected virtual void OnDisable()=>Unbind();
        protected virtual void OnDestroy()=>Unbind();
    }
    /// <summary>実際に増加したHPだけを示す既存の回復演出。</summary>
    public sealed class OutsideHealingSpriteCanvas:OutsideFeedbackSpriteCanvas
    {
        public const float Duration=.6f;
        public static int FrameIndex(float elapsed)=>ResolveFrame(elapsed,Duration);
        protected override Olca.Core.Domain.CombatFeedbackKind Trigger=>Olca.Core.Domain.CombatFeedbackKind.Healing;
        protected override string SheetStem=>"trial_healing_effect";
        protected override string EffectName=>"OutsideHealingEffect";
        protected override float PulseDuration=>Duration;
    }
    /// <summary>成立した防御付与だけを表示する。既存防御の再bindでは開始しない。</summary>
    public sealed class OutsideShieldActivationSpriteCanvas:OutsideFeedbackSpriteCanvas
    {
        public const float Duration=.4f;
        public static int FrameIndex(float elapsed)=>ResolveFrame(elapsed,Duration);
        protected override Olca.Core.Domain.CombatFeedbackKind Trigger=>Olca.Core.Domain.CombatFeedbackKind.Guard;
        protected override string SheetStem=>"trial_shield_activation";
        protected override string EffectName=>"OutsideShieldActivation";
        protected override float PulseDuration=>Duration;
        protected override void Subscribe(Olca.Core.Domain.PlayerCharacter value){value.OnBarrierApplied+=OnApplied;}
        protected override void Unsubscribe(Olca.Core.Domain.PlayerCharacter value){value.OnBarrierApplied-=OnApplied;}
        private void OnApplied(int amount)=>OnFeedback(new Olca.Core.Domain.CombatFeedback(Trigger,amount));
    }
    /// <summary>バリアが実際に吸収した通知だけを示す。残耐久や防御時間は変更しない。</summary>
    public sealed class OutsideShieldHitSpriteCanvas:OutsideFeedbackSpriteCanvas
    {
        public const float Duration=.3f;
        public static int FrameIndex(float elapsed)=>ResolveFrame(elapsed,Duration);
        protected override Olca.Core.Domain.CombatFeedbackKind Trigger=>Olca.Core.Domain.CombatFeedbackKind.BarrierDamage;
        protected override string SheetStem=>"trial_shield_hit_effect";
        protected override string EffectName=>"OutsideShieldHitEffect";
        protected override float PulseDuration=>Duration;
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>試用壁の先だけをUIメッシュから切り取り、スプライトの寸法・UV・時計は変えない。</summary>
    public sealed class OutsideSwordWallClip : UnityEngine.UI.BaseMeshEffect
    {
        private Transform space;private int side;private float edge;private Matrix4x4 configuredMatrix;private bool clipping;
        private readonly List<UIVertex> source=new List<UIVertex>(64),polygon=new List<UIVertex>(8),result=new List<UIVertex>(96);
        public void SetClip(Transform coordinateSpace,int facing,float boundary)
        {
            var matrix=coordinateSpace.worldToLocalMatrix*transform.localToWorldMatrix;
            bool changed=!clipping||space!=coordinateSpace||side!=facing||edge!=boundary||configuredMatrix!=matrix;
            space=coordinateSpace;side=facing;edge=boundary;configuredMatrix=matrix;clipping=true;
            if(!enabled)enabled=true;else if(changed)graphic?.SetVerticesDirty();
        }
        public void ClearClip(){clipping=false;space=null;if(enabled)enabled=false;}
        public override void ModifyMesh(VertexHelper vh)
        {
            if(!IsActive()||!clipping||space==null)return;
            var matrix=space.worldToLocalMatrix*transform.localToWorldMatrix;source.Clear();result.Clear();vh.GetUIVertexStream(source);
            float Distance(UIVertex v)=>(edge-matrix.MultiplyPoint3x4(v.position).x)*side;
            for(int n=0;n+2<source.Count;n+=3){
                polygon.Clear();var previous=source[n+2];float prior=Distance(previous);
                for(int k=0;k<3;k++){
                    var current=source[n+k];float distance=Distance(current);
                    if((prior>=0)!=(distance>=0))polygon.Add(Interpolate(previous,current,Mathf.Clamp01(prior/(prior-distance))));
                    if(distance>=0)polygon.Add(current);previous=current;prior=distance;
                }
                for(int k=1;k+1<polygon.Count;k++){result.Add(polygon[0]);result.Add(polygon[k]);result.Add(polygon[k+1]);}
            }
            vh.Clear();vh.AddUIVertexTriangleStream(result);
        }
        private static UIVertex Interpolate(UIVertex a,UIVertex b,float t)
        {
            a.position=Vector3.LerpUnclamped(a.position,b.position,t);a.normal=Vector3.LerpUnclamped(a.normal,b.normal,t);a.tangent=Vector4.LerpUnclamped(a.tangent,b.tangent,t);a.color=Color.LerpUnclamped(a.color,b.color,t);
            a.uv0=Vector4.LerpUnclamped(a.uv0,b.uv0,t);a.uv1=Vector4.LerpUnclamped(a.uv1,b.uv1,t);a.uv2=Vector4.LerpUnclamped(a.uv2,b.uv2,t);a.uv3=Vector4.LerpUnclamped(a.uv3,b.uv3,t);return a;
        }
        protected override void OnDisable(){clipping=false;space=null;base.OnDisable();}
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>着地前は破線の予定輪だけ。着地後の短い実線輪と区別し、爆発表示はしない。</summary>
    public sealed class OutsideSlimeLandingGraphic : MaskableGraphic
    {
        private bool landed;private float age;
        public void SetLanding(bool value,float progress)
        {
            if(landed==value&&Mathf.Approximately(age,progress))return;
            landed=value;age=Mathf.Clamp01(progress);SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var rect=rectTransform.rect;var center=rect.center;float rx=rect.width*.5f,ry=rect.height*.5f;
            if(rx<=0||ry<=0)return;Color32 tint=landed?new Color(.85f,.95f,1,1-age):new Color(1,.65f,.15f,.85f);
            const int segments=32;float innerX=Mathf.Max(0,rx-2),innerY=Mathf.Max(0,ry-2);
            for(int n=0;n<segments;n++){
                if(!landed&&n%2!=0)continue;float a=n*Mathf.PI*2/segments,b=(n+1)*Mathf.PI*2/segments;int start=vh.currentVertCount;
                vh.AddVert(center+new Vector2(Mathf.Cos(a)*rx,Mathf.Sin(a)*ry),tint,Vector2.zero);vh.AddVert(center+new Vector2(Mathf.Cos(b)*rx,Mathf.Sin(b)*ry),tint,Vector2.zero);
                vh.AddVert(center+new Vector2(Mathf.Cos(b)*innerX,Mathf.Sin(b)*innerY),tint,Vector2.zero);vh.AddVert(center+new Vector2(Mathf.Cos(a)*innerX,Mathf.Sin(a)*innerY),tint,Vector2.zero);
                vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);
            }
        }
    }
}

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private UnityEngine.UI.Button branchButton441;private OutsideBranchView441 branchView441;
        private bool CanOpenBranch441=>CanPlay&&presentation.Core.TrialMapId==Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId&&System.Math.Abs(presentation.Core.Position-Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexDoorX)<=60000&&presentation.Core.IsGrounded&&!presentation.Core.IsMoving&&!presentation.HasAttackAction&&!presentation.IsCharging&&presentation.NormalKickKind==null&&presentation.Bombs.Count==0&&presentation.InitialShots.Count==0&&presentation.Player?.IsActionPrevented==false;
        private void InitializeBranch441(Olca.Presentation.Management.GameplayPanelFactory factory)
        {
            branchButton441=factory.Button("OutsideBranchEntry441","ブンキ カイロウ（カリ）",transform,new UnityEngine.Vector2(0,.5f),new UnityEngine.Vector2(195,-220));branchButton441.GetComponent<UnityEngine.RectTransform>().sizeDelta=new UnityEngine.Vector2(350,48);branchButton441.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};Olca.Presentation.Dungeon.UiLook.Button(branchButton441);
            branchButton441.onClick.AddListener(()=>{if(CanOpenBranch441&&branchView441==null)branchView441=OutsideBranchView441.Open(this,gaugeLabel.font,idleSprite,riseSprite,apexSprite,fallSprite,idleClip,moveClip,pixelsPerWorldUnit);});
        }
        private void RenderBranchEntry441(){if(branchButton441==null)return;branchButton441.gameObject.SetActive(IsOpen&&presentation.Core.TrialMapId==Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexMapId&&System.Math.Abs(presentation.Core.Position-Olca.Core.Logic.Integration.OutsideTrialRoute.AnnexDoorX)<=60000);branchButton441.interactable=CanOpenBranch441;}
        internal void ReleaseBranch441(OutsideBranchView441 view){if(branchView441!=view)return;branchView441=null;if(IsOpen)SetCovered(false);}
        private void ClearBranch441(){if(branchView441!=null)branchView441.Close();branchView441=null;if(branchButton441!=null)branchButton441.gameObject.SetActive(false);}
    }
    /// <summary>新しい独立区間の投影。既存のActor素材・モーションと追従カメラ式を借用する。</summary>
    public sealed class OutsideBranchView441 : UnityEngine.MonoBehaviour
    {
        public Olca.Unity.OutsideBranchRuntime441 Runtime {get;private set;}
        private OutsideExplorationView owner;private bool disposed,closed,paused,focused=true;private float pixels;
        private UnityEngine.Sprite idle,rise,apex,fall;private UnityEngine.AnimationClip idleClip,moveClip;private UnityEngine.SpriteRenderer sample;
        private Olca.Unity.GeneratedSpriteAssets.Lease assets;private Olca.Unity.MenuPadScope menu;
        private readonly TrialGroundComboMotion combo=new TrialGroundComboMotion();
        private UnityEngine.UI.Image playerImage,guardImage,floor,platform,platformEdge,gate,warning,warningTop,warningBottom,exit;
        private OutsideGroundKickTrace442 kickTrace442;
        private UnityEngine.UI.Text heading,help,status,guardLabel,upperLabel,exitLabel,warningLabel;
        private UnityEngine.UI.Button close,retry,pause;private Olca.Presentation.Effects.ProvisionalCombatGraphic chargeFx;private UnityEngine.UI.Image hitFx;
        public bool Live=>!disposed&&this!=null&&gameObject.activeInHierarchy&&owner!=null&&owner.IsOpen;
        public static OutsideBranchView441 Open(OutsideExplorationView owner,UnityEngine.Font font,UnityEngine.Sprite idle,UnityEngine.Sprite rise,UnityEngine.Sprite apex,UnityEngine.Sprite fall,UnityEngine.AnimationClip idleClip,UnityEngine.AnimationClip moveClip,float pixels)
        {
            var go=new UnityEngine.GameObject("OutsideBranch441",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(owner.transform,false);var r=(UnityEngine.RectTransform)go.transform;r.anchorMin=UnityEngine.Vector2.zero;r.anchorMax=UnityEngine.Vector2.one;r.offsetMin=r.offsetMax=UnityEngine.Vector2.zero;go.GetComponent<UnityEngine.UI.Image>().color=new UnityEngine.Color(.09f,.12f,.15f,1);
            var v=go.AddComponent<OutsideBranchView441>();v.owner=owner;v.idle=idle;v.rise=rise;v.apex=apex;v.fall=fall;v.idleClip=idleClip;v.moveClip=moveClip;v.pixels=pixels;
            try{owner.SetCovered(true);v.Initialize(font);go.transform.SetAsLastSibling();return v;}catch{v.Close();owner.SetCovered(false);throw;}
        }
        private UnityEngine.UI.Image Box(string name,UnityEngine.Color color)
        {
            var g=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));g.transform.SetParent(transform,false);var im=g.GetComponent<UnityEngine.UI.Image>();im.color=color;im.raycastTarget=false;im.rectTransform.anchorMin=im.rectTransform.anchorMax=new UnityEngine.Vector2(0,.21f);return im;
        }
        private void Initialize(UnityEngine.Font font)
        {
            assets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);
            UnityEngine.UI.Text Label(string name,string text,UnityEngine.Vector2 anchor,UnityEngine.Vector2 at,UnityEngine.Vector2 size,int point){var t=factory.Label(name,transform,anchor,at,size);t.text=text;t.fontSize=point;t.color=UnityEngine.Color.white;t.raycastTarget=false;t.resizeTextForBestFit=true;t.resizeTextMinSize=16;t.resizeTextMaxSize=point;return t;}
            for(int i=0;i<5;i++){var column=Box("BranchBackdropColumn"+i,new UnityEngine.Color(.13f,.16f,.18f,.5f));column.rectTransform.anchorMin=column.rectTransform.anchorMax=new UnityEngine.Vector2((i+.5f)/5,.21f);column.rectTransform.pivot=new UnityEngine.Vector2(.5f,0);column.rectTransform.sizeDelta=new UnityEngine.Vector2(90,650);}
            floor=Box("BranchFloor",new UnityEngine.Color(.25f,.31f,.34f,1));floor.rectTransform.pivot=new UnityEngine.Vector2(0,1);
            platform=Box("BranchUpperPlatform",new UnityEngine.Color(.25f,.45f,.51f,1));platformEdge=Box("BranchUpperEdge",new UnityEngine.Color(.8f,.96f,1,1));gate=Box("BranchGate",new UnityEngine.Color(.8f,.43f,.17f,1));
            warning=Box("BranchLowWarning",new UnityEngine.Color(.9f,.18f,.12f,.2f));warningTop=Box("BranchWarningTop",new UnityEngine.Color(1,.35f,.2f,1));warningBottom=Box("BranchWarningBottom",new UnityEngine.Color(1,.35f,.2f,1));exit=Box("BranchExit",new UnityEngine.Color(.25f,.7f,.48f,1));
            playerImage=Box("BranchPlayer",UnityEngine.Color.white);guardImage=Box("BranchGoblin",UnityEngine.Color.white);
            var trace=new UnityEngine.GameObject("BranchGroundKickTrace442",typeof(UnityEngine.RectTransform),typeof(OutsideGroundKickTrace442));trace.transform.SetParent(transform,false);kickTrace442=trace.GetComponent<OutsideGroundKickTrace442>();kickTrace442.raycastTarget=false;kickTrace442.rectTransform.anchorMin=kickTrace442.rectTransform.anchorMax=new UnityEngine.Vector2(0,.21f);kickTrace442.rectTransform.pivot=UnityEngine.Vector2.zero;kickTrace442.enabled=false;
            guardLabel=Label("BranchGuardLabel","",new UnityEngine.Vector2(0,.21f),UnityEngine.Vector2.zero,new UnityEngine.Vector2(330,52),20);
            upperLabel=Label("BranchUpperLabel","ノレル ウワミチ",new UnityEngine.Vector2(0,.21f),UnityEngine.Vector2.zero,new UnityEngine.Vector2(230,32),18);
            exitLabel=Label("BranchExitLabel","デグチ",new UnityEngine.Vector2(0,.21f),UnityEngine.Vector2.zero,new UnityEngine.Vector2(140,40),22);
            warningLabel=Label("BranchWarningLabel","ヒクい フリオロシ",new UnityEngine.Vector2(0,.21f),UnityEngine.Vector2.zero,new UnityEngine.Vector2(300,36),20);
            heading=Label("BranchHeading","ブンキ カイロウ（カリ） / セーブ・ホウシュウなし",new UnityEngine.Vector2(0,1),new UnityEngine.Vector2(450,-36),new UnityEngine.Vector2(850,50),26);
            help=Label("BranchGuide","ショキ: ←→/AD イドウ / Space ジャンプ / Z キック・ホジでタメ / ウエはカイヒ・シタはハンゲキ",new UnityEngine.Vector2(.5f,1),new UnityEngine.Vector2(0,-106),new UnityEngine.Vector2(1750,68),20);
            status=Label("BranchStatus","",new UnityEngine.Vector2(.5f,0),new UnityEngine.Vector2(0,72),new UnityEngine.Vector2(1600,90),24);
            UnityEngine.UI.Button Button(string name,string text,float x,System.Action action){var b=factory.Button(name,text,transform,new UnityEngine.Vector2(1,1),new UnityEngine.Vector2(x,-36));b.GetComponent<UnityEngine.RectTransform>().sizeDelta=new UnityEngine.Vector2(190,60);b.navigation=new UnityEngine.UI.Navigation{mode=UnityEngine.UI.Navigation.Mode.None};b.onClick.AddListener(()=>{if(Live)action();});return b;}
            close=Button("BranchClose","モドる",-110,Close);retry=Button("BranchRetry","モウイチド",-320,()=>{paused=false;Runtime.Reset();});pause=Button("BranchPause","トメル",-530,()=>{paused=!paused;Runtime.Suspend();Runtime.Session.Paused=paused;});
            var src=new UnityEngine.GameObject("BranchMotionSample",typeof(UnityEngine.SpriteRenderer),typeof(UnityEngine.Animator));src.transform.SetParent(transform,false);sample=src.GetComponent<UnityEngine.SpriteRenderer>();sample.enabled=false;src.GetComponent<UnityEngine.Animator>().enabled=false;
            Olca.Presentation.Effects.ProvisionalCombatGraphic Effect(string name){var g=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(Olca.Presentation.Effects.ProvisionalCombatGraphic));g.transform.SetParent(transform,false);var fx=g.GetComponent<Olca.Presentation.Effects.ProvisionalCombatGraphic>();fx.raycastTarget=false;fx.rectTransform.anchorMin=fx.rectTransform.anchorMax=new UnityEngine.Vector2(0,.21f);fx.enabled=false;return fx;}
            chargeFx=Effect("BranchCharge");hitFx=Box("BranchHit",UnityEngine.Color.white);hitFx.enabled=false;
            // 命中時だけ既存の透過火花を借用し、敵を隠す白い矩形を残さない。
            Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(assets,2,0);
            menu=new Olca.Unity.MenuPadScope(()=>Live){CancelOnly=true,SuppressPadCancel=true};menu.Bind(transform,close.name,Close);
            Runtime=new Olca.Unity.OutsideBranchRuntime441(()=>Live&&!paused&&focused);Render();
        }
        private void Update(){if(!Live){if(!disposed)Close();return;}Runtime.Pump(!paused&&focused&&UnityEngine.Application.isFocused);Render();}
        private void LateUpdate(){menu?.Tick();}
        private void OnApplicationFocus(bool value){focused=value;if(!value){paused=true;if(Runtime!=null){Runtime.Suspend();Runtime.Session.Paused=true;}}}
        private void Place(UnityEngine.UI.Image image,UnityEngine.Sprite sprite,float x,float y,int facing,bool authored)
        {
            image.sprite=sprite;if(sprite==null){image.enabled=false;return;}image.enabled=true;image.rectTransform.pivot=sprite.pivot/sprite.rect.size;image.rectTransform.sizeDelta=(UnityEngine.Vector2)sprite.bounds.size*pixels;image.rectTransform.anchoredPosition=new UnityEngine.Vector2(x,y);image.rectTransform.localScale=new UnityEngine.Vector3((authored?1:-1)*facing,1,1);
        }
        public void Render()
        {
            if(disposed||Runtime==null)return;var s=Runtime.Session;var core=s.Core;var r=(UnityEngine.RectTransform)transform;float width=r.rect.width,height=r.rect.height,viewport=width/pixels;
            var camera=Olca.Core.Logic.Integration.OutsideCameraPolicy.Follow;float center=camera.ResolveHorizontalCenter(core.Position,core.Definition.Width,viewport);
            float actorTop=UnityEngine.Mathf.Max(idle.bounds.max.y,UnityEngine.Mathf.Max(rise.bounds.max.y,UnityEngine.Mathf.Max(apex.bounds.max.y,fall.bounds.max.y)));float vertical=camera.ResolveVerticalOffset(core.Height,height/pixels,height*.21f/pixels,144/pixels,actorTop);
            float X(long x)=>(x/60000f-center+viewport*.5f)*pixels;float Y(long y)=>(y/60000f-vertical)*pixels;
            void Rect(UnityEngine.UI.Image im,float x,float y,float w,float h){im.rectTransform.anchoredPosition=new UnityEngine.Vector2(x,y);im.rectTransform.sizeDelta=new UnityEngine.Vector2(w,h);}
            Rect(floor,0,Y(0),width,height*.21f);Rect(platform,X(9*60000),Y(2*60000)-9,10*pixels,18);Rect(platformEdge,X(9*60000),Y(2*60000),10*pixels,3);
            Rect(gate,X(Olca.Core.Logic.Integration.OutsideBranchTrial441.GateX),Y(45000),12,1.5f*pixels);gate.enabled=!s.GateOpen;
            Rect(exit,X(18*60000),Y(0)+.7f*pixels,.12f*pixels,1.4f*pixels);exitLabel.rectTransform.anchoredPosition=new UnityEngine.Vector2(X(18*60000),Y(0)+1.8f*pixels);exitLabel.gameObject.SetActive(System.Math.Abs(core.Position-18*60000)>72000);
            upperLabel.gameObject.SetActive(false);
            bool danger=!s.Guard.IsDead&&(s.Phase==Olca.Core.Logic.Integration.OutsideGoblinPhase.Windup||s.Phase==Olca.Core.Logic.Integration.OutsideGoblinPhase.Strike);warning.enabled=warningTop.enabled=warningBottom.enabled=danger;warningLabel.gameObject.SetActive(danger);
            float mid=X(Olca.Core.Logic.Integration.OutsideBranchTrial441.GuardX+s.GuardFacing*Olca.Core.Logic.Integration.OutsideBranchTrial441.Reach/2);Rect(warning,mid,Y(45000),3*pixels,1.5f*pixels);Rect(warningTop,mid,Y(90000),3*pixels,4);Rect(warningBottom,mid,Y(0),3*pixels,4);warningLabel.rectTransform.anchoredPosition=new UnityEngine.Vector2(UnityEngine.Mathf.Clamp(mid,160,width-160),UnityEngine.Mathf.Max(140,Y(0)-50));
            var body=idle;if(!core.IsGrounded)body=System.Math.Abs(core.VerticalVelocity)<=Olca.Core.Logic.Integration.OutsideExplorationCore.ValidationGravity?apex:core.VerticalVelocity>0?rise:fall;else{var clip=core.IsMoving?moveClip:idleClip;if(clip!=null){clip.SampleAnimation(sample.gameObject,(float)(((core.IsMoving?core.MovementFrame:core.IdleFrame)/60d)%clip.length));body=sample.sprite??idle;}}
            UnityEngine.Sprite kick=null;if(s.Kick.IsActive)kick=combo.Resolve(s.Kick.Kind,s.Kick.ElapsedFrames,s.Kick.ActiveTiming,false);if(kick!=null)body=kick;
            Place(playerImage,body,X(core.Position),Y(core.Height),s.Kick.IsActive?s.Kick.AttackFacing:core.Facing,kick!=null);playerImage.color=s.Frame-s.LastHurtFrame<12?new UnityEngine.Color(1,.45f,.4f):UnityEngine.Color.white;
            int frame=s.Guard.IsDead?9:s.Frame-s.LastHitFrame<12?8:s.Phase==Olca.Core.Logic.Integration.OutsideGoblinPhase.Windup?4+System.Math.Min(2,s.Age/8):s.Phase==Olca.Core.Logic.Integration.OutsideGoblinPhase.Strike?7:(int)(s.Frame/10%4);var goblin=AnnexGoblinSprite.Resolve(assets,frame);
            Place(guardImage,goblin,X(Olca.Core.Logic.Integration.OutsideBranchTrial441.GuardX),Y(0),s.GuardFacing,true);guardImage.color=s.Guard.IsDead?new UnityEngine.Color(1,1,1,UnityEngine.Mathf.Clamp01(1-(s.Frame-s.DeathFrame)/18f)):UnityEngine.Color.white;
            // 実際の足元と実射程から描画。有効3frameだけで、接触/HPの判断には使わない。
            kickTrace442.rectTransform.anchoredPosition=new UnityEngine.Vector2(X(core.Position),Y(core.Height));kickTrace442.rectTransform.sizeDelta=new UnityEngine.Vector2(Olca.Core.Logic.Integration.OutsideKickTrialDefinition.Range/60000f,.5f)*pixels;kickTrace442.rectTransform.localScale=new UnityEngine.Vector3(s.Kick.IsActive?s.Kick.AttackFacing:core.Facing,1,1);kickTrace442.Project(s.Kick);
            guardLabel.text="モンバン（カリ） HP "+s.Guard.CurrentHp+" / 30";guardLabel.gameObject.SetActive(false);
            chargeFx.rectTransform.anchoredPosition=new UnityEngine.Vector2(X(core.Position),Y(core.Height)+1.1f*pixels);chargeFx.rectTransform.sizeDelta=new UnityEngine.Vector2(3.2f,3.2f)*pixels;if(s.IsCharging)chargeFx.ShowChargeParticles(s.Frame,s.ChargeFrames,60);else chargeFx.ClearChargeParticles();
            long hitAge=s.Frame-s.LastHitFrame;hitFx.enabled=hitAge>=0&&hitAge<12;hitFx.rectTransform.anchoredPosition=new UnityEngine.Vector2(X(Olca.Core.Logic.Integration.OutsideBranchTrial441.GuardX),Y(0)+.8f*pixels);hitFx.rectTransform.sizeDelta=new UnityEngine.Vector2(150,150);if(hitFx.enabled)hitFx.sprite=Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(assets,2,(int)hitAge/2);else hitFx.sprite=null;
            heading.rectTransform.sizeDelta=new UnityEngine.Vector2(UnityEngine.Mathf.Max(250,width-700),50);heading.rectTransform.anchoredPosition=new UnityEngine.Vector2((width-700)*.5f,-36);help.rectTransform.sizeDelta=new UnityEngine.Vector2(width-50,68);status.rectTransform.sizeDelta=new UnityEngine.Vector2(width-50,90);
            status.text="HP "+s.Player.CurrentHp+" /100  |  テキ "+s.Guard.CurrentHp+" /30  |  "+(s.Completed?(s.UsedUpper?"ウワミチで トウタツ":"シタミチで トウタツ")+" / モドるか モウイチド":s.Player.IsDead?"HP0 / モウイチド":paused?"テイシチュウ":s.Phase==Olca.Core.Logic.Integration.OutsideGoblinPhase.Recovery?"コウチョク / モンが アイています":s.Guard.IsDead?"ゲキハ / モンが アキました":"アカい ハンイに チュウイ");pause.GetComponentInChildren<UnityEngine.UI.Text>().text=paused?"サイカイ":"トメル";
        }
        public void Close(){if(closed)return;closed=true;DisposeTrial();gameObject.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(gameObject);else DestroyImmediate(gameObject);}
        private void DisposeTrial(){if(disposed)return;disposed=true;menu?.Dispose();menu=null;Runtime?.Dispose();combo.Dispose();if(playerImage!=null)playerImage.sprite=null;if(guardImage!=null)guardImage.sprite=null;assets?.Dispose();assets=null;if(owner!=null)owner.ReleaseBranch441(this);}
        private void OnDisable()=>Close();private void OnDestroy()=>DisposeTrial();
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>回廊の通常地上1段目だけ。有効frameの実射程を低い軌跡で投影し、命中は判断しない。</summary>
    public sealed class OutsideGroundKickTrace442 : UnityEngine.UI.MaskableGraphic
    {
        public int SampleFrame {get;private set;}
        public void Project(Olca.Core.Logic.Integration.OutsideNormalKickSequence kick)
        {
            bool visible=kick!=null&&kick.IsActive&&!kick.IsCharged&&kick.Kind==Olca.Core.Logic.Integration.OutsideKickKind.GroundFirst&&kick.IsHitWindow;
            enabled=visible;if(!visible)return;SampleFrame=kick.ElapsedFrames-kick.ActiveTiming.StartupFrames;SetVerticesDirty();
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear();if(!enabled)return;var r=rectTransform.rect;float opacity=SampleFrame==1?1:.75f;
            UnityEngine.Vector2 Point(float t)=>new UnityEngine.Vector2(UnityEngine.Mathf.Lerp(r.xMin,r.xMax,t),r.yMin+r.height*(.28f+.2f*UnityEngine.Mathf.Sin(t*UnityEngine.Mathf.PI)+SampleFrame*.025f));
            void Stroke(float thickness,UnityEngine.Color tint){
                for(int n=0;n<20;n++){
                    float a=.12f+.88f*n/20,b=.12f+.88f*(n+1)/20;var p=Point(a);var q=Point(b);var d=(q-p).normalized;var normal=new UnityEngine.Vector2(-d.y,d.x)*r.height*thickness*(.35f+.65f*UnityEngine.Mathf.Sin((a+b)*.5f*UnityEngine.Mathf.PI));
                    int k=vh.currentVertCount;UnityEngine.Vector2 Clamp(UnityEngine.Vector2 v)=>new UnityEngine.Vector2(UnityEngine.Mathf.Clamp(v.x,r.xMin,r.xMax),UnityEngine.Mathf.Clamp(v.y,r.yMin,r.yMax));
                    vh.AddVert(Clamp(p-normal),tint,UnityEngine.Vector2.zero);vh.AddVert(Clamp(p+normal),tint,UnityEngine.Vector2.zero);vh.AddVert(Clamp(q+normal),tint,UnityEngine.Vector2.zero);vh.AddVert(Clamp(q-normal),tint,UnityEngine.Vector2.zero);vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k+2,k+3,k);
                }
            }
            Stroke(.11f,new UnityEngine.Color(.015f,.14f,.22f,.9f*opacity));Stroke(.045f,new UnityEngine.Color(.65f,.96f,1,opacity));
        }
    }
}
