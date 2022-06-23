using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Geometry;
using Stride.Importer.Common.MikkTFace;

namespace Stride.Importer.Gltf.MikkTFace.SMikkContext
{
    public partial class SMikkTSpaceContext : ISMikkTSpace
    {

        public bool GenTangSpaceDefault()
        {
            return GenTangSpace(180.0f);
        }

        public bool GenTangSpace(float fAngularThreshold)
        {
            // count nr_triangles
            int[][] piTriListIn = null;
            int[][] piGroupTrianglesBuffer = null;
            STriInfo[] pTriInfos = null;
            SGroup[] pGroups = null;
            STSpace[] psTspace = null;
            int iNrTrianglesIn = 0, f = 0, t = 0, i = 0;
            int iNrTSPaces = 0, iTotTris = 0, iDegenTriangles = 0, iNrMaxGroups = 0;
            int iNrActiveGroups = 0, index = 0;
            var iNrFaces = GetNumFaces();
            var bRes = false;
            var fThresCos = (float)Math.Cos(fAngularThreshold * (float)Math.PI / 180.0f);

            // count triangles on supported faces
            for (f = 0; f < iNrFaces; f++)
            {
                var verts = GetNumVerticesOfFace(f);
                if (verts == 3) ++iNrTrianglesIn;
                else if (verts == 4) iNrTrianglesIn += 2;
            }
            if (iNrTrianglesIn <= 0) return false;

            // allocate memory for an index list
            piTriListIn = new int[iNrTrianglesIn][].Select(x => new int[3]).ToArray(); //(int*)malloc(sizeof(int) * 3 * iNrTrianglesIn);
            pTriInfos = new STriInfo[iNrTrianglesIn];//(STriInfo*)malloc(sizeof(STriInfo) * iNrTrianglesIn);
            if (piTriListIn == null || pTriInfos == null)
            {
                if (piTriListIn != null) piTriListIn = null;
                if (pTriInfos != null) pTriInfos = null;
                return false;
            }

            // make an initial triangle --> face index list
            iNrTSPaces = GenerateInitialVerticesIndexList(pTriInfos, ref piTriListIn, iNrTrianglesIn);

            // make a welded index list of identical positions and attributes (pos, norm, texc)
            //printf("gen welded index list begin\n");
            GenerateSharedVerticesIndexList(ref piTriListIn, iNrTrianglesIn);
            //printf("gen welded index list end\n");

            // Mark all degenerate triangles
            iTotTris = iNrTrianglesIn;
            iDegenTriangles = 0;
            for (t = 0; t < iTotTris; t++)
            {
                var i0 = piTriListIn[t * 3 + 0];
                var i1 = piTriListIn[t * 3 + 1];
                var i2 = piTriListIn[t * 3 + 2];
                var p0 = GetPosition(i0);
                var p1 = GetPosition(i1);
                var p2 = GetPosition(i2);
                if (p0 == p1 || p0 == p2 || p1 == p2)  // degenerate
                {
                    pTriInfos[t].IFlag |= MARK_DEGENERATE;
                    ++iDegenTriangles;
                }
            }
            iNrTrianglesIn = iTotTris - iDegenTriangles;

            // mark all triangle pairs that belong to a quad with only one
            // good triangle. These need special treatment in DegenEpilogue().
            // Additionally, move all good triangles to the start of
            // pTriInfos[] and piTriListIn[] without changing order and
            // put the degenerate triangles last.
            DegenPrologue(ref pTriInfos, ref piTriListIn, iNrTrianglesIn, iTotTris);


            // evaluate triangle level attributes and neighbor list
            //printf("gen neighbors list begin\n");
            InitTriInfo(ref pTriInfos, ref piTriListIn, iNrTrianglesIn);
            //printf("gen neighbors list end\n");


            // based on the 4 rules, identify groups based on connectivity
            iNrMaxGroups = iNrTrianglesIn * 3;
            pGroups = new SGroup[iNrMaxGroups]; // (SGroup*)malloc(sizeof(SGroup) * iNrMaxGroups);
            piGroupTrianglesBuffer = new int[iNrTrianglesIn][].Select(x => new int[3]).ToArray(); //(int*)malloc(sizeof(int) * iNrTrianglesIn * 3);
            if (pGroups == null || piGroupTrianglesBuffer == null)
            {
                if (pGroups != null) pGroups = null;
                if (piGroupTrianglesBuffer != null) piGroupTrianglesBuffer = null;
                piTriListIn = null;
                pTriInfos = null;
                return false;
            }
            //printf("gen 4rule groups begin\n");
            iNrActiveGroups =
                Build4RuleGroups(pTriInfos, pGroups, piGroupTrianglesBuffer, piTriListIn, iNrTrianglesIn);
            //printf("gen 4rule groups end\n");

            //

            psTspace = new STSpace[iNrTSPaces];// (STSpace*)malloc(sizeof(STSpace) * iNrTSPaces);
            if (psTspace == null)
            {
                piTriListIn = null;
                pTriInfos = null;
                pGroups = null;
                piGroupTrianglesBuffer = null;
                return false;
            }
            for (t = 0; t < iNrTSPaces; t++)
            {
                psTspace[t].VOs.X = 1.0f; psTspace[t].VOs.Y = 0.0f; psTspace[t].VOs.z = 0.0f; psTspace[t].FMagS = 1.0f;
                psTspace[t].VOt.X = 0.0f; psTspace[t].VOt.Y = 1.0f; psTspace[t].VOt.z = 0.0f; psTspace[t].FMagT = 1.0f;
            }

            // make tspaces, each group is split up into subgroups if necessary
            // based on fAngularThreshold. Finally a tangent space is made for
            // every resulting subgroup
            //printf("gen tspaces begin\n");
            bRes = GenerateTSpaces(psTspace, pTriInfos, pGroups, iNrActiveGroups, piTriListIn, fThresCos);
            //printf("gen tspaces end\n");

            // clean up
            pGroups = null;
            piGroupTrianglesBuffer = null;

            if (!bRes)  // if an allocation in GenerateTSpaces() failed
            {
                // clean up and return false
                pTriInfos = null;
                piTriListIn = null;
                psTspace = null;

                return false;
            }


            // degenerate quads with one good triangle will be fixed by copying a space from
            // the good triangle to the coinciding vertex.
            // all other degenerate triangles will just copy a space from any good triangle
            // with the same welded index in piTriListIn[].
            DegenEpilogue(psTspace, pTriInfos, piTriListIn, iNrTrianglesIn, iTotTris);

            pTriInfos = null;
            piTriListIn = null;


            index = 0;
            for (f = 0; f < iNrFaces; f++)
            {
                int verts = GetNumVerticesOfFace(f);
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
                    STSpace pTSpace = psTspace[index];
                    Vector3 tang = new(pTSpace.VOs.X, pTSpace.VOs.Y, pTSpace.VOs.Z);
                    Vector3 bitang = new(pTSpace.VOt.X, pTSpace.VOt.Y, pTSpace.VOt.Z); ;
                    if (SetTSpace != null)
                        (tang, bitang) = SetTSpace(pTSpace.FMagS, pTSpace.FMagT, pTSpace.Orient, f, i);
                    if (SetTSpaceBasic != null)
                        tang = SetTSpaceBasic(pTSpace.Orient == true ? 1 : -1, f, i);

                    ++index;
                }
            }

