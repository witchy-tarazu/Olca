using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Integration;

namespace Olca.Presentation.Exploration
{
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
        }
        private void CreateMiniMap()
        {
            var go=new GameObject("OutsideMiniMap",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);miniMapPanel=(RectTransform)go.transform;miniMapPanel.anchorMin=miniMapPanel.anchorMax=new Vector2(0,1);miniMapPanel.anchoredPosition=new Vector2(255,-348);miniMapPanel.sizeDelta=new Vector2(430,96);var panel=go.GetComponent<Image>();panel.color=new Color(.005f,.03f,.045f,.97f);panel.raycastTarget=false;
            Text Label(string name,string text,Vector2 at,Vector2 size,int points){var obj=new GameObject(name,typeof(RectTransform),typeof(Text));obj.transform.SetParent(go.transform,false);var t=obj.GetComponent<Text>();t.font=gaugeLabel.font;t.text=text;t.fontSize=points;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.rectTransform.anchoredPosition=at;t.rectTransform.sizeDelta=size;return t;}
            Label("MiniMapTitle","ミニマップ（シヨウ）",new Vector2(0,35),new Vector2(410,24),14);Label("MiniMapLegend","P: ゲンザイチ / R: カイフク / 4worldグリッド（カリ）",new Vector2(0,-36),new Vector2(420,20),12);
            Image Tile(string name,Vector2 at,Vector2 size){var obj=new GameObject(name,typeof(RectTransform),typeof(Image));obj.transform.SetParent(go.transform,false);var image=obj.GetComponent<Image>();image.raycastTarget=false;image.rectTransform.anchoredPosition=at;image.rectTransform.sizeDelta=size;return image;}
            miniMapCells=new Image[24];for(int row=0;row<3;row++)for(int column=0;column<8;column++)miniMapCells[row*8+column]=Tile("MiniMapCell_"+column+"_"+row,new Vector2(-175+column*50,-16+row*16),new Vector2(48,14));
            Image Marker(string name,string text,Color color){var im=Tile(name,Vector2.zero,new Vector2(16,16));im.color=color;var label=Label(name+"Label",text,Vector2.zero,new Vector2(16,20),12);label.transform.SetParent(im.transform,false);return im;}
            miniMapRecovery=Marker("MiniMapRecovery","R",Color.gray);miniMapPlayer=Marker("MiniMapPlayer","P",new Color(.6f,.34f,.08f,1));
        }
        private void ClearMiniMap(){miniMapRevision=null;miniMapSource=null;miniMapData=null;miniMapShownRevision=long.MinValue;miniMapShownId=miniMapDrawId=null;miniMapDrawRevision=long.MinValue;miniMapDrawLeft=int.MinValue;if(miniMapPanel!=null)miniMapPanel.gameObject.SetActive(false);}
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
            bool charging=presentation.IsCharging;
            if(presentation!=null&&(presentation.IsChargedKick||charging&&
                (!presentation.Core.IsGrounded||!presentation.Core.IsMoving)&&!presentation.Core.IsWallSliding&&!presentation.Core.IsWallKicking)){
                bool ground=presentation.IsChargedKick?presentation.NormalKickKind<=OutsideKickKind.GroundThird:presentation.Core.IsGrounded;
                var pose=(trialChargeMotion??(trialChargeMotion=new TrialChargeMotion())).Resolve(ground,charging,
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
        private void ReleaseTraversalReferences(){trialBombMotion?.Dispose();trialBombMotion=null;slashSprites?.Dispose();slashSprites=null;trialChargeMotion?.Dispose();trialChargeMotion=null;trialGroundCombo?.Dispose();trialGroundCombo=null;trialFireBody?.Dispose();trialFireBody=null;ClearTraversalDisplay();DestroyGapImages();DestroyWaterImages();if(breathPanel!=null){if(UnityEngine.Application.isPlaying)Destroy(breathPanel.gameObject);else DestroyImmediate(breathPanel.gameObject);}breathPanel=breathFill=null;breathLabel=null;wallSlideFrames=null;traversalDashSprite=null;trialKickPrepare=trialKickContact=trialAirForwardContact=trialDashKickContact=null;trialComboSprites=trialAirDownSprites=null;traversalResources=null;renderedTraversal=null;wallImages.Clear();}
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
    /// <summary>所持/4枠の表示と操作だけ。保存は既存の進行サービスへ委譲する。</summary>
    /// <summary>登録済み1地点の移動確認。保存と時間停止はController/Sessionが所有する。</summary>
    public sealed class OutsideFastTravelView : MonoBehaviour
    {
        private ManagedScreenContext context;private Olca.Unity.MenuPadScope menu;private Button confirm;private Text message;private Func<bool> discovered,ownerAlive;private Action travel,close;private bool busy;private long renderEpoch;private readonly Dictionary<Button,Action> actions=new Dictionary<Button,Action>();
        private bool CanOperate=>this!=null&&isActiveAndEnabled&&context?.IsCurrent==true&&!context.DialogBlocksInput&&!busy&&ownerAlive?.Invoke()==true;
        public void Initialize(Font font,Func<bool> known,Action execute,Action closed,Func<bool> alive=null,Func<OutsideTrialMapOverview[]> maps=null,Action<string> acquire=null)
        {
            mapAcquire=acquire;mapSource=maps;mapFont=font;discovered=known;travel=execute;close=closed;ownerAlive=alive??(()=>true);var rect=(RectTransform)transform;rect.sizeDelta=new Vector2(1240,680);gameObject.AddComponent<Image>().color=new Color(.025f,.075f,.105f,.98f);
            Text Label(string name,string value,float y,int size){var go=new GameObject(name,typeof(RectTransform),typeof(Text));go.transform.SetParent(transform,false);var t=go.GetComponent<Text>();t.font=font;t.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value);t.color=Color.white;t.fontSize=size;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;t.rectTransform.anchoredPosition=new Vector2(0,y);t.rectTransform.sizeDelta=new Vector2(1140,150);return t;}
            Label("FastTravelTitle","ファストトラベル / シヨウ カイフクチテン",230,36);message=Label("FastTravelMessage","",50,28);
            Button Make(string name,string text,float x,Action action){var go=new GameObject(name,typeof(RectTransform),typeof(Image),typeof(Button));go.transform.SetParent(transform,false);var r=(RectTransform)go.transform;r.anchoredPosition=new Vector2(x,-200);r.sizeDelta=new Vector2(480,90);var b=go.GetComponent<Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(b);var t=Label(name+"Label",text,0,28);t.transform.SetParent(go.transform,false);t.rectTransform.anchoredPosition=Vector2.zero;t.rectTransform.sizeDelta=new Vector2(450,80);actions.Add(b,action);return b;}
            confirm=Make("ConfirmOutsideFastTravel","イドウする",-280,()=>{if(discovered?.Invoke()==true&&(mapSource==null||SelectedMap?.MapId==OutsideMapRevealTrial.HomeMapId))travel?.Invoke();});Make("CancelOutsideFastTravel","モドる",280,()=>close?.Invoke());
            if(mapSource!=null){
                rect.sizeDelta=new Vector2(1380,930);var title=transform.Find("FastTravelTitle").GetComponent<Text>();title.text="ゼンタイマップ（シヨウ）";title.rectTransform.anchoredPosition=new Vector2(0,390);title.rectTransform.sizeDelta=new Vector2(1260,70);
                message.rectTransform.anchoredPosition=new Vector2(0,-245);message.rectTransform.sizeDelta=new Vector2(1240,110);message.fontSize=22;
                foreach(var b in actions.Keys)((RectTransform)b.transform).anchoredPosition=new Vector2(((RectTransform)b.transform).anchoredPosition.x,-350);
                confirm.GetComponentInChildren<Text>().text="カイフクチテンへ イドウ";
                mapRate=Label("MapExplorationRate","",245,30);mapRate.rectTransform.sizeDelta=new Vector2(1240,60);
                var guide=Label("MapInputGuide","",-423,20);guide.rectTransform.sizeDelta=new Vector2(1250,40);mapGuide=guide;
                for(int index=0;index<2;index++){int captured=index;var b=Make("MapChoice_"+index,"",0,()=>SelectMap(captured));((RectTransform)b.transform).anchoredPosition=new Vector2(index==0?-300:300,310);((RectTransform)b.transform).sizeDelta=new Vector2(560,62);b.GetComponentInChildren<Text>().fontSize=22;mapChoices.Add(b);}
                if(mapAcquire!=null){acquireButton=Make("AcquireTrialMapItem","",0,()=>{var map=SelectedMap;if(map!=null&&!map.ItemRevealed)mapAcquire(map.MapId);});((RectTransform)acquireButton.transform).anchoredPosition=new Vector2(0,-150);((RectTransform)acquireButton.transform).sizeDelta=new Vector2(720,62);var label=acquireButton.GetComponentInChildren<Text>();label.fontSize=22;label.rectTransform.sizeDelta=new Vector2(700,56);}

            }
        }
        public void Open(ManagedScreenContext owner){context=owner;if(mapSource!=null){mapData=mapSource()??Array.Empty<OutsideTrialMapOverview>();selectedMap=Math.Max(0,Array.FindIndex(mapData,m=>m.IsCurrent));RenderMap();}long epoch=++renderEpoch;foreach(var pair in actions){var action=pair.Value;pair.Key.onClick=new Button.ButtonClickedEvent();pair.Key.onClick.AddListener(()=>{if(CanOperate&&renderEpoch==epoch&&ReferenceEquals(context,owner))action();});}gameObject.SetActive(true);transform.SetAsLastSibling();SetBusy(false,null);menu?.Dispose();menu=new Olca.Unity.MenuPadScope(()=>CanOperate);menu.Bind(transform,confirm.interactable?confirm.name:"CancelOutsideFastTravel",()=>close?.Invoke());}
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
            var legend=new GameObject("MapLegend",typeof(RectTransform),typeof(Text)).GetComponent<Text>();legend.transform.SetParent(root.transform,false);legend.font=mapFont;legend.fontSize=18;legend.color=new Color(.8f,.96f,1);legend.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana("P: ゲンザイチ / R: カイフクチテン（イドウは ジッホウモンご） / アカルいクカク: カイジずみ\n4worldグリッド（カリ） / シヨウリツ: タカラバコ・カイフクチテンの カイホウ");legend.alignment=TextAnchor.MiddleCenter;legend.raycastTarget=false;legend.rectTransform.sizeDelta=new Vector2(1220,70);legend.rectTransform.anchoredPosition=new Vector2(0,mapAcquire==null?-185:-160);
        }
        public void Hide(){renderEpoch++;menu?.Dispose();menu=null;var prior=context;context=null;if(this!=null)gameObject.SetActive(false);prior?.NotifyHidden();}
        private void RefreshMapGuide(){if(mapGuide!=null)mapGuide.text=GameplayPauseView.InputGuideText(Olca.Unity.UnityInputProvider.ActiveGamepadProfile,Olca.Unity.InputGuideActivity.Current==Olca.Unity.InputGuideDevice.Gamepad)+" / ポーズへ モドる";}
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
        private UnityEngine.UI.Text Text(string name,string value,float x,float y,float width,float height,int size){var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(content,false);var label=go.GetComponent<UnityEngine.UI.Text>();label.font=font;label.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value);label.fontSize=size;label.alignment=UnityEngine.TextAnchor.MiddleCenter;label.color=UnityEngine.Color.white;label.raycastTarget=false;var r=label.rectTransform;r.anchoredPosition=new UnityEngine.Vector2(x,y);r.sizeDelta=new UnityEngine.Vector2(width,height);return label;}
        private UnityEngine.UI.Button Button(string name,string text,float x,float y,float width,System.Action action){var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(content,false);var r=(UnityEngine.RectTransform)go.transform;r.anchoredPosition=new UnityEngine.Vector2(x,y);r.sizeDelta=new UnityEngine.Vector2(width,74);var b=go.GetComponent<UnityEngine.UI.Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(b);var label=Text(name+"Label",text,0,0,width-20,68,25);label.transform.SetParent(go.transform,false);label.rectTransform.anchoredPosition=UnityEngine.Vector2.zero;label.resizeTextForBestFit=true;label.resizeTextMinSize=18;label.resizeTextMaxSize=25;var captured=context;long capturedRender=renderEpoch;b.onClick.AddListener(()=>{if(b!=null&&b.isActiveAndEnabled&&CanOperate&&capturedRender==renderEpoch&&ReferenceEquals(context,captured))action();});return b;}
        private void Render()
        {
            renderEpoch++;menu?.Dispose();menu=null;
            if(content!=null){content.gameObject.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content.gameObject);else DestroyImmediate(content.gameObject);}
            var root=new UnityEngine.GameObject("OutsideItemContents",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));root.transform.SetParent(transform,false);content=root.transform;((UnityEngine.RectTransform)content).sizeDelta=new UnityEngine.Vector2(1540,920);root.GetComponent<UnityEngine.UI.Image>().color=new UnityEngine.Color(.025f,.075f,.105f,.98f);
            var data=session.OutsideInventorySnapshot;bool pending=session.HasPendingSave;
            Text("OutsideItemsTitle","タンサク アイテム / ショートカット",0,385,1400,65,40);
            Text("OutsideWallet","エイゾク ツウカ: "+data.Currency+"  / カード チョウカ トークンとはベツ",0,310,1400,50,25);
            for(int n=0;n<4;n++){
                int index=n;string id=data.Shortcuts[n];var item=definitions.SingleOrDefault(v=>v.Id==id);int count=data.Items.FirstOrDefault(v=>v.Id==id)?.Count??0;
                var button=Button("OutsideItemSlot_"+n,new[]{"1 / ウエ","2 / ミギ","3 / シタ","4 / ヒダリ"}[n]+"\n"+(id==null?"ミトウロク":item?.Name??"ミトウロクID")+"  "+count,-555+n*370,195,340,()=>{selectedSlot=index;Render();});button.interactable=!pending;if(index==selectedSlot)button.GetComponent<UnityEngine.UI.Image>().color=Olca.Presentation.Dungeon.UiLook.Chosen;
            }
            Text("OutsideItemsHelp","ワクをエラんで、ショジ アイテムをトウロク。ジュウフクはイレカエ。",0,100,1420,55,25);
            var known=data.Items.Where(v=>definitions.Any(d=>d.Id==v.Id)).ToArray();
            if(known.Length==0)Text("OutsideNoItems","トウロクできる ショジ アイテムはありません。",0,0,1400,60,28);
            // 1ページを限定し、長い所持一覧で下部操作を押し出さない。
            int pages=System.Math.Max(1,(known.Length+1)/2);page=System.Math.Min(page,pages-1);var rows=known.Skip(page*2).Take(2).ToArray();
            for(int i=0;i<rows.Length;i++){string id=rows[i].Id;var definition=definitions.Single(d=>d.Id==id);var button=Button("RegisterOutsideItem_"+i,definition.Name+"  / ショジ: "+rows[i].Count,0,-i*90,1240,()=>{session.TryAssignOutsideItem(selectedSlot,id,definitions);Render();});button.interactable=!pending;}
            if(pages>1){Button("OutsideItemPrevious","マエ",-350,-185,270,()=>{page--;Render();}).interactable=!pending&&page>0;Text("OutsideItemPage",(page+1)+" / "+pages,0,-185,300,55,25);Button("OutsideItemNext","ツギ",350,-185,270,()=>{page++;Render();}).interactable=!pending&&page+1<pages;}
            int unknown=data.Items.Count(v=>!definitions.Any(d=>d.Id==v.Id));
            Text("OutsideItemsStatus",useMessage??session.Error??(unknown>0?"ミトウロク ID "+unknown+"ケンは ホゾンのままホジしています。":"ショジスウ0でも トウロクをホジします。"),0,-260,1400,44,25);
            string selectedId=data.Shortcuts[selectedSlot];int remaining=cooldown?.Invoke(selectedId)??0;
            Button("UseOutsideItem","トウロク アイテムを シヨウ"+(remaining>0?" / "+((remaining+59L)/60)+"ビョウ":""),0,-330,1240,()=>{useMessage=useItem?.Invoke(selectedId);Render();}).interactable=!pending&&useItem!=null&&selectedId!=null&&remaining==0;
            Text("OutsideItemsScope","カリ: HP+25 / ドクケシ / HP+25とドクケシ / HP25%。CD: 3/2/3/3ビョウ。フクゴウ・25%はショップ。",0,55,1440,30,20);

            Button("ClearOutsideItemSlot","ワクをカラにする",-480,-410,420,()=>{session.TryAssignOutsideItem(selectedSlot,null,definitions);Render();}).interactable=!pending;
            Button("RetryOutsideItemsSave","ホゾンをサイシコウ",0,-410,420,()=>{session.Retry();Render();}).interactable=pending;
            Button("CloseOutsideItems","モドる",480,-410,420,()=>close?.Invoke());
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
                var mark=itemMarks[i]=factory.Label("OutsideItemMark_"+i,transform,UnityEngine.Vector2.zero,new UnityEngine.Vector2(x,96),new UnityEngine.Vector2(82,55));mark.fontSize=24;mark.raycastTarget=false;
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
        private readonly List<RectTransform> feedbackObstacles=new List<RectTransform>();
        public void ConfigureCombatFeedback(Olca.Presentation.Effects.CombatNumberStyle style)
        {
            trialNumberStyle=style;BindTrialTargetFeedback();
            if(feedbackCanvas==null&&style!=null)feedbackCanvas=gameObject.AddComponent<OutsideCombatFeedbackCanvas>();if(feedbackCanvas==null)return;var owner=context;var lease=presentation;
            feedbackCanvas.Bind(lease?.Player,style,()=>this!=null&&ReferenceEquals(context,owner)&&owner?.IsCurrent==true&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)lease.Core.Position/OutsideMovementDefinition.UnitsPerWorldUnit,(float)lease.Core.Height/OutsideMovementDefinition.UnitsPerWorldUnit+player.rectTransform.rect.height/pixelsPerWorldUnit+.2f),pixelsPerWorldUnit);
        }
        private void ClearCombatFeedback(){feedbackCanvas?.Unbind();feedbackObstacles.Clear();}
        private void RenderCombatFeedback(float camera,float verticalOffset)
        {
            if(feedbackCanvas==null||feedbackCanvas.ActiveCount==0)return;feedbackObstacles.Clear();if(mapNameLabel!=null&&mapNameLabel.isActiveAndEnabled&&mapNameLabel.color.a>0)feedbackObstacles.Add(mapNameLabel.rectTransform);if(recoveryPoint!=null)feedbackObstacles.Add((RectTransform)recoveryPoint.transform);if(reloadPoint!=null&&reloadPoint.gameObject.activeInHierarchy)feedbackObstacles.Add(reloadPoint);if(entrance!=null)feedbackObstacles.Add((RectTransform)entrance.transform);foreach(var slot in supplySlots)if(slot?.Button!=null)feedbackObstacles.Add((RectTransform)slot.Button.transform);
            if(trialTargetName!=null)feedbackObstacles.Add(trialTargetPlate);if(trialTargetGauge!=null&&trialTargetGauge.enabled)feedbackObstacles.Add(trialTargetGauge.rectTransform);if(secondTrialGauge!=null&&secondTrialGauge.enabled)feedbackObstacles.Add(secondTrialGauge.rectTransform);float floor=-((RectTransform)transform).rect.height*.29f;feedbackCanvas.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,floor+(world.y-verticalOffset)*pixelsPerWorldUnit),feedbackObstacles,mapNameLabel!=null&&mapNameLabel.isActiveAndEnabled&&mapNameLabel.color.a>0?mapNameLabel.rectTransform:null);
        }
    }
}

