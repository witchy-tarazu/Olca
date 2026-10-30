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
                    for(int part=0;part<5;part++){
                        var go=new GameObject(part==0?"Fill":"Edge"+part);go.transform.SetParent(root.transform,false);
                        var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=whiteSprite;renderer.sortingOrder=2;
                        renderer.color=new Color(1f,.28f,.12f,part==0?.15f:.9f);
                        float w=panelSize*panelScale,h=panelHeight*panelScale,edge=.035f*panelSize;
                        go.transform.localPosition=part==1?new Vector3(0,h/2,0):part==2?new Vector3(0,-h/2,0):part==3?new Vector3(-w/2,0,0):part==4?new Vector3(w/2,0,0):Vector3.zero;
                        go.transform.localScale=part<=2?new Vector3(w,part==0?h:edge,1):new Vector3(edge,h,1);
                    }
                }
            }
            for(int x=0;x<GRID_WIDTH;x++){var root=attackMarkers[x];root.transform.position=new Vector3(GridIndexToWorldX(x),panelY-panelHeight*panelScale*.5f,panelZ-.01f);root.SetActive(battleField!=null&&attackTiles.Contains(x));}
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

        private void LateUpdate() => Refresh();

        /// <summary>画面内の床を描画し、戦闘6マスとの重複は除外する</summary>
        public void Refresh()
        {
            if (panelRenderers == null) return;
            RefreshTelegraphs();
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
                case PanelType.Magma:   return new Color(1.0f, 0.4f, 0.2f, 0.85f);
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
