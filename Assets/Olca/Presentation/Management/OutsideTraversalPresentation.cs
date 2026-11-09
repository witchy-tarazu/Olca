using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Olca.Core.Logic.Integration;

namespace Olca.Presentation.Exploration
{
    public sealed partial class OutsideExplorationView
    {
        private Sprite traversalDashSprite;
        private Sprite[] wallSlideFrames,wallKickFrames;
        private Olca.Unity.ResourceManager traversalResources;
        private OutsideTraversalProfile renderedTraversal;
        private readonly List<Image> wallImages=new List<Image>();
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
            if(core.IsWallSliding){
                if(wallSlideFrames==null)wallSlideFrames=new[]{traversalResources.GetShared<Sprite>("OlcaTraversal/WallSlide_01"),traversalResources.GetShared<Sprite>("OlcaTraversal/WallSlide_02")};
                return wallSlideFrames[(int)(core.WallSlideFrame/6%wallSlideFrames.Length)];
            }
            if(core.IsWallKicking){
                if(wallKickFrames==null)wallKickFrames=new[]{traversalResources.GetShared<Sprite>("OlcaTraversal/WallKick_01"),traversalResources.GetShared<Sprite>("OlcaTraversal/WallKick_02")};
                return wallKickFrames[Math.Min(wallKickFrames.Length-1,core.WallKickFrame/6)];
            }
            // 地形で実際に停止している間は立ち姿。ダッシュの残時間や再開条件は変えない。
            return core.IsDashing?core.IsMoving?traversalDashSprite:idleSprite:null;
        }
        private float ResolveVerticalViewOffset(OutsideExplorationCore core)
        {
            float Top(Sprite sprite)=>sprite!=null?sprite.bounds.max.y:0;
            float actorTop=Mathf.Max(Top(idleSprite),Mathf.Max(Top(riseSprite),Mathf.Max(Top(apexSprite),Mathf.Max(Top(fallSprite),Top(traversalDashSprite)))));
            // 壁4枚は共通256px/PPU100/pivotY14。未ロード時も同じ上端でView再生成の跳ねを防ぐ。
            if(core.TraversalProfile?.Walls.Count>0)actorTop=Mathf.Max(actorTop,2.42f);
            float height=((RectTransform)transform).rect.height;
            return presentation.Camera.ResolveVerticalOffset(core.Height,height/pixelsPerWorldUnit,height*.21f/pixelsPerWorldUnit,144/pixelsPerWorldUnit,actorTop);
        }
        private void RenderTraversal(OutsideExplorationCore core,float camera,float viewport,float verticalOffset)
        {
            const float unit=OutsideMovementDefinition.UnitsPerWorldUnit;
            float Origin(long x)=>(x/unit-camera+viewport*.5f)*pixelsPerWorldUnit;
            var profile=core.TraversalProfile;
            if(!ReferenceEquals(renderedTraversal,profile)){
                foreach(var image in wallImages)if(image!=null){if(UnityEngine.Application.isPlaying)Destroy(image.gameObject);else DestroyImmediate(image.gameObject);}wallImages.Clear();renderedTraversal=profile;
                if(profile!=null)foreach(var wall in profile.Walls){
                    var go=new GameObject("OutsideValidationWall-"+wall.Id,typeof(RectTransform),typeof(Image));go.transform.SetParent(transform,false);var image=go.GetComponent<Image>();image.sprite=terrain.sprite;image.type=Image.Type.Tiled;image.color=new Color(.6f,.85f,.94f,.8f);image.raycastTarget=false;
                    image.rectTransform.anchorMin=image.rectTransform.anchorMax=new Vector2(0,.21f);image.rectTransform.pivot=new Vector2(.5f,0);go.transform.SetSiblingIndex(player.transform.GetSiblingIndex());wallImages.Add(image);
                }
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
            var samples=core.Afterimages;
            for(int i=0;i<afterimageImages.Length;i++){
                var image=afterimageImages[i];if(i>=samples.Count){image.enabled=false;image.sprite=null;continue;}
                var sample=samples[i];var sprite=sample.Pose==OutsideAfterimagePose.Dash?traversalDashSprite:sample.Pose==OutsideAfterimagePose.JumpRise?riseSprite:sample.Pose==OutsideAfterimagePose.JumpApex?apexSprite:fallSprite;
                image.sprite=sprite;image.enabled=sprite!=null;if(sprite==null)continue;
                image.color=new Color(.65f,.9f,1,.25f*(12-(core.Frame-sample.Frame))/12f);
                var rect=image.rectTransform;rect.pivot=sprite.pivot/sprite.rect.size;rect.sizeDelta=new Vector2(sprite.bounds.size.x,sprite.bounds.size.y)*pixelsPerWorldUnit;
                rect.anchoredPosition=new Vector2(Origin(sample.X),(sample.Y/unit-verticalOffset)*pixelsPerWorldUnit);rect.localScale=new Vector3(-sample.Facing,1,1);
            }
            if(movementGuideText.text.Length>0){
                if(profile!=null&&profile.WallActionsUnlocked&&profile.Walls.Count>0)movementGuideText.text+="\nタイオウするカベ: ホウコウホジでスベル / ジャンプでケる";
                if(core.DashCooldownRemaining>0)movementGuideText.text+="\nダッシュ マチ: "+(Math.Ceiling(core.DashCooldownRemaining/6d)/10d).ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"s";
            }
        }
        private void ClearTraversalDisplay()
        {foreach(var image in afterimageImages)if(image!=null){image.enabled=false;image.sprite=null;}foreach(var image in wallImages)if(image!=null)image.gameObject.SetActive(false);}
        private void ReleaseTraversalReferences(){ClearTraversalDisplay();wallSlideFrames=wallKickFrames=null;traversalDashSprite=null;traversalResources=null;renderedTraversal=null;wallImages.Clear();}
    }
}
