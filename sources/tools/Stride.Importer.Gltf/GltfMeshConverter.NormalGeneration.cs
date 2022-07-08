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


        public Triangle() { }
        public Vector3 FlatNormal => Vector3.Cross(Positions[1] - Positions[0], Positions[2] - Positions[0]);
    }
    public Vector3[] GenerateNormals(MeshPrimitive primitive, bool flatNormals = false)
    {
        var posits = primitive.GetVertexColumns().Positions.Select(x => x.ToStride()).ToList();
        List<Triangle> tris = primitive.GetTriangleIndices().Select(x => new Triangle { A = x.A, B = x.B, C = x.C, Positions = new Vector3[] { posits[x.A], posits[x.B], posits[x.C] }));
        if (flatNormals) return tris.Select(x => x.FlatNormal).SelectMany(x => new Vector3[]{x,x,x}).ToArray();
        else
        {
            foreach (var tri in tris)
            {
                tri.Normals[0] = Vector3.Normalize(tris.Where(x => (x.A == tri.A) || (x.B == tri.A) || (x.C == tri.A)).Select(x => x.FlatNormal).VectorAverage());
                tri.Normals[1] = Vector3.Normalize(tris.Where(x => (x.A == tri.B) || (x.B == tri.B) || (x.C == tri.B)).Select(x => x.FlatNormal).VectorAverage());
                tri.Normals[2] = Vector3.Normalize(tris.Where(x => (x.A == tri.C) || (x.B == tri.C) || (x.C == tri.C)).Select(x => x.FlatNormal).VectorAverage());
            }
            return tris.SelectMany(x => x.Normals).ToArray();
        }
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
