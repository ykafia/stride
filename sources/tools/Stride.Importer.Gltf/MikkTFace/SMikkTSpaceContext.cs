using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Geometry;

namespace Stride.Importer.Common.MikkTFace
{
    public class SMikkTSpaceContext : ISMikkTSpace
    {

        public VertexBufferColumns Columns { get; set; }

        

        public int GetNumberOfFace() => Columns.Positions.Count / 3;

        public int GetNumberOfVerticesFor(SMikkTSpaceContext context, int faceId) => 3;

        public Vector3 GetPosition(int idFace, int idVert) => Columns.Positions[idFace * 3 + idVert];
        public Vector2 GetTexCoord(int idFace, int idVert) => Columns.TexCoords0[idFace * 3 + idVert];
        public Vector3 GetNormal(int idFace, int idVert) => Columns.Normals[idFace * 3 + idVert];

        public void SetTSpace(float[] fvTangent, float[] fvBiTangent, float magS, float magT, bool IsOrientationPreserving, int idFace, int idVert)
        {
            throw new NotImplementedException();
        }

        public Vector3 SetTSpaceBasic(float fSign, int idFace, int idVert)
        {
            throw new NotImplementedException();
        }

        public bool GenTangSpaceDefault()
        {
            return GenTangSpace(180.0f);
        }

