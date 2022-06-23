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
            for (e = 0; e < iEntries; e++)
            {
                int i = pTable[e];
                int index = piTriList_in_and_out[i];
                Vector3 vP = GetPosition(index);
                Vector3 vN = GetNormal(index);
                Vector2 vT = GetTexCoord(index);

                bool bNotFound = true;
                int e2 = 0, i2rec = -1;
                while (e2 < e && bNotFound)
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
        public void DegenPrologue(ref STriInfo[] pTriInfos, ref int[] piTriList_out, int iNrTrianglesIn, int iTotTris)
        {

            int iNextGoodTriangleSearchIndex = -1;
            bool bStillFindingGoodOnes;

            // locate quads with only one good triangle
            int t = 0;
            while (t < (iTotTris - 1))
            {
                int iFO_a = pTriInfos[t].IOrgFaceNumber;
                int iFO_b = pTriInfos[t + 1].IOrgFaceNumber;
                if (iFO_a == iFO_b) // this is a quad
                {
                    bool bIsDeg_a = (pTriInfos[t].IFlag & MARK_DEGENERATE) != 0;
                    bool bIsDeg_b = (pTriInfos[t + 1].IFlag & MARK_DEGENERATE) != 0;
                    if (bIsDeg_a ^ bIsDeg_b)
                    {
                        pTriInfos[t].IFlag |= QUAD_ONE_DEGEN_TRI;
                        pTriInfos[t + 1].IFlag |= QUAD_ONE_DEGEN_TRI;
                    }
                    t += 2;
                }

                else
                    ++t;
            }

            // reorder list so all degen triangles are moved to the back
            // without reordering the good triangles
            iNextGoodTriangleSearchIndex = 1;
            t = 0;
            bStillFindingGoodOnes = true;
            while (t < iNrTrianglesIn && bStillFindingGoodOnes)
            {
                bool bIsGood = (pTriInfos[t].IFlag & MARK_DEGENERATE) == 0 ? true : false;
                if (bIsGood)
                {
                    if (iNextGoodTriangleSearchIndex < (t + 2))
                        iNextGoodTriangleSearchIndex = t + 2;
                }
                else
                {
                    int t0, t1;
                    // search for the first good triangle.
                    bool bJustADegenerate = true;
                    while (bJustADegenerate && iNextGoodTriangleSearchIndex < iTotTris)
                    {
                        var bIsGood2 = (pTriInfos[iNextGoodTriangleSearchIndex].IFlag & MARK_DEGENERATE) == 0 ? true : false;
                        if (bIsGood2) bJustADegenerate = false;
                        else ++iNextGoodTriangleSearchIndex;
                    }

                    t0 = t;
                    t1 = iNextGoodTriangleSearchIndex;
                    ++iNextGoodTriangleSearchIndex;
                    //assert(iNextGoodTriangleSearchIndex > (t + 1));

                    // swap triangle t0 and t1
                    if (!bJustADegenerate)
                    {
                        int i = 0;
                        for (i = 0; i < 3; i++)
                        {
                            int index = piTriList_out[t0 * 3 + i];
                            piTriList_out[t0 * 3 + i] = piTriList_out[t1 * 3 + i];
                            piTriList_out[t1 * 3 + i] = index;
                        }
                        {
                            STriInfo tri_info = pTriInfos[t0];
                            pTriInfos[t0] = pTriInfos[t1];
                            pTriInfos[t1] = tri_info;
                        }
                    }
                    else
                        bStillFindingGoodOnes = false; // this is not supposed to happen
                }

                if (bStillFindingGoodOnes) ++t;
            }

            //assert(bStillFindingGoodOnes);  // code will still work.
            //assert(iNrTrianglesIn == t);
        }
        public void InitTriInfo(ref STriInfo[] pTriInfos, ref int[][] piTriListIn, int iNrTrianglesIn)
        {

            int f = 0, i = 0, t = 0;
            // pTriInfos[f].iFlag is cleared in GenerateInitialVerticesIndexList() which is called before this function.

            // generate neighbor info list
            for (f = 0; f < iNrTrianglesIn; f++)
                for (i = 0; i < 3; i++)
                {
                    pTriInfos[f].FaceNeighbors[i] = -1;
                    pTriInfos[f].AssignedGroup[i] = null;

                    pTriInfos[f].VOs.X = 0.0f; pTriInfos[f].VOs.Y = 0.0f; pTriInfos[f].VOs.Z = 0.0f;
                    pTriInfos[f].VOt.X = 0.0f; pTriInfos[f].VOt.Z = 0.0f; pTriInfos[f].VOt.Z = 0.0f;
                    pTriInfos[f].FMagS = 0;
                    pTriInfos[f].FMagT = 0;

                    // assumed bad
                    pTriInfos[f].IFlag |= GROUP_WITH_ANY;
                }

            // evaluate first order derivatives
            for (f = 0; f < iNrTrianglesIn; f++)
            {
                // initial values
                Vector3 v1 = GetPosition(piTriListIn[f][0]);
                Vector3 v2 = GetPosition(piTriListIn[f][1]);
                Vector3 v3 = GetPosition(piTriListIn[f][2]);
                Vector2 t1 = GetTexCoord(piTriListIn[f][0]);
                Vector2 t2 = GetTexCoord(piTriListIn[f][1]);
                Vector2 t3 = GetTexCoord(piTriListIn[f][2]);

                float t21x = t2.X - t1.X;
                float t21y = t2.Y - t1.Y;
                float t31x = t3.X - t1.X;
                float t31y = t3.Y - t1.Y;
                Vector3 d1 = v2 - v1;
                Vector3 d2 = v3 - v1;

                float fSignedAreaSTx2 = t21x * t31y - t21y * t31x;
                //assert(fSignedAreaSTx2!=0);
                Vector3 VOs = t31y * d1 - t21y * d2;   // eq 18
                Vector3 vOt = -t31x * d1 + t21x * d2; // eq 19

                pTriInfos[f].IFlag |= (fSignedAreaSTx2 > 0 ? ORIENT_PRESERVING : 0);

                if (fSignedAreaSTx2 > float.Epsilon)
                {
                    float fAbsArea = MathF.Abs(fSignedAreaSTx2);
                    float fLenOs = VOs.Length();
                    float fLenOt = vOt.Length();
                    float fS = (pTriInfos[f].IFlag & ORIENT_PRESERVING) == 0 ? (-1.0f) : 1.0f;
                    if (fLenOs > float.Epsilon) pTriInfos[f].VOs = fS / fLenOs * VOs;
                    if (fLenOt > float.Epsilon) pTriInfos[f].VOt = fS / fLenOt * vOt;

                    // evaluate magnitudes prior to normalization of VOs and vOt
                    pTriInfos[f].FMagS = fLenOs / fAbsArea;
                    pTriInfos[f].FMagT = fLenOt / fAbsArea;

                    // if this is a good triangle
                    if (pTriInfos[f].FMagS > float.Epsilon && pTriInfos[f].FMagT > float.Epsilon)
                        pTriInfos[f].IFlag &= (~GROUP_WITH_ANY);
                }
            }
            // force otherwise healthy quads to a fixed orientation
            while (t < (iNrTrianglesIn - 1))
            {
                int iFO_a = pTriInfos[t].IOrgFaceNumber;
                int iFO_b = pTriInfos[t + 1].IOrgFaceNumber;
                if (iFO_a == iFO_b) // this is a quad
                {
                    bool bIsDeg_a = (pTriInfos[t].IFlag & MARK_DEGENERATE) != 0;
                    bool bIsDeg_b = (pTriInfos[t + 1].IFlag & MARK_DEGENERATE) != 0;

                    // bad triangles should already have been removed by
                    // DegenPrologue(), but just in case check bIsDeg_a and bIsDeg_a are false
                    if ((bIsDeg_a || bIsDeg_b) == false)
                    {
                        bool bOrientA = (pTriInfos[t].IFlag & ORIENT_PRESERVING) != 0;
                        bool bOrientB = (pTriInfos[t + 1].IFlag & ORIENT_PRESERVING) != 0;
                        // if this happens the quad has extremely bad mapping!!
                        if (bOrientA != bOrientB)
                        {
                            //printf("found quad with bad mapping\n");
                            bool bChooseOrientFirstTri = false;
                            if ((pTriInfos[t + 1].IFlag & GROUP_WITH_ANY) != 0) bChooseOrientFirstTri = true;
                            else if (CalcTexArea(ref piTriListIn[t * 3 + 0]) >= CalcTexArea(ref piTriListIn[(t + 1) * 3 + 0]))
                                bChooseOrientFirstTri = true;

                            // force match
                            {
                                int t0 = bChooseOrientFirstTri ? t : (t + 1);
                                int t1 = bChooseOrientFirstTri ? (t + 1) : t;
                                pTriInfos[t1].IFlag &= (~ORIENT_PRESERVING);    // clear first
                                pTriInfos[t1].IFlag |= (pTriInfos[t0].IFlag & ORIENT_PRESERVING);   // copy bit
                            }
                        }
                    }
                    t += 2;
                }
                else
                    ++t;
            }

            // match up edge pairs
            {
                Vector3[] pEdges = new Vector3[iNrTrianglesIn]; //(SEdge*)malloc(sizeof(SEdge) * iNrTrianglesIn * 3);
                if (pEdges == null)
                    BuildNeighborsSlow(ref pTriInfos, ref piTriListIn, iNrTrianglesIn);
                else
                {
                    BuildNeighborsFast(ref pTriInfos, ref pEdges, ref piTriListIn, iNrTrianglesIn);

                    pEdges = null;
                }
            }
        }
        public float CalcTexArea(ref int[] indices)
        {

            Vector2 t1 = GetTexCoord(indices[0]);
            Vector2 t2 = GetTexCoord(indices[1]);
            Vector2 t3 = GetTexCoord(indices[2]);

            float t21x = t2.X - t1.X;
            float t21y = t2.Y - t1.Y;
            float t31x = t3.X - t1.X;
            float t31y = t3.Y - t1.Y;

            float fSignedAreaSTx2 = t21x * t31y - t21y * t31x;

            return fSignedAreaSTx2 < 0 ? (-fSignedAreaSTx2) : fSignedAreaSTx2;
        }
        public void BuildNeighborsSlow(ref STriInfo[] pTriInfos, ref int[][] piTriListIn, int iNrTrianglesIn)
        {

            int f = 0, i = 0;
            for (f = 0; f < iNrTrianglesIn; f++)
            {
                for (i = 0; i < 3; i++)
                {
                    // if unassigned
                    if (pTriInfos[f].FaceNeighbors[i] == -1)
                    {
                        int i0_A = piTriListIn[f][i];
                        int i1_A = piTriListIn[f][(i < 2 ? (i + 1) : 0)];

                        // search for a neighbor
                        bool bFound = false;
                        int t = 0, j = 0;
                        while (!bFound && t < iNrTrianglesIn)
                        {
                            if (t != f)
                            {
                                j = 0;
                                while (!bFound && j < 3)
                                {
                                    // in rev order
                                    int i1_B = piTriListIn[t][j];
                                    int i0_B = piTriListIn[t][(j < 2 ? (j + 1) : 0)];
                                    //assert(!(i0_A==i1_B && i1_A==i0_B));
                                    if (i0_A == i0_B && i1_A == i1_B)
                                        bFound = true;
                                    else
                                        ++j;
                                }
                            }

                            if (!bFound) ++t;
                        }

                        // assign neighbors
                        if (bFound)
                        {
                            pTriInfos[f].FaceNeighbors[i] = t;
                            //assert(pTriInfos[t].FaceNeighbors[j]==-1);
                            pTriInfos[t].FaceNeighbors[j] = f;
                        }
                    }
                }
            }
        }

        static void BuildNeighborsFast(ref STriInfo[] pTriInfos, ref Vector3[] pEdges, ref int[][] piTriListIn, int iNrTrianglesIn)
        {
            // build array of edges
            int uSeed = INTERNAL_RND_SORT_SEED;                // could replace with a random seed?
            int iEntries = 0, iCurStartIndex = -1, f = 0, i = 0;
            for (f = 0; f < iNrTrianglesIn * 3; f++)
            {
                for (i = 0; i < 3; i++)
                {
                    int i0 = piTriListIn[f][i];
                    int i1 = piTriListIn[f][i < 2 ? (i + 1) : 0];
                    pEdges[f * 3 + i].X = i0 < i1 ? i0 : i1;           // put minimum index in i0
                    pEdges[f * 3 + i].Y = !(i0 < i1) ? i0 : i1;        // put maximum index in i1
                    pEdges[f * 3 + i].Z = f;                            // record face number
                }
            }
            // sort over all edges by i0, this is the pricy one.
            QuickSortEdges(pEdges, 0, iNrTrianglesIn * 3 - 1, 0, uSeed);    // sort channel 0 which is i0

            // sub sort over i1, should be fast.
            // could replace this with a 64 bit int sort over (i0,i1)
            // with i0 as msb in the quicksort call above.
            iEntries = iNrTrianglesIn * 3;
            iCurStartIndex = 0;
            for (i = 1; i < iEntries; i++)
            {
                if (pEdges[iCurStartIndex].X != pEdges[i].X)
                {
                    int iL = iCurStartIndex;
                    int iR = i - 1;
                    //const int iElems = i-iL;
                    iCurStartIndex = i;
                    QuickSortEdges(pEdges, iL, iR, 1, uSeed);   // sort channel 1 which is i1
                }
            }

            // sub sort over f, which should be fast.
            // this step is to remain compliant with BuildNeighborsSlow() when
            // more than 2 triangles use the same edge (such as a butterfly topology).
            iCurStartIndex = 0;
            for (i = 1; i < iEntries; i++)
            {
                if (pEdges[iCurStartIndex].i0 != pEdges[i].i0 || pEdges[iCurStartIndex].i1 != pEdges[i].i1)
                {
                    int iL = iCurStartIndex;
                    int iR = i - 1;
                    //const int iElems = i-iL;
                    iCurStartIndex = i;
                    QuickSortEdges(pEdges, iL, iR, 2, uSeed);   // sort channel 2 which is f
                }
            }

            // pair up, adjacent triangles
            for (i = 0; i < iEntries; i++)
            {
                int i0 = (int)pEdges[i].X;
                int i1 = (int)pEdges[i].Y;
                f = (int)pEdges[i].Z;
                bool bUnassigned_A;

                int i0_A, i1_A;
                int edgenum_A, edgenum_B = 0;   // 0,1 or 2
                GetEdge(&i0_A, &i1_A, &edgenum_A, &piTriListIn[f * 3], i0, i1); // resolve index ordering and edge_num
                bUnassigned_A = pTriInfos[f].FaceNeighbors[edgenum_A] == -1 ? TTRUE : TFALSE;

                if (bUnassigned_A)
                {
                    // get true index ordering
                    int j = i + 1, t;
                    tbool bNotFound = TTRUE;
                    while (j < iEntries && i0 == pEdges[j].i0 && i1 == pEdges[j].i1 && bNotFound)
                    {
                        tbool bUnassigned_B;
                        int i0_B, i1_B;
                        t = pEdges[j].f;
                        // flip i0_B and i1_B
                        GetEdge(&i1_B, &i0_B, &edgenum_B, &piTriListIn[t * 3], pEdges[j].i0, pEdges[j].i1); // resolve index ordering and edge_num
                                                                                                            //assert(!(i0_A==i1_B && i1_A==i0_B));
                        bUnassigned_B = pTriInfos[t].FaceNeighbors[edgenum_B] == -1 ? TTRUE : TFALSE;
                        if (i0_A == i0_B && i1_A == i1_B && bUnassigned_B)
                            bNotFound = TFALSE;
                        else
                            ++j;
                    }

                    if (!bNotFound)
                    {
                        int t = pEdges[j].f;
                        pTriInfos[f].FaceNeighbors[edgenum_A] = t;
                        //assert(pTriInfos[t].FaceNeighbors[edgenum_B]==-1);
                        pTriInfos[t].FaceNeighbors[edgenum_B] = f;
                    }
                }
            }
        }
    }
}
