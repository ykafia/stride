// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Stride.Core.BuildEngine;
using Stride.Core.Serialization.Contents;
using Stride.Animations;
using Stride.Importer.Common;
using Stride.Rendering;
using Stride.Rendering.Data;

namespace Stride.Assets.Models
{
    [Description("Import Gltf")]
    public class ImportGltfCommand : ImportModelCommand
    {
        private static string[] supportedExtensions = GltfAssetImporter.FileExtensions.Split(';');

        /// <inheritdoc/>
        public override string Title { get { string title = "Import Assimp "; try { title += Path.GetFileName(SourcePath) ?? "[File]"; } catch { title += "[INVALID PATH]"; } return title; } }

        public static bool IsSupportingExtensions(string ext)
        {
            if (string.IsNullOrEmpty(ext))
                return false;

            var extToLower = ext.ToLowerInvariant();

            return supportedExtensions.Any(supExt => supExt.Equals(extToLower));
        }

        private Importer.Gltf.GltfMeshConverter CreateMeshConverter(ICommandContext commandContext)
        {
            return new Importer.Gltf.GltfMeshConverter(commandContext.Logger);
        }

        protected override Model LoadModel(ICommandContext commandContext, ContentManager contentManager)
        {
            var converter = CreateMeshConverter(commandContext);
            var sceneData = converter.ExtractMeshes(SourcePath.FullPath);
            return sceneData;
        }

        protected override Dictionary<string, AnimationClip> LoadAnimation(ICommandContext commandContext, ContentManager contentManager, out TimeSpan duration)
        {
            var meshConverter = this.CreateMeshConverter(commandContext);
            var sceneData = meshConverter.ConvertAnimation(SourcePath, Location);

            duration = sceneData.Duration;
            return sceneData.AnimationClips;
        }

        protected override Skeleton LoadSkeleton(ICommandContext commandContext, ContentManager contentManager)
        {
            var meshConverter = this.CreateMeshConverter(commandContext);
            var sceneData = meshConverter.ConvertSkeleton(SourcePath.FullPath);
            return sceneData;
        }

        public override string ToString()
        {
            return "Import Assimp " + base.ToString();
        }
    }
}
