using UnityEngine;
using Olca.Core.Logic.Battle;
using Olca.Core.Domain;
using Vector2Int = Olca.Core.Domain.Vector2Int;

namespace Olca.Presentation.Field
{
    /// <summary>
    /// World Space上の6x1バトルグリッド
    /// SpriteRendererを使用してダンジョン世界空間に描画する
    /// 探索から戦闘まで同じ接地面とタイルを継続描画する
    /// </summary>
    [DefaultExecutionOrder(110)]
    public class WorldBattleGridView : MonoBehaviour
    {
        [Header("グリッド設定")]
        [SerializeField] private float panelSize = 1.6f;   // 160px / PPU100 = 1グリッドセル
        [SerializeField] private float panelHeight = 0.8f; // 幅:高さ = 2:1
        [SerializeField] private float panelY = -0.3f;      // 探索・戦闘共通の接地面
        [SerializeField] private float panelZ = 0.1f;       // 背景より手前・キャラより奥
        [SerializeField] private float panelScale = 0.95f;  // 隙間を作るスケール
        [SerializeField] private Sprite panelSprite;

        private const int GRID_WIDTH = 6;

        private BattleField battleField;
        private float originX;

        private SpriteRenderer[] panelRenderers;
        private Sprite whiteSprite;

        private readonly System.Collections.Generic.HashSet<int> attackTiles=new System.Collections.Generic.HashSet<int>();
        private GameObject[] attackMarkers;
        private BattleSystem telegraphBattle;
        public void BindTelegraphs(BattleSystem battle){telegraphBattle=battle;RefreshTelegraphs();}
        public void ShowTelegraphs(System.Collections.Generic.IReadOnlyList<Vector2Int> tiles)
        {
            attackTiles.Clear();if(tiles!=null)foreach(var tile in tiles)if(tile.Y==0&&tile.X>=0&&tile.X<GRID_WIDTH)attackTiles.Add(tile.X);
            RefreshTelegraphs();
        }
        private void RefreshTelegraphs()
        {
            // Play中の再コンパイルでは未生成のnull配列が空配列として復元される。遅延生成前の状態へ戻す。
            if(attackMarkers!=null&&attackMarkers.Length==0)attackMarkers=null;
            // Update後に中断されても、描画直前にはCoreの確定集合を読む。
            if(telegraphBattle!=null){attackTiles.Clear();foreach(var tile in telegraphBattle.EnemyAttackTiles)if(tile.Y==0&&tile.X>=0&&tile.X<GRID_WIDTH)attackTiles.Add(tile.X);}
            if(panelRenderers==null||attackMarkers==null&&attackTiles.Count==0)return;
            if(attackMarkers==null){
                attackMarkers=new GameObject[GRID_WIDTH];if(whiteSprite==null)whiteSprite=CreateWhiteSprite();
                for(int x=0;x<GRID_WIDTH;x++){
                    var root=new GameObject("EnemyAttackTelegraph_"+x);root.transform.SetParent(transform,false);attackMarkers[x]=root;
                    float w=panelSize*panelScale,h=panelHeight*panelScale;
                    var fill=HazardPart(root.transform,"Fill",whiteSprite,Vector2.zero,new Vector2(w,h),new Color(1f,.8f,.3f,.035f),2);
                    // 色面ではなく角括弧と注意記号で予告を識別し、地形や足元を透かす。
                    HazardPart(fill.transform,"WarningStem",whiteSprite,new Vector2(0,.09f),new Vector2(.018f,.18f),new Color(1f,.96f,.7f,1),2);
                    HazardPart(fill.transform,"WarningDot",whiteSprite,new Vector2(0,-.065f),new Vector2(.025f,.035f),new Color(1f,.96f,.7f,1),2);
                    for(int corner=0;corner<4;corner++){
                        float sx=corner%2==0?-1:1,sy=corner<2?1:-1;
                        var edge=new GameObject("Edge"+(corner+1));edge.transform.SetParent(root.transform,false);edge.transform.localPosition=new Vector3(sx*w*.475f,sy*h*.46f,0);
                        foreach(bool horizontal in new[]{true,false}){
                            var at=horizontal?new Vector2(-sx*w*.1f,0):new Vector2(0,-sy*h*.15f);
                            var size=horizontal?new Vector2(w*.2f,panelSize*.025f):new Vector2(panelSize*.025f,h*.3f);
                            var outline=HazardPart(edge.transform,horizontal?"HorizontalOutline":"VerticalOutline",whiteSprite,at,size+new Vector2(.025f,.025f),new Color(.035f,.055f,.065f,.95f),2);outline.transform.localPosition+=new Vector3(0,0,.001f);
                            HazardPart(edge.transform,horizontal?"Horizontal":"Vertical",whiteSprite,at,size,new Color(1f,.93f,.57f,1f),2);
                        }
                    }
                }
            }
            for(int x=0;x<GRID_WIDTH;x++){var root=attackMarkers[x];root.transform.position=new Vector3(GridIndexToWorldX(x),panelY-panelHeight*panelScale*.5f,panelZ-.01f);root.SetActive(battleField!=null&&attackTiles.Contains(x));}
        }

