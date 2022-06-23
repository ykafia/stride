using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Stride.Importer.Common.MikkTFace
{
    public interface ISMikkTSpace
    {
        /// <summary>
        /// Gets the number of faces
        /// </summary>
        /// <returns></returns>
        int GetNumFaces();
        /// <summary>
        /// Gets number of vertices for one face
        /// </summary>
        /// <param name="context"></param>
        /// <param name="faceId"></param>
        /// <returns></returns>
        int GetNumVerticesOfFace(int faceId);

        /// <summary>
        /// returns the position/normal/texcoord of the referenced face of vertex number iVert.
        /// iVert is in the range {0,1,2} for triangles and {0,1,2,3} for quads.
        /// </summary>
        /// <param name="idFace"></param>
        /// <param name="idVert"></param>
        /// <returns></returns>
        Vector3 GetPosition(int idFace, int idVert);
        /// <summary>
        /// returns the position/normal/texcoord of the referenced face of vertex number iVert.
        /// iVert is in the range {0,1,2} for triangles and {0,1,2,3} for quads.
        /// </summary>
        /// <param name="idFace"></param>
        /// <param name="idVert"></param>
        /// <returns></returns>
        Vector3 GetNormal(int idFace, int idVert);
        /// <summary>
        /// returns the position/normal/texcoord of the referenced face of vertex number iVert.
        /// iVert is in the range {0,1,2} for triangles and {0,1,2,3} for quads.
        /// </summary>
        /// <param name="idFace"></param>
        /// <param name="idVert"></param>
        /// <returns></returns>
        Vector2 GetTexCoord(int idFace, int idVert);


        /// <summary>
        /// either (or both) of the two setTSpace callbacks can be set.
        /// The call-back m_setTSpaceBasic() is sufficient for basic normal mapping.
        /// This function is used to return the tangent and fSign to the application.
        /// fvTangent is a unit length vector.
        /// For normal maps it is sufficient to use the following simplified version of the bitangent which is generated at pixel/vertex level.
        /// bitangent = fSign * cross(vN, tangent);
        /// Note that the results are returned unindexed. It is possible to generate a new index list
        /// But averaging/overwriting tangent spaces by using an already existing index list WILL produce INCRORRECT results.
        /// DO NOT! use an already existing index list.
        /// </summary>
        /// <param name="fSign"></param>
        /// <param name="idFace"></param>
        /// <param name="idVert"></param>
        /// <returns></returns>
        public Vector3 SetTSpaceBasic(float fSign, int idFace, int idVert);


        /// <summary>
        /// This function is used to return tangent space results to the application.
        /// fvTangent and fvBiTangent are unit length vectors and fMagS and fMagT are their
        /// true magnitudes which can be used for relief mapping effects.
        /// fvBiTangent is the "real" bitangent and thus may not be perpendicular to fvTangent.
        /// However, both are perpendicular to the vertex normal.
        /// For normal maps it is sufficient to use the following simplified version of the bitangent which is generated at pixel/vertex level.
        /// fSign = bIsOrientationPreserving ? 1.0f : (-1.0f);
        /// bitangent = fSign * cross(vN, tangent);
        /// Note that the results are returned unindexed. It is possible to generate a new index list
        /// But averaging/overwriting tangent spaces by using an already existing index list WILL produce INCRORRECT results.
        /// DO NOT! use an already existing index list.
        /// </summary>
        /// <param name="fvTangent"></param>
        /// <param name="fvBiTangent"></param>
        /// <param name="magS"></param>
        /// <param name="magT"></param>
        /// <param name="IsOrientationPreserving"></param>
        /// <param name="idFace"></param>
        /// <param name="idVert"></param>
        public (Vector3, Vector3) SetTSpace(float magS, float magT, bool IsOrientationPreserving, int idFace, int idVert);
    }
}
