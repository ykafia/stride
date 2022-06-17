using System;
using Stride.Core.Diagnostics;
using Stride.Importer.Common;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{
    private Logger logger;


    public GltfMeshConverter(){}
    public GltfMeshConverter(Logger logger)
    {
        this.logger = logger;
    }




    public AnimationInfo ConvertAnimation(string fullPath, string v)
    {
        throw new NotImplementedException();
    }

    public EntityInfo ExtractEntity(string fullPath, string outputFileName, bool extractTextureDependencies)
    {
        throw new NotImplementedException();
    }
}