namespace Olca.Presentation.Exploration
{
    // World EnemyViewと同じ円/外周半円の配色と5度分割をOverlayへ適応する。生成Textureは不要。
    /// <summary>既存の即時効果を遅らせず、接触から12論理frameだけ振り抜く暫定表示。</summary>
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
        private Image secondTrialImage;private Text secondTrialHp;private OutsideTrialHpArc secondTrialGauge;private OutsideCombatFeedbackCanvas secondTrialFeedback;private Olca.Core.Domain.Enemy secondFeedbackTarget;
        private OutsideSwordTrailGraphic trialSwordTrail;private RectTransform trialTargetPlate;private Image trialTargetImage,trialSwordImage;private Text trialTargetName,trialTargetHp;private OutsideTrialHpArc trialTargetGauge;
        private Olca.Unity.GeneratedSpriteAssets.Lease trialSwordAssets;private OutsideCombatFeedbackCanvas trialTargetFeedback;private Olca.Core.Domain.Enemy feedbackTarget;
        private Olca.Presentation.Effects.CombatNumberStyle trialNumberStyle;private OutsideExplorationSession.PresentationLease feedbackLease;
        private Button trialEnemyAttackButton;
        private void InitializeTrialSword(Font font,Olca.Presentation.Field.GameFieldView field){
            var body=new GameObject("OutsideTrialTarget",typeof(RectTransform),typeof(Image));body.transform.SetParent(transform,false);trialTargetImage=body.GetComponent<Image>();trialTargetImage.sprite=field.AuthoredTrialEnemySprite;if(trialTargetImage.sprite==null)throw new InvalidOperationException("試用標的の既存素材がありません");trialTargetImage.raycastTarget=false;var rect=trialTargetImage.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(0,.21f);rect.pivot=trialTargetImage.sprite.pivot/trialTargetImage.sprite.rect.size;rect.sizeDelta=trialTargetImage.sprite.bounds.size*pixelsPerWorldUnit*.8f;rect.localScale=new Vector3(-1,1,1);
            var factory=new Olca.Presentation.Management.GameplayPanelFactory(transform,font,null);trialTargetName=factory.Label("OutsideTrialTargetName",transform,new Vector2(0,.21f),Vector2.zero,new Vector2(300,40));trialTargetName.font=field.AuthoredTrialEnemyFont??font;trialTargetName.fontSize=18;trialTargetName.alignment=TextAnchor.MiddleCenter;trialTargetName.color=Color.white;trialTargetName.raycastTarget=false;var plate=new GameObject("OutsideTrialTargetNamePlate",typeof(RectTransform),typeof(Image));plate.transform.SetParent(transform,false);var pr=(RectTransform)plate.transform;pr.anchorMin=pr.anchorMax=new Vector2(0,.21f);pr.sizeDelta=new Vector2(316,48);plate.GetComponent<Image>().color=new Color(.025f,.07f,.1f,.9f);plate.GetComponent<Image>().raycastTarget=true;trialEnemyAttackButton=plate.AddComponent<Button>();trialEnemyAttackButton.targetGraphic=plate.GetComponent<Image>();trialEnemyAttackButton.navigation=new Navigation{mode=Navigation.Mode.None};var colors=trialEnemyAttackButton.colors;colors.normalColor=new Color(.025f,.07f,.1f,.9f);colors.highlightedColor=colors.selectedColor=new Color(.06f,.2f,.25f,1);colors.pressedColor=new Color(.02f,.09f,.12f,1);colors.disabledColor=new Color(.025f,.07f,.1f,.6f);trialEnemyAttackButton.colors=colors;pr.SetSiblingIndex(trialTargetName.transform.GetSiblingIndex());trialTargetPlate=pr;
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
        private float TrialTargetTop()=>Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(trialTargetImage.sprite).size.y*pixelsPerWorldUnit*.8f;
        private void BindTrialTargetFeedback(){if(trialTargetFeedback==null||trialNumberStyle==null||presentation?.IsCurrent!=true||presentation.Core.TrialMapId!=null)return;var lease=presentation;var target=lease.TrialTarget;if(ReferenceEquals(feedbackTarget,target)&&ReferenceEquals(secondFeedbackTarget,lease.SecondTrialTarget)&&ReferenceEquals(feedbackLease,lease))return;feedbackTarget=target;feedbackLease=lease;trialEnemyAttackButton.onClick=new Button.ButtonClickedEvent();trialEnemyAttackButton.onClick.AddListener(()=>{if(this!=null&&IsOpen&&CanPlay&&ReferenceEquals(presentation,lease)&&lease.IsCurrent){lease.ToggleTrialEnemyAttack();var es=UnityEngine.EventSystems.EventSystem.current;if(es!=null&&es.currentSelectedGameObject==trialEnemyAttackButton.gameObject)es.SetSelectedGameObject(null);}});secondFeedbackTarget=lease.SecondTrialTarget;secondTrialFeedback.BindEnemy(secondFeedbackTarget,trialNumberStyle,()=>this!=null&&IsOpen&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)OutsideKickTrialDefinition.SecondTargetX/OutsideMovementDefinition.UnitsPerWorldUnit,TrialTargetTop()/pixelsPerWorldUnit+.5f),pixelsPerWorldUnit);trialTargetFeedback.BindEnemy(target,trialNumberStyle,()=>this!=null&&IsOpen&&ReferenceEquals(presentation,lease)&&lease.IsCurrent,()=>new Vector2((float)OutsideExplorationSession.TrialTargetX/OutsideMovementDefinition.UnitsPerWorldUnit,TrialTargetTop()/pixelsPerWorldUnit+.5f),pixelsPerWorldUnit);}
        private readonly List<Image> bombImages=new List<Image>();
        private Olca.Unity.GeneratedSpriteAssets.Lease bombAssets;
        private void ClearBombVisuals(){foreach(var image in bombImages)if(image!=null){image.enabled=false;image.sprite=null;}bombAssets?.Dispose();bombAssets=null;}
        private void RenderBombs(float camera,float viewport,float verticalOffset)
        {
            if(presentation?.IsCurrent!=true||presentation.Core.TrialMapId!=null){ClearBombVisuals();return;}
            var flights=presentation.Bombs;var blasts=presentation.BombBlasts;int used=0;long frame=presentation.Core.Frame;
            // 飛翔開始時に一度だけ用意し、着弾frameで画像decode/画素変換を行わない。
            if(bombAssets==null&&(flights.Count>0||blasts.Count>0)){bombAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,out _,bombAssets);}
            Image Part(string key,float x,float y,float visibleWidth,float visibleHeight,float alpha,float angle=0,(long Left,long Right)? clip=null)
            {
                if(used==bombImages.Count){var wrapper=new GameObject("TrialOutsideBombClip",typeof(RectTransform),typeof(RectMask2D));wrapper.transform.SetParent(transform,false);var wr=wrapper.GetComponent<RectTransform>();wr.anchorMin=wr.anchorMax=new Vector2(0,.21f);var go=new GameObject("TrialOutsideBombVisual",typeof(RectTransform),typeof(Image));go.transform.SetParent(wrapper.transform,false);var created=go.GetComponent<Image>();created.raycastTarget=false;created.rectTransform.anchorMin=created.rectTransform.anchorMax=new Vector2(.5f,.5f);bombImages.Add(created);}
                var image=bombImages[used++];Sprite sprite;bool found=Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(key,out sprite,bombAssets??(bombAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this)));image.sprite=found?sprite:null;image.enabled=found;image.color=new Color(1,1,1,alpha);
                if(found){float aw=key==Olca.Presentation.Field.SharedAttackPropCatalog.Bomb?Olca.Presentation.Field.SharedAttackPropCatalog.BombVisibleHeight:Olca.Presentation.Field.SharedAttackPropCatalog.BlastVisibleWidth;float ah=key==Olca.Presentation.Field.SharedAttackPropCatalog.Bomb?Olca.Presentation.Field.SharedAttackPropCatalog.BombVisibleHeight:Olca.Presentation.Field.SharedAttackPropCatalog.BlastVisibleHeight;image.rectTransform.sizeDelta=new Vector2(sprite.bounds.size.x*visibleWidth/aw,sprite.bounds.size.y*visibleHeight/ah)*pixelsPerWorldUnit;}
                var parent=(RectTransform)image.transform.parent;var mask=parent.GetComponent<RectMask2D>();mask.enabled=clip.HasValue;
                float half=image.rectTransform.sizeDelta.x/(2*pixelsPerWorldUnit),left=x-half,right=x+half;
                if(clip.HasValue){left=Mathf.Max(left,clip.Value.Left/(float)OutsideMovementDefinition.UnitsPerWorldUnit);right=Mathf.Min(right,clip.Value.Right/(float)OutsideMovementDefinition.UnitsPerWorldUnit);}
                image.enabled&=right>left;float center=(left+right)*.5f;
                parent.anchoredPosition=new Vector2((center-camera+viewport*.5f)*pixelsPerWorldUnit,(y-verticalOffset)*pixelsPerWorldUnit);parent.sizeDelta=new Vector2(Mathf.Max(0,right-left)*pixelsPerWorldUnit,image.rectTransform.sizeDelta.y);
                image.rectTransform.anchoredPosition=new Vector2((x-center)*pixelsPerWorldUnit,0);image.rectTransform.localRotation=Quaternion.Euler(0,0,angle);return image;
            }
            foreach(var flight in flights){float age=frame-flight.FiredAt;float target=flight.Launch.ImpactX/(float)OutsideMovementDefinition.UnitsPerWorldUnit;
                // 着弾予定だけの薄い目印。ダメージはSessionの90frame到達時に一度だけ発生する。
                if(!flight.Launch.Dissipates){var clip=OutsideBombOcclusion.HorizontalBounds(presentation.Core.TraversalProfile?.Walls,flight.Launch.X,flight.Launch.ImpactX,flight.Launch.ImpactHeight);for(int i=-1;i<=1;i++)Part(Olca.Presentation.Field.SharedAttackPropCatalog.Blast,target+i,flight.Launch.ImpactHeight/(float)OutsideMovementDefinition.UnitsPerWorldUnit+.38f,.9f,.18f,.18f,clip:clip);}
                var visible=TrialBombThrowMotion.FlightPosition(flight,frame);
                Part(Olca.Presentation.Field.SharedAttackPropCatalog.Bomb,visible.x,visible.y,.5f,.5f,1f,-flight.Launch.Facing*360f*age/OutsideBombLaunchData.FlightFrames);
            }
            foreach(var blast in blasts){float alpha=.9f*(1f-(frame-blast.Frame)/(float)OutsideBombLaunchData.BlastFrames);var clip=OutsideBombOcclusion.HorizontalBounds(presentation.Core.TraversalProfile?.Walls,blast.SourceX,blast.X,blast.Height);for(int i=-1;i<=1;i++)Part(Olca.Presentation.Field.SharedAttackPropCatalog.BlastImpact,blast.X/(float)OutsideMovementDefinition.UnitsPerWorldUnit+i,blast.Height/(float)OutsideMovementDefinition.UnitsPerWorldUnit+.38f,.9f,.35f,alpha,clip:clip);}
            for(int i=used;i<bombImages.Count;i++)if(bombImages[i]!=null)bombImages[i].enabled=false;
        }
        private readonly List<Image> initialShotImages=new List<Image>();
        private void RenderInitialShots(float camera,float viewport,float verticalOffset){
            var shots=presentation.InitialShots;
            for(int i=0;i<shots.Count;i++){
                if(i>=initialShotImages.Count){var go=new GameObject("TrialStraightShot",typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var created=go.GetComponent<Image>();created.raycastTarget=false;created.color=new Color(.45f,.95f,1,.95f);created.rectTransform.anchorMin=created.rectTransform.anchorMax=new Vector2(0,.21f);initialShotImages.Add(created);}
                var shot=shots[i];var image=initialShotImages[i];image.enabled=true;image.rectTransform.sizeDelta=new Vector2(.35f,.1f)*pixelsPerWorldUnit;image.rectTransform.anchoredPosition=new Vector2(((float)shot.Position(presentation.Core.Frame)/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit,((float)shot.Height/OutsideMovementDefinition.UnitsPerWorldUnit+1.2f-verticalOffset)*pixelsPerWorldUnit);
            }
            for(int i=shots.Count;i<initialShotImages.Count;i++)if(initialShotImages[i]!=null)initialShotImages[i].enabled=false;
        }
        private void RenderTrialSword(float camera,float viewport,float verticalOffset){
            if(trialTargetImage==null)return;
            RenderInitialShots(camera,viewport,verticalOffset);RenderBombs(camera,viewport,verticalOffset);
            bool combat=presentation.Core.TrialMapId==null;foreach(var graphic in new Graphic[]{trialTargetImage,trialTargetName,trialTargetHp,trialTargetGauge,secondTrialImage,secondTrialHp,secondTrialGauge})if(graphic!=null)graphic.enabled=combat;
            trialTargetPlate.gameObject.SetActive(combat);if(!combat){ClearTrialSword();return;}BindTrialTargetFeedback();var core=presentation.Core;var target=presentation.TrialTarget;float x=((float)OutsideExplorationSession.TrialTargetX/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit;float y=-verticalOffset*pixelsPerWorldUnit;trialTargetImage.rectTransform.anchoredPosition=new Vector2(x,y-Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(trialTargetImage.sprite).min.y*pixelsPerWorldUnit*.8f);trialTargetImage.enabled=!target.IsDead;
            float top=TrialTargetTop()+44.55f+y;trialTargetHp.rectTransform.anchoredPosition=new Vector2(x,top);trialTargetHp.text=target.CurrentHp.ToString();trialTargetHp.enabled=!target.IsDead;trialTargetGauge.rectTransform.anchoredPosition=new Vector2(x,top);trialTargetGauge.SetFraction(target.CurrentHp/(float)target.MaxHp);trialTargetGauge.enabled=!target.IsDead;trialTargetName.rectTransform.anchoredPosition=new Vector2(x+210,top+50);
            trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;
            var second=presentation.SecondTrialTarget;float sx=((float)OutsideKickTrialDefinition.SecondTargetX/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit;
            secondTrialImage.rectTransform.anchoredPosition=new Vector2(sx,y-Olca.Presentation.Effects.DefensiveFieldVisual.GetLocalBodyBounds(secondTrialImage.sprite).min.y*pixelsPerWorldUnit*.8f);secondTrialImage.enabled=!second.IsDead;secondTrialHp.rectTransform.anchoredPosition=new Vector2(sx,top);secondTrialHp.text=second.CurrentHp.ToString();secondTrialHp.enabled=!second.IsDead;secondTrialGauge.rectTransform.anchoredPosition=new Vector2(sx,top);secondTrialGauge.SetFraction(second.CurrentHp/(float)second.MaxHp);secondTrialGauge.enabled=!second.IsDead;
            trialTargetName.lineSpacing=presentation.TrialContactDamageEnabled?1.5f:1f;
            trialTargetName.rectTransform.sizeDelta=new Vector2(300,presentation.TrialContactDamageEnabled?60:40);trialTargetPlate.sizeDelta=new Vector2(316,presentation.TrialContactDamageEnabled?68:48);
            trialTargetName.text=presentation.TrialContactDamageEnabled?((target.IsDead&&second.IsDead)?"カイフクデ サイセイ":((target.IsDead||second.IsDead)?"1タイ":"2タイ")+" / セッショク10（カリ）\n"+(target.IsDead?"AIコウゲキOFF":presentation.TrialEnemyAttackEnabled?"AIコウゲキON / クリック":"AIコウゲキOFF / クリック")):target.IsDead?"カイフクデ サイセイ":presentation.TrialEnemyAttackEnabled?"コウゲキON / クリック（カリ）":"コウゲキOFF / クリック（カリ）";trialEnemyAttackButton.interactable=CanPlay&&!target.IsDead;trialTargetImage.color=presentation.TrialEnemyAttackEnabled&&presentation.TrialEnemyWindupFrames<=12?new Color(1,.45f,.45f):Color.white;trialTargetName.rectTransform.anchoredPosition=new Vector2(x+210,top+100);trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;PlaceTrialTargetName();
            var strike=presentation.TrialStrike;float swordAngle=0,swordAlpha=0;bool show=strike!=null&&OutsideSwordTrailGraphic.TrySample(core.Frame>=strike.Frame?core.Frame-strike.Frame:-1,strike.Facing,out swordAngle,out swordAlpha);trialSwordImage.enabled=show;trialSwordTrail.enabled=show;if(show){if(trialSwordAssets==null)trialSwordAssets=Olca.Unity.GeneratedSpriteAssets.Acquire(this);if(trialSwordImage.sprite==null){if(!Olca.Presentation.Field.SharedAttackPropCatalog.TryGetSprite(Olca.Presentation.Field.SharedAttackPropCatalog.Sword,out var sprite,trialSwordAssets))throw new InvalidOperationException("共用ソード素材がありません");trialSwordImage.sprite=sprite;trialSwordImage.rectTransform.pivot=sprite.pivot/sprite.rect.size;trialSwordImage.rectTransform.sizeDelta=sprite.bounds.size*pixelsPerWorldUnit;}trialSwordImage.rectTransform.anchoredPosition=new Vector2(((float)strike.X/OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f+.35f*strike.Facing)*pixelsPerWorldUnit,((float)strike.Y/OutsideMovementDefinition.UnitsPerWorldUnit+1.2f-verticalOffset)*pixelsPerWorldUnit);float tip=(float)(strike.Reach>0?strike.Reach:OutsideExplorationSession.TrialSwordRange)/OutsideMovementDefinition.UnitsPerWorldUnit-.35f;float scale=tip/trialSwordImage.sprite.bounds.max.x;trialSwordImage.rectTransform.localScale=new Vector3(strike.Facing*scale,scale*(strike.VerticalScale>0?strike.VerticalScale:1),1);trialSwordImage.rectTransform.localRotation=Quaternion.Euler(0,0,swordAngle);trialSwordImage.color=new Color(1,1,1,swordAlpha);trialSwordTrail.rectTransform.anchoredPosition=trialSwordImage.rectTransform.anchoredPosition;trialSwordTrail.rectTransform.localScale=new Vector3(strike.Facing,strike.VerticalScale>0?strike.VerticalScale:1,1);trialSwordTrail.SetPose(swordAngle*strike.Facing,swordAlpha,tip*pixelsPerWorldUnit);}
            PaintSecondTrialFeedback(camera,verticalOffset);
            if(trialTargetFeedback.ActiveCount>0){var obstacles=TrialNumberObstacles();trialTargetFeedback.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,-((RectTransform)transform).rect.height*.29f+(world.y-verticalOffset)*pixelsPerWorldUnit),obstacles,TrialNumberProtectedOverlay());}
        }
        // 試用標的の名札だけを横へ避け、主人公の頭と体を隠さない。標的・HP・判定は動かさない。
        private void PlaceTrialTargetName()
        {
            float shift=EntranceTitleOffset(BoundsInView(trialTargetPlate),BoundsInView(player.rectTransform),((RectTransform)transform).rect);
            trialTargetName.rectTransform.anchoredPosition+=new Vector2(shift,0);
            trialTargetPlate.anchoredPosition=trialTargetName.rectTransform.anchoredPosition;
        }
        private List<RectTransform> TrialNumberObstacles()=>new List<RectTransform>{trialTargetPlate,trialTargetGauge.rectTransform,secondTrialGauge.rectTransform,entrance!=null?(RectTransform)entrance.transform:null,reloadPoint,mapNameLabel!=null?mapNameLabel.rectTransform:null};
        private RectTransform TrialNumberProtectedOverlay()=>mapNameLabel!=null&&mapNameLabel.isActiveAndEnabled&&mapNameLabel.color.a>0?mapNameLabel.rectTransform:null;
        private void PaintSecondTrialFeedback(float camera,float verticalOffset){if(secondTrialFeedback?.ActiveCount>0)secondTrialFeedback.Paint(world=>new Vector2((world.x-camera)*pixelsPerWorldUnit,-((RectTransform)transform).rect.height*.29f+(world.y-verticalOffset)*pixelsPerWorldUnit),TrialNumberObstacles(),TrialNumberProtectedOverlay());}
        private void ClearTrialSword(){ClearBombVisuals();foreach(var image in initialShotImages)if(image!=null)image.enabled=false;if(trialEnemyAttackButton!=null){trialEnemyAttackButton.onClick.RemoveAllListeners();trialEnemyAttackButton.interactable=false;}if(trialSwordTrail!=null)trialSwordTrail.enabled=false;trialTargetFeedback?.Unbind();secondTrialFeedback?.Unbind();secondFeedbackTarget=null;feedbackTarget=null;feedbackLease=null;trialNumberStyle=null;if(trialSwordImage!=null){trialSwordImage.enabled=false;trialSwordImage.sprite=null;}trialSwordAssets?.Dispose();trialSwordAssets=null;}
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
            var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Text));go.transform.SetParent(content,false);var text=go.GetComponent<UnityEngine.UI.Text>();text.font=font;text.text=Olca.Presentation.CustomScreen.CustomDisplay.Katakana(value);text.fontSize=size;text.alignment=UnityEngine.TextAnchor.MiddleCenter;text.color=UnityEngine.Color.white;text.raycastTarget=false;text.rectTransform.anchoredPosition=new UnityEngine.Vector2(x,y);text.rectTransform.sizeDelta=new UnityEngine.Vector2(width,height);return text;
        }
        private UnityEngine.UI.Button Button(string name,string text,float x,float y,float width,System.Action action)
        {
            var go=new UnityEngine.GameObject(name,typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image),typeof(UnityEngine.UI.Button));go.transform.SetParent(content,false);var rect=(UnityEngine.RectTransform)go.transform;rect.anchoredPosition=new UnityEngine.Vector2(x,y);rect.sizeDelta=new UnityEngine.Vector2(width,90);var button=go.GetComponent<UnityEngine.UI.Button>();Olca.Presentation.Dungeon.UiLook.MenuButton(button);var label=Text(name+"Label",text,0,0,width-30,84,26);label.transform.SetParent(go.transform,false);var owner=context;long epoch=renderEpoch;button.onClick.AddListener(()=>{if(button!=null&&button.isActiveAndEnabled&&button.IsInteractable()&&CanOperate&&ReferenceEquals(context,owner)&&epoch==renderEpoch)action();});return button;
        }
        private void Attempt(bool funds)
        {
            if(attemptFrame==frame())return;attemptFrame=frame();
            var chosen=shop.Offers.SingleOrDefault(v=>v.Id==selected);bool overflow=chosen?.CardDefinition!=null&&session.OutsideShopOwnedCount(chosen)>=4;
            bool success=funds?session.TryClaimOutsideShopTrialFunds(out message):session.TryPurchaseOutsideShop(shop,selected,out message);if(success)message=funds?"シヨウ シキン"+Olca.Core.Domain.OutsideShopDefinition.TrialFunds+"を ウケトリ・ホゾンしました":(chosen.CardDefinition!=null?(overflow?"カード ジョウゲン / "+chosen.OverflowTokens+"トークンへ ヘンカン・ホゾン":"カード1マイ コウニュウ・ホゾン / ヘンセイチテンで ヘンセイ"):shop.Offers.Single(v=>v.Id==selected).AccessoryId!=null?"ソウショクヒンを コウニュウ・ホゾン / ソウビ・パーツで ソウビ":shop.Offers.Single(v=>v.Id==selected).PartId!=null?"パーツを コウニュウ・ホゾン / ポーズの ソウビ・パーツで ハイチ":chosen.UnlockItemId!=null?"カイホウアイテムを コウニュウ・ホゾン / イリグチの マスカイホウで シヨウ":"1コ コウニュウ・ホゾンしました");Render();
        }
        private void ChangePage(int delta){page=System.Math.Max(0,System.Math.Min((shop.Offers.Count-1)/PageSize,page+delta));selected=message=null;Render();}
        private void Render()
        {
            bool paged=shop.Offers.Count>PageSize;var offers=shop.Offers.Skip(page*PageSize).Take(PageSize).ToArray();int count=offers.Length;
            renderEpoch++;menu?.Dispose();menu=null;if(content!=null){content.gameObject.SetActive(false);if(UnityEngine.Application.isPlaying)Destroy(content.gameObject);else DestroyImmediate(content.gameObject);}
            var go=new UnityEngine.GameObject("OutsideShopContents",typeof(UnityEngine.RectTransform),typeof(UnityEngine.UI.Image));go.transform.SetParent(transform,false);content=go.transform;((UnityEngine.RectTransform)content).sizeDelta=new UnityEngine.Vector2(1480,paged?980:850);go.GetComponent<UnityEngine.UI.Image>().color=new UnityEngine.Color(.025f,.075f,.105f,.98f);
            var inventory=session.OutsideInventorySnapshot;Text("OutsideShopTitle","シヨウ ショップ（カリ）",0,340,1400,65,38);Text("OutsideShopWallet","エイゾク ツウカ: "+inventory.Currency+" / カード チョウカ トークンとはベツ",0,270,1380,55,25);
            bool many=paged||count>6,three=!paged&&count==3,four=paged||count>=4;int offerColumns=paged||count>=5?3:2;float offerWidth=paged||three||count>=5?440:640;
            for(int i=0;i<count;i++){var offer=offers[i];int ownedCount=session.OutsideShopOwnedCount(offer);string price=(offer.CardDefinition!=null?"カード1マイ ":offer.AccessoryId!=null?"ソウショクヒン ":offer.PartId!=null?"パーツ 1コ ":offer.UnlockItemId!=null?"カイホウ 1コ ":"1コ ")+offer.Price+"ツウカ";string owned="ショジ "+ownedCount+(!offer.IsEquipment?" / "+offer.Maximum:"");string stock=offer.PurchaseLimit==0?"ザイコ ムセイゲン":"ザイコ "+session.OutsideShopRemaining(shop,offer.Id)+" / "+offer.PurchaseLimit+"（ホジュウなし）";var button=Button("OutsideShopOffer_"+offer.Id,offer.Name+(three||four?"\n":" / ")+price+"\n"+owned+"\n"+stock,three?(i-1)*475:four?(i%offerColumns-(offerColumns-1)*.5f)*(offerColumns==3?475:700):i==0?-350:350,paged?135-(i/offerColumns)*120:many?200-(i/offerColumns)*120:four?170-(i/offerColumns)*140:155,offerWidth,()=>{selected=offer.Id;message=null;Render();});((UnityEngine.RectTransform)button.transform).sizeDelta=new UnityEngine.Vector2(offerWidth,many?112:four?132:three?136:118);var label=button.GetComponentInChildren<UnityEngine.UI.Text>();label.fontSize=many?18:three||four?20:23;label.rectTransform.sizeDelta=new UnityEngine.Vector2(offerWidth-30,many?106:four?126:three?130:112);if(selected==offer.Id)button.GetComponent<UnityEngine.UI.Image>().color=Olca.Presentation.Dungeon.UiLook.Chosen;}
            if(paged){var previous=Button("OutsideShopPrevious","マエ",-560,220,220,()=>ChangePage(-1));previous.interactable=page>0;var next=Button("OutsideShopNext","ツギ",560,220,220,()=>ChangePage(1));next.interactable=(page+1)*PageSize<shop.Offers.Count;foreach(var control in new[]{previous,next})((UnityEngine.RectTransform)control.transform).sizeDelta=new UnityEngine.Vector2(220,44);Text("OutsideShopPage",$"ショウヒン {page+1} / {(shop.Offers.Count+PageSize-1)/PageSize}",0,220,600,40,23);}
            bool canBuy=session.CanPurchaseOutsideShop(shop,selected,out var error);var chosen=shop.Offers.SingleOrDefault(v=>v.Id==selected);Text("OutsideShopSelection",chosen==null?"ショウヒンを エラんでください":chosen.Name+" / コウニュウゴ "+(canBuy?(inventory.Currency-chosen.Price).ToString():"—")+"ツウカ",0,paged?-205:many?-130:four?-78:45,1380,many?45:four?55:60,27);
            Text("OutsideShopStatus",message??(chosen==null?"コウニュウで ツウカとショウヒンを イッカツ ホゾン":canBuy?(chosen.CardDefinition!=null?"カード ジョウゲン4マイ / チョウカは "+chosen.OverflowTokens+"トークンへ ヘンカン":"1カイのコウニュウで1コ。キンガク・シキン・ハイチはカリ。"):error),0,paged?-250:many?-180:four?-137:-20,1380,many?45:four?55:65,four?22:24);
            var buy=Button("OutsideShopBuy","コウニュウする",0,paged?-305:many?-240:four?-210:-105,520,()=>Attempt(false));buy.interactable=canBuy;if(four)((UnityEngine.RectTransform)buy.transform).sizeDelta=new UnityEngine.Vector2(520,70);
            var fundsButton=Button("OutsideShopFunds","シヨウ シキン"+Olca.Core.Domain.OutsideShopDefinition.TrialFunds+" / 1カイだけ",-350,paged?-380:many?-330:four?-305:-245,640,()=>Attempt(true));fundsButton.interactable=!session.HasClaimedOutsideSupply(Olca.Core.Domain.OutsideShopDefinition.TrialFundsId)&&!session.HasPendingSave;
            var backButton=Button("OutsideShopBack","モドる（コウニュウずみは ホゾンずみ）",350,paged?-380:many?-330:four?-305:-245,640,()=>closed?.Invoke());
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
            chargeRing.enabled=false;
            if(presentation.IsCharging){
                var chargeCore=presentation.Core;float chargeUnit=OutsideMovementDefinition.UnitsPerWorldUnit;
                float chargeX=(chargeCore.Position/chargeUnit-camera+viewport*.5f)*pixelsPerWorldUnit;
                float chargeY=(chargeCore.Height/chargeUnit-verticalOffset+1.15f)*pixelsPerWorldUnit;
                float fraction=Mathf.Clamp01(presentation.ChargeFrames/(float)Mathf.Max(1,presentation.ChargeRequiredFrames));bool full=fraction>=1;
                var tint=Olca.Presentation.Effects.ProvisionalPixelPattern.Tint(Olca.Presentation.Effects.TrialFireKamuiVisual.IsActive(presentation.Player));
                chargeRing.rectTransform.anchoredPosition=new Vector2(chargeX,chargeY);chargeRing.rectTransform.sizeDelta=Vector2.one*(full?1.8f:1.45f)*pixelsPerWorldUnit;
                chargeRing.Arc(2,fraction,full?1:.65f,tint,chargeCore.Facing<0);chargeRing.enabled=true;kickTrail.enabled=false;
                kickCaption.text=full?"カンリョウ / ハナス（カリ）":"タメチュウ（カリ）";kickCaptionKey=-1;kickCaptionPlate.gameObject.SetActive(true);
                float chargeWidth=((RectTransform)transform).rect.width;kickCaptionPlate.anchoredPosition=new Vector2(Mathf.Clamp(chargeX,182,Mathf.Max(182,chargeWidth-182)),(chargeCore.Height/chargeUnit-verticalOffset)*pixelsPerWorldUnit-30);return;
            }
            if(!kind.HasValue||timing==null||presentation.Player==null){kickTrail.enabled=false;kickCaptionPlate.gameObject.SetActive(false);kickCaptionKey=-1;return;}
            var core=presentation.Core;bool hit=presentation.IsKickHitWindow,recovery=presentation.IsKickRecovery;int elapsed=presentation.KickElapsedFrames;long low,high;if(presentation.IsChargedKick)OutsideChargeKickData.Bounds(kind.Value<=OutsideKickKind.GroundThird,out low,out high);else OutsideKickTrialDefinition.Bounds(kind.Value,out low,out high);
            float unit=OutsideMovementDefinition.UnitsPerWorldUnit,range=(kind.Value==OutsideKickKind.Dash?OutsideKickTrialDefinition.DashRange:OutsideKickTrialDefinition.Range)/unit;int facing=presentation.NormalKickFacing??core.Facing;
            float center=(core.Position/unit+facing*range*.5f-camera+viewport*.5f)*pixelsPerWorldUnit,y=(core.Height/unit+(low+high)*.5f/unit-verticalOffset)*pixelsPerWorldUnit;
            kickTrail.rectTransform.anchoredPosition=new Vector2(center,y);kickTrail.rectTransform.sizeDelta=new Vector2(range*pixelsPerWorldUnit,(high-low)/unit*pixelsPerWorldUnit);
            float alpha=hit?1:recovery?.4f*Mathf.Clamp01(1-(elapsed-timing.StartupFrames-timing.ActiveFrames)/(float)Mathf.Max(1,timing.RecoveryFrames)):0;
            int frame=Olca.Presentation.Effects.TrialSlashSprites.FrameIndex(hit?elapsed-timing.StartupFrames:-1,timing.ActiveFrames,recovery?elapsed-timing.StartupFrames-timing.ActiveFrames:-1,timing.RecoveryFrames);
            int row=presentation.IsChargedKick?3:kind.Value==OutsideKickKind.GroundSecond?1:kind.Value==OutsideKickKind.GroundThird?2:0;
            kickTrail.sprite=(slashSprites??(slashSprites=new Olca.Presentation.Effects.TrialSlashSprites())).Resolve(row,frame);kickTrail.color=new Color(1,1,1,hit?1:alpha);kickTrail.rectTransform.localScale=new Vector3(facing,1,1);kickTrail.enabled=alpha>0&&kickTrail.sprite!=null;
            int key=(presentation.IsChargedKick?100:0)+(int)kind.Value*3+(hit?1:recovery?2:0);if(kickCaptionKey!=key){kickCaptionKey=key;kickCaption.text=presentation.IsChargedKick?"タメ（カリ） / "+(hit?"ハツドウ":recovery?"フッキ":"ジュンビ"):ProvisionalKickCaption(kind.Value,hit,recovery);}kickCaptionPlate.gameObject.SetActive(true);
            float width=((RectTransform)transform).rect.width;float x=(core.Position/unit-camera+viewport*.5f)*pixelsPerWorldUnit;kickCaptionPlate.anchoredPosition=new Vector2(Mathf.Clamp(x,182,Mathf.Max(182,width-182)),(core.Height/unit-verticalOffset)*pixelsPerWorldUnit-30);
        }
        private void RenderTargetBursts(float camera,float viewport,float verticalOffset)
        {
            bool visible=presentation?.IsCurrent==true&&presentation.Core.TrialMapId==null;
            if(!visible){ClearTargetBursts();return;}var core=presentation.Core;bool newLease=!ReferenceEquals(burstLease,presentation);burstLease=presentation;
            for(int i=0;i<2;i++){
                var target=i==0?presentation.TrialTarget:presentation.SecondTrialTarget;bool dead=target.IsDead;
                if(newLease||!ReferenceEquals(observedBurstTargets[i],target)){observedBurstTargets[i]=target;observedDead[i]=dead;targetDeathAt[i]=-1;}
                else if(!observedDead[i]&&dead)targetDeathAt[i]=presentation.Player?.IsBlind==true?-1:core.Frame;
                observedDead[i]=dead;if(presentation.Player?.IsBlind==true||!dead)targetDeathAt[i]=-1;
                long age=targetDeathAt[i]<0?-1:core.Frame-targetDeathAt[i];var effect=targetBursts[i];effect.enabled=age>=0&&age<14;if(!effect.enabled)continue;
                long position=i==0?OutsideExplorationSession.TrialTargetX:OutsideKickTrialDefinition.SecondTargetX;float x=(position/(float)OutsideMovementDefinition.UnitsPerWorldUnit-camera+viewport*.5f)*pixelsPerWorldUnit,top=TrialTargetTop();effect.rectTransform.anchoredPosition=new Vector2(x,top*.5f-verticalOffset*pixelsPerWorldUnit);effect.rectTransform.sizeDelta=Vector2.one*Mathf.Max(48,top*1.2f);effect.Burst(age/14f,Olca.Presentation.Effects.ProvisionalPixelPattern.Tint(false));
            }
        }
        private void ClearTargetBursts(){burstLease=null;for(int i=0;i<2;i++){observedBurstTargets[i]=null;observedDead[i]=false;targetDeathAt[i]=-1;if(targetBursts[i]!=null)targetBursts[i].enabled=false;}}
        private void ClearKickEffects(){if(chargeRing!=null)chargeRing.enabled=false;if(kickTrail!=null){kickTrail.enabled=false;kickTrail.sprite=null;}if(kickCaptionPlate!=null)kickCaptionPlate.gameObject.SetActive(false);kickCaptionKey=-1;ClearTargetBursts();}
    }
}

namespace Olca.Presentation.Exploration
{
    /// <summary>地上三段の試用連番。Sessionの論理時刻を読み、判定・移動・保存は変更しない。</summary>
    /// <summary>探索タメ専用の試用本体。共有Spriteを借り、破棄・ゲーム更新は行わない。</summary>
    /// <summary>地上ボムの試用4姿勢。Sessionの前後隙を表示へ写すだけで、独自時計を持たない。</summary>
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