        private GameObject[] magmaMarkers;
        internal static Color MagmaSurfaceColor => new Color(.85f,.52f,.38f,.95f);
        private static GameObject HazardPart(Transform parent,string name,Sprite sprite,Vector2 position,Vector2 size,Color color,int order)
        {
            var part=new GameObject(name);part.transform.SetParent(parent,false);part.transform.localPosition=new Vector3(position.x,position.y,-.001f);part.transform.localScale=new Vector3(size.x,size.y,1);
            var renderer=part.AddComponent<SpriteRenderer>();renderer.sprite=sprite;renderer.color=color;renderer.sortingOrder=order;return part;
        }
        // 戦闘床と探索の床面で同じ亀裂図形を使う。画像・判定・地形の寿命は持たない。
        internal static GameObject CreateMagmaPattern(Transform parent,Sprite sprite,int sortingLayer)
        {
            var root=new GameObject("MagmaPattern");root.transform.SetParent(parent,false);
            var points=new[]{new Vector2(-.44f,.18f),new Vector2(-.31f,.1f),new Vector2(-.23f,.16f),new Vector2(-.11f,-.06f),new Vector2(.03f,-.03f),new Vector2(.12f,-.16f),new Vector2(.28f,-.09f),new Vector2(.43f,-.2f)};
            void Segment(Vector2 a,Vector2 b,int index)
            {
                var delta=b-a;float angle=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
                // 均一な発光線にせず、幅の違う暗い岩の割れ目と短い溶岩の明部を組み合わせる。
                float width=index%3==0?.095f:index%3==1?.065f:.08f;
                for(int layer=0;layer<3;layer++){
                    bool core=layer==2;var center=core?Vector2.Lerp(a,b,index%2==0?.65f:.38f):(a+b)*.5f;
                    var size=new Vector2(delta.magnitude*(core?.34f:1f),width*(layer==0?1f:core?.25f:.57f));
                    var color=layer==0?new Color(.57f,.12f,.045f,1):core?new Color(.98f,.53f,.13f,1):new Color(.84f,.24f,.055f,1);
                    var part=HazardPart(root.transform,"Fissure"+index+(core?"Core":layer==0?"Rim":"Lava"),sprite,center,size,color,1);
                    part.transform.localRotation=Quaternion.Euler(0,0,angle);part.GetComponent<SpriteRenderer>().sortingLayerID=sortingLayer;part.transform.localPosition+=new Vector3(0,0,-.001f*layer);
                }
            }
            for(int i=1;i<points.Length;i++)Segment(points[i-1],points[i],i);
            Segment(points[2],new Vector2(-.32f,-.22f),8);Segment(points[5],new Vector2(.19f,.02f),9);return root;
        }
        private void RefreshMagma()
        {
            if(magmaMarkers!=null&&magmaMarkers.Length==0)magmaMarkers=null;
            for(int x=0;x<GRID_WIDTH;x++){
                bool active=battleField!=null&&battleField.GetPanelType(new Vector2Int(x,0))==PanelType.Magma;
                if(active){if(magmaMarkers==null)magmaMarkers=new GameObject[GRID_WIDTH];if(magmaMarkers[x]==null){if(whiteSprite==null)whiteSprite=CreateWhiteSprite();magmaMarkers[x]=CreateMagmaPattern(transform,whiteSprite,panelRenderers[x].sortingLayerID);magmaMarkers[x].name="PanelMagma_"+x;}}
                if(magmaMarkers==null||magmaMarkers[x]==null)continue;
                var root=magmaMarkers[x];root.SetActive(active);root.transform.position=new Vector3(GridIndexToWorldX(x),panelY-panelHeight*panelScale*.5f,panelZ-.005f);root.transform.localScale=new Vector3(panelSize*panelScale,panelHeight*panelScale,1);
            }
        }
        private Sprite floorSprite;
        private SpriteRenderer[] surroundingPanels;
        private const int SURROUNDING_COUNT = 12; // 横7マスとカメラ移動の余白

