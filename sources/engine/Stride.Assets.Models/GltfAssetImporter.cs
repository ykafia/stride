// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using Stride.Core.Assets;
using Stride.Core;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Animations;
using Stride.Assets.Textures;
using Stride.Importer.Common;
using Stride.Importer.Gltf;

namespace Stride.Assets.Models
{
    public class GltfAssetImporter : ModelAssetImporter
    {

        // Supported file extensions for this importer
        internal const string FileExtensions = ".gltf;.glb;";

        private static readonly Guid Uid = new Guid("30243FC0-CEC7-4433-977E-95DCA29D846E");

        public override Guid Id => Uid;

        public override string Description => "Gltf importer used for creating entities, 3D Models or animations assets";

        public override string SupportedFileExtensions => FileExtensions;

        /// <inheritdoc/>
        public override EntityInfo GetEntityInfo(UFile localPath, Logger logger, AssetImporterParameters importParameters)
        {
            var meshConverter = new GltfMeshConverter();

            if (!importParameters.InputParameters.TryGet(DeduplicateMaterialsKey, out var deduplicateMaterials))
                deduplicateMaterials = true;    // Dedupe is the default value

            var entityInfo = meshConverter.ExtractEntity(localPath.FullPath, null, importParameters.IsTypeSelectedForOutput(typeof(TextureAsset)));
            return entityInfo;
        }

        /// <inheritdoc/>
        public override void GetAnimationDuration(UFile localPath, Logger logger, AssetImporterParameters importParameters, out TimeSpan startTime, out TimeSpan endTime)
        {
            var meshConverter = new GltfMeshConverter(logger);
            var sceneData = meshConverter.ConvertAnimation(localPath.FullPath, "");

            startTime = CompressedTimeSpan.MaxValue; // This will go down, so we start from positive infinity
            endTime = CompressedTimeSpan.MinValue;   // This will go up, so we start from negative infinity

            foreach (var animationClip in sceneData.AnimationClips)
            {
                foreach (var animationCurve in animationClip.Value.Curves)
                {
                    foreach (var compressedTimeSpan in animationCurve.Keys)
                    {
                        if (compressedTimeSpan < startTime)
                            startTime = compressedTimeSpan;
                        if (compressedTimeSpan > endTime)
                            endTime = compressedTimeSpan;
                    }
                }
            }

            if (startTime == CompressedTimeSpan.MaxValue)
                startTime = CompressedTimeSpan.Zero;
            if (endTime == CompressedTimeSpan.MinValue)
                endTime = CompressedTimeSpan.Zero;
        }

        public override IEnumerable<AssetItem> Import(UFile localPath, AssetImporterParameters importParameters)
        {
            var assetItems = base.Import(localPath, importParameters);
            // Need to remember the DeduplicateMaterials setting per ModelAsset since the importer may be rerun on this asset
            if (!importParameters.InputParameters.TryGet(DeduplicateMaterialsKey, out var deduplicateMaterials))
                deduplicateMaterials = true;    // Dedupe is the default value
            foreach (var item in assetItems)
            {
                if (item.Asset is ModelAsset modelAsset)
                {
                    modelAsset.DeduplicateMaterials = deduplicateMaterials;
                }
            }
            return assetItems;
        }
    }
}
