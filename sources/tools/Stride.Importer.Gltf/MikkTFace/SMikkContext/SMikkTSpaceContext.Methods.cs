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
            int[] piTriListIn = null;
            int[] piGroupTrianglesBuffer = null;
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
            piTriListIn = new int[3 * iNrTrianglesIn]; //(int*)malloc(sizeof(int) * 3 * iNrTrianglesIn);
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
                        pContext->m_pInterface->m_setTSpaceBasic(pContext, tang, pTSpace->bOrient == TTRUE ? 1.0f : -1.0f, f, i);

                    ++index;
                }
            }

            free(psTspace);


            return TTRUE;
        }

        public int GenerateInitialVerticesIndexList(STriInfo[] pTriInfos, ref int[] piTriList_out, int iNrTrianglesIn)
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
                    piTriList_out[iDstTriIndex * 3 + 0] = MakeIndex(f, 0);
                    piTriList_out[iDstTriIndex * 3 + 1] = MakeIndex(f, 1);
                    piTriList_out[iDstTriIndex * 3 + 2] = MakeIndex(f, 2);
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
                            piTriList_out[iDstTriIndex * 3 + 0] = i0;
                            piTriList_out[iDstTriIndex * 3 + 1] = i1;
                            piTriList_out[iDstTriIndex * 3 + 2] = i2;
                            ++iDstTriIndex; // next
                            {
                                uint[] pVerts_B = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_B[0] = 0; pVerts_B[1] = 2; pVerts_B[2] = 3;
                            }
                            piTriList_out[iDstTriIndex * 3 + 0] = i0;
                            piTriList_out[iDstTriIndex * 3 + 1] = i2;
                            piTriList_out[iDstTriIndex * 3 + 2] = i3;
                            ++iDstTriIndex; // next
                        }
                        else
                        {
                            {
                                uint[] pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_A[0] = 0; pVerts_A[1] = 1; pVerts_A[2] = 3;
                            }
                            piTriList_out[iDstTriIndex * 3 + 0] = i0;
                            piTriList_out[iDstTriIndex * 3 + 1] = i1;
                            piTriList_out[iDstTriIndex * 3 + 2] = i3;
                            ++iDstTriIndex; // next
                            {
                                uint[] pVerts_B = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_B[0] = 1; pVerts_B[1] = 2; pVerts_B[2] = 3;
                            }
                            piTriList_out[iDstTriIndex * 3 + 0] = i1;
                            piTriList_out[iDstTriIndex * 3 + 1] = i2;
                            piTriList_out[iDstTriIndex * 3 + 2] = i3;
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
        static int FindGridCell(float fMin, float fMax, float fVal)
        {
            float fIndex = iCells * ((fVal - fMin) / (fMax - fMin));
            int iIndex = (int)fIndex;
            return iIndex < iCells ? (iIndex >= 0 ? iIndex : 0) : (iCells - 1);
        }
        public void GenerateSharedVerticesIndexList(ref int[] piTriList_in_and_out, int iNrTrianglesIn)
        {

            // Generate bounding box
            int[][] piHashTable = null;
            int[] piHashCount = null;
            int[] piHashOffsets = null;
            int[] piHashCount2 = null;
            STmpVert[] pTmpVert = null;
            int i = 0, iChannel = 0, k = 0, e = 0;
            int iMaxCount = 0;
            Vector3 vMin = GetPosition(0), vMax = vMin, vDim;
            float fMin, fMax;
            for (i = 1; i < (iNrTrianglesIn * 3); i++)
            {
                int index = piTriList_in_and_out[i];

                Vector3 vP = GetPosition(index);
                if (vMin.X > vP.X) vMin.X = vP.X;
                else if (vMax.X < vP.X) vMax.X = vP.X;
                if (vMin.Y > vP.Y) vMin.Y = vP.Y;
                else if (vMax.Y < vP.Y) vMax.Y = vP.Y;
                if (vMin.Z > vP.Z) vMin.Z = vP.Z;
                else if (vMax.Z < vP.Z) vMax.Z = vP.Z;
            }

            vDim = vMax - vMin;
            iChannel = 0;
            fMin = vMin.X;
            fMax = vMax.X;
            if (vDim.Y > vDim.X && vDim.Y > vDim.Z)
            {
                iChannel = 1;
                fMin = vMin.Y;
                fMax = vMax.Y;
            }

            else if (vDim.Z > vDim.X)
            {
                iChannel = 2;
                fMin = vMin.Z;
                fMax = vMax.Z;
            }

            // make allocations
            piHashTable = new int[iNrTrianglesIn][].Select(x => new int[3]).ToArray(); // (int*)malloc(sizeof(int) * iNrTrianglesIn * 3);
            piHashCount = new int[iCells]; //(int*)malloc(sizeof(int) * g_iCells);
            piHashOffsets = new int[iCells]; //(int*)malloc(sizeof(int) * iCells);
            piHashCount2 = new int[iCells]; //(int*)malloc(sizeof(int) * iCells);

            var piHash = new HashSet<int>();

            if (piHashTable == null || piHashCount == null || piHashOffsets == null || piHashCount2 == null)
            {
                if (piHashTable != null) piHashTable = null;
                if (piHashCount != null) piHashCount = null;
                if (piHashOffsets != null) piHashOffsets = null;
                if (piHashCount2 != null) piHashCount2 = null;
                GenerateSharedVerticesIndexListSlow(ref piTriList_in_and_out, iNrTrianglesIn);
                return;
            }


            // count amount of elements in each cell unit
            for (i = 0; i < (iNrTrianglesIn * 3); i++)
            {
                int index = piTriList_in_and_out[i];
                Vector3 vP = GetPosition(index);
                float fVal = iChannel == 0 ? vP.X : (iChannel == 1 ? vP.Y : vP.Z);
                int iCell = FindGridCell(fMin, fMax, fVal);
                ++piHashCount[iCell];
            }

            // evaluate start index of each cell.
            piHashOffsets[0] = 0;
            for (k = 1; k < iCells; k++)
                piHashOffsets[k] = piHashOffsets[k - 1] + piHashCount[k - 1];

            // insert vertices
            for (i = 0; i < (iNrTrianglesIn * 3); i++)
            {
                int index = piTriList_in_and_out[i];
                Vector3 vP = GetPosition(index);
                float fVal = iChannel == 0 ? vP.X : (iChannel == 1 ? vP.Y : vP.Z);
                int iCell = FindGridCell(fMin, fMax, fVal);
                int[] pTable = null;

                //assert(piHashCount2[iCell] < piHashCount[iCell]);
                //pTable = &piHashTable[piHashOffsets[iCell]];
                pTable[piHashCount2[iCell]] = i;    // vertex i has been inserted.
                ++piHashCount2[iCell];
            }
            //for (k = 0; k < iCells; k++)
            //    assert(piHashCount2[k] == piHashCount[k]);  // verify the count
            piHashCount2 = null;

            // find maximum amount of entries in any hash entry
            iMaxCount = piHashCount[0];
            for (k = 1; k < iCells; k++)
                if (iMaxCount < piHashCount[k])
                    iMaxCount = piHashCount[k];
            pTmpVert = new STmpVert[iMaxCount]; // (STmpVert*)malloc(sizeof(STmpVert) * iMaxCount);


            // complete the merge
            for (k = 0; k < iCells; k++)
            {
                // extract table of cell k and amount of entries in it
                int[] pTable = piHashTable[piHashOffsets[k]];
                int iEntries = piHashCount[k];
                if (iEntries < 2) continue;

                if (pTmpVert != null)
                {
                    for (e = 0; e < iEntries; e++)
                    {
                        int tmp = pTable[e];
                        Vector3 vP = GetPosition(piTriList_in_and_out[tmp]);
                        pTmpVert[e].Vert[0] = vP.X; pTmpVert[e].Vert[1] = vP.Y;
                        pTmpVert[e].Vert[2] = vP.Z; pTmpVert[e].Index = tmp;
                    }
                    MergeVertsFast(ref piTriList_in_and_out, pTmpVert, 0, iEntries - 1);
                }
                else
                    MergeVertsSlow(ref piTriList_in_and_out, pTable, iEntries);
            }

            if (pTmpVert != null) { pTmpVert = null; }
            piHashTable = null;
            piHashCount = null;
            piHashOffsets = null;
        }
        void GenerateSharedVerticesIndexListSlow(ref int[] piTriList_in_and_out, int iNrTrianglesIn)
        {

            int iNumUniqueVerts = 0, t = 0, i = 0;
            for (t = 0; t < iNrTrianglesIn; t++)
            {
                for (i = 0; i < 3; i++)
                {
                    int offs = t * 3 + i;
                    int index = piTriList_in_and_out[offs];

                    Vector3 vP = GetPosition(index);
                    Vector3 vN = GetNormal(index);
                    Vector2 vT = GetTexCoord(index);

                    bool bFound = false;
                    int t2 = 0, index2rec = -1;
                    while (!bFound && t2 <= t)
                    {
                        int j = 0;
                        while (!bFound && j < 3)
                        {
                            int index2 = piTriList_in_and_out[t2 * 3 + j];
                            Vector3 vP2 = GetPosition(index2);
                            Vector3 vN2 = GetNormal(index2);
                            Vector2 vT2 = GetTexCoord(index2);

                            if (vP == vP2 && vN == vN2 && vT == vT2)
                                bFound = true;
                            else
                                ++j;
                        }
                        if (!bFound) ++t2;
                    }

                    //assert(bFound);
                    // if we found our own
                    if (index2rec == index) { ++iNumUniqueVerts; }

                    piTriList_in_and_out[offs] = index2rec;
                }
            }
        }

        public void MergeVertsFast(ref int[] piTriList_in_and_out, STmpVert[] pTmpVert, int iL_in, int iR_in)
        {
            // make bbox
            int c = 0, l = 0, channel = 0;
            float[] fvMin = new float[3];
            float[] fvMax = new float[3];
            float dx = 0, dy = 0, dz = 0, fSep = 0;
            for (c = 0; c < 3; c++)
            { fvMin[c] = pTmpVert[iL_in].Vert[c]; fvMax[c] = fvMin[c]; }
            for (l = (iL_in + 1); l <= iR_in; l++)
                for (c = 0; c < 3; c++)
                    if (fvMin[c] > pTmpVert[l].Vert[c]) fvMin[c] = pTmpVert[l].Vert[c];
                    else if (fvMax[c] < pTmpVert[l].Vert[c]) fvMax[c] = pTmpVert[l].Vert[c];

            dx = fvMax[0] - fvMin[0];
            dy = fvMax[1] - fvMin[1];
            dz = fvMax[2] - fvMin[2];

            channel = 0;
            if (dy > dx && dy > dz) channel = 1;
            else if (dz > dx) channel = 2;

            fSep = 0.5f * (fvMax[channel] + fvMin[channel]);

            // terminate recursion when the separation/average value
            // is no longer strictly between fMin and fMax values.
            if (fSep >= fvMax[channel] || fSep <= fvMin[channel])
            {
                // complete the weld
                for (l = iL_in; l <= iR_in; l++)
                {
                    int i = pTmpVert[l].Index;
                    int index = piTriList_in_and_out[i];
                    Vector3 vP = GetPosition(index);
                    Vector3 vN = GetNormal(index);
                    Vector2 vT = GetTexCoord(index);

                    bool bNotFound = true;
                    int l2 = iL_in, i2rec = -1;
                    while (l2 < l && bNotFound)
                    {
                        int i2 = pTmpVert[l2].Index;
                        int index2 = piTriList_in_and_out[i2];
                        Vector3 vP2 = GetPosition(index2);
                        Vector3 vN2 = GetNormal(index2);
                        Vector2 vT2 = GetTexCoord(index2);
                        i2rec = i2;

                        if (vP == vP2 && vN == vN2 && vT == vT2)
                            bNotFound = false;
                        else
                            ++l2;
                    }

                    // merge if previously found
                    if (!bNotFound)
                        piTriList_in_and_out[i] = piTriList_in_and_out[i2rec];
                }
            }
            else
            {
                int iL = iL_in, iR = iR_in;
                //assert((iR_in - iL_in) > 0);    // at least 2 entries

                // separate (by fSep) all points between iL_in and iR_in in pTmpVert[]
                while (iL < iR)
                {
                    bool bReadyLeftSwap = false, bReadyRightSwap = false;
                    while ((!bReadyLeftSwap) && iL < iR)
                    {
                        //assert(iL >= iL_in && iL <= iR_in);
                        bReadyLeftSwap = !(pTmpVert[iL].Vert[channel] < fSep);
                        if (!bReadyLeftSwap) ++iL;
                    }
                    while ((!bReadyRightSwap) && iL < iR)
                    {
                        //assert(iR >= iL_in && iR <= iR_in);
                        bReadyRightSwap = pTmpVert[iR].Vert[channel] < fSep;
                        if (!bReadyRightSwap) --iR;
                    }
                    //assert((iL < iR) || !(bReadyLeftSwap && bReadyRightSwap));

                    if (bReadyLeftSwap && bReadyRightSwap)
                    {
                        STmpVert sTmp = pTmpVert[iL];
                        //assert(iL < iR);
                        pTmpVert[iL] = pTmpVert[iR];
                        pTmpVert[iR] = sTmp;
                        ++iL; --iR;
                    }
                }

                //assert(iL == (iR + 1) || (iL == iR));
                if (iL == iR)
                {
                    bool bReadyRightSwap = pTmpVert[iR].Vert[channel] < fSep;
                    if (bReadyRightSwap) ++iL;
                    else --iR;
                }

                // only need to weld when there is more than 1 instance of the (x,y,z)
                if (iL_in < iR)
                    MergeVertsFast(ref piTriList_in_and_out, pTmpVert, iL_in, iR);    // weld all left of fSep
                if (iL < iR_in)
                    MergeVertsFast(ref piTriList_in_and_out, pTmpVert, iL, iR_in);    // weld all right of (or equal to) fSep
            }
        }
        public void MergeVertsSlow(ref int[] piTriList_in_and_out, int[] pTable, int iEntries)
        {
            // this can be optimized further using a tree structure or more hashing.
            int e = 0;
	        for (e=0; e<iEntries; e++)
	        {
		        int i = pTable[e];
                int index = piTriList_in_and_out[i];
                Vector3 vP = GetPosition(index);
                Vector3 vN = GetNormal(index);
                Vector2 vT = GetTexCoord(index);

                bool bNotFound = true;
                int e2 = 0, i2rec = -1;
		        while (e2<e && bNotFound)
		        {
			        int i2 = pTable[e2];
                    int index2 = piTriList_in_and_out[i2];
                    Vector3 vP2 = GetPosition(index2);
                    Vector3 vN2 = GetNormal(index2);
                    Vector2 vT2 = GetTexCoord(index2);
                    i2rec = i2;

			        if (vP == vP2 && vN == vN2 && vT == vT2)
				        bNotFound = false;
			        else
				        ++e2;
		        }
		
		        // merge if previously found
		        if (!bNotFound)
			        piTriList_in_and_out[i] = piTriList_in_and_out[i2rec];
	        }
        }
    }
}
