using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Schema2;

namespace Stride.Importer.Gltf.MikkTSpace;

public interface MikkTSpaceContext
{

    public int GetNumFaces();

    public int GetNumVerticesOfFace(int face);

    public void GetPosition(int face, int vert, out float[] pos);

    public void GetNormal(int face, int vert, out float[] norm);

    public void GetTexCoord(int face, int vert, out float[] tex);

    public void SetTSpaceBasic(float[] tangent, float sign, int face, int vert);

    void SetTSpace(float[] tangent, float[] biTangent, float magS, float magT,
            bool isOrientationPreserving, int face, int vert);
}

public class MikkTSpaceImpl : MikkTSpaceContext
{

    MeshPrimitive mesh;

    public MikkTSpaceImpl(MeshPrimitive mesh)
    {
        this.mesh = mesh;
        //replacing any existing tangent buffer, if you came here you want them new.
        mesh.GetVertexColumns().Tangents = new List<Vector4>(mesh.GetVertexColumns().Positions.Count);
    }


    public int GetNumFaces()
    {
        return mesh.GetVertexColumns().Positions.Count / 3;
    }


    public int GetNumVerticesOfFace(int face)
    {
        return 3;
    }


    public void GetPosition(int face, int vert, out float[] pos)
    {
        int vertIndex = GetIndex(face, vert);
        var position = mesh.GetVertexColumns().Positions;
        pos = new float[] { position[vertIndex].X, position[vertIndex].Y, position[vertIndex].Z };
    }


    public void GetNormal(int face, int vert, out float[] normal)
    {
        int vertIndex = GetIndex(face, vert);
        var normals = mesh.GetVertexColumns().Normals;
        normal = new float[] { normals[vertIndex].X, normals[vertIndex].Y, normals[vertIndex].Z };

    }


    public void GetTexCoord(int face, int vert, out float[] tc)
    {
        int vertIndex = GetIndex(face, vert);
        var tcs = mesh.GetVertexColumns().TexCoords0;
        tc = new float[] { tcs[vertIndex].X, tcs[vertIndex].Y };
    }


    public void SetTSpaceBasic(float[] tangent, float sign, int face, int vert)
    {
        int vertIndex = GetIndex(face, vert);
        if (mesh.GetVertexColumns().Tangents == null)
            mesh.GetVertexColumns().Tangents = new List<Vector4>(mesh.GetVertexColumns().Positions.Count);
        var tangentBuffer = mesh.GetVertexColumns().Tangents;
        for (int i = tangentBuffer.Count - 1; i <= vertIndex; i++)
            tangentBuffer.Add(default);
        tangentBuffer[vertIndex] = new Vector4(tangent);
    }


    public void SetTSpace(float[] tangent, float[] biTangent, float magS, float magT, bool isOrientationPreserving, int face, int vert)
    {
        //Do nothing
    }

    private int GetIndex(int face, int vert)
    {
        var index = mesh.GetIndices();
        uint vertIndex = index[face * 3 + vert];
        return (int)vertIndex;
    }

}
