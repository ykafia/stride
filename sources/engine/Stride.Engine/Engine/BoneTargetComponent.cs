// Copyright (c) Stride contributors (https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Linq;
using Stride.Core;
using Stride.Core.Collections;
using Stride.Engine.Design;
using Stride.Engine.Processors;

namespace Stride.Engine
{
    [DataContract("BoneTargetComponent")]
    [Display("Bone Target", Expand = ExpandRule.Once)]
    [DefaultEntityComponentProcessor(typeof(BoneTargetProcessor))]
    [ComponentOrder(1450)]
    [ComponentCategory("Model")]
    public sealed class BoneTargetComponent : EntityComponent
    {

        [DataMemberIgnore]
        public bool IsValid { get; private set; }

        /// <summary>
        /// Gets or sets the model which contains the hierarchy to use.
        /// </summary>
        /// <value>
        /// The model which contains the hierarchy to use.
        /// </value>
        /// <userdoc>The model that contains the skeleton to attach this entity to. If null, the entity attaches to the parent. 
        [DataMember(10)]
        [Display("Model (parent if not selected)")]
        public ModelComponent Target { get; set; }

        /// <summary>
        /// Gets or sets the name of the node.
        /// </summary>
        /// <value>
        /// The name of the node.
        /// </value>
        /// <userdoc>The bone/joint to use as a target.</userdoc>
        [DataMember(20)]
        [Display("Bone")]
        public string NodeName { get; set; }

        internal void OnHierarchyChanged(object sender, Entity entity)
        {
            if (entity == null || entity.Id != Target?.Entity.Id) return;
            ValidityCheck();
        }

        public void ValidityCheck()
        {
            this.IsValid = Target.Skeleton.Nodes.Select(x => x.Name).Contains(NodeName);
        }
    }
}
