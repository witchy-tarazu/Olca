using System;
using UnityEngine;
namespace Olca.Unity
{
    /// <summary>Worldの共有import素材取得だけを担う。生成Sprite/Texture/演出の寿命はViewへ残す。</summary>
    public sealed class WorldVisualResources
    {
        private readonly ResourceManager resources;
        public WorldVisualResources(ResourceManager resources){this.resources=resources??throw new ArgumentNullException(nameof(resources));}
        public T Find<T>(string path) where T:UnityEngine.Object=>resources.FindShared<T>(path);
        public Material Ground=>resources.GetShared<Material>("OlcaSystem/WorldGround");
        public Material Background=>resources.GetShared<Material>("OlcaSystem/WorldDungeonBackground");
    }
}
