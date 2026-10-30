using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Olca.Presentation.Battle
{
    /// <summary>専用Spriteを並べ、残り割合だけ色を残す。数値/入力/効果時間を所有しない。</summary>
    public sealed class CombatStatusIconStrip : MonoBehaviour
    {
        private sealed class Slot { internal GameObject Root; internal Image Gray,Color; }
        private readonly List<Slot> slots=new List<Slot>();
        private int columns=5;private float size=48,gap=6;
        public int VisibleCount { get; private set; }
        public void Configure(int columns,float size=48,float gap=6)
        {if(columns<1||size<=0||gap<0||float.IsNaN(size)||float.IsInfinity(size)||float.IsNaN(gap)||float.IsInfinity(gap))throw new ArgumentOutOfRangeException();this.columns=columns;this.size=size;this.gap=gap;}
        public void Show(IReadOnlyList<CombatIconEntry> entries)
        {
            if(entries==null)throw new ArgumentNullException(nameof(entries));
            var ids=new HashSet<string>();foreach(var item in entries)if(item==null||!ids.Add(item.Id))throw new ArgumentException("重複したアイコン識別です");
            while(slots.Count<entries.Count)slots.Add(Create(slots.Count));
            VisibleCount=0;
            for(int n=0;n<slots.Count;n++){
                var slot=slots[n];
                if(n>=entries.Count){slot.Root.SetActive(false);continue;}
                var entry=entries[n];
                if(!CombatStatusIconCatalog.TryGet(entry.Icon,out var color,out var gray)){slot.Root.SetActive(false);continue;}
                slot.Root.SetActive(true);VisibleCount++;
                slot.Root.name="CombatStatus_"+entry.Id;var rect=(RectTransform)slot.Root.transform;
                rect.anchoredPosition=new Vector2(n%columns*(size+gap),-(n/columns)*(size+gap));rect.sizeDelta=new Vector2(size,size);
                slot.Gray.sprite=gray;slot.Color.sprite=color;slot.Color.fillAmount=entry.RemainingFraction??1;
            }
            int width=Mathf.Min(columns,entries.Count),height=Mathf.CeilToInt(entries.Count/(float)columns);
            var root=(RectTransform)transform;root.sizeDelta=new Vector2(width==0?0:width*size+(width-1)*gap,height==0?0:height*size+(height-1)*gap);
        }
        private Slot Create(int index)
        {
            var obj=new GameObject("CombatStatusSlot"+index,typeof(RectTransform),typeof(Image));obj.transform.SetParent(transform,false);var rect=(RectTransform)obj.transform;
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
            // 既存の面材を共有し、背景の明暗にかかわらず灰色部分も読める薄い背面を置く。
            var face=obj.GetComponent<Image>();face.sprite=Resources.Load<Sprite>("OlcaUI/description");face.type=Image.Type.Sliced;face.pixelsPerUnitMultiplier=16;face.color=new Color(1,1,1,.85f);face.raycastTarget=false;
            Image Layer(string name){var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(obj.transform,false);var image=go.GetComponent<Image>();image.raycastTarget=false;image.preserveAspect=true;
                var r=image.rectTransform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=new Vector2(4,4);r.offsetMax=new Vector2(-4,-4);return image;}
            var gray=Layer("Gray");var color=Layer("Remaining");color.type=Image.Type.Filled;color.fillMethod=Image.FillMethod.Vertical;color.fillOrigin=(int)Image.OriginVertical.Bottom;
            return new Slot{Root=obj,Gray=gray,Color=color};
        }
        public void Clear(){foreach(var slot in slots)if(slot.Root!=null)slot.Root.SetActive(false);VisibleCount=0;((RectTransform)transform).sizeDelta=Vector2.zero;}
        private void OnDisable()=>Clear();
    }
}
