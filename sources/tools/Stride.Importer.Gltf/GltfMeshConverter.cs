using System;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Importer.Common;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using System.IO;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{
    private Logger logger;


    public GltfMeshConverter(){}
    public GltfMeshConverter(Logger logger)
    {
        this.logger = logger;
    }

    public static ModelRoot LoadGltf(Stride.Core.IO.UFile sourcePath)
    {
        return sourcePath.GetFileExtension() switch
        {
            ".gltf" => SharpGLTF.Schema2.ModelRoot.Load(sourcePath),
            null => SharpGLTF.Schema2.ModelRoot.Load(sourcePath),
            ".glb" => SharpGLTF.Schema2.ModelRoot.ReadGLB(new FileStream(sourcePath.FullPath, FileMode.Open)),
            _ => throw new Exception("Unsupported file extension")
        };
    }



    public AnimationInfo ConvertAnimation(string fullPath, string v)
    {
        throw new NotImplementedException();
    }

    public List<string> ExtractAnimationNodes(ModelRoot root)
    {
        return root.LogicalAnimations.SelectMany(x => x.Channels).Select(x => x.TargetNode.Name).ToList();
    }

    public List<string> ExtractTextureDependencies(ModelRoot root, string fullPath)
    {
        // No textures
        List<string> result = new List<string>();

        if (root.LogicalTextures.Count == 0) return result;
        // Check process textures that have paths

        result.AddRange(root.LogicalTextures.Select(x => x.PrimaryImage.Content).Where(x => x.SourcePath != null).Select(x => x.SourcePath));
        foreach(var t in root.LogicalTextures.Where(x => x.PrimaryImage.Content.SourcePath == null))
        {
            var texPath = Path.Combine(
                Path.GetDirectoryName(fullPath),
                Path.GetFileNameWithoutExtension(fullPath),
                $"{t.LogicalIndex}.{t.PrimaryImage.Content.FileExtension}"
            );
            Directory.CreateDirectory(Path.GetDirectoryName(texPath));
            if(!File.Exists(texPath))
                File.WriteAllBytes(texPath,t.PrimaryImage.Content.Content.ToArray());
            result.Add(texPath);
        }
        return result;
    }

    public EntityInfo ExtractEntity(string fullPath, string outputFileName, bool extractTextureDependencies)
    {
        var fileName = Path.GetFileName(fullPath);
        var model = LoadGltf(fullPath);
        var textures = ExtractTextureDependencies(model, fullPath);
        return new EntityInfo
        {
            TextureDependencies = textures,
            AnimationNodes = ExtractAnimationNodes(model),
            Materials = ExtractMaterials(model,fullPath,textures)
        };
    }
}
