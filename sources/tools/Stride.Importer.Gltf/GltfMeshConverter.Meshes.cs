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
using Stride.Graphics.Data;
using SharpGLTF.Geometry;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{
    public Model ExtractMeshes(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var model = LoadGltf(sourcePath);
        var textures = ExtractTextureDependencies(model, sourcePath);
        var animationNodes = ExtractAnimationNodes(model);
        var materials = ExtractMaterialsAsList(model, sourcePath, textures);
        var meshes = ExtractMeshParameters(model, sourcePath);
        return ExtractMeshes(model, materials);
    }

    public Model ExtractMeshes(ModelRoot root, SortedList<int, MaterialAsset> materials)
    {
        var result = new Model();

        List<Model> models = root.LogicalMeshes.Select(ConvertMeshes).ToList();
        return models[0];
    }

    private Model ConvertMeshes(SharpGLTF.Schema2.Mesh m)
    {
        return new Model
        {
            Meshes = m.Primitives.Select(ConvertPrimitives).ToList()
        };
    }

    private Rendering.Mesh ConvertPrimitives(MeshPrimitive primitive)
    {
        var primType = primitive.DrawPrimitiveType.AsSdPrim();
        var indices = primitive.GetIndices() ?? new List<uint> { 0,1,2 };
        var drawCount = primType == Graphics.PrimitiveType.TriangleList ? indices.Distinct().Count() : 0;
        var idBuff = SerializeIndexBuffer(Rewind(indices));
        //var vBuffs = SerializeVertexBuffer(primitive.GetVertexColumns());
        var vBuffs = SerializeVertexBuffer(primitive, indices.Distinct().Count());
        var draw = new MeshDraw
        {
            PrimitiveType = primType,
            DrawCount = drawCount,
            IndexBuffer = idBuff,
            VertexBuffers = vBuffs
        };
        return new Rendering.Mesh
        {
            Draw = draw,
            MaterialIndex = primitive.Material == null ? 0 : primitive.Material.LogicalIndex,
        };
    }

    public List<uint> Rewind(IList<uint> indices)
    {
        return indices
            .Chunk(3)
            .Select(tri => new uint[] { tri[0], tri[2], tri[1] })
            .SelectMany(x => x)
            .ToList();
    }

    private VertexBufferBinding[] SerializeVertexBuffer(MeshPrimitive primitive, int indexCount)
    {
        var result = new List<VertexBufferBinding>();
        var declarationList = new List<VertexElement>();
        var byteOffset = 0;
        foreach(var (k,v) in primitive.VertexAccessors)
        {
            declarationList.Add(
                (k,v.Format.ByteSize, v.Encoding) switch
                {
                    ("POSITION", 12, EncodingType.FLOAT) => VertexElement.Position<Vector3>(offsetInBytes: byteOffset),
                    ("NORMAL", 12, EncodingType.FLOAT) => VertexElement.Normal<Vector3>(offsetInBytes: byteOffset),
                    ("TEXCOORD_0", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(0, byteOffset),
                    ("TEXCOORD_1", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(1, byteOffset),
                    ("TEXCOORD_2", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(2, byteOffset),
                    ("TEXCOORD_3", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(3, byteOffset),
                    ("TANGENT", 16, EncodingType.FLOAT) => VertexElement.Tangent<Vector4>(offsetInBytes: byteOffset),
                    ("JOINTS_0", 8, EncodingType.UNSIGNED_SHORT) => new VertexElement(VertexElementUsage.BlendIndices,0, PixelFormat.R16G16B16A16_UInt, byteOffset),
                    ("WEIGHTS_0", 16, EncodingType.FLOAT) => new VertexElement(VertexElementUsage.BlendWeight, 0, PixelFormat.R32G32B32A32_Float, byteOffset),

                    _ => throw new NotImplementedException()
                }
            );
            byteOffset += v.Format.ByteSize;
        }
        List<byte> vertBuf = new();
        for(int i = 0; i < indexCount; i++)
        {
            foreach(var ve in declarationList)
            {
                vertBuf.AddRange(primitive.VertexAccessors[ve.SemanticName.ToGLTFAccessor(ve.SemanticIndex)].TryGetVertexBytes(i).ToArray());
            }
        }
        var declaration = new VertexDeclaration(declarationList.ToArray());
        var buff = GraphicsSerializerExtensions.ToSerializableVersion(new BufferData(BufferFlags.VertexBuffer, vertBuf.ToArray()));
        result.Add(new VertexBufferBinding(buff, declaration, primitive.GetVertexColumns().Positions.Count));
        return result.ToArray();
    }
    public IndexBufferBinding SerializeIndexBuffer(IList<uint> indices)
    {
        var buf = GraphicsSerializerExtensions.ToSerializableVersion(
            new BufferData(
                BufferFlags.IndexBuffer,
                indices.Select(BitConverter.GetBytes).SelectMany(x => x).ToArray()
            )
        );
        return new IndexBufferBinding(buf, true, indices.Count);
    }
}
