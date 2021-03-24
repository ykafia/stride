using System;
using System.Linq;
using Stride.Core.Assets;
using Stride.Core.Assets.Compiler;
using Stride.Engine;

namespace Stride.Assets.Entities.ComponentChecks
{
    /// <summary>
    /// Checks the validity of a <see cref="BoneTargetComponent"/>.
    /// </summary>
    public class BoneTargetComponentCheck : IEntityComponentCheck
    {
        /// <inheritdoc/>
        public bool AppliesTo(Type componentType)
        {
            return componentType == typeof(BoneTargetComponent);
        }

        /// <inheritdoc/>
        public void Check(EntityComponent component, Entity entity, AssetItem assetItem, string targetUrlInStorage, AssetCompilerResult result)
        {
            var boneTargetComponent = component as BoneTargetComponent;
            if (boneTargetComponent.IsValid)
            {
                result.Warning($"The Model Node Link between {entity.Name} and {boneTargetComponent.Target?.Entity.Name} is invalid.");
                boneTargetComponent.Target = null;
            }
        }
    }
}
