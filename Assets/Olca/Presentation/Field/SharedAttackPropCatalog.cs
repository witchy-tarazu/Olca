using System.Collections.Generic;
using UnityEngine;

namespace Olca.Presentation.Field
{
    /// <summary>標準ソードの解決済み一マスだけを描く。モデル時計以外では進行しない。</summary>
    public sealed class BattleSwordTrace475 : System.IDisposable
    {
        private readonly Transform parent;
        private readonly System.Func<WorldBattleGridView> readGrid;
        private readonly System.Func<SpriteRenderer> readBody;
        private Olca.Core.Logic.Battle.BattleSystem owner;
        private Olca.Unity.GeneratedSpriteAssets.Lease assets;
        private GameObject root;
        private SpriteRenderer trace;
        private long began = -1;
        private int column;
        private bool disposed;
        private static readonly float[] Opacity = { 1f, .9f, .85f, .7f, .5f, .25f };
        public BattleSwordTrace475(Transform parent, System.Func<WorldBattleGridView> grid, System.Func<SpriteRenderer> body)
        { this.parent = parent; readGrid = grid; readBody = body; }
        public void Bind(Olca.Core.Logic.Battle.BattleSystem battle)
        {
            if (disposed || ReferenceEquals(owner, battle)) return;
            if (owner != null) owner.CardUsage.OnDirectEffectResolved -= Record;
            Clear(); owner = battle;
            if (owner != null) {
                // 初回の生成を戦闘開始時へ寄せ、攻撃の描画フレームには持ち込まない。
                assets ??= Olca.Unity.GeneratedSpriteAssets.Acquire(parent);
                Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(assets, 1, 1);
                owner.CardUsage.OnDirectEffectResolved += Record;
            }
        }
        private void Record(Olca.Core.Domain.Card card)
        {
            // 他の剣・通常攻撃・変身の絵へ横展開しない。命中の有無は発生条件にしない。
            if (owner == null || card?.Id != "sword" || card.ExceedLevel != Olca.Core.Domain.ExceedLevel.Normal ||
                card.Series != Olca.Core.Domain.CardSeries.Sword ||
                (card.TargetType != Olca.Core.Logic.CardEffects.Effects.DamageTargetType.Single && card.TargetType != Olca.Core.Logic.CardEffects.Effects.DamageTargetType.FrontTile) ||
                card.Modifier.AdditionalEffects.Count != 0 || card.MoveMotionTime != 0 || card.AttackMotionTime != 0 ||
                owner.Transformations.ActiveId != null || owner.Player.IsDead) return;
            column = owner.Player.GridPosition.X + 1;
            if (column < 0 || column >= Olca.Core.Logic.Battle.BattleField.GRID_WIDTH) return;
            began = owner.LogicalFrame * 50;
        }
        public void Render()
        {
            var grid = readGrid();
            if (disposed || owner == null || owner.IsGameOver && owner.State != Olca.Core.Logic.Battle.BattleState.Victory || owner.Player.IsDead || grid == null || began < 0) { Clear(); return; }
            long age = owner.LogicalFrame * 50 - began;
            if (age < 0 || age >= 600) { Clear(); return; } // 12論理フレーム、判定の追加なし。勝利で時計が止まる間は既存の終了表示返却まで保持。
            if (root == null) {
                root = new GameObject("BattleSwordTrace475"); root.transform.SetParent(parent, false);
                trace = root.AddComponent<SpriteRenderer>();
            }
            assets ??= Olca.Unity.GeneratedSpriteAssets.Acquire(parent);
            int frame = (int)(age / 100);
            // 発生時は既存の面状コマを使い、薄い末尾へつなぐ。新しい白面を重ねない。
            int pose = frame < 2 ? 1 : frame < 4 ? 2 : 3;
            trace.sprite = Olca.Presentation.Effects.AuthoredAttackVfx.Resolve(assets, 1, pose);
            trace.color = new Color(1, 1, 1, Opacity[frame]);
            var body = readBody();
            trace.sortingLayerID = body != null ? body.sortingLayerID : 0;
            trace.sortingOrder = body != null ? body.sortingOrder + 3 : 5;
            float cell = grid.CellSize;
            root.transform.position = new Vector3(grid.GridIndexToWorldX(column), grid.GroundY - grid.CellHeight * .45f + cell * .6f, body != null ? body.transform.position.z - .02f : -.02f);
            root.transform.localScale = new Vector3(cell * .92f / 2.56f, cell * 1.05f / 2.56f, 1);
            trace.enabled = trace.sprite != null;
        }
        private void Clear() { began = -1; if (trace != null) { trace.enabled = false; trace.sprite = null; } }
        public void Dispose()
        {
            if (disposed) return;
            Bind(null); disposed = true; assets?.Dispose(); assets = null;
            if (root != null) { if (Application.isPlaying) Object.Destroy(root); else Object.DestroyImmediate(root); }
            root = null; trace = null;
        }
    }

