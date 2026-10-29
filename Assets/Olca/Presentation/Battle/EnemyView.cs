using UnityEngine;
using Olca.Core.Domain;

namespace Olca.Presentation.Battle
{
    /// <summary>
    /// 敵1体のView（World Space版）
    /// SpriteRenderer + TextMesh ベースで Canvas 不要
    /// WorldEnemyView と組み合わせて使用する
    /// </summary>
    public class EnemyView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer enemyImage;
        [SerializeField] private TextMesh enemyNameText;
        [SerializeField] private TextMesh hpText;
        [SerializeField] private Sprite gruntSprite;
        [SerializeField] private Sprite bossSprite;

        private Enemy enemy;
        private Olca.Presentation.Effects.DefensiveFieldVisual defensiveField;
        private Olca.Presentation.Effects.ActorConditionVisual guardVisual, paralysisVisual;
        private void RefreshConditionVisuals()
        {
            if (!isActiveAndEnabled || enemy == null || enemy.IsDead) { guardVisual?.Clear(); paralysisVisual?.Clear(); return; }
            if (guardVisual == null && enemy.Guard.IsActive) guardVisual = new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Guard);
            if (paralysisVisual == null && enemy.IsParalyzed) paralysisVisual = new Olca.Presentation.Effects.ActorConditionVisual(Olca.Presentation.Effects.ActorConditionKind.Paralyze);
            guardVisual?.Refresh(enemyImage, enemy.Guard.IsActive); paralysisVisual?.Refresh(enemyImage, enemy.IsParalyzed);
        }
        private void RefreshDefensiveField()
        {
            if (!isActiveAndEnabled || enemy == null || enemy.IsDead) { defensiveField?.Clear(); return; }
            if (defensiveField == null && (enemy.HasBarrier || enemy.HasAura)) defensiveField = new Olca.Presentation.Effects.DefensiveFieldVisual();
            defensiveField?.Refresh(enemyImage, enemy.HasBarrier, enemy.HasAura);
        }
        private static readonly System.Collections.Generic.List<EnemyView> bossGauges=new System.Collections.Generic.List<EnemyView>();
        private static readonly System.Collections.Generic.List<EnemyView> normalGauges=new System.Collections.Generic.List<EnemyView>();
        private const float BossHudSpacing=.10f;
        private Canvas statusCanvas;
        private CombatStatusIconStrip statusIcons;
        private void HideStatusIcons(){statusIcons?.Clear();if(statusCanvas!=null)statusCanvas.gameObject.SetActive(false);}
        private void RefreshStatusIcons()
        {
            var camera=Camera.main;
            if(!isActiveAndEnabled||!GaugeVisible||!IsBossGauge||camera==null){HideStatusIcons();return;}
            var entries=CombatStatusIcons.ForEnemy(enemy);
            if(entries.Count==0){HideStatusIcons();return;}
            if(statusCanvas==null){
                var obj=new GameObject("BossCombatStatusIcons",typeof(RectTransform),typeof(Canvas));obj.transform.SetParent(transform,false);
                statusCanvas=obj.GetComponent<Canvas>();statusCanvas.renderMode=RenderMode.WorldSpace;statusCanvas.overrideSorting=true;statusCanvas.sortingOrder=7;
                var rect=(RectTransform)obj.transform;rect.pivot=new Vector2(.5f,1);rect.sizeDelta=new Vector2(52,520);
                var row=new GameObject("Icons",typeof(RectTransform));row.transform.SetParent(obj.transform,false);var rr=(RectTransform)row.transform;rr.anchorMin=rr.anchorMax=new Vector2(0,1);rr.pivot=new Vector2(0,1);rr.anchoredPosition=Vector2.zero;
                statusIcons=row.AddComponent<CombatStatusIconStrip>();statusIcons.Configure(1);
            }
            statusCanvas.gameObject.SetActive(true);statusIcons.Show(entries);
            float offset=BossHudSpacing*Mathf.Max(0,bossGauges.IndexOf(this));
            float depth=Mathf.Max(camera.nearClipPlane+1,camera.WorldToViewportPoint(transform.position).z);
            var point=camera.ViewportToWorldPoint(new Vector3(.90f-offset,.8f,depth));
            float worldHeight=Vector3.Distance(camera.ViewportToWorldPoint(new Vector3(.9f,.8f,depth)),camera.ViewportToWorldPoint(new Vector3(.9f,.845f,depth)));
            float scale=worldHeight/48;var parentScale=transform.lossyScale;
            statusCanvas.transform.localScale=new Vector3(scale/(parentScale.x==0?1:parentScale.x),scale/(parentScale.y==0?1:parentScale.y),scale/(parentScale.z==0?1:parentScale.z));
            statusCanvas.transform.position=point;statusCanvas.transform.rotation=camera.transform.rotation;
        }
        private bool hpVisible;
        private LineRenderer hpBack, hpFill, poiseBack, poiseFill;
        private Material gaugeMaterial;
        private Vector3 hpLocalPosition, hpLocalScale, nameLocalPosition;
        private bool namePositionCached;
        public bool IsBossGauge => enemy?.EnemyId == "boss";
        public bool GaugeVisible => hpVisible && enemy != null && !enemy.IsDead;
        // 固定HUDを敵本体の頭上位置として数えない。
        public bool IsFixedHudRenderer(Renderer renderer)=>IsBossGauge&&
            (renderer==hpBack||renderer==hpFill||renderer==poiseBack||renderer==poiseFill||hpText!=null&&renderer==hpText.GetComponent<Renderer>());
        public float HpFraction => enemy == null ? 0 : Mathf.Clamp01((float)enemy.CurrentHp / enemy.MaxHp);
        public float? PoiseFraction => enemy?.Poise == null ? (float?)null :
            enemy.IsStaggered ? 0 : Mathf.Clamp01((float)(enemy.Poise.Value-enemy.AccumulatedPoiseDamage)/enemy.Poise.Value);

        private void EnsureGauges()
        {
            if(hpBack!=null)return;
            gaugeMaterial=new Material(Shader.Find("Sprites/Default"));
            LineRenderer Line(string name,Color color) {
                var obj=new GameObject(name);obj.transform.SetParent(transform,false);
                var line=obj.AddComponent<LineRenderer>();line.sharedMaterial=gaugeMaterial;
                line.useWorldSpace=true;line.startColor=line.endColor=color;line.sortingOrder=3;
                line.numCapVertices=2;return line;
            }
            hpBack=Line("EnemyHpTrack",new Color(.035f,.1f,.13f,1));
            hpFill=Line("EnemyHpFill",new Color(.42f,.85f,.87f,1));
            poiseBack=Line("EnemyPoiseTrack",new Color(.12f,.13f,.08f,1));
            poiseFill=Line("EnemyPoiseFill",new Color(.95f,.8f,.27f,1));
        }
        private static void Arc(LineRenderer line,Vector3 center,float radius,float degrees,float width)
        {
            line.startWidth=line.endWidth=width;
            int steps=Mathf.Max(1,Mathf.CeilToInt(degrees/5));line.positionCount=steps+1;
            for(int n=0;n<=steps;n++){float a=(90-degrees*n/steps)*Mathf.Deg2Rad;line.SetPosition(n,center+new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*radius);}
        }
        private static void Bar(LineRenderer line,Vector3 bottom,Vector3 top,float width,float fraction)
        {line.startWidth=line.endWidth=width;line.positionCount=2;line.SetPosition(0,bottom);line.SetPosition(1,Vector3.Lerp(bottom,top,fraction));}
        private void RefreshGauges()
        {
            RefreshStatusIcons();
            bool visible=GaugeVisible;if(hpBack==null&&!visible)return;EnsureGauges();
            hpBack.enabled=visible;hpFill.enabled=visible&&HpFraction>0;
            poiseBack.enabled=visible&&PoiseFraction.HasValue;poiseFill.enabled=poiseBack.enabled&&PoiseFraction.Value>0;
            if(!visible)return;
            var camera=Camera.main;
            if(IsBossGauge&&camera!=null){
                float offset=BossHudSpacing*Mathf.Max(0,bossGauges.IndexOf(this));
                float depth=Mathf.Max(camera.nearClipPlane+1,camera.WorldToViewportPoint(transform.position).z);
                Vector3 Point(float x,float y)=>camera.ViewportToWorldPoint(new Vector3(x-offset,y,depth));
                float height=Vector3.Distance(Point(.965f,.3f),Point(.965f,.78f));
                Bar(hpBack,Point(.965f,.3f),Point(.965f,.78f),height*.055f,1);
                Bar(hpFill,Point(.965f,.3f),Point(.965f,.78f),height*.043f,HpFraction);
                Bar(poiseBack,Point(.94f,.3f),Point(.94f,.78f),height*.018f,1);
                Bar(poiseFill,Point(.94f,.3f),Point(.94f,.78f),height*.012f,PoiseFraction??0);
                if(hpText!=null)hpText.transform.position=Point(.965f,.265f);
                FitHpText(height*.14f);
            }else{
                if(hpText!=null)hpText.transform.localPosition=hpLocalPosition;
                if(enemyNameText!=null)enemyNameText.transform.localPosition=nameLocalPosition;
                float radius=camera!=null&&camera.orthographic?camera.orthographicSize*.055f:.55f;
                int row=0;
                // 同じ位置にいる敵も、HP/強靭度の値を個体ごとに読めるよう分ける。
                foreach(var other in normalGauges){
                    if(other==this)break;
                    if(other!=null&&other.GaugeVisible&&Vector3.Distance(other.transform.position,transform.position)<radius*2.4f)row++;
                }
                var center=(hpText!=null?hpText.transform.position:transform.position+Vector3.up)+Vector3.down*(row*radius*2.65f);
                if(hpText!=null)hpText.transform.position=center;
                FitHpText(radius*2);
                Arc(hpBack,center,radius,360,radius*.16f);Arc(hpFill,center,radius,360*HpFraction,radius*.11f);
                Arc(poiseBack,center,radius*1.28f,180,radius*.09f);Arc(poiseFill,center,radius*1.28f,180*(PoiseFraction??0),radius*.06f);
                // 同位置の名前は各HP円の左へそろえる。円の行間や本体・文字の大きさは変えない。
                if(enemyNameText!=null&&normalGauges.Exists(other=>other!=this&&other!=null&&other.GaugeVisible&&
                    Vector3.Distance(other.transform.position,transform.position)<radius*2.4f)){
                    var labelRenderer=enemyNameText.GetComponent<Renderer>();
                    if(labelRenderer!=null){var bounds=labelRenderer.bounds;var position=enemyNameText.transform.position;
                        position.x+=center.x-radius*1.6f-bounds.max.x;position.y+=center.y-bounds.center.y;
                        enemyNameText.transform.position=position;}
                }
            }
        }
        private void FitHpText(float diameter)
        {
            if(hpText==null)return;var renderer=hpText.GetComponent<Renderer>();
            float height=renderer!=null?renderer.bounds.size.y:0;if(height<.0001f)return;
            // 最大HPの桁数に合わせ、残HPの桁が変わっても数字の大きさを変えない。
            float targetHeight=Mathf.Min(diameter*.4f,diameter*.8f/(.7f*enemy.MaxHp.ToString().Length));
            hpText.transform.localScale*=targetHeight/height;
        }
        private void RegisterGauge(){var list=IsBossGauge?bossGauges:normalGauges;if(!list.Contains(this))list.Add(this);}
        private void OnEnable(){if(hpVisible)RegisterGauge();}
        private void OnDisable(){guardVisual?.Clear();paralysisVisual?.Clear();defensiveField?.Clear();HideStatusIcons();bossGauges.Remove(this);normalGauges.Remove(this);}
        private void LateUpdate(){if(hpVisible)RefreshGauges();RefreshDefensiveField();RefreshConditionVisuals();}
        private void OnDestroy(){guardVisual?.Dispose();paralysisVisual?.Dispose();defensiveField?.Dispose();HideStatusIcons();bossGauges.Remove(this);normalGauges.Remove(this);if(gaugeMaterial!=null){if(UnityEngine.Application.isPlaying)Destroy(gaugeMaterial);else DestroyImmediate(gaugeMaterial);}}

        private bool deathPresentationActive;
        public void SetDeathPresentationActive(bool active)=>deathPresentationActive=active;

        /// <summary>WorldEnemyView がダメージフラッシュに使う SpriteRenderer</summary>
        public SpriteRenderer BodyRenderer => enemyImage;

        public void Initialize(Enemy enemy)
        {
            guardVisual?.Clear(); paralysisVisual?.Clear(); defensiveField?.Clear();
            this.enemy = enemy;
            if(hpText!=null&&hpLocalScale!=Vector3.zero){hpText.transform.localScale=hpLocalScale;hpText.transform.localPosition=hpLocalPosition;}
            if(enemyNameText!=null&&namePositionCached)enemyNameText.transform.localPosition=nameLocalPosition;
            ApplyAppearance();
            if(enemyNameText!=null){nameLocalPosition=enemyNameText.transform.localPosition;namePositionCached=true;}
            if(hpText!=null){hpLocalPosition=hpText.transform.localPosition;hpLocalScale=hpText.transform.localScale;}


            if (enemyNameText != null)
                enemyNameText.text = Olca.Presentation.CustomScreen.CustomDisplay.Katakana(enemy.Name);

            // HP/名前テキストをグリッド・敵スプライトより手前に描画
            SetTextSortingOrder(hpText, 4);
            SetTextSortingOrder(enemyNameText, 3);

            HideHp(); // ダンジョン探索中は名前・HP非表示
            Refresh();
        }

        /// <summary>既存の敵IDに対応する仮素材を表示する。戦闘データは変更しない。</summary>
        private void ApplyAppearance()
        {
            var sprite = enemy.EnemyId == "boss" ? bossSprite : gruntSprite;
            if (enemyImage == null || sprite == null) return;

            enemyImage.sprite = sprite;
            enemyImage.color = Color.white;
            enemyImage.flipX = true; // 右向きの原画を、左側のプレイヤーへ向ける

            // 大型ボスでも名前とHPが絵に重ならないよう、スプライトの上に配置する。
            float top = sprite.bounds.max.y;
            if (enemyNameText != null)
                enemyNameText.transform.localPosition = new Vector3(0f, top + 1.05f, -0.1f);
            if (hpText != null)
                hpText.transform.localPosition = new Vector3(0f, top + 0.25f, -0.1f);
        }

        private static void SetTextSortingOrder(TextMesh tm, int order)
        {
            if (tm == null) return;
            var mr = tm.GetComponent<MeshRenderer>();
            if (mr != null) mr.sortingOrder = order;
        }

        /// <summary>HPテキストと死亡表示を更新（毎フレーム呼ぶ）</summary>
        public void Refresh()
        {
            if (enemy == null) return;
            if (hpText != null)
                hpText.text = $"{enemy.CurrentHp}";
            RefreshGauges();
            gameObject.SetActive(!enemy.IsDead||deathPresentationActive);
            RefreshDefensiveField();
            RefreshConditionVisuals();
        }

        /// <summary>バトル開始時に呼ぶ（名前・HP表示）</summary>
        public void ShowHp()
        {
            hpVisible=true;RegisterGauge();
            RefreshGauges();
            if (hpText != null) hpText.gameObject.SetActive(true);
            if (enemyNameText != null) enemyNameText.gameObject.SetActive(true);
        }

        /// <summary>バトル終了時に呼ぶ（名前・HP非表示）</summary>
        public void HideHp()
        {
            hpVisible=false;bossGauges.Remove(this);normalGauges.Remove(this);
            RefreshGauges();
            if (hpText != null) hpText.gameObject.SetActive(false);
            if (enemyNameText != null) enemyNameText.gameObject.SetActive(false);
        }
    }
}
