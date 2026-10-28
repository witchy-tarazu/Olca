using System;
using System.Collections.Generic;
using Olca.Core.Domain;
using UnityEngine;

namespace Olca.Presentation.Battle
{
    public enum CombatIconId { Poison, Paralyze, Confusion, Blind, Guard, Barrier, Aura, Float, Concentrate }

    /// <summary>同じ効果種を複数の表示先で共有する。不明な状態を別アイコンへ置換しない。</summary>
    public static class CombatStatusIconCatalog
    {
        private sealed class Pair { internal Sprite Color, Gray; }
        private static readonly Dictionary<CombatIconId, Pair> loaded = new Dictionary<CombatIconId, Pair>();
        public static CombatIconId? ForStatus(StatusEffectType type)
        {
            switch(type) {
                case StatusEffectType.Poison:return CombatIconId.Poison;
                case StatusEffectType.Paralyze:return CombatIconId.Paralyze;
                case StatusEffectType.Confusion:return CombatIconId.Confusion;
                case StatusEffectType.Blind:return CombatIconId.Blind;
                default:return null;
            }
        }
        public static bool TryGet(CombatIconId id, out Sprite color, out Sprite gray)
        {
            color=gray=null;
            if(!Enum.IsDefined(typeof(CombatIconId),id))return false;
            if(loaded.TryGetValue(id,out var pair)&&pair.Color!=null&&pair.Gray!=null){color=pair.Color;gray=pair.Gray;return true;}
            var source=Resources.Load<TextAsset>("OlcaCombatStatus/"+id.ToString().ToLowerInvariant());
            if(source==null)return false;
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
            if(!texture.LoadImage(source.bytes,false)){DestroyOwned(texture);return false;}
            var pixels=texture.GetPixels32();
            for(int n=0;n<pixels.Length;n++){
                var p=pixels[n];byte l=(byte)((p.r*54+p.g*183+p.b*19)>>8);pixels[n]=new Color32(l,l,l,p.a);
            }
            var muted=new Texture2D(texture.width,texture.height,TextureFormat.RGBA32,false);
            muted.SetPixels32(pixels);muted.Apply(false,true);texture.Apply(false,true);
            Sprite Make(Texture2D image,string name){image.name=name;image.filterMode=FilterMode.Point;image.wrapMode=TextureWrapMode.Clamp;image.hideFlags=HideFlags.HideAndDontSave;
                var sprite=Sprite.Create(image,new Rect(0,0,image.width,image.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);sprite.name=name;sprite.hideFlags=HideFlags.HideAndDontSave;return sprite;}
            color=Make(texture,"status/"+id);gray=Make(muted,"status/"+id+"/gray");loaded[id]=new Pair{Color=color,Gray=gray};return true;
        }
        private static void DestroyOwned(UnityEngine.Object value){if(UnityEngine.Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
    }

    public sealed class CombatIconEntry
    {
        public string Id { get; }
        public CombatIconId Icon { get; }
        public float? RemainingFraction { get; }
        public CombatIconEntry(string id,CombatIconId icon,float? remainingFraction=null)
        {
            if(string.IsNullOrWhiteSpace(id)||!Enum.IsDefined(typeof(CombatIconId),icon)||remainingFraction.HasValue&&
                (float.IsNaN(remainingFraction.Value)||float.IsInfinity(remainingFraction.Value)||remainingFraction<0||remainingFraction>1))throw new ArgumentException("アイコンの識別/割合が不正です");
            Id=id;Icon=icon;RemainingFraction=remainingFraction;
        }
    }

    /// <summary>確定モデルの読取だけで列を構成し、時計や付与予約を進めない。</summary>
    public static class CombatStatusIcons
    {
        private static float? Fraction(int? remaining,int? duration)=>remaining.HasValue&&duration>0?Mathf.Clamp01(remaining.Value/(float)duration.Value):(float?)null;
        private static List<CombatIconEntry> Statuses(IReadOnlyList<StatusEffectSnapshot> values)
        {
            var result=new List<CombatIconEntry>();
            foreach(var value in values){var icon=CombatStatusIconCatalog.ForStatus(value.Type);if(icon.HasValue)result.Add(new CombatIconEntry("status/"+value.Type+"/"+value.Kind,icon.Value,Fraction(value.RemainingFrames,value.DurationFrames)));}
            return result;
        }
        private static void Buffs(List<CombatIconEntry> result,bool guard,bool barrier,bool aura,bool floating,bool concentrate,int? remaining,int? duration)
        {
            if(guard)result.Add(new CombatIconEntry("buff/guard",CombatIconId.Guard));
            if(barrier)result.Add(new CombatIconEntry("buff/barrier",CombatIconId.Barrier,Fraction(remaining,duration)));
            if(aura)result.Add(new CombatIconEntry("buff/aura",CombatIconId.Aura,Fraction(remaining,duration)));
            if(floating)result.Add(new CombatIconEntry("buff/float",CombatIconId.Float));
            if(concentrate)result.Add(new CombatIconEntry("buff/concentrate",CombatIconId.Concentrate));
        }
        public static IReadOnlyList<CombatIconEntry> ForPlayer(PlayerCharacter player)
        {
            if(player==null||player.IsDead)return Array.Empty<CombatIconEntry>();
            var result=Statuses(player.InspectStatuses());Buffs(result,player.Guard.IsActive,player.HasBarrier,player.HasAura,player.TerrainState.IsFloating,player.IsConcentrating,player.ProtectionRemainingFrames,player.ProtectionDurationFrames);return result.AsReadOnly();
        }
        public static IReadOnlyList<CombatIconEntry> ForEnemy(Enemy enemy)
        {
            if(enemy==null||enemy.IsDead)return Array.Empty<CombatIconEntry>();
            var result=Statuses(enemy.InspectStatuses());Buffs(result,enemy.Guard.IsActive,enemy.HasBarrier,enemy.HasAura,enemy.TerrainState.IsFloating,enemy.IsConcentrating,enemy.ProtectionRemainingFrames,enemy.ProtectionDurationFrames);return result.AsReadOnly();
        }
    }
}