        private void Awake()
        {
            var source = panelSprite;
            if (source == null) source = whiteSprite = CreateWhiteSprite();
            // 画像を縦に潰さず、指定比率に中央を切り出して等倍比率で拡大する。
            var rect = source.rect;
            float ratio = panelSize / panelHeight;
            float width = Mathf.Min(rect.width, rect.height * ratio);
            float height = width / ratio;
            rect = new Rect(rect.center.x - width * 0.5f, rect.center.y - height * 0.5f, width, height);
            floorSprite = Sprite.Create(source.texture, rect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
            floorSprite.name = source.name + "_FloorCrop";
            panelRenderers = new SpriteRenderer[GRID_WIDTH];
            for (int x = 0; x < GRID_WIDTH; x++) panelRenderers[x] = CreatePanel("GridPanel_" + x);
            surroundingPanels = new SpriteRenderer[SURROUNDING_COUNT];
            for (int x = 0; x < surroundingPanels.Length; x++) surroundingPanels[x] = CreatePanel("FloorPanel_" + x);
            Refresh();
        }

        private SpriteRenderer CreatePanel(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            float scale = panelScale * panelSize / floorSprite.bounds.size.x;
            go.transform.localScale = new Vector3(scale, scale, 1f);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = floorSprite;
            renderer.sortingOrder = 1;
            renderer.color = GetPanelColor(PanelType.Normal);
            return renderer;
        }

        /// <summary>床の継ぎ目に合わせた戦闘範囲へ地形色を適用する</summary>
        public void Show(BattleField field, float contactPositionX)
        {
            battleField = field;
            originX = SnapWorldXToCellCenter(contactPositionX);
            UpdatePanelPositions();
            Refresh();
        }

        /// <summary>戦闘の地形色を解除し、共通の探索床を描画し続ける</summary>
        public void Hide()
        {
            battleField = null;telegraphBattle=null;attackTiles.Clear();
            Refresh();
        }

        public float SnapWorldXToCellCenter(float worldX) => Mathf.Round(worldX / panelSize) * panelSize;
        public float CellHeight => panelHeight;

        private void OnEnable() => Refresh();
        private void OnDisable()
        {
            if(attackMarkers!=null)foreach(var marker in attackMarkers)if(marker!=null)marker.SetActive(false);
            if(magmaMarkers!=null)foreach(var marker in magmaMarkers)if(marker!=null)marker.SetActive(false);
        }
        private void LateUpdate() => Refresh();

        /// <summary>画面内の床を描画し、戦闘6マスとの重複は除外する</summary>
        public void Refresh()
        {
            if (panelRenderers == null) return;
            RefreshTelegraphs();
            RefreshMagma();
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                panelRenderers[x].enabled = battleField != null;
                if (battleField != null) panelRenderers[x].color = GetPanelColor(battleField.GetPanelType(new Vector2Int(x, 0)));
            }
            var camera = Camera.main;
            float center = camera != null ? camera.transform.position.x : originX;
            int first = Mathf.FloorToInt(center / panelSize) - SURROUNDING_COUNT / 2;
            int battleFirst = Mathf.RoundToInt(originX / panelSize);
            for (int i = 0; i < surroundingPanels.Length; i++)
            {
                int column = first + i;
                var renderer = surroundingPanels[i];
                renderer.enabled = battleField == null || column < battleFirst || column >= battleFirst + GRID_WIDTH;
                renderer.transform.position = new Vector3(column * panelSize, panelY - panelHeight * panelScale * 0.5f, panelZ);
            }
        }

