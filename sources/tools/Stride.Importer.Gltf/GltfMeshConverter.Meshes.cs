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
using Stride.Extensions;

namespace Stride.Importer.Gltf;

public partial class GltfMeshConverter
{
    public Model ExtractMeshes(string sourcePath)
    {
        var fileName = Path.GetFileName(sourcePath);
        var model = LoadGltf(sourcePath);
        var textures = ExtractTextureDependencies(model, sourcePath);
        var animationNodes = ExtractAnimationNodes(model);
        //var materials = ExtractMaterialsAsList(model, sourcePath, textures);
        var meshes = ExtractMeshParameters(model, sourcePath);
        return ExtractMeshes(model);
    }

    public Model ExtractMeshes(ModelRoot root)
    {
        var result = new Model();

        var meshes = root.LogicalMeshes
               .Select(x => (x.Primitives.Select(x => ConvertPrimitives(x)).ToList(), ConvertNumerics(x.VisualParents.First().WorldMatrix))).ToList();
        meshes.ForEach(mesh =>
            {
                var mat = mesh.Item2;
                mesh.Item1.ForEach(m => { foreach (var vb in m.Draw.VertexBuffers) { vb.TransformBuffer(ref mat); } });
            }
        );
        result = new Model { Meshes = meshes.SelectMany(x => x.Item1).ToList() };
        result.Skeleton = ConvertSkeleton(root);
        return result;
    }

    private Model ConvertMeshes(SharpGLTF.Schema2.Mesh m)
    {
        var model = new Model();
        foreach (var p in m.Primitives.Select(ConvertPrimitives))
        {
            model.Add(p);
        }
        return model;
    }

