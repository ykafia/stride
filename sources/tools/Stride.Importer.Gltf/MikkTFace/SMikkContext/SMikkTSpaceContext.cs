using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Geometry;
using Stride.Core.Diagnostics;
using Stride.Importer.Common.MikkTFace;

namespace Stride.Importer.Gltf.MikkTFace.SMikkContext
{
    public partial class SMikkTSpaceContext : ISMikkTSpace
    {

        public static int MARK_DEGENERATE = 1;
        public static int QUAD_ONE_DEGEN_TRI = 2;
        public static int GROUP_WITH_ANY = 4;
        public static int ORIENT_PRESERVING = 8;
        public static int INTERNAL_RND_SORT_SEED = 39871946;
        static int iCells = 2048;

        public VertexBufferColumns Columns { get; set; }
        private Logger logger;

        public SMikkTSpaceContext(Logger logger, VertexBufferColumns cols)
        {
            this.logger = logger;
            Columns = cols;
        }


        public int MakeIndex(int a, int b) => a * 3 + b;
        public int GetNumFaces() => Columns.Positions.Count / 3;

        public int GetNumVerticesOfFace(int faceId) => 3;

        public Vector3 GetPosition(int idFace, int idVert) => Columns.Positions[idFace * 3 + idVert];

        public (int, int) IndexToData(int index) => (index / 3, index % 3);
        public Vector3 GetPosition(int index)
        {
            (var iF, var iI) = IndexToData(index);
            return GetPosition(iF, iI);
        }

        public Vector2 GetTexCoord(int idFace, int idVert) => Columns.TexCoords0[idFace * 3 + idVert];

        public Vector2 GetTexCoord(int index)
        {
            (var i, var j) = IndexToData(index);
            return GetTexCoord(i, j);
        }
        public Vector3 GetNormal(int idFace, int idVert) => Columns.Normals[idFace * 3 + idVert];
        public Vector3 GetNormal(int index)
        {
            (var iF, var iI) = IndexToData(index);
            return GetNormal(iF, iI);
        }
        public (Vector3, Vector3) SetTSpace(float magS, float magT, bool IsOrientationPreserving, int idFace, int idVert)
        {
            throw new NotImplementedException();
        }

        public Vector3 SetTSpaceBasic(float fSign, int idFace, int idVert)
        {
            throw new NotImplementedException();
        }

    }
    
}
