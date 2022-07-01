using System;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Importer.Common;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using System.IO;
using Stride.Graphics;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Core.Assets;
using Stride.Rendering;
using Stride.Assets.Materials;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{
    public Skeleton ConvertSkeleton(string fullPath)
    {
        var root = LoadGltf(fullPath);
        return ConvertSkeleton(root);// new Skeleton { Nodes = new List<ModelNodeDefinition>().ToArray() };
    }
    public Skeleton ConvertSkeleton(ModelRoot root)
    {

        Skeleton result = new Skeleton();
        var nodes = new List<ModelNodeDefinition>();
        var skins = root.LogicalSkins;
        //var skin = root.LogicalNodes.First(x => x.Mesh == root.LogicalMeshes.First()).Skin;
        // If there is no corresponding skins return a skeleton with 2 bones (an empty skeleton would make the editor crash)
        foreach (var skin in skins)
        {
            var jointList = Enumerable.Range(0, skin.JointsCount).Select(x => skin.GetJoint(x).Joint).ToList();
            nodes.AddRange(
                jointList
                .Select(
                    x =>
                    new ModelNodeDefinition
                    {
                        Name = x.Name ?? "Joint_" + x.LogicalIndex,
                        Flags = ModelNodeFlags.Default,
                        ParentIndex = jointList.IndexOf(x.VisualParent) + 1,
                        Transform = new TransformTRS
                        {
                            Position = x.LocalTransform.Translation.ToStride(),
                            Rotation = x.LocalTransform.Rotation.ToStride(),
                            Scale = x.LocalTransform.Scale.ToStride()
                        }

                    }
                )
            );
            // And insert a parent one not caught by the above function (GLTF does not consider the parent bone as a bone)

        }
        nodes.Insert(
                    0,
                    new ModelNodeDefinition
                    {
                        Name = "Armature",
                        Flags = ModelNodeFlags.EnableRender,
                        ParentIndex = -1,
                        Transform = new TransformTRS
                        {
                            Position = Vector3.Zero,
                            Rotation = Quaternion.Identity,
                            Scale = Vector3.Zero
                        }
                    });
        result.Nodes = nodes.ToArray();
        return result;
    }
}