    private Rendering.Mesh ConvertPrimitives(MeshPrimitive primitive)
    {
        var primType = primitive.DrawPrimitiveType.AsSdPrim();
        var indices = new List<int>();
        if (primitive.GetIndices() != null)
            indices = primitive.GetIndices().Select(x => (int)x).ToList();
        else if (primitive.GetTriangleIndices() != null)
            indices = primitive.GetTriangleIndices().SelectMany(x => new int[] { x.A, x.C, x.B }).ToList();
        else
            throw new Exception("There is no indices, or indices not supported");
        var drawCount = indices.Count();
        var idBuff =
            SerializeIndexBuffer(
                primType == Graphics.PrimitiveType.TriangleList ?
                    primitive.GetTriangleIndices().SelectMany(x => new int[] { x.A, x.C, x.B }).ToList()
                    : primitive.GetIndices().Select(x => (int)x).ToList()
        );
        //var vBuffs = SerializeVertexBuffer(primitive.GetVertexColumns());
        var vBuffs = SerializeVertexBuffer(primitive);
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

    public List<int> Rewind(IEnumerable<int> indices)
    {
        return indices
            .Chunk(3)
            .Select(tri => new int[] { tri[0], tri[2], tri[1] })
            .SelectMany(x => x)
            .ToList();
    }

    private VertexBufferBinding[] SerializeVertexBuffer(MeshPrimitive primitive)
    {
        var result = new List<VertexBufferBinding>();
        var declarationList = new List<VertexElement>();

        //var cols = primitive.GetVertexColumns();
        //if (cols.Positions != null)
        //    declarationList.Add(VertexElement.Position<Vector3>());
        //if (cols.Normals != null)
        //    declarationList.Add(VertexElement.Normal<Vector3>());

        var byteOffset = 0;

        foreach (var (k, v) in primitive.VertexAccessors)
        {
            declarationList.Add(
                (k, v.Format.ByteSize, v.Encoding) switch
                {
                    // Default values
                    ("POSITION", 12, EncodingType.FLOAT) => VertexElement.Position<Vector3>(),
                    ("NORMAL", 12, EncodingType.FLOAT) => VertexElement.Normal<Vector3>(),
                    ("COLOR_0", 16, EncodingType.FLOAT) => VertexElement.Color<Vector4>(0),
                    ("COLOR_1", 16, EncodingType.FLOAT) => VertexElement.Color<Vector4>(1),

                    ("TEXCOORD_0", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(0),
                    ("TEXCOORD_1", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(1),
                    ("TEXCOORD_2", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(2),
                    ("TEXCOORD_3", 8, EncodingType.FLOAT) => VertexElement.TextureCoordinate<Vector2>(3),

                    ("TANGENT", 16, EncodingType.FLOAT) => VertexElement.Tangent<Vector4>(),
                    ("JOINTS_0", 8, EncodingType.UNSIGNED_SHORT) => new VertexElement(VertexElementUsage.BlendIndices, 0, PixelFormat.R16G16B16A16_UInt, byteOffset),
                    ("WEIGHTS_0", 16, EncodingType.FLOAT) => new VertexElement(VertexElementUsage.BlendWeight, 0, PixelFormat.R32G32B32A32_Float, byteOffset),

                    _ => throw new NotImplementedException($"Format for {k} with {v.Encoding} and {v.Format.ByteSize} is not yet supported")
                }
            );
            byteOffset += v.Format.ByteSize;
        }
        List<byte[]> generatedNormalsBytes = new();
        List<Vector3[]> generatedNormals = new();

        bool hasNormals = true;
        if (!declarationList.Any(x => x.SemanticName == "NORMAL"))
        {
            declarationList.Add(VertexElement.Normal<Vector3>());
            declarationList.Add(VertexElement.Tangent<Vector3>());
            declarationList.Add(VertexElement.BiTangent<Vector3>());

            hasNormals = false;
            GenerateNormals(primitive, out var normals, out var tangents, out var bitangents);
            for (int i = 0; i < normals.Length; i++)
            {
                var nbuf = new byte[3 * 4 * 3];
                System.Buffer.BlockCopy(normals[i].ToArray(),0,nbuf,0,nbuf.Length);
                System.Buffer.BlockCopy(tangents[i].ToArray(),3*4,nbuf,0,nbuf.Length);
                System.Buffer.BlockCopy(bitangents[i].ToArray(),3*4*2,nbuf,0,nbuf.Length);
                generatedNormalsBytes.Add(nbuf);
            }
        }
        List<byte> vertBuf = new();
        for (int i = 0; i < primitive.GetVertexColumns().Positions.Count; i++)
        {
            foreach (var ve in declarationList)
            {
                if (!hasNormals && ve.SemanticName == "NORMAL")
                    vertBuf.AddRange(generatedNormalsBytes[i]);
                else 
                    vertBuf.AddRange(primitive.VertexAccessors[ve.SemanticName.ToGLTFAccessor(ve.SemanticIndex)].TryGetVertexBytes(i).ToArray());
                //vertBuf.AddRange(
                //    ve.SemanticName switch
                //    {
                //        "POSITION" => primitive.GetVertexColumns().Positions[i].ToBytes(),
                //        "NORMAL" => primitive.GetVertexColumns().Normals[i].ToBytes(),
                //        "TEXCOORD" => primitive.GetVertexColumns().TexCoords0[i].ToBytes(),
                //        "TANGENT" => primitive.GetVertexColumns().Tangents[i].ToBytes(),
                //        "COLOR" => primitive.GetVertexColumns().Colors0[i].ToBytes(),
                //        _ => throw new NotImplementedException(),
                //    }
                //);
            }
        }
        var declaration = new VertexDeclaration(declarationList.ToArray());
        var buff = GraphicsSerializerExtensions.ToSerializableVersion(new BufferData(BufferFlags.VertexBuffer, vertBuf.ToArray()));
        result.Add(new VertexBufferBinding(buff, declaration, primitive.GetVertexColumns().Positions.Count));
        return result.ToArray();
    }
    public IndexBufferBinding SerializeIndexBuffer(List<int> indices)
    {
        var buf = GraphicsSerializerExtensions.ToSerializableVersion(
            new BufferData
            {
                BufferFlags = BufferFlags.IndexBuffer,
                Content = indices.Select(BitConverter.GetBytes).SelectMany(x => x).ToArray(),
                Usage = GraphicsResourceUsage.Default,
                StructureByteStride = 4
            }
        );
        return new IndexBufferBinding(buf, true, indices.Count);
    }

    public static Matrix ConvertNumerics(System.Numerics.Matrix4x4 mat)
    {
        return new Matrix(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44
            );
    }
}
