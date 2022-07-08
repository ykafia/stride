using System;
using System.Linq;
using Stride.Core.Diagnostics;
using Stride.Importer.Common;
using SharpGLTF.Schema2;
using System.Collections.Generic;
using System.IO;
using Stride.Assets.Materials;
using Stride.Graphics;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Core.Mathematics;
using Stride.Core.Serialization;
using Stride.Core.Assets;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{


    public static Graphics.Texture GenerateTexture(string sourceTextureFile, Vector2 textureUVscaling, TextureAddressMode addressModeU = TextureAddressMode.Wrap, TextureAddressMode addressModeV = TextureAddressMode.Wrap, string vfsOutputPath = "")
    {
        var textureFileName = Path.GetFileNameWithoutExtension(sourceTextureFile);

        var uvScaling = textureUVscaling;
        var textureName = textureFileName;

        return AttachedReferenceManager.CreateProxyObject<Graphics.Texture>(AssetId.Empty, textureName);

        
    }

    public static ComputeTextureScalar GenerateTextureScalar(string sourceTextureFile, TextureCoordinate textureUVSetIndex, Vector2 textureUVscaling, TextureAddressMode addressModeU = TextureAddressMode.Wrap, TextureAddressMode addressModeV = TextureAddressMode.Wrap, string vfsOutputPath = "")
    {
        var textureFileName = Path.GetFileNameWithoutExtension(sourceTextureFile);

        var uvScaling = textureUVscaling;
        var textureName = textureFileName;

        var texture = AttachedReferenceManager.CreateProxyObject<Graphics.Texture>(AssetId.Empty, textureName);

        var currentTexture =
            new ComputeTextureScalar(texture, textureUVSetIndex, uvScaling, Vector2.Zero)
            {
                AddressModeU = addressModeU,
                AddressModeV = addressModeV
            };

        return currentTexture;
    }

    public static ComputeTextureColor GenerateTextureColor(string sourceTextureFile, TextureCoordinate textureUVSetIndex, Vector2 textureUVscaling, TextureAddressMode addressModeU = TextureAddressMode.Wrap, TextureAddressMode addressModeV = TextureAddressMode.Wrap, string vfsOutputPath = "")
    {
        var textureFileName = Path.GetFileNameWithoutExtension(sourceTextureFile);

        var uvScaling = textureUVscaling;
        var textureName = textureFileName;

        var texture = AttachedReferenceManager.CreateProxyObject<Graphics.Texture>(AssetId.Empty, textureName);


        var currentTexture =
            new ComputeTextureColor(texture, textureUVSetIndex, uvScaling, Vector2.Zero)
            {
                AddressModeU = addressModeU,
                AddressModeV = addressModeV
            };

        return currentTexture;
    }
    public static Dictionary<string, MaterialAsset> ExtractMaterials(ModelRoot root, string sourcePath, List<string> textures)
    {
        var materials = new Dictionary<string, MaterialAsset>();
        foreach(var mat in root.LogicalMaterials)
        {
            var material = new MaterialAsset
            {
                Attributes = new MaterialAttributes()
            };
            foreach (var chan in mat.Channels)
            {

                if (chan.Texture != null && !chan.HasDefaultContent)
                {

                    var gltfImg = chan.Texture.PrimaryImage;
                    var imgPath = gltfImg.Content.SourcePath ?? textures.First(x => Path.GetFileNameWithoutExtension(x) == gltfImg.LogicalIndex.ToString());

                    switch (chan.Key)
                    {
                        case "BaseColor":
                            material.Attributes.Diffuse = new MaterialDiffuseMapFeature(GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            material.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
                            break;
                        case "MetallicRoughness":
                            var tex = GenerateTexture(imgPath, Vector2.One));
                            var metal = new ComputeTextureScalar(tex, (TextureCoordinate)chan.TextureCoordinate, Vector2.One, Vector2.Zero) { Channel = ColorChannel.R };
                            var gloss = new ComputeTextureScalar(tex, (TextureCoordinate)chan.TextureCoordinate, Vector2.One, Vector2.Zero) { Channel = ColorChannel.G };
                            material.Attributes.MicroSurface = new MaterialGlossinessMapFeature(gloss);
                            material.Attributes.Specular = new MaterialMetalnessMapFeature(metal);
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "SpecularColor":
                            material.Attributes.Specular = new MaterialSpecularMapFeature() { SpecularMap = GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One)};
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        //case "SpecularFactor":
                        //    material.Attributes.Specular = new MaterialSpecularMapFeature() { Intensity = specularFactor };
                        //    material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                        //    break;
                        case "Normal":
                            material.Attributes.Surface = new MaterialNormalMapFeature(GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            break;
                        case "Occlusion":
                            material.Attributes.Occlusion = new MaterialOcclusionMapFeature();
                            break;
                        case "Emissive":
                            material.Attributes.Emissive = new MaterialEmissiveMapFeature(GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            break;
                    }
                }
                else if (chan.Texture == null && !chan.HasDefaultContent)
                {
                    var vt = new ComputeColor(chan.Parameters.ToColor());
                    var x = new ComputeFloat(chan.Parameters.GetX());


                    switch (chan.Key)
                    {
                        case "BaseColor":
                            material.Attributes.Diffuse = new MaterialDiffuseMapFeature(vt);
                            material.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
                            //material.Attributes.Transparency = new MaterialTransparencyBlendFeature();
                            break;
                        case "MetallicRoughness":
                            var metallic = new ComputeFloat((float)chan.Parameters.First(x => x.Name.StartsWith("MetallicFactor")).Value);
                            var roughness = new ComputeFloat((float)chan.Parameters.First(x => x.Name.StartsWith("RoughnessFactor")).Value);
                            material.Attributes.MicroSurface =
                                new MaterialGlossinessMapFeature(roughness) { Invert = true};
                            material.Attributes.Specular = new MaterialMetalnessMapFeature(metallic);
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "SpecularColor":
                            var specularColor = new ComputeColor(((System.Numerics.Vector3)chan.Parameters.First(x => x.Name.StartsWith("RGB")).Value).ToColor());
                            material.Attributes.Specular = new MaterialSpecularMapFeature() { SpecularMap = specularColor};
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "SpecularFactor":
                            var specularFactor = new ComputeFloat((float)chan.Parameters[0].Value);
                            if (material.Attributes.Specular is MaterialMetalnessMapFeature)
                                material.Attributes.Specular = new MaterialSpecularMapFeature() { SpecularMap = new ComputeColor(Color.Gray), Intensity = specularFactor };
                            else if (material.Attributes.Specular is MaterialSpecularMapFeature spec)
                                spec.Intensity = specularFactor;
                            break;
                        case "Normal":
                            material.Attributes.Surface = new MaterialNormalMapFeature(vt) { IsXYNormal = true };
                            break;
                        case "Occlusion":
                            material.Attributes.Occlusion = new MaterialOcclusionMapFeature() { CavityMap = vt as IComputeScalar };
                            break;
                        case "Emissive":
                            material.Attributes.Emissive = new MaterialEmissiveMapFeature(vt);
                            break;
                    }
                }

            }
            material.Attributes.CullMode = CullMode.Back;
            var materialName = Path.GetFileNameWithoutExtension(sourcePath) + "_" + (mat.Name ?? "Material") + "_" + mat.LogicalIndex;

            materials.TryAdd(materialName, material);
        }
        return materials;
    }

    public static SortedList<int, MaterialAsset> ExtractMaterialsAsList(ModelRoot root, string sourcePath, List<string> textures)
    {
        var materials = new SortedList<int, MaterialAsset>();
        foreach (var mat in root.LogicalMaterials)
        {
            var material = new MaterialAsset
            {
                Attributes = new MaterialAttributes()
            };
            foreach (var chan in mat.Channels)
            {

                if (chan.Texture != null && !chan.HasDefaultContent)
                {

                    var gltfImg = chan.Texture.PrimaryImage;
                    var imgPath = gltfImg.Content.SourcePath ?? textures.First(x => Path.GetFileNameWithoutExtension(x) == gltfImg.LogicalIndex.ToString());

                    switch (chan.Key)
                    {
                        case "BaseColor":
                            material.Attributes.Diffuse = new MaterialDiffuseMapFeature(GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            material.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
                            break;
                        case "MetallicRoughness":
                            material.Attributes.MicroSurface = new MaterialGlossinessMapFeature(new ComputeFloat(0.5f));
                            material.Attributes.Specular = new MaterialMetalnessMapFeature(GenerateTextureScalar(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "Normal":
                            material.Attributes.Surface = new MaterialNormalMapFeature(GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            break;
                        case "Occlusion":
                            material.Attributes.Occlusion = new MaterialOcclusionMapFeature();
                            break;
                        case "Emissive":
                            material.Attributes.Emissive = new MaterialEmissiveMapFeature(GenerateTextureColor(imgPath, (TextureCoordinate)chan.TextureCoordinate, Vector2.One));
                            break;
                    }
                }
                else if (chan.Texture == null && !chan.HasDefaultContent)
                {
                    var vt = new ComputeColor(chan.Parameters.ToColor());
                    var x = new ComputeFloat(chan.Parameters.GetX());
                    

                    switch (chan.Key)
                    {
                        case "BaseColor":
                            material.Attributes.Diffuse = new MaterialDiffuseMapFeature(vt);
                            material.Attributes.DiffuseModel = new MaterialDiffuseLambertModelFeature();
                            //material.Attributes.Transparency = new MaterialTransparencyBlendFeature();
                            break;
                        case "MetallicRoughness":
                            var metallic = new ComputeFloat((float)chan.Parameters.First(x => x.Name.StartsWith("MetallicFactor")).Value);
                            var rougness = new ComputeFloat((float)chan.Parameters.First(x => x.Name.StartsWith("RoughnessFactor")).Value);
                            material.Attributes.MicroSurface = 
                                new MaterialGlossinessMapFeature(rougness)
                                {
                                    Invert = true
                                };
                            material.Attributes.Specular = new MaterialMetalnessMapFeature(metallic);
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "SpecularColor":
                            var specularColor = new ComputeColor(((System.Numerics.Vector3)chan.Parameters.First(x => x.Name.StartsWith("RGB")).Value).ToColor());
                            material.Attributes.Specular = new MaterialSpecularMapFeature() { SpecularMap = specularColor };
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "SpecularFactor":
                            // TODO : use specular map with ComputeColor grey + factor is intensity
                            var specularFactor = new ComputeFloat((float)chan.Parameters.First(x => x.Name.StartsWith("SpecularFactor")).Value);
                            var white = new ComputeColor(Color.White);
                            material.Attributes.Specular = new MaterialSpecularMapFeature() { SpecularMap = white, Intensity = specularFactor};
                            material.Attributes.SpecularModel = new MaterialSpecularMicrofacetModelFeature();
                            break;
                        case "Normal":
                            material.Attributes.Surface = new MaterialNormalMapFeature(vt) { IsXYNormal = true };
                            break;
                        case "Occlusion":
                            material.Attributes.Occlusion = new MaterialOcclusionMapFeature() { CavityMap = vt as IComputeScalar };
                            break;
                        case "Emissive":
                            material.Attributes.Emissive = new MaterialEmissiveMapFeature(vt);
                            break;
                    }
                }

            }
            material.Attributes.CullMode = CullMode.Back;
            var materialKey = mat.LogicalIndex;

            materials.TryAdd(materialKey, material);
        }
        return materials;
    }
}
