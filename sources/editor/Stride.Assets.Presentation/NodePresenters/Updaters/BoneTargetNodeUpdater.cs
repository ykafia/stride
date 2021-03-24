// Copyright (c) Stride contributors (https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Assets.Editor.Quantum.NodePresenters;
using Stride.Core.Assets.Editor.ViewModel;
using Stride.Assets.Models;
using Stride.Assets.Presentation.NodePresenters.Keys;
using Stride.Engine;
using Stride.Rendering;
using Stride.SpriteStudio.Offline;
using Stride.SpriteStudio.Runtime;

namespace Stride.Assets.Presentation.NodePresenters.Updaters
{
    internal sealed class BoneTargetNodeUpdater : AssetNodePresenterUpdaterBase
    {
        protected override void UpdateNode(IAssetNodePresenter node)
        {
            var entity = node.Root.Value as Entity;
            var asset = node.Asset;
            if (asset == null || entity == null)
                return;

            if (node.Name == nameof(BoneTargetComponent.Target) && node.Parent?.Value is BoneTargetComponent)
            {
                var parent = (IAssetNodePresenter)node.Parent;
                parent.AttachedProperties.Set(BoneTargetData.Key, GetAvailableNodesForLink(asset, (BoneTargetComponent)parent?.Value));
            }
        }

        private static IEnumerable<NodeInformation> GetAvailableNodesForLink(AssetViewModel viewModel, BoneTargetComponent BoneTargetComponent)
        {
            return GetAvailableNodesForLink(viewModel, BoneTargetComponent?.Target?.Model ?? BoneTargetComponent?.Entity?.Transform.Parent?.Entity?.Get<ModelComponent>()?.Model);
        }

        private static IEnumerable<NodeInformation> GetAvailableNodesForLink(AssetViewModel viewModel, Model model)
        {
            var parentModelAsset = viewModel?.AssetItem.Package.Session.FindAssetFromProxyObject(model);
            var modelAsset = parentModelAsset?.Asset as ModelAsset;
            if (modelAsset != null)
            {
                var skeletonAsset = parentModelAsset.Package.FindAssetFromProxyObject(modelAsset.Skeleton);
                if (skeletonAsset != null)
                {
                    return ((SkeletonAsset)skeletonAsset.Asset).Nodes;
                }
            }
            return Enumerable.Empty<NodeInformation>();
        }
    }
}