        private void OnDestroy()
        {
            if (floorSprite != null) DestroyOwned(floorSprite);
            if (whiteSprite != null)
            {
                DestroyOwned(whiteSprite.texture);
                DestroyOwned(whiteSprite);
            }
        }

        private static void DestroyOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        /// <summary>グリッドインデックスから World X 座標を返す</summary>
        public float GridIndexToWorldX(int gridX) => originX + LogicalToWorld(gridX);

        /// <summary>論理マス単位の距離をワールド単位へ変換する</summary>
        public float LogicalToWorld(float logicalDistance) => logicalDistance * panelSize;

        /// <summary>ワールド単位の距離を論理マス単位へ変換する</summary>
        public float WorldToLogical(float worldDistance) => worldDistance / panelSize;

        /// <summary>1マスの幅（Unity単位）</summary>
        public float CellSize => panelSize;

        /// <summary>地面の高さ（World Y）</summary>
        public float GroundY => panelY;

        /// <summary>描画倍率を含む床Spriteの下端。接地面や論理マス位置は変えない。</summary>
        public float RenderedFloorBottomY => panelY - panelHeight * panelScale;

        /// <summary>グリッド全体の幅（Unity単位）</summary>
        public float GridWidth => GRID_WIDTH * panelSize;

        /// <summary>グリッド中央の World X 座標（カメラ中央合わせ用）</summary>
        public float GridCenterWorldX => originX + (GRID_WIDTH - 1) * 0.5f * panelSize;

        /// <summary>
        /// プレイヤーがグリッド左半分の中央列に見えるためのカメラオフセットX
        /// （プレイヤー座標からカメラ中心までの距離）
        /// 左半分の中央列 = 列1（0,1,2の中央）、グリッド中心 = 列2.5 → オフセット = 1.5
        /// </summary>
        public float LeftHalfCenterCameraOffsetX =>
            ((GRID_WIDTH - 1) * 0.5f - (GRID_WIDTH / 2 / 2)) * panelSize;

        // ==================== 内部処理 ====================

        private void UpdatePanelPositions()
        {
            for (int x = 0; x < GRID_WIDTH; x++)
            {
                panelRenderers[x].transform.position = new Vector3(
                    originX + x * panelSize, panelY - panelHeight * panelScale * 0.5f, panelZ);
            }
        }

        private Color GetPanelColor(PanelType type)
        {
            switch (type)
            {
                case PanelType.Normal:  return new Color(0.75f, 0.75f, 0.75f, 0.85f);
                case PanelType.Ice:     return new Color(0.6f, 0.9f, 1.0f, 0.85f);
                case PanelType.Grass:   return new Color(0.5f, 0.85f, 0.5f, 0.85f);
                case PanelType.Magma:   return MagmaSurfaceColor;
                case PanelType.Cracked: return new Color(0.9f, 0.85f, 0.5f, 0.85f);
                case PanelType.Hole:    return new Color(0.1f, 0.1f, 0.1f, 0.85f);
                default:                return new Color(0.5f, 0.5f, 0.5f, 0.85f);
            }
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}