            psTspace = null;


            return true;
        }

        public int GenerateInitialVerticesIndexList(STriInfo[] pTriInfos, ref int[][] piTriList_out, int iNrTrianglesIn)
        {

            int iTSpacesOffs = 0, f = 0, t = 0;
            var iDstTriIndex = 0;
            for (f = 0; f < GetNumFaces(); f++)
            {
                var verts = GetNumVerticesOfFace(f);
                if (verts != 3 && verts != 4) continue;

                pTriInfos[iDstTriIndex].IOrgFaceNumber = f;
                pTriInfos[iDstTriIndex].ITSpacesOffs = iTSpacesOffs;

                if (verts == 3)
                {
                    var pVerts = pTriInfos[iDstTriIndex].vert_num;
                    pVerts[0] = 0; pVerts[1] = 1; pVerts[2] = 2;
                    piTriList_out[iDstTriIndex][0] = MakeIndex(f, 0);
                    piTriList_out[iDstTriIndex][1] = MakeIndex(f, 1);
                    piTriList_out[iDstTriIndex][2] = MakeIndex(f, 2);
                    ++iDstTriIndex; // next
                }
                else
                {
                    {
                        pTriInfos[iDstTriIndex + 1].IOrgFaceNumber = f;
                        pTriInfos[iDstTriIndex + 1].ITSpacesOffs = iTSpacesOffs;
                    }

                    {
                        // need an order independent way to evaluate
                        // tspace on quads. This is done by splitting
                        // along the shortest diagonal.
                        var i0 = MakeIndex(f, 0);
                        var i1 = MakeIndex(f, 1);
                        var i2 = MakeIndex(f, 2);
                        var i3 = MakeIndex(f, 3);
                        Vector2 T0 = GetTexCoord(i0);
                        Vector2 T1 = GetTexCoord(i1);
                        Vector2 T2 = GetTexCoord(i2);
                        Vector2 T3 = GetTexCoord(i3);
                        float distSQ_02 = (T2 - T0).LengthSquared();
                        float distSQ_13 = (T3 - T1).LengthSquared();
                        bool bQuadDiagIs_02;
                        if (distSQ_02 < distSQ_13)
                            bQuadDiagIs_02 = true;
                        else if (distSQ_13 < distSQ_02)
                            bQuadDiagIs_02 = true;
                        else
                        {
                            Vector3 P0 = GetPosition(i0);
                            Vector3 P1 = GetPosition(i1);
                            Vector3 P2 = GetPosition(i2);
                            Vector3 P3 = GetPosition(i3);
                            float distSQ_02b = (P2 - P0).LengthSquared();
                            float distSQ_13b = (P3 - P1).LengthSquared();

                            bQuadDiagIs_02 = distSQ_13b >= distSQ_02b;
                        }

                        if (bQuadDiagIs_02)
                        {
                            {
                                uint[] pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_A[0] = 0; pVerts_A[1] = 1; pVerts_A[2] = 2;
                            }
                            piTriList_out[iDstTriIndex][0] = i0;
                            piTriList_out[iDstTriIndex][1] = i1;
                            piTriList_out[iDstTriIndex][2] = i2;
                            ++iDstTriIndex; // next
                            {
                                uint[] pVerts_B = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_B[0] = 0; pVerts_B[1] = 2; pVerts_B[2] = 3;
                            }
                            piTriList_out[iDstTriIndex][0] = i0;
                            piTriList_out[iDstTriIndex][1] = i2;
                            piTriList_out[iDstTriIndex][2] = i3;
                            ++iDstTriIndex; // next
                        }
                        else
                        {
                            {
                                uint[] pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_A[0] = 0; pVerts_A[1] = 1; pVerts_A[2] = 3;
                            }
                            piTriList_out[iDstTriIndex][0] = i0;
                            piTriList_out[iDstTriIndex][1] = i1;
                            piTriList_out[iDstTriIndex][2] = i3;
                            ++iDstTriIndex; // next
                            {
                                uint[] pVerts_B = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_B[0] = 1; pVerts_B[1] = 2; pVerts_B[2] = 3;
                            }
                            piTriList_out[iDstTriIndex][0] = i1;
                            piTriList_out[iDstTriIndex][1] = i2;
                            piTriList_out[iDstTriIndex][2] = i3;
                            ++iDstTriIndex; // next
                        }
                    }
                }

                iTSpacesOffs += verts;
                //assert(iDstTriIndex <= iNrTrianglesIn);
            }

            for (t = 0; t < iNrTrianglesIn; t++)
                pTriInfos[t].IFlag = 0;

            // return total amount of tspaces
            return iTSpacesOffs;
        }
        
    }
}
