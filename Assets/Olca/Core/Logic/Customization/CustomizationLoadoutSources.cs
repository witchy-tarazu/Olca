using System;
using System.Collections.Generic;
using System.Linq;
using Olca.Core.Domain;
namespace Olca.Core.Logic.Customization
{
    /// <summary>集計規則を選ばず、有効な装備・配置の供給元を一個体ずつ渡す。</summary>
    public sealed class CustomizationLoadoutSources
    {
        public IReadOnlyList<string> Accessories { get; }
        public IReadOnlyList<PartPlacement> Parts { get; }
        public CustomizationLoadoutSources(CustomizationCatalog catalog, CustomizationProgress progress)
        {
            var draft = new CustomizationEditDraft(catalog, progress, 0);
            Accessories = Array.AsReadOnly(draft.Accessories.ToArray());
            Parts = Array.AsReadOnly(draft.Placements.ToArray());
        }
    }
}
