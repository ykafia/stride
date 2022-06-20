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
        return null;// new Skeleton { Nodes = new List<ModelNodeDefinition>().ToArray() };
    }
}
