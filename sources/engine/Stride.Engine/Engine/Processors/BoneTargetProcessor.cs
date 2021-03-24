// Copyright (c) Stride contributors (https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using Stride.Rendering;

namespace Stride.Engine.Processors
{
    public class BoneTargetProcessor : EntityProcessor<BoneTargetComponent>
    {
        public Dictionary<BoneTargetComponent, BoneTargetComponent>.KeyCollection BoneTargetComponents => ComponentDatas.Keys;

        protected override void OnEntityComponentAdding(Entity entity, BoneTargetComponent component, BoneTargetComponent data)
        {
            //populate the valid property
            component.ValidityCheck();

            entity.EntityManager.HierarchyChanged += component.OnHierarchyChanged;
        }

        protected override void OnEntityComponentRemoved(Entity entity, BoneTargetComponent component, BoneTargetComponent data)
        {
            // Reset TransformLink
            
            entity.EntityManager.HierarchyChanged -= component.OnHierarchyChanged;
        }

        public BoneTargetProcessor()
        {
            Order = -300;
        }
    }
}
