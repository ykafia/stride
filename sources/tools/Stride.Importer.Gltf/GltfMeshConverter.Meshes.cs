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

    public Model ExtractMeshes(ModelRoot root, SortedList<int,MaterialAsset> materials)
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
        var drawCount = primType == Graphics.PrimitiveType.TriangleList ? primitive.GetIndices().Distinct().Count() : 0;
        var idBuff = SerializeIndexBuffer(primitive.GetIndices());
        var vBuffs = SerializeVertexBuffer(primitive.GetVertexColumns());
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

    private VertexBufferBinding[] SerializeVertexBuffer(VertexBufferColumns cols)
    {
        var result = new List<VertexBufferBinding>();
        var bytes = new List<byte>();

        var vElems = new List<VertexElement>();
        if (cols.Positions?.Count > 0)
            vElems.Add(VertexElement.Position<Vector3>());
        if (cols.Normals?.Count > 0)
            vElems.Add(VertexElement.Normal<Vector3>());
        if (cols.Colors0?.Count > 0)
            vElems.Add(VertexElement.Color<Vector3>());
        if (cols.TexCoords0?.Count > 0)
            vElems.Add(VertexElement.TextureCoordinate<Vector2>(0));
        if (cols.TexCoords1?.Count > 0)
            vElems.Add(VertexElement.TextureCoordinate<Vector2>(1));
        if (cols.TexCoords2?.Count > 0)
            vElems.Add(VertexElement.TextureCoordinate<Vector2>(2));
        if (cols.TexCoords3?.Count > 0)
            vElems.Add(VertexElement.TextureCoordinate<Vector2>(3));
        if (cols.Tangents?.Count > 0)
            vElems.Add(VertexElement.Tangent<Vector3>());
        if (cols.Joints0?.Count > 0)
            vElems.Add(new VertexElement(VertexElementUsage.BlendIndices, PixelFormat.R32G32B32A32_Float));
        if (cols.Weights0?.Count > 0)
            vElems.Add(new VertexElement(VertexElementUsage.BlendWeight, PixelFormat.R32G32B32A32_Float));

        var declaration = new VertexDeclaration(vElems.ToArray());

        for (int i = 0; i < cols.Positions.Count; i ++)
        {
            if (cols.Positions?.Count > 0)
                bytes.AddRange(cols.Positions[i].ToBytes());
            if (cols.Normals?.Count > 0)
                bytes.AddRange(cols.Normals[i].ToBytes());
            if (cols.Colors0?.Count > 0)
                bytes.AddRange(cols.Colors0[i].ToBytes());
            if (cols.TexCoords0?.Count > 0)
                bytes.AddRange(cols.TexCoords0[i].ToBytes());
            if (cols.TexCoords1?.Count > 0)
                bytes.AddRange(cols.TexCoords1[i].ToBytes());
            if (cols.TexCoords2?.Count > 0)
                bytes.AddRange(cols.TexCoords2[i].ToBytes());
            if (cols.TexCoords3?.Count > 0)
                bytes.AddRange(cols.TexCoords3[i].ToBytes());
            if (cols.Tangents?.Count > 0)
                bytes.AddRange(cols.Tangents[i].ToBytes());
            if (cols.Joints0?.Count > 0)
                bytes.AddRange(cols.Joints0[i].ToBytes());
            //if (cols.Joints1?.Count > 0)
                //bytes.AddRange(cols.Joints1[i].ToBytes());
            if (cols.Weights0?.Count > 0)
                bytes.AddRange(cols.Weights0[i].ToBytes());
            //if (cols.Weights1?.Count > 0)
                //bytes.AddRange(cols.Weights1[i].ToBytes());
        }
        var buff = GraphicsSerializerExtensions.ToSerializableVersion(new BufferData(BufferFlags.VertexBuffer, bytes.ToArray()));
        result.Add(new VertexBufferBinding(buff, declaration, cols.Positions.Count));
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
