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
    struct Triangle
    {
        public int A = 0, B = 0, C = 0;
        public Vector3[] Positions = new Vector3[3];
        public Vector3[] Normals = new Vector3[3];
        public Vector2[] TexCoords = new Vector2[3];
        public Vector3[] Tangents = new Vector3[3];
        public Vector3[] BiTangents = new Vector3[3];

        public Triangle() { }
        public Vector3 FlatNormal => Vector3.Cross(Positions[1] - Positions[0], Positions[2] - Positions[0]);
    }
    public void GenerateNormals(MeshPrimitive primitive, out Vector3[] normals, out Vector3[] tangents, out Vector3[] bitangents, bool flatNormals = false)
    {
        var posits = primitive.GetVertexColumns().Positions.Select(x => x.ToStride()).ToList();
        IEnumerable<Triangle> tris = primitive
            .GetTriangleIndices()
            .Select(
                x => new Triangle 
                { 
                    A = x.A, 
                    B = x.B, 
                    C = x.C, 
                    Positions = new Vector3[] { posits[x.A], posits[x.B], posits[x.C] } 
                });
        //if (flatNormals)
        //{
        //    normals = tris.Select(x => x.FlatNormal).SelectMany(x => new Vector3[] { x, x, x }).ToArray();
        //}
        //else
        //{
        foreach (var tri in tris)
        {
            tri.Normals[0] = Vector3.Normalize(tris.Where(x => (x.A == tri.A) || (x.B == tri.A) || (x.C == tri.A)).Select(x => x.FlatNormal).VectorAverage());
            tri.Normals[1] = Vector3.Normalize(tris.Where(x => (x.A == tri.B) || (x.B == tri.B) || (x.C == tri.B)).Select(x => x.FlatNormal).VectorAverage());
            tri.Normals[2] = Vector3.Normalize(tris.Where(x => (x.A == tri.C) || (x.B == tri.C) || (x.C == tri.C)).Select(x => x.FlatNormal).VectorAverage());

            var edge1 = tri.Positions[1] - tri.Positions[0];
            var edge2 = tri.Positions[2] - tri.Positions[0];

            var uvEdge1 = tri.TexCoords[1] - tri.TexCoords[0];
            var uvEdge2 = tri.TexCoords[2] - tri.TexCoords[0];

            var dR = uvEdge1.X * uvEdge2.Y - uvEdge2.X * uvEdge1.Y;

            // Workaround to handle degenerated case
            // TODO: We need to understand more how we can handle this more accurately
            if (MathUtil.IsZero(dR))
            {
                dR = 1;
            }
            var r = 1.0f / dR;
            var t = (uvEdge2.Y * edge1 - uvEdge1.Y * edge2) * r;
            var b = (uvEdge1.X * edge2 - uvEdge2.X * edge1) * r;

            // Contribute to every vertex
            tri.Tangents[0] += t;
            tri.Tangents[1] += t;
            tri.Tangents[2] += t;

            tri.BiTangents[0] += b;
            tri.BiTangents[1] += b;
            tri.BiTangents[2] += b;


        }
        normals = tris.SelectMany(x => x.Normals).ToArray();
        tangents = tris.SelectMany(x => x.Tangents).ToArray();
        bitangents = tris.SelectMany(x => x.BiTangents).ToArray();
        //}
    }

}
public static class EnumerableExtension
{
    public static Vector3 VectorAverage(this IEnumerable<Vector3> source)
    {
        var x = Vector3.Zero;
        foreach (var v in source)
        {
            x = +v;
        }
        x /= source.Count();
        return x;
    }
}