        public bool GenTangSpace(float fAngularThreshold)
        {
            // count nr_triangles
            int* piTriListIn = NULL, *piGroupTrianglesBuffer = NULL;
            STriInfo* pTriInfos = NULL;
            SGroup* pGroups = NULL;
            STSpace* psTspace = NULL;
            int iNrTrianglesIn = 0, f = 0, t = 0, i = 0;
            int iNrTSPaces = 0, iTotTris = 0, iDegenTriangles = 0, iNrMaxGroups = 0;
            int iNrActiveGroups = 0, index = 0;
            const int iNrFaces = pContext->m_pInterface->m_getNumFaces(pContext);
            tbool bRes = TFALSE;
            const float fThresCos = (float)cos((fAngularThreshold * (float)M_PI) / 180.0f);

            // verify all call-backs have been set
            if (pContext->m_pInterface->m_getNumFaces == NULL ||
                pContext->m_pInterface->m_getNumVerticesOfFace == NULL ||
                pContext->m_pInterface->m_getPosition == NULL ||
                pContext->m_pInterface->m_getNormal == NULL ||
                pContext->m_pInterface->m_getTexCoord == NULL)
                return TFALSE;

            // count triangles on supported faces
            for (f = 0; f < iNrFaces; f++)
            {
                const int verts = pContext->m_pInterface->m_getNumVerticesOfFace(pContext, f);
                if (verts == 3) ++iNrTrianglesIn;
                else if (verts == 4) iNrTrianglesIn += 2;
            }
            if (iNrTrianglesIn <= 0) return TFALSE;

            // allocate memory for an index list
            piTriListIn = (int*)malloc(sizeof(int) * 3 * iNrTrianglesIn);
            pTriInfos = (STriInfo*)malloc(sizeof(STriInfo) * iNrTrianglesIn);
            if (piTriListIn == NULL || pTriInfos == NULL)
            {
                if (piTriListIn != NULL) free(piTriListIn);
                if (pTriInfos != NULL) free(pTriInfos);
                return TFALSE;
            }

            // make an initial triangle --> face index list
            iNrTSPaces = GenerateInitialVerticesIndexList(pTriInfos, piTriListIn, pContext, iNrTrianglesIn);

            // make a welded index list of identical positions and attributes (pos, norm, texc)
            //printf("gen welded index list begin\n");
            GenerateSharedVerticesIndexList(piTriListIn, pContext, iNrTrianglesIn);
            //printf("gen welded index list end\n");

            // Mark all degenerate triangles
            iTotTris = iNrTrianglesIn;
            iDegenTriangles = 0;
            for (t = 0; t < iTotTris; t++)
            {
                const int i0 = piTriListIn[t * 3 + 0];
                const int i1 = piTriListIn[t * 3 + 1];
                const int i2 = piTriListIn[t * 3 + 2];
                const SVec3 p0 = GetPosition(pContext, i0);
                const SVec3 p1 = GetPosition(pContext, i1);
                const SVec3 p2 = GetPosition(pContext, i2);
                if (veq(p0, p1) || veq(p0, p2) || veq(p1, p2))  // degenerate
                {
                    pTriInfos[t].iFlag |= MARK_DEGENERATE;
                    ++iDegenTriangles;
                }
            }
            iNrTrianglesIn = iTotTris - iDegenTriangles;

            // mark all triangle pairs that belong to a quad with only one
            // good triangle. These need special treatment in DegenEpilogue().
            // Additionally, move all good triangles to the start of
            // pTriInfos[] and piTriListIn[] without changing order and
            // put the degenerate triangles last.
            DegenPrologue(pTriInfos, piTriListIn, iNrTrianglesIn, iTotTris);


            // evaluate triangle level attributes and neighbor list
            //printf("gen neighbors list begin\n");
            InitTriInfo(pTriInfos, piTriListIn, pContext, iNrTrianglesIn);
            //printf("gen neighbors list end\n");


            // based on the 4 rules, identify groups based on connectivity
            iNrMaxGroups = iNrTrianglesIn * 3;
            pGroups = (SGroup*)malloc(sizeof(SGroup) * iNrMaxGroups);
            piGroupTrianglesBuffer = (int*)malloc(sizeof(int) * iNrTrianglesIn * 3);
            if (pGroups == NULL || piGroupTrianglesBuffer == NULL)
            {
                if (pGroups != NULL) free(pGroups);
                if (piGroupTrianglesBuffer != NULL) free(piGroupTrianglesBuffer);
                free(piTriListIn);
                free(pTriInfos);
                return TFALSE;
            }
            //printf("gen 4rule groups begin\n");
            iNrActiveGroups =
                Build4RuleGroups(pTriInfos, pGroups, piGroupTrianglesBuffer, piTriListIn, iNrTrianglesIn);
            //printf("gen 4rule groups end\n");

            //

            psTspace = (STSpace*)malloc(sizeof(STSpace) * iNrTSPaces);
            if (psTspace == NULL)
            {
                free(piTriListIn);
                free(pTriInfos);
                free(pGroups);
                free(piGroupTrianglesBuffer);
                return TFALSE;
            }
            memset(psTspace, 0, sizeof(STSpace) * iNrTSPaces);
            for (t = 0; t < iNrTSPaces; t++)
            {
                psTspace[t].vOs.x = 1.0f; psTspace[t].vOs.y = 0.0f; psTspace[t].vOs.z = 0.0f; psTspace[t].fMagS = 1.0f;
                psTspace[t].vOt.x = 0.0f; psTspace[t].vOt.y = 1.0f; psTspace[t].vOt.z = 0.0f; psTspace[t].fMagT = 1.0f;
            }

            // make tspaces, each group is split up into subgroups if necessary
            // based on fAngularThreshold. Finally a tangent space is made for
            // every resulting subgroup
            //printf("gen tspaces begin\n");
            bRes = GenerateTSpaces(psTspace, pTriInfos, pGroups, iNrActiveGroups, piTriListIn, fThresCos, pContext);
            //printf("gen tspaces end\n");

            // clean up
            free(pGroups);
            free(piGroupTrianglesBuffer);

            if (!bRes)  // if an allocation in GenerateTSpaces() failed
            {
                // clean up and return false
                free(pTriInfos); free(piTriListIn); free(psTspace);
                return TFALSE;
            }


            // degenerate quads with one good triangle will be fixed by copying a space from
            // the good triangle to the coinciding vertex.
            // all other degenerate triangles will just copy a space from any good triangle
            // with the same welded index in piTriListIn[].
            DegenEpilogue(psTspace, pTriInfos, piTriListIn, pContext, iNrTrianglesIn, iTotTris);

            free(pTriInfos); free(piTriListIn);

            index = 0;
            for (f = 0; f < iNrFaces; f++)
            {
                const int verts = pContext->m_pInterface->m_getNumVerticesOfFace(pContext, f);
                if (verts != 3 && verts != 4) continue;


                // I've decided to let degenerate triangles and group-with-anythings
                // vary between left/right hand coordinate systems at the vertices.
                // All healthy triangles on the other hand are built to always be either or.

                /*// force the coordinate system orientation to be uniform for every face.
                // (this is already the case for good triangles but not for
                // degenerate ones and those with bGroupWithAnything==true)
                bool bOrient = psTspace[index].bOrient;
                if (psTspace[index].iCounter == 0)	// tspace was not derived from a group
                {
                    // look for a space created in GenerateTSpaces() by iCounter>0
                    bool bNotFound = true;
                    int i=1;
                    while (i<verts && bNotFound)
                    {
                        if (psTspace[index+i].iCounter > 0) bNotFound=false;
                        else ++i;
                    }
                    if (!bNotFound) bOrient = psTspace[index+i].bOrient;
                }*/

                // set data
                for (i = 0; i < verts; i++)
                {
                    const STSpace* pTSpace = &psTspace[index];
                    float tang[] = { pTSpace->vOs.x, pTSpace->vOs.y, pTSpace->vOs.z };
                    float bitang[] = { pTSpace->vOt.x, pTSpace->vOt.y, pTSpace->vOt.z };
                    if (pContext->m_pInterface->m_setTSpace != NULL)
                        pContext->m_pInterface->m_setTSpace(pContext, tang, bitang, pTSpace->fMagS, pTSpace->fMagT, pTSpace->bOrient, f, i);
                    if (pContext->m_pInterface->m_setTSpaceBasic != NULL)
                        pContext->m_pInterface->m_setTSpaceBasic(pContext, tang, pTSpace->bOrient == TTRUE ? 1.0f : (-1.0f), f, i);

                    ++index;
                }
            }

            free(psTspace);


            return TTRUE;
        }

    }
}