    /// <summary>通常盤面のボム着弾だけの自作6コマ。時計・範囲・命中は所有しない。</summary>
    public static class BattleBombLayers451
    {
        public static Sprite Frame(int frame,bool center,Olca.Unity.GeneratedSpriteAssets.Lease assets)
        {
            frame=Mathf.Clamp(frame,0,5);int age=frame;
            return Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("battle-bomb451/"+center+"/"+frame,owned=>{
                const int w=160,h=128;var pixels=new Color[w*h];float t=age/5f;
                void Ellipse(float cx,float cy,float rx,float ry,Color color){
                    for(int y=Mathf.Max(0,Mathf.FloorToInt(cy-ry));y<Mathf.Min(h,cy+ry+1);y++)for(int x=Mathf.Max(0,Mathf.FloorToInt(cx-rx));x<Mathf.Min(w,cx+rx+1);x++){
                        float d=Mathf.Pow((x-cx)/rx,2)+Mathf.Pow((y-cy)/ry,2);if(d>1)continue;
                        float a=color.a*Mathf.Clamp01((1-d)*9);var old=pixels[y*w+x];float alpha=a+old.a*(1-a);
                        pixels[y*w+x]=alpha>0?new Color((color.r*a+old.r*old.a*(1-a))/alpha,(color.g*a+old.g*old.a*(1-a))/alpha,(color.b*a+old.b*old.a*(1-a))/alpha,alpha):Color.clear;
                    }
                }
                // 暗い煙の輪郭→暖色の芯→細い地表の圧力輪。頭上の数字まで覆わない。
                for(int n=0;n<5;n++){
                    float cx=80+(n-2)*(12+5*t),cy=28+24*t+(n%2)*12;
                    Ellipse(cx,cy,19+10*t,23+12*t,new Color(.18f,.13f,.16f,.48f));
                    Ellipse(cx-2,cy+2,15+10*t,19+12*t,new Color(.52f,.38f,.29f,.52f));
                }
                float heat=1-t*.88f;
                Ellipse(80,24+8*t,36+10*t,32-12*t,new Color(1,.27f,.035f,.9f*heat));
                Ellipse(80,22,22+7*t,24-10*t,new Color(1,.73f,.13f,.95f*heat));
                if(center)Ellipse(80,19,12+5*t,20-12*t,new Color(1,.96f,.72f,.9f*heat));
                Ellipse(80,7,31+36*t,5,new Color(.45f,.18f,.08f,.7f*(1-t*.5f)));
                Ellipse(80,9,27+36*t,2,new Color(1,.65f,.2f,.8f*(1-t*.65f)));
                for(int n=0;n<5;n++)Ellipse(80+(n-2)*(18+8*t),23+(n%3)*13+24*t,1.5f,3,new Color(1,.8f,.35f,.85f*(1-t*.7f)));
                var texture=owned.CreateTexture(w,h);texture.SetPixels(pixels);texture.Apply(false,true);texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;
                texture.name="Bomb451-"+center+"-"+age;var sprite=owned.CreateSprite(texture,new Rect(0,0,w,h),new Vector2(.5f,0));sprite.name=texture.name;return sprite;
            },assets);
        }
    }
    /// <summary>仮の共用物体Sprite。カード性能・本体ポーズ・フレーム配分を所有しない。</summary>
    public static class SharedAttackPropCatalog
    {
        public const string Sword = "shared/sword";
        public const string ThrowingKnife = "shared/throwing-knife";
        public const string ShotBolt = "shared/shot-bolt";
        public const string Bomb = "shared/bomb";
        public const string Blast = "shared/blast";
        // 探索の実着弾だけの試用明色。元の輪郭/alphaを変えず、予告とは別keyで所有する。
        public const string BlastImpact = "shared/blast-impact";
        public const float BlastVisibleWidth = 54f / 100f, BlastVisibleHeight = 8f / 100f;
        public const float BombVisibleHeight = 38f / 100f;

        public static bool TryGetSprite(string key, out Sprite sprite, Olca.Unity.GeneratedSpriteAssets.Lease assets=null)
        {
            sprite = null;
            string resource;
            Vector2 pivot;
            switch (key)
            {
                case Sword: resource = "shared_sword"; pivot = new Vector2(90f / 256f, .5f); break;
                case ThrowingKnife: resource = "shared_throwing_knife"; pivot = new Vector2(.5f, .5f); break;
                case ShotBolt: resource = "shared_shot_bolt"; pivot = new Vector2(.5f, .5f); break;
                case Bomb: resource = "shared_bomb"; pivot = new Vector2(.5f, .5f); break;
                case Blast: resource = "shared_blast"; pivot = new Vector2(.5f, .5f); break;
                case BlastImpact: resource = "shared_blast"; pivot = new Vector2(.5f, .5f); break;
                default: return false;
            }
            sprite=Olca.Unity.GeneratedSpriteAssets.Get<Sprite>("prop/"+key,owned=>{
                var source=Resources.Load<TextAsset>("OlcaAttackProps/"+resource);
                if(source==null)return null;
                var texture=owned.CreateTexture(2,2);
                if(!texture.LoadImage(source.bytes,key!=BlastImpact))return null;
                if(key==BlastImpact){
                    var pixels=texture.GetPixels32();
                    for(int i=0;i<pixels.Length;i++){var c=pixels[i];c.r=(byte)((c.r+255)/2);c.g=(byte)((c.g+255)/2);c.b=(byte)((c.b+255)/2);pixels[i]=c;}
                    texture.SetPixels32(pixels);texture.Apply(false,true);
                }
                texture.name=key;texture.hideFlags=HideFlags.HideAndDontSave;texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
                var result=owned.CreateSprite(texture,new Rect(0,0,texture.width,texture.height),pivot);
                result.name=key;result.hideFlags=HideFlags.HideAndDontSave;return result;
            },assets);
            return sprite!=null;
        }
    }
}
