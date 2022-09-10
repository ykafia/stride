using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Schema2;
using Stride.Core.Diagnostics;
using Stride.Importer.Gltf.MikkTSpace;

namespace Stride.Importer.Gltf.MikkTSpace;

public class MikktspaceTangentGenerator
{

    private int MARK_DEGENERATE = 1;
    private int QUAD_ONE_DEGEN_TRI = 2;
    private int GROUP_WITH_ANY = 4;
    private int ORIENT_PRESERVING = 8;
    private long INTERNAL_RND_SORT_SEED = 39871946 & 0xffffffffL;
    int CELLS = 2048;

    private Logger logger;

    /**
     * A private constructor to inhibit instantiation of this class.
     */
    public MikktspaceTangentGenerator(Logger log)
    {
        logger = log;
    }

    int MakeIndex(int face, int vert)
    {
        Debug.Assert(vert >= 0 && vert < 4 && face >= 0);
        return (face << 2) | (vert & 0x3);
    }

    private void IndexToData(ref int face, ref int vert, int indexIn)
    {
        vert = indexIn & 0x3;
        face = indexIn >> 2;
    }

    TSpace AvgTSpace(TSpace tS0, TSpace tS1)
    {
        TSpace tsRes = new TSpace();

        // this if is important. Due to floating point precision
        // averaging when s0 == s1 will cause a slight difference
        // which results in tangent space splits later on
        if (tS0.magS == tS1.magS && tS0.magT == tS1.magT && tS0.os.Equals(tS1.os) && tS0.ot.Equals(tS1.ot))
        {
            tsRes.magS = tS0.magS;
            tsRes.magT = tS0.magT;
            tsRes.os = tS0.os;
            tsRes.ot = tS0.ot;
        }
        else
        {
            tsRes.magS = 0.5f * (tS0.magS + tS1.magS);
            tsRes.magT = 0.5f * (tS0.magT + tS1.magT);
            tsRes.os = Vector3.Normalize(tS0.os + tS1.os);
            tsRes.ot = Vector3.Normalize(tS0.ot + tS1.ot);
        }
        return tsRes;
    }

    public void Generate(MeshPrimitive s, Vector3[] normals, out Vector3[] tangents)
    {
        MikkTSpaceImpl context = new MikkTSpaceImpl(s, normals);
        if (!GenTangSpaceDefault(context))
        {
            logger.Error("Failed to generate tangents for geometry");
        }
        tangents = context.Tangents;
        //TangentUtils.generateBindPoseTangentsIfNecessary(g.GetMesh());

    }

    public bool GenTangSpaceDefault(MikkTSpaceContext mikkTSpace)
    {
        return GenTangSpace(mikkTSpace, 180.0f);
    }

    public bool GenTangSpace(MikkTSpaceContext mikkTSpace, float angularThreshold)
    {

        // count nr_triangles
        int[] piTriListIn;
        int[] piGroupTrianglesBuffer;
        TriInfo[] pTriInfos;
        Group[] pGroups;
        TSpace[] psTspace;
        int iNrTrianglesIn = 0;
        int iNrTSPaces, iTotTris, iDegenTriangles, iNrMaxGroups;
        int iNrActiveGroups, index;
        int iNrFaces = mikkTSpace.GetNumFaces();
        //bool bRes = false;
        float fThresCos = MathF.Cos((angularThreshold * MathF.PI) / 180.0f);

        // count triangles on supported faces
        for (int f = 0; f < iNrFaces; f++)
        {
            int verts = mikkTSpace.GetNumVerticesOfFace(f);
            if (verts == 3)
            {
                ++iNrTrianglesIn;
            }
            else if (verts == 4)
            {
                iNrTrianglesIn += 2;
            }
        }
        if (iNrTrianglesIn <= 0)
        {
            return false;
        }

        piTriListIn = new int[3 * iNrTrianglesIn];
        pTriInfos = new TriInfo[iNrTrianglesIn];

        // make an initial triangle -. face index list
        iNrTSPaces = GenerateInitialVerticesIndexList(pTriInfos, piTriListIn, mikkTSpace, iNrTrianglesIn);

        // make a welded index list of identical positions and attributes (pos, norm, texc)        
        GenerateSharedVerticesIndexList(ref piTriListIn, mikkTSpace, iNrTrianglesIn);

        // Mark all degenerate triangles
        iTotTris = iNrTrianglesIn;
        iDegenTriangles = 0;
        for (int t = 0; t < iTotTris; t++)
        {
            int i0 = piTriListIn[t * 3 + 0];
            int i1 = piTriListIn[t * 3 + 1];
            int i2 = piTriListIn[t * 3 + 2];
            Vector3 p0 = GetPosition(mikkTSpace, i0);
            Vector3 p1 = GetPosition(mikkTSpace, i1);
            Vector3 p2 = GetPosition(mikkTSpace, i2);
            if (p0 == p1 || p0 == p2 || p1 == p2)
            {// degenerate
                pTriInfos[t].flag |= MARK_DEGENERATE;
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
        InitTriInfo(pTriInfos, piTriListIn, mikkTSpace, iNrTrianglesIn);

        // based on the 4 rules, identify groups based on connectivity
        iNrMaxGroups = iNrTrianglesIn * 3;
        pGroups = new Group[iNrMaxGroups];
        piGroupTrianglesBuffer = new int[iNrTrianglesIn * 3];

        iNrActiveGroups
                = Build4RuleGroups(pTriInfos, pGroups, piGroupTrianglesBuffer, piTriListIn, iNrTrianglesIn);

        psTspace = new TSpace[iNrTSPaces];

        for (int t = 0; t < iNrTSPaces; t++)
        {
            TSpace tSpace = new TSpace();
            tSpace.os = new(1.0f, 0.0f, 0.0f);
            tSpace.magS = 1.0f;
            tSpace.ot = new(0.0f, 1.0f, 0.0f);
            tSpace.magT = 1.0f;
            psTspace[t] = tSpace;
        }

        // make tspaces, each group is split up into subgroups if necessary
        // based on fAngularThreshold. Finally a tangent space is made for
        // every resulting subgroup
        GenerateTSpaces(psTspace, pTriInfos, pGroups, iNrActiveGroups, piTriListIn, fThresCos, mikkTSpace);

        // degenerate quads with one good triangle will be fixed by copying a space from
        // the good triangle to the coinciding vertex.
        // all other degenerate triangles will just copy a space from any good triangle
        // with the same welded index in piTriListIn[].
        DegenEpilogue(psTspace, pTriInfos, piTriListIn, mikkTSpace, iNrTrianglesIn, iTotTris);

        index = 0;
        for (int f = 0; f < iNrFaces; f++)
        {
            int verts = mikkTSpace.GetNumVerticesOfFace(f);
            if (verts != 3 && verts != 4)
            {
                continue;
            }

            // I've decided to let degenerate triangles and group-with-anythings
            // vary between left/right hand coordinate systems at the vertices.
            // All healthy triangles on the other hand are built to always be either or.

            /*// force the coordinate system orientation to be uniform for every face.
             // (this is already the case for good triangles but not for
             // degenerate ones and those with bGroupWithAnything==true)
             bool bOrient = psTspace[index].bOrient;
             if (psTspace[index].iCounter == 0)  // tspace was not derived from a group
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
            // Set data
            for (int i = 0; i < verts; i++)
            {
                TSpace pTSpace = psTspace[index];
                float[] tang = { pTSpace.os.X, pTSpace.os.Y, pTSpace.os.Z };
                float[] bitang = { pTSpace.ot.X, pTSpace.ot.Y, pTSpace.ot.Z };
                mikkTSpace.SetTSpace(tang, bitang, pTSpace.magS, pTSpace.magT, pTSpace.orient, f, i);
                mikkTSpace.SetTSpaceBasic(tang, pTSpace.orient == true ? 1.0f : (-1.0f), f, i);
                ++index;
            }
        }

        return true;
    }

    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // it is IMPORTANT that this function is called to evaluate the hash since
    // inlining could potentially reorder instructions and generate different
    // results for the same effective input value fVal.
    int FindGridCell(float min, float max, float val)
    {
        float fIndex = CELLS * ((val - min) / (max - min));
        int iIndex = (int)fIndex;
        return iIndex < CELLS ? (iIndex >= 0 ? iIndex : 0) : (CELLS - 1);
    }

    void GenerateSharedVerticesIndexList(ref int[] piTriList_in_and_out, MikkTSpaceContext mikkTSpace, int iNrTrianglesIn)
    {

        // Generate bounding box
        TmpVert[] pTmpVert;
        Vector3 vMin = GetPosition(mikkTSpace, 0);
        Vector3 vMax = vMin;
        Vector3 vDim;
        float fMin, fMax;
        for (int i = 1; i < (iNrTrianglesIn * 3); i++)
        {
            int index = piTriList_in_and_out[i];

            Vector3 vP = GetPosition(mikkTSpace, index);
            if (vMin.X > vP.X)
            {
                vMin.X = vP.X;
            }
            else if (vMax.X < vP.X)
            {
                vMax.X = vP.X;
            }
            if (vMin.Y > vP.Y)
            {
                vMin.Y = vP.Y;
            }
            else if (vMax.Y < vP.Y)
            {
                vMax.Y = vP.Y;
            }
            if (vMin.Z > vP.Z)
            {
                vMin.Z = vP.Z;
            }
            else if (vMax.Z < vP.Z)
            {
                vMax.Z = vP.Z;
            }
        }

        vDim = vMax - vMin;
        int iChannel = 0;
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

        //TODO Nehon: this is really fishy... seems like a hashtable implementation with nasty array manipulation...
        int[] piHashTable = new int[iNrTrianglesIn * 3];
        int[] piHashCount = new int[CELLS];
        int[] piHashOffSets = new int[CELLS];
        int[] piHashCount2 = new int[CELLS];

        // count amount of elements in each cell unit
        for (int i = 0; i < (iNrTrianglesIn * 3); i++)
        {
            int index = piTriList_in_and_out[i];
            Vector3 vP = GetPosition(mikkTSpace, index);
            float fVal = iChannel == 0 ? vP.X : (iChannel == 1 ? vP.Y : vP.Z);
            int iCell = FindGridCell(fMin, fMax, fVal);
            ++piHashCount[iCell];
        }

        // evaluate start index of each cell.
        piHashOffSets[0] = 0;
        for (int k = 1; k < CELLS; k++)
        {
            piHashOffSets[k] = piHashOffSets[k - 1] + piHashCount[k - 1];
        }

        // insert vertices
        for (int i = 0; i < (iNrTrianglesIn * 3); i++)
        {
            int index = piTriList_in_and_out[i];
            Vector3 vP = GetPosition(mikkTSpace, index);
            float fVal = iChannel == 0 ? vP.X : (iChannel == 1 ? vP.Y : vP.Z);
            int iCell = FindGridCell(fMin, fMax, fVal);

            Debug.Assert(piHashCount2[iCell] < piHashCount[iCell]);

            //    int * pTable = &piHashTable[piHashOffSets[iCell]];
            //    pTable[piHashCount2[iCell]] = i;  // vertex i has been inserted.
            piHashTable[piHashOffSets[iCell] + piHashCount2[iCell]] = i;// vertex i has been inserted.     
            ++piHashCount2[iCell];
        }
        for (int k = 0; k < CELLS; k++)
        {
            Debug.Assert(piHashCount2[k] == piHashCount[k]);  // verify the count
        }

        // find maximum amount of entries in any hash entry
        int iMaxCount = piHashCount[0];
        for (int k = 1; k < CELLS; k++)
        {
            if (iMaxCount < piHashCount[k])
            {
                iMaxCount = piHashCount[k];
            }
        }

        pTmpVert = new TmpVert[iMaxCount];

        // complete the merge
        for (int k = 0; k < CELLS; k++)
        {
            // extract table of cell k and amount of entries in it
            // int * pTable = &piHashTable[piHashOffSets[k]];
            int iEntries = piHashCount[k];
            if (iEntries < 2)
            {
                continue;
            }

            if (pTmpVert != null)
            {
                for (int e = 0; e < iEntries; e++)
                {
                    int j = piHashTable[piHashOffSets[k] + e];
                    Vector3 vP = GetPosition(mikkTSpace, piTriList_in_and_out[j]);
                    pTmpVert[e] = new TmpVert();
                    pTmpVert[e].vert[0] = vP.X;
                    pTmpVert[e].vert[1] = vP.Y;
                    pTmpVert[e].vert[2] = vP.Z;
                    pTmpVert[e].index = j;
                }
                MergeVertsFast(piTriList_in_and_out, pTmpVert, mikkTSpace, 0, iEntries - 1);
            }
            else
            {
                //TODO Ulikely to be null 
                int[] pTable = piHashTable[piHashOffSets[k]..(piHashOffSets[k] + iEntries)]; // Array.Copy(piHashTable, piHashOffSets[k], pTable, 0, piHashOffSets[k] + iEntries);
                MergeVertsSlow(piTriList_in_and_out, mikkTSpace, pTable, iEntries);
            }
        }
    }

    void MergeVertsFast(int[] piTriList_in_and_out, TmpVert[] pTmpVert, MikkTSpaceContext mikkTSpace, int iL_in, int iR_in)
    {
        // make bbox        
        float[] fvMin = new float[3];
        float[] fvMax = new float[3];
        for (int c = 0; c < 3; c++)
        {
            fvMin[c] = pTmpVert[iL_in].vert[c];
            fvMax[c] = fvMin[c];
        }
        for (int l = (iL_in + 1); l <= iR_in; l++)
        {
            for (int c = 0; c < 3; c++)
            {
                if (fvMin[c] > pTmpVert[l].vert[c])
                {
                    fvMin[c] = pTmpVert[l].vert[c];
                }
                else if (fvMax[c] < pTmpVert[l].vert[c])
                {
                    fvMax[c] = pTmpVert[l].vert[c];
                }
            }
        }

        float dx = fvMax[0] - fvMin[0];
        float dy = fvMax[1] - fvMin[1];
        float dz = fvMax[2] - fvMin[2];

        int channel = 0;
        if (dy > dx && dy > dz)
        {
            channel = 1;
        }
        else if (dz > dx)
        {
            channel = 2;
        }

        float fSep = 0.5f * (fvMax[channel] + fvMin[channel]);

        // terminate recursion when the separation/average value
        // is no longer strictly between fMin and fMax values.
        if (fSep >= fvMax[channel] || fSep <= fvMin[channel])
        {
            // complete the weld
            for (int l = iL_in; l <= iR_in; l++)
            {
                int i = pTmpVert[l].index;
                int index = piTriList_in_and_out[i];
                Vector3 vP = GetPosition(mikkTSpace, index);
                Vector3 vN = GetNormal(mikkTSpace, index);
                Vector3 vT = GetTexCoord(mikkTSpace, index);

                bool bNotFound = true;
                int l2 = iL_in, i2rec = -1;
                while (l2 < l && bNotFound)
                {
                    int i2 = pTmpVert[l2].index;
                    int index2 = piTriList_in_and_out[i2];
                    Vector3 vP2 = GetPosition(mikkTSpace, index2);
                    Vector3 vN2 = GetNormal(mikkTSpace, index2);
                    Vector3 vT2 = GetTexCoord(mikkTSpace, index2);
                    i2rec = i2;

                    //if (vP==vP2 && vN==vN2 && vT==vT2)
                    if (vP.X == vP2.X && vP.Y == vP2.Y && vP.Z == vP2.Z
                            && vN.X == vN2.X && vN.Y == vN2.Y && vN.Z == vN2.Z
                            && vT.X == vT2.X && vT.Y == vT2.Y && vT.Z == vT2.Z)
                    {
                        bNotFound = false;
                    }
                    else
                    {
                        ++l2;
                    }
                }

                // merge if previously found
                if (!bNotFound)
                {
                    piTriList_in_and_out[i] = piTriList_in_and_out[i2rec];
                }
            }
        }
        else
        {
            int iL = iL_in, iR = iR_in;
            Debug.Assert((iR_in - iL_in) > 0);  // at least 2 entries

            // separate (by fSep) all points between iL_in and iR_in in pTmpVert[]
            while (iL < iR)
            {
                bool bReadyLeftSwap = false, bReadyRightSwap = false;
                while ((!bReadyLeftSwap) && iL < iR)
                {
                    Debug.Assert(iL >= iL_in && iL <= iR_in);
                    bReadyLeftSwap = pTmpVert[iL].vert[channel] >= fSep;
                    if (!bReadyLeftSwap)
                    {
                        ++iL;
                    }
                }
                while ((!bReadyRightSwap) && iL < iR)
                {
                    Debug.Assert(iR >= iL_in && iR <= iR_in);
                    bReadyRightSwap = pTmpVert[iR].vert[channel] < fSep;
                    if (!bReadyRightSwap)
                    {
                        --iR;
                    }
                }
                Debug.Assert((iL < iR) || !(bReadyLeftSwap && bReadyRightSwap));

                if (bReadyLeftSwap && bReadyRightSwap)
                {
                    TmpVert sTmp = pTmpVert[iL];
                    Debug.Assert(iL < iR);
                    pTmpVert[iL] = pTmpVert[iR];
                    pTmpVert[iR] = sTmp;
                    ++iL;
                    --iR;
                }
            }

            Debug.Assert(iL == (iR + 1) || (iL == iR));
            if (iL == iR)
            {
                bool bReadyRightSwap = pTmpVert[iR].vert[channel] < fSep;
                if (bReadyRightSwap)
                {
                    ++iL;
                }
                else
                {
                    --iR;
                }
            }

            // only need to weld when there is more than 1 instance of the (x,y,z)
            if (iL_in < iR)
            {
                MergeVertsFast(piTriList_in_and_out, pTmpVert, mikkTSpace, iL_in, iR);  // weld all left of fSep
            }
            if (iL < iR_in)
            {
                MergeVertsFast(piTriList_in_and_out, pTmpVert, mikkTSpace, iL, iR_in);  // weld all right of (or equal to) fSep
            }
        }
    }

    void MergeVertsSlow(int[] piTriList_in_and_out, MikkTSpaceContext mikkTSpace, int[] pTable, int iEntries)
    {
        // this can be optimized further using a tree structure or more hashing.
        for (int e = 0; e < iEntries; e++)
        {
            int i = pTable[e];
            int index = piTriList_in_and_out[i];
            Vector3 vP = GetPosition(mikkTSpace, index);
            Vector3 vN = GetNormal(mikkTSpace, index);
            Vector3 vT = GetTexCoord(mikkTSpace, index);

            bool bNotFound = true;
            int e2 = 0, i2rec = -1;
            while (e2 < e && bNotFound)
            {
                int i2 = pTable[e2];
                int index2 = piTriList_in_and_out[i2];
                Vector3 vP2 = GetPosition(mikkTSpace, index2);
                Vector3 vN2 = GetNormal(mikkTSpace, index2);
                Vector3 vT2 = GetTexCoord(mikkTSpace, index2);
                i2rec = i2;

                if (vP == vP2 && vN == vN2 && vT == vT2)
                {
                    bNotFound = false;
                }
                else
                {
                    ++e2;
                }
            }

            // merge if previously found
            if (!bNotFound)
            {
                piTriList_in_and_out[i] = piTriList_in_and_out[i2rec];
            }
        }
    }

    //TODO Nehon : Not used...seems it's used in the original version if the structure to store the data in the regular method failed...
    void GenerateSharedVerticesIndexListSlow(int[] piTriList_in_and_out, MikkTSpaceContext mikkTSpace, int iNrTrianglesIn)
    {
        int iNumUniqueVerts = 0;
        for (int t = 0; t < iNrTrianglesIn; t++)
        {
            for (int i = 0; i < 3; i++)
            {
                int offs = t * 3 + i;
                int index = piTriList_in_and_out[offs];

                Vector3 vP = GetPosition(mikkTSpace, index);
                Vector3 vN = GetNormal(mikkTSpace, index);
                Vector3 vT = GetTexCoord(mikkTSpace, index);

                bool bFound = false;
                int t2 = 0, index2rec = -1;
                while (!bFound && t2 <= t)
                {
                    int j = 0;
                    while (!bFound && j < 3)
                    {
                        int index2 = piTriList_in_and_out[t2 * 3 + j];
                        Vector3 vP2 = GetPosition(mikkTSpace, index2);
                        Vector3 vN2 = GetNormal(mikkTSpace, index2);
                        Vector3 vT2 = GetTexCoord(mikkTSpace, index2);

                        if (vP == vP2 && vN == vN2 && vT == vT2)
                        {
                            bFound = true;
                        }
                        else
                        {
                            ++j;
                        }
                    }
                    if (!bFound)
                    {
                        ++t2;
                    }
                }

                Debug.Assert(bFound);
                // if we found our own
                if (index2rec == index)
                {
                    ++iNumUniqueVerts;
                }

                piTriList_in_and_out[offs] = index2rec;
            }
        }
    }

    int GenerateInitialVerticesIndexList(TriInfo[] pTriInfos, int[] piTriList_out, MikkTSpaceContext mikkTSpace, int iNrTrianglesIn)
    {
        int iTSpacesOffs = 0;
        int iDstTriIndex = 0;
        for (int f = 0; f < mikkTSpace.GetNumFaces(); f++)
        {
            int verts = mikkTSpace.GetNumVerticesOfFace(f);
            if (verts != 3 && verts != 4)
            {
                continue;
            }

            //TODO nehon : clean this, have a local TrinInfo and assign it to pTriInfo[iDstTriIndex] at the end... and change those variables names...
            pTriInfos[iDstTriIndex] = new TriInfo();
            pTriInfos[iDstTriIndex].orgFaceNumber = f;
            pTriInfos[iDstTriIndex].tSpacesOffs = iTSpacesOffs;

            if (verts == 3)
            {
                //TODO same here it should be easy once the local TriInfo is created.
                byte[] pVerts = pTriInfos[iDstTriIndex].vertNum;
                pVerts[0] = 0;
                pVerts[1] = 1;
                pVerts[2] = 2;
                piTriList_out[iDstTriIndex * 3 + 0] = MakeIndex(f, 0);
                piTriList_out[iDstTriIndex * 3 + 1] = MakeIndex(f, 1);
                piTriList_out[iDstTriIndex * 3 + 2] = MakeIndex(f, 2);
                ++iDstTriIndex;  // next
            }
            else
            {
                // Should not go there, we don't want to support quads but just in case
                {//TODO remove those useless brackets...
                    pTriInfos[iDstTriIndex + 1].orgFaceNumber = f;
                    pTriInfos[iDstTriIndex + 1].tSpacesOffs = iTSpacesOffs;
                }

                {
                    // need an order independent way to evaluate
                    // tspace on quads. This is done by splitting
                    // along the shortest diagonal.
                    int i0 = MakeIndex(f, 0);
                    int i1 = MakeIndex(f, 1);
                    int i2 = MakeIndex(f, 2);
                    int i3 = MakeIndex(f, 3);
                    Vector3 T0 = GetTexCoord(mikkTSpace, i0);
                    Vector3 T1 = GetTexCoord(mikkTSpace, i1);
                    Vector3 T2 = GetTexCoord(mikkTSpace, i2);
                    Vector3 T3 = GetTexCoord(mikkTSpace, i3);
                    float distSQ_02 = (T2 - T0).LengthSquared();
                    float distSQ_13 = (T3 - T1).LengthSquared();
                    bool bQuadDiagIs_02;
                    if (distSQ_02 < distSQ_13)
                    {
                        bQuadDiagIs_02 = true;
                    }
                    else if (distSQ_13 < distSQ_02)
                    {
                        bQuadDiagIs_02 = false;
                    }
                    else
                    {
                        Vector3 P0 = GetPosition(mikkTSpace, i0);
                        Vector3 P1 = GetPosition(mikkTSpace, i1);
                        Vector3 P2 = GetPosition(mikkTSpace, i2);
                        Vector3 P3 = GetPosition(mikkTSpace, i3);
                        float distSQ_022 = (P2 - P0).LengthSquared();
                        float distSQ_132 = (P3 - P1).LengthSquared();

                        bQuadDiagIs_02 = distSQ_132 >= distSQ_022;
                    }

                    if (bQuadDiagIs_02)
                    {
                        {
                            byte[] pVerts_A = pTriInfos[iDstTriIndex].vertNum;
                            pVerts_A[0] = 0;
                            pVerts_A[1] = 1;
                            pVerts_A[2] = 2;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i0;
                        piTriList_out[iDstTriIndex * 3 + 1] = i1;
                        piTriList_out[iDstTriIndex * 3 + 2] = i2;
                        ++iDstTriIndex;  // next
                        {
                            byte[] pVerts_B = pTriInfos[iDstTriIndex].vertNum;
                            pVerts_B[0] = 0;
                            pVerts_B[1] = 2;
                            pVerts_B[2] = 3;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i0;
                        piTriList_out[iDstTriIndex * 3 + 1] = i2;
                        piTriList_out[iDstTriIndex * 3 + 2] = i3;
                        ++iDstTriIndex;  // next
                    }
                    else
                    {
                        {
                            byte[] pVerts_A = pTriInfos[iDstTriIndex].vertNum;
                            pVerts_A[0] = 0;
                            pVerts_A[1] = 1;
                            pVerts_A[2] = 3;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i0;
                        piTriList_out[iDstTriIndex * 3 + 1] = i1;
                        piTriList_out[iDstTriIndex * 3 + 2] = i3;
                        ++iDstTriIndex;  // next
                        {
                            byte[] pVerts_B = pTriInfos[iDstTriIndex].vertNum;
                            pVerts_B[0] = 1;
                            pVerts_B[1] = 2;
                            pVerts_B[2] = 3;
                        }
                        piTriList_out[iDstTriIndex * 3 + 0] = i1;
                        piTriList_out[iDstTriIndex * 3 + 1] = i2;
                        piTriList_out[iDstTriIndex * 3 + 2] = i3;
                        ++iDstTriIndex;  // next
                    }
                }
            }

            iTSpacesOffs += verts;
            Debug.Assert(iDstTriIndex <= iNrTrianglesIn);
        }

        for (int t = 0; t < iNrTrianglesIn; t++)
        {
            pTriInfos[t].flag = 0;
        }

        // return total amount of tspaces
        return iTSpacesOffs;
    }

    Vector3 GetPosition(MikkTSpaceContext mikkTSpace, int index)
    {
        int iF = 0;
        int iI = 0;
        IndexToData(ref iF, ref iI, index);
        mikkTSpace.GetPosition(iF, iI, out var pos);
        return new Vector3(pos);
    }

    Vector3 GetNormal(MikkTSpaceContext mikkTSpace, int index)
    {
        //TODO nehon: very ugly but works... using arrays to pass integers as references in the IndexToData
        int iF = 0;
        int iI = 0;
        IndexToData(ref iF, ref iI, index);
        mikkTSpace.GetNormal(iF, iI, out var norm);
        return new Vector3(norm);
    }

    Vector3 GetTexCoord(MikkTSpaceContext mikkTSpace, int index)
    {
        //TODO nehon: very ugly but works... using arrays to pass integers as references in the IndexToData
        int iF = 0;
        int iI = 0;
        IndexToData(ref iF, ref iI, index);
        mikkTSpace.GetTexCoord(iF, iI, out var texc);
        return new Vector3(texc[0], texc[1], 1.0f);
    }

    // returns the texture area times 2
    float CalcTexArea(MikkTSpaceContext mikkTSpace, int[] indices)
    {
        Vector3 t1 = GetTexCoord(mikkTSpace, indices[0]);
        Vector3 t2 = GetTexCoord(mikkTSpace, indices[1]);
        Vector3 t3 = GetTexCoord(mikkTSpace, indices[2]);

        float t21x = t2.X - t1.X;
        float t21y = t2.Y - t1.Y;
        float t31x = t3.X - t1.X;
        float t31y = t3.Y - t1.Y;

        float fSignedAreaSTx2 = t21x * t31y - t21y * t31x;

        return fSignedAreaSTx2 < 0 ? (-fSignedAreaSTx2) : fSignedAreaSTx2;
    }

    bool isNotZero(float v)
    {
        return Math.Abs(v) > 0;
    }

    void InitTriInfo(TriInfo[] pTriInfos, int[] piTriListIn, MikkTSpaceContext mikkTSpace, int iNrTrianglesIn)
    {

        // pTriInfos[f].flag is cleared in GenerateInitialVerticesIndexList() which is called before this function.
        // generate neighbor info list
        for (int f = 0; f < iNrTrianglesIn; f++)
        {
            for (int i = 0; i < 3; i++)
            {
                pTriInfos[f].faceNeighbors[i] = -1;
                pTriInfos[f].assignedGroup[i] = null;

                pTriInfos[f].os.X = 0.0f;
                pTriInfos[f].os.Y = 0.0f;
                pTriInfos[f].os.Z = 0.0f;
                pTriInfos[f].ot.X = 0.0f;
                pTriInfos[f].ot.Y = 0.0f;
                pTriInfos[f].ot.Z = 0.0f;
                pTriInfos[f].magS = 0;
                pTriInfos[f].magT = 0;

                // assumed bad
                pTriInfos[f].flag |= GROUP_WITH_ANY;
            }
        }

        // evaluate first order derivatives
        for (int f = 0; f < iNrTrianglesIn; f++)
        {
            // initial values
            Vector3 v1 = GetPosition(mikkTSpace, piTriListIn[f * 3 + 0]);
            Vector3 v2 = GetPosition(mikkTSpace, piTriListIn[f * 3 + 1]);
            Vector3 v3 = GetPosition(mikkTSpace, piTriListIn[f * 3 + 2]);
            Vector3 t1 = GetTexCoord(mikkTSpace, piTriListIn[f * 3 + 0]);
            Vector3 t2 = GetTexCoord(mikkTSpace, piTriListIn[f * 3 + 1]);
            Vector3 t3 = GetTexCoord(mikkTSpace, piTriListIn[f * 3 + 2]);

            float t21x = t2.X - t1.X;
            float t21y = t2.Y - t1.Y;
            float t31x = t3.X - t1.X;
            float t31y = t3.Y - t1.Y;
            Vector3 d1 = v2 - v1;
            Vector3 d2 = v3 - v1;

            float fSignedAreaSTx2 = t21x * t31y - t21y * t31x;
            //Debug.Assert(fSignedAreaSTx2!=0);
            Vector3 vOs = (d1 * t31y) - (d2 * t21y);  // eq 18
            Vector3 vOt = (d1 * -t31x) + (d2 * t21x);  // eq 19

            pTriInfos[f].flag |= (fSignedAreaSTx2 > 0 ? ORIENT_PRESERVING : 0);

            if (isNotZero(fSignedAreaSTx2))
            {
                float fAbsArea = Math.Abs(fSignedAreaSTx2);
                float fLenOs = vOs.Length();
                float fLenOt = vOt.Length();
                float fS = (pTriInfos[f].flag & ORIENT_PRESERVING) == 0 ? (-1.0f) : 1.0f;
                if (isNotZero(fLenOs))
                {
                    pTriInfos[f].os = vOs * (fS / fLenOs);
                }
                if (isNotZero(fLenOt))
                {
                    pTriInfos[f].ot = vOt * (fS / fLenOt);
                }

                // evaluate magnitudes prior to normalization of vOs and vOt
                pTriInfos[f].magS = fLenOs / fAbsArea;
                pTriInfos[f].magT = fLenOt / fAbsArea;

                // if this is a good triangle
                if (isNotZero(pTriInfos[f].magS) && isNotZero(pTriInfos[f].magT))
                {
                    pTriInfos[f].flag &= (~GROUP_WITH_ANY);
                }
            }
        }

        // force otherwise healthy quads to a fixed orientation
        int t = 0;
        while (t < (iNrTrianglesIn - 1))
        {
            int iFO_a = pTriInfos[t].orgFaceNumber;
            int iFO_b = pTriInfos[t + 1].orgFaceNumber;
            if (iFO_a == iFO_b)
            {
                // this is a quad
                bool bIsDeg_a = (pTriInfos[t].flag & MARK_DEGENERATE) != 0;
                bool bIsDeg_b = (pTriInfos[t + 1].flag & MARK_DEGENERATE) != 0;

                // bad triangles should already have been removed by
                // DegenPrologue(), but just in case check bIsDeg_a and bIsDeg_a are false
                if ((bIsDeg_a || bIsDeg_b) == false)
                {
                    bool bOrientA = (pTriInfos[t].flag & ORIENT_PRESERVING) != 0;
                    bool bOrientB = (pTriInfos[t + 1].flag & ORIENT_PRESERVING) != 0;
                    // if this happens the quad has extremely bad mapping!!
                    if (bOrientA != bOrientB)
                    {
                        //printf("found quad with bad mapping\n");
                        bool bChooseOrientFirstTri = false;
                        if ((pTriInfos[t + 1].flag & GROUP_WITH_ANY) != 0)
                        {
                            bChooseOrientFirstTri = true;
                        }
                        else if (CalcTexArea(mikkTSpace, piTriListIn[(t * 3 + 0)..(t * 3 + 3)]) >= CalcTexArea(mikkTSpace, piTriListIn[((t + 1) * 3 + 0)..((t + 1) * 3 + 3)]))
                        {
                            bChooseOrientFirstTri = true;
                        }

                        // force match
                        {
                            int t0 = bChooseOrientFirstTri ? t : (t + 1);
                            int t1 = bChooseOrientFirstTri ? (t + 1) : t;
                            pTriInfos[t1].flag &= (~ORIENT_PRESERVING);  // clear first
                            pTriInfos[t1].flag |= (pTriInfos[t0].flag & ORIENT_PRESERVING);  // copy bit
                        }
                    }
                }
                t += 2;
            }
            else
            {
                ++t;
            }
        }

        // match up edge pairs
        {
            //Edge * pEdges = (Edge *) malloc(sizeof(Edge)*iNrTrianglesIn*3);
            Edge[] pEdges = new Edge[iNrTrianglesIn * 3];

            //TODO nehon weird... original algorithm checked if pEdges is null but it's just been allocated... weirder, it does something different if the edges are null...
            //    if (pEdges==null)
            //      BuildNeighborsSlow(pTriInfos, piTriListIn, iNrTrianglesIn);
            //    else
            //    {
            BuildNeighborsFast(pTriInfos, pEdges, piTriListIn, iNrTrianglesIn);

            //    }
        }
    }

    int Build4RuleGroups(TriInfo[] pTriInfos, Group[] pGroups, int[] piGroupTrianglesBuffer, int[] piTriListIn, int iNrTrianglesIn)
    {
        int iNrMaxGroups = iNrTrianglesIn * 3;
        int iNrActiveGroups = 0;
        int iOffSet = 0;
        // (void)iNrMaxGroups;  /* quiet warnings in non debug mode */
        for (int f = 0; f < iNrTrianglesIn; f++)
        {
            for (int i = 0; i < 3; i++)
            {
                // if not assigned to a group
                if ((pTriInfos[f].flag & GROUP_WITH_ANY) == 0 && pTriInfos[f].assignedGroup[i] == null)
                {
                    bool bOrPre;
                    int vert_index = piTriListIn[f * 3 + i];
                    Debug.Assert(iNrActiveGroups < iNrMaxGroups);
                    pTriInfos[f].assignedGroup[i] = new Group();
                    pGroups[iNrActiveGroups] = pTriInfos[f].assignedGroup[i];
                    pTriInfos[f].assignedGroup[i].vertexRepresentative = vert_index;
                    pTriInfos[f].assignedGroup[i].orientationPreserving = (pTriInfos[f].flag & ORIENT_PRESERVING) != 0;
                    pTriInfos[f].assignedGroup[i].nrFaces = 0;

                    ++iNrActiveGroups;

                    addTriToGroup(pTriInfos[f].assignedGroup[i], f);
                    bOrPre = (pTriInfos[f].flag & ORIENT_PRESERVING) != 0;
                    int neigh_indexL = pTriInfos[f].faceNeighbors[i];
                    int neigh_indexR = pTriInfos[f].faceNeighbors[i > 0 ? (i - 1) : 2];
                    if (neigh_indexL >= 0)
                    {
                        // neighbor
                        bool bAnswer
                                = AssignRecur(piTriListIn, pTriInfos, neigh_indexL,
                                        pTriInfos[f].assignedGroup[i]);

                        bool bOrPre2 = (pTriInfos[neigh_indexL].flag & ORIENT_PRESERVING) != 0;
                        bool bDiff = bOrPre != bOrPre2;
                        Debug.Assert(bAnswer || bDiff);
                        //(void)bAnswer, (void)bDiff;  /* quiet warnings in non debug mode */
                    }
                    if (neigh_indexR >= 0)
                    {
                        // neighbor
                        bool bAnswer
                                = AssignRecur(piTriListIn, pTriInfos, neigh_indexR,
                                        pTriInfos[f].assignedGroup[i]);

                        bool bOrPre2 = (pTriInfos[neigh_indexR].flag & ORIENT_PRESERVING) != 0;
                        bool bDiff = bOrPre != bOrPre2;
                        Debug.Assert(bAnswer || bDiff);
                        //(void)bAnswer, (void)bDiff;  /* quiet warnings in non debug mode */
                    }

                    int[] faceIndices = new int[pTriInfos[f].assignedGroup[i].nrFaces];
                    //pTriInfos[f].assignedGroup[i].faceIndices.toArray(faceIndices);
                    for (int j = 0; j < faceIndices.Length; j++)
                    {
                        faceIndices[j] = pTriInfos[f].assignedGroup[i].faceIndices[j];
                    }

                    //Nehon: copy back the faceIndices data into the groupTriangleBuffer.
                    Array.Copy(faceIndices, 0, piGroupTrianglesBuffer, iOffSet, pTriInfos[f].assignedGroup[i].nrFaces);
                    // update offSet
                    iOffSet += pTriInfos[f].assignedGroup[i].nrFaces;
                    // since the groups are disjoint a triangle can never
                    // belong to more than 3 groups. Subsequently something
                    // is completely screwed if this Debug.Assertion ever hits.
                    Debug.Assert(iOffSet <= iNrMaxGroups);
                }
            }
        }

        return iNrActiveGroups;
    }

    void addTriToGroup(Group group, int triIndex)
    {
        //group.faceIndices[group.nrFaces] = triIndex;
        group.faceIndices.Add(triIndex);
        ++group.nrFaces;
    }

    bool AssignRecur(int[] piTriListIn, TriInfo[] psTriInfos, int iMyTriIndex, Group pGroup)
    {
        TriInfo pMyTriInfo = psTriInfos[iMyTriIndex];

        // track down vertex
        int iVertRep = pGroup.vertexRepresentative;
        int index = 3 * iMyTriIndex;
        int i = -1;
        if (piTriListIn[index] == iVertRep)
        {
            i = 0;
        }
        else if (piTriListIn[index + 1] == iVertRep)
        {
            i = 1;
        }
        else if (piTriListIn[index + 2] == iVertRep)
        {
            i = 2;
        }
        Debug.Assert(i >= 0 && i < 3);

        // early out
        if (pMyTriInfo.assignedGroup[i] == pGroup)
        {
            return true;
        }
        else if (pMyTriInfo.assignedGroup[i] != null)
        {
            return false;
        }
        if ((pMyTriInfo.flag & GROUP_WITH_ANY) != 0)
        {
            // first to group with a group-with-anything triangle
            // determines its orientation.
            // This is the only existing order dependency in the code!!
            if (pMyTriInfo.assignedGroup[0] == null
                    && pMyTriInfo.assignedGroup[1] == null
                    && pMyTriInfo.assignedGroup[2] == null)
            {
                pMyTriInfo.flag &= (~ORIENT_PRESERVING);
                pMyTriInfo.flag |= (pGroup.orientationPreserving ? ORIENT_PRESERVING : 0);
            }
        }
        {
            bool bOrient = (pMyTriInfo.flag & ORIENT_PRESERVING) != 0;
            if (bOrient != pGroup.orientationPreserving)
            {
                return false;
            }
        }

        addTriToGroup(pGroup, iMyTriIndex);
        pMyTriInfo.assignedGroup[i] = pGroup;

        {
            int neigh_indexL = pMyTriInfo.faceNeighbors[i];
            int neigh_indexR = pMyTriInfo.faceNeighbors[i > 0 ? (i - 1) : 2];
            if (neigh_indexL >= 0)
            {
                AssignRecur(piTriListIn, psTriInfos, neigh_indexL, pGroup);
            }
            if (neigh_indexR >= 0)
            {
                AssignRecur(piTriListIn, psTriInfos, neigh_indexR, pGroup);
            }
        }

        return true;
    }

    bool GenerateTSpaces(TSpace[] psTspace, TriInfo[] pTriInfos, Group[] pGroups,
            int iNrActiveGroups, int[] piTriListIn, float fThresCos,
            MikkTSpaceContext mikkTSpace)
    {
        TSpace[] pSubGroupTspace;
        SubGroup[] pUniSubGroups;
        int[] pTmpMembers;
        int iMaxNrFaces = 0, iUniqueTspaces = 0, g = 0, i = 0;
        for (g = 0; g < iNrActiveGroups; g++)
        {
            if (iMaxNrFaces < pGroups[g].nrFaces)
            {
                iMaxNrFaces = pGroups[g].nrFaces;
            }
        }

        if (iMaxNrFaces == 0)
        {
            return true;
        }

        // make initial allocations
        pSubGroupTspace = new TSpace[iMaxNrFaces];
        pUniSubGroups = new SubGroup[iMaxNrFaces];
        pTmpMembers = new int[iMaxNrFaces];


        iUniqueTspaces = 0;
        for (g = 0; g < iNrActiveGroups; g++)
        {
            Group pGroup = pGroups[g];
            int iUniqueSubGroups = 0, s = 0;

            for (i = 0; i < pGroup.nrFaces; i++) // triangles
            {
                int f = pGroup.faceIndices[i];  // triangle number
                int index = -1, iVertIndex = -1, iOF_1 = -1, iMembers = 0, j = 0, l = 0;
                SubGroup tmp_group = new SubGroup();
                bool bFound;
                Vector3 n, vOs, vOt;
                if (pTriInfos[f].assignedGroup[0] == pGroup)
                {
                    index = 0;
                }
                else if (pTriInfos[f].assignedGroup[1] == pGroup)
                {
                    index = 1;
                }
                else if (pTriInfos[f].assignedGroup[2] == pGroup)
                {
                    index = 2;
                }
                Debug.Assert(index >= 0 && index < 3);

                iVertIndex = piTriListIn[f * 3 + index];
                Debug.Assert(iVertIndex == pGroup.vertexRepresentative);

                // is normalized already
                n = GetNormal(mikkTSpace, iVertIndex);

                // project
                vOs = pTriInfos[f].os - (n * (Vector3.Dot(n, pTriInfos[f].os)));
                vOt = pTriInfos[f].ot - (n * (Vector3.Dot(n, pTriInfos[f].ot)));
                vOs = Vector3.Normalize(vOs);
                vOt = Vector3.Normalize(vOt);

                // original face number
                iOF_1 = pTriInfos[f].orgFaceNumber;

                iMembers = 0;
                for (j = 0; j < pGroup.nrFaces; j++)
                {
                    int t = pGroup.faceIndices[j];  // triangle number
                    int iOF_2 = pTriInfos[t].orgFaceNumber;

                    // project
                    Vector3 vOs2 = pTriInfos[t].os - (n * (Vector3.Dot(n, pTriInfos[t].os)));
                    Vector3 vOt2 = pTriInfos[t].ot - (n * (Vector3.Dot(n, pTriInfos[t].ot)));
                    vOs2 = Vector3.Normalize(vOs2);
                    vOt2 = Vector3.Normalize(vOt2);

                    {
                        bool bAny = ((pTriInfos[f].flag | pTriInfos[t].flag) & GROUP_WITH_ANY) != 0;
                        // make sure triangles which belong to the same quad are joined.
                        bool bSameOrgFace = iOF_1 == iOF_2;

                        float fCosS = Vector3.Dot(vOs, vOs2);
                        float fCosT = Vector3.Dot(vOt, vOt2);

                        Debug.Assert(f != t || bSameOrgFace);  // sanity check
                        if (bAny || bSameOrgFace || (fCosS > fThresCos && fCosT > fThresCos))
                        {
                            pTmpMembers[iMembers++] = t;
                        }
                    }
                }

                // sort pTmpMembers
                tmp_group.nrFaces = iMembers;
                tmp_group.triMembers = pTmpMembers;
                if (iMembers > 1)
                {
                    quickSort(pTmpMembers, 0, iMembers - 1, INTERNAL_RND_SORT_SEED);
                }

                // look for an existing match
                bFound = false;
                l = 0;
                while (l < iUniqueSubGroups && !bFound)
                {
                    bFound = compareSubGroups(tmp_group, pUniSubGroups[l]);
                    if (!bFound)
                    {
                        ++l;
                    }
                }

                // assign tangent space index
                Debug.Assert(bFound || l == iUniqueSubGroups);
                //piTempTangIndices[f*3+index] = iUniqueTspaces+l;

                // if no match was found we allocate a new subgroup
                if (!bFound)
                {
                    // insert new subgroup
                    int[] pIndices = new int[iMembers];
                    pUniSubGroups[iUniqueSubGroups] = new SubGroup();
                    pUniSubGroups[iUniqueSubGroups].nrFaces = iMembers;
                    pUniSubGroups[iUniqueSubGroups].triMembers = pIndices;
                    Array.Copy(tmp_group.triMembers, 0, pIndices, 0, iMembers);
                    //memcpy(pIndices, tmp_group.pTriMembers, iMembers*sizeof(int));
                    pSubGroupTspace[iUniqueSubGroups]
                            = evalTspace(tmp_group.triMembers, iMembers, piTriListIn, pTriInfos, mikkTSpace, pGroup.vertexRepresentative);
                    ++iUniqueSubGroups;
                }

                // output tspace
                {
                    int iOffs = pTriInfos[f].tSpacesOffs;
                    int iVert = pTriInfos[f].vertNum[index];
                    TSpace pTS_out = psTspace[iOffs + iVert];
                    Debug.Assert(pTS_out.counter < 2);
                    Debug.Assert(((pTriInfos[f].flag & ORIENT_PRESERVING) != 0) == pGroup.orientationPreserving);
                    if (pTS_out.counter == 1)
                    {
                        pTS_out.Set(AvgTSpace(pTS_out, pSubGroupTspace[l]));
                        pTS_out.counter = 2;  // update counter
                        pTS_out.orient = pGroup.orientationPreserving;
                    }
                    else
                    {
                        Debug.Assert(pTS_out.counter == 0);
                        pTS_out.Set(pSubGroupTspace[l]);
                        pTS_out.counter = 1;  // update counter
                        pTS_out.orient = pGroup.orientationPreserving;
                    }
                }
            }

            iUniqueTspaces += iUniqueSubGroups;
        }

        return true;
    }

    TSpace evalTspace(int[] face_indices, int iFaces, int[] piTriListIn, TriInfo[] pTriInfos,
            MikkTSpaceContext mikkTSpace, int iVertexRepresentative)
    {
        TSpace res = new TSpace();
        float fAngleSum = 0;

        for (int face = 0; face < iFaces; face++)
        {
            int f = face_indices[face];

            // only valid triangles Get to add their contribution
            if ((pTriInfos[f].flag & GROUP_WITH_ANY) == 0)
            {

                int i = -1;
                if (piTriListIn[3 * f + 0] == iVertexRepresentative)
                {
                    i = 0;
                }
                else if (piTriListIn[3 * f + 1] == iVertexRepresentative)
                {
                    i = 1;
                }
                else if (piTriListIn[3 * f + 2] == iVertexRepresentative)
                {
                    i = 2;
                }
                Debug.Assert(i >= 0 && i < 3);

                // project
                int index = piTriListIn[3 * f + i];
                Vector3 n = GetNormal(mikkTSpace, index);
                Vector3 vOs = pTriInfos[f].os - (n * (Vector3.Dot(n, pTriInfos[f].os)));
                Vector3 vOt = pTriInfos[f].ot - (n * (Vector3.Dot(n, pTriInfos[f].ot)));
                vOs = Vector3.Normalize(vOs);
                vOt = Vector3.Normalize(vOt);

                int i2 = piTriListIn[3 * f + (i < 2 ? (i + 1) : 0)];
                int i1 = piTriListIn[3 * f + i];
                int i0 = piTriListIn[3 * f + (i > 0 ? (i - 1) : 2)];

                Vector3 p0 = GetPosition(mikkTSpace, i0);
                Vector3 p1 = GetPosition(mikkTSpace, i1);
                Vector3 p2 = GetPosition(mikkTSpace, i2);
                Vector3 v1 = p0 - p1;
                Vector3 v2 = p2 - p1;

                // project
                v1 -= n * Vector3.Dot(n, v1);
                v1 = Vector3.Normalize(v1);
                v2 -= n * Vector3.Dot(n, v2);
                v2 = Vector3.Normalize(v2);

                // weight contribution by the angle
                // between the two edge vectors
                float fCos = Vector3.Dot(v1,v2);
                fCos = fCos > 1 ? 1 : (fCos < (-1) ? (-1) : fCos);
                float fAngle = (float)Math.Acos(fCos);
                float fMagS = pTriInfos[f].magS;
                float fMagT = pTriInfos[f].magT;

                res.os += (vOs * (fAngle));
                res.ot += (vOt * (fAngle));
                res.magS += (fAngle * fMagS);
                res.magT += (fAngle * fMagT);
                fAngleSum += fAngle;
            }
        }

        // normalize
        res.os = Vector3.Normalize(res.os);
        res.ot = Vector3.Normalize(res.ot);

        if (fAngleSum > 0)
        {
            res.magS /= fAngleSum;
            res.magT /= fAngleSum;
        }

        return res;
    }

    bool compareSubGroups(SubGroup pg1, SubGroup pg2)
    {
        if (pg2 == null || (pg1.nrFaces != pg2.nrFaces))
        {
            return false;
        }
        bool stillSame = true;
        int i = 0;
        while (i < pg1.nrFaces && stillSame)
        {
            stillSame = pg1.triMembers[i] == pg2.triMembers[i];
            if (stillSame)
            {
                ++i;
            }
        }
        return stillSame;
    }

    void quickSort(int[] pSortBuffer, int iLeft, int iRight, long uSeed)
    {
        int iL, iR, n, index, iMid, iTmp;

        // Random
        long t = uSeed & 31;
        t = ((int)uSeed << (int)t) | ((int)uSeed >> (int)(32 - t));
        uSeed = uSeed + t + 3;
        // Random end
        uSeed = uSeed & 0xffffffffL;

        iL = iLeft;
        iR = iRight;
        n = (iR - iL) + 1;
        Debug.Assert(n >= 0);
        index = (int)((uSeed & 0xffffffffL) % n);

        iMid = pSortBuffer[index + iL];

        do
        {
            while (pSortBuffer[iL] < iMid)
            {
                ++iL;
            }
            while (pSortBuffer[iR] > iMid)
            {
                --iR;
            }

            if (iL <= iR)
            {
                iTmp = pSortBuffer[iL];
                pSortBuffer[iL] = pSortBuffer[iR];
                pSortBuffer[iR] = iTmp;
                ++iL;
                --iR;
            }
        } while (iL <= iR);

        if (iLeft < iR)
        {
            quickSort(pSortBuffer, iLeft, iR, uSeed);
        }
        if (iL < iRight)
        {
            quickSort(pSortBuffer, iL, iRight, uSeed);
        }
    }

    void BuildNeighborsFast(TriInfo[] pTriInfos, Edge[] pEdges, int[] piTriListIn, int iNrTrianglesIn)
    {
        // build array of edges
        long uSeed = INTERNAL_RND_SORT_SEED;        // could replace with a random seed?

        for (int f = 0; f < iNrTrianglesIn; f++)
        {
            for (int i = 0; i < 3; i++)
            {
                int i0 = piTriListIn[f * 3 + i];
                int i1 = piTriListIn[f * 3 + (i < 2 ? (i + 1) : 0)];
                pEdges[f * 3 + i] = new Edge();
                pEdges[f * 3 + i].SetI0(i0 < i1 ? i0 : i1);      // put minimum index in i0
                pEdges[f * 3 + i].SetI1(!(i0 < i1) ? i0 : i1);    // put maximum index in i1
                pEdges[f * 3 + i].SetF(f);              // record face number
            }
        }

        // sort over all edges by i0, this is the pricey one.
        QuickSortEdges(pEdges, 0, iNrTrianglesIn * 3 - 1, 0, uSeed);  // sort channel 0 which is i0

        // sub sort over i1, should be fast.
        // could replace this with a 64 bit int sort over (i0,i1)
        // with i0 as msb in the quicksort call above.
        int iEntries = iNrTrianglesIn * 3;
        int iCurStartIndex = 0;
        for (int i = 1; i < iEntries; i++)
        {
            if (pEdges[iCurStartIndex].GetI0() != pEdges[i].GetI0())
            {
                int iL = iCurStartIndex;
                int iR = i - 1;
                //int iElems = i-iL;
                iCurStartIndex = i;
                QuickSortEdges(pEdges, iL, iR, 1, uSeed);  // sort channel 1 which is i1
            }
        }

        // sub sort over f, which should be fast.
        // this step is to remain compliant with BuildNeighborsSlow() when
        // more than 2 triangles use the same edge (such as a butterfly topology).
        iCurStartIndex = 0;
        for (int i = 1; i < iEntries; i++)
        {
            if (pEdges[iCurStartIndex].GetI0() != pEdges[i].GetI0() || pEdges[iCurStartIndex].GetI1() != pEdges[i].GetI1())
            {
                int iL = iCurStartIndex;
                int iR = i - 1;
                //int iElems = i-iL;
                iCurStartIndex = i;
                QuickSortEdges(pEdges, iL, iR, 2, uSeed);  // sort channel 2 which is f
            }
        }

        // pair up, adjacent triangles
        for (int i = 0; i < iEntries; i++)
        {
            int i0 = pEdges[i].GetI0();
            int i1 = pEdges[i].GetI1();
            int g = pEdges[i].GetF();
            bool bUnassigned_A;

            int[] i0_A = new int[1];
            int[] i1_A = new int[1];
            int[] edgenum_A = new int[1];
            int[] edgenum_B = new int[1];
            //int edgenum_B=0;  // 0,1 or 2
            int[] triList = new int[3];
            Array.Copy(piTriListIn, g * 3, triList, 0, 3);
            GetEdge(i0_A, i1_A, edgenum_A, triList, i0, i1);  // resolve index ordering and edge_num
            bUnassigned_A = pTriInfos[g].faceNeighbors[edgenum_A[0]] == -1;

            if (bUnassigned_A)
            {
                // Get true index ordering
                int j = i + 1, t;
                bool bNotFound = true;
                while (j < iEntries && i0 == pEdges[j].GetI0() && i1 == pEdges[j].GetI1() && bNotFound)
                {
                    bool bUnassigned_B;
                    int[] i0_B = new int[1];
                    int[] i1_B = new int[1];
                    t = pEdges[j].GetF();
                    // flip i0_B and i1_B
                    Array.Copy(piTriListIn, t * 3, triList, 0, 3);
                    GetEdge(i1_B, i0_B, edgenum_B, triList, pEdges[j].GetI0(), pEdges[j].GetI1());  // resolve index ordering and edge_num
                                                                                                    //Debug.Assert(!(i0_A==i1_B && i1_A==i0_B));
                    bUnassigned_B = pTriInfos[t].faceNeighbors[edgenum_B[0]] == -1;
                    if (i0_A[0] == i0_B[0] && i1_A[0] == i1_B[0] && bUnassigned_B)
                    {
                        bNotFound = false;
                    }
                    else
                    {
                        ++j;
                    }
                }

                if (!bNotFound)
                {
                    int t2 = pEdges[j].GetF();
                    pTriInfos[g].faceNeighbors[edgenum_A[0]] = t2;
                    //Debug.Assert(pTriInfos[t].FaceNeighbors[edgenum_B]==-1);
                    pTriInfos[t2].faceNeighbors[edgenum_B[0]] = g;
                }
            }
        }
    }

    void buildNeighborsSlow(TriInfo[] pTriInfos, int[] piTriListIn, int iNrTrianglesIn)
    {

        for (int f = 0; f < iNrTrianglesIn; f++)
        {
            for (int i = 0; i < 3; i++)
            {
                // if unassigned
                if (pTriInfos[f].faceNeighbors[i] == -1)
                {
                    int i0_A = piTriListIn[f * 3 + i];
                    int i1_A = piTriListIn[f * 3 + (i < 2 ? (i + 1) : 0)];

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
                                int i1_B = piTriListIn[t * 3 + j];
                                int i0_B = piTriListIn[t * 3 + (j < 2 ? (j + 1) : 0)];
                                //Debug.Assert(!(i0_A==i1_B && i1_A==i0_B));
                                if (i0_A == i0_B && i1_A == i1_B)
                                {
                                    bFound = true;
                                }
                                else
                                {
                                    ++j;
                                }
                            }
                        }

                        if (!bFound)
                        {
                            ++t;
                        }
                    }

                    // assign neighbors
                    if (bFound)
                    {
                        pTriInfos[f].faceNeighbors[i] = t;
                        //Debug.Assert(pTriInfos[t].FaceNeighbors[j]==-1);
                        pTriInfos[t].faceNeighbors[j] = f;
                    }
                }
            }
        }
    }

    void QuickSortEdges(Edge[] pSortBuffer, int iLeft, int iRight, int channel, long uSeed)
    {
        // early out
        Edge sTmp;
        int iElems = iRight - iLeft + 1;
        if (iElems < 2)
        {
            return;
        }
        else if (iElems == 2)
        {
            if (pSortBuffer[iLeft].array[channel] > pSortBuffer[iRight].array[channel])
            {
                sTmp = pSortBuffer[iLeft];
                pSortBuffer[iLeft] = pSortBuffer[iRight];
                pSortBuffer[iRight] = sTmp;
            }
            return;
        }

        // Random
        long t = uSeed & 31;
        t = ((int)uSeed << (int)t) | ((int)uSeed >> (int)(32 - t));
        uSeed = uSeed + t + 3;
        // Random end

        uSeed = uSeed & 0xffffffffL;

        int iL = iLeft;
        int iR = iRight;
        int n = (iR - iL) + 1;
        Debug.Assert(n >= 0);
        int index = (int)(uSeed % n);

        int iMid = pSortBuffer[index + iL].array[channel];

        do
        {
            while (pSortBuffer[iL].array[channel] < iMid)
            {
                ++iL;
            }
            while (pSortBuffer[iR].array[channel] > iMid)
            {
                --iR;
            }

            if (iL <= iR)
            {
                sTmp = pSortBuffer[iL];
                pSortBuffer[iL] = pSortBuffer[iR];
                pSortBuffer[iR] = sTmp;
                ++iL;
                --iR;
            }
        } while (iL <= iR);

        if (iLeft < iR)
        {
            QuickSortEdges(pSortBuffer, iLeft, iR, channel, uSeed);
        }
        if (iL < iRight)
        {
            QuickSortEdges(pSortBuffer, iL, iRight, channel, uSeed);
        }
    }

    // resolve ordering and edge number
    void GetEdge(int[] i0_out, int[] i1_out, int[] edgenum_out, int[] indices, int i0_in, int i1_in)
    {
        edgenum_out[0] = -1;

        // test if first index is on the edge
        if (indices[0] == i0_in || indices[0] == i1_in)
        {
            // test if second index is on the edge
            if (indices[1] == i0_in || indices[1] == i1_in)
            {
                edgenum_out[0] = 0;  // first edge
                i0_out[0] = indices[0];
                i1_out[0] = indices[1];
            }
            else
            {
                edgenum_out[0] = 2;  // third edge
                i0_out[0] = indices[2];
                i1_out[0] = indices[0];
            }
        }
        else
        {
            // only second and third index is on the edge
            edgenum_out[0] = 1;  // second edge
            i0_out[0] = indices[1];
            i1_out[0] = indices[2];
        }
    }

    void DegenPrologue(TriInfo[] pTriInfos, int[] piTriList_out, int iNrTrianglesIn, int iTotTris)
    {

        // locate quads with only one good triangle
        int t = 0;
        while (t < (iTotTris - 1))
        {
            int iFO_a = pTriInfos[t].orgFaceNumber;
            int iFO_b = pTriInfos[t + 1].orgFaceNumber;
            if (iFO_a == iFO_b)
            {
                // this is a quad
                bool bIsDeg_a = (pTriInfos[t].flag & MARK_DEGENERATE) != 0;
                bool bIsDeg_b = (pTriInfos[t + 1].flag & MARK_DEGENERATE) != 0;
                //TODO nehon : Check this in detail as this operation is utterly strange
                if ((bIsDeg_a ^ bIsDeg_b) != false)
                {
                    pTriInfos[t].flag |= QUAD_ONE_DEGEN_TRI;
                    pTriInfos[t + 1].flag |= QUAD_ONE_DEGEN_TRI;
                }
                t += 2;
            }
            else
            {
                ++t;
            }
        }

        // reorder list so all degen triangles are moved to the back
        // without reordering the good triangles
        int iNextGoodTriangleSearchIndex = 1;
        t = 0;
        bool bStillFindingGoodOnes = true;
        while (t < iNrTrianglesIn && bStillFindingGoodOnes)
        {
            bool bIsGood = (pTriInfos[t].flag & MARK_DEGENERATE) == 0;
            if (bIsGood)
            {
                if (iNextGoodTriangleSearchIndex < (t + 2))
                {
                    iNextGoodTriangleSearchIndex = t + 2;
                }
            }
            else
            {
                // search for the first good triangle.
                bool bJustADegenerate = true;
                while (bJustADegenerate && iNextGoodTriangleSearchIndex < iTotTris)
                {
                    bool bIsGood2 = (pTriInfos[iNextGoodTriangleSearchIndex].flag & MARK_DEGENERATE) == 0;
                    if (bIsGood2)
                    {
                        bJustADegenerate = false;
                    }
                    else
                    {
                        ++iNextGoodTriangleSearchIndex;
                    }
                }

                int t0 = t;
                int t1 = iNextGoodTriangleSearchIndex;
                ++iNextGoodTriangleSearchIndex;
                Debug.Assert(iNextGoodTriangleSearchIndex > (t + 1));

                // swap triangle t0 and t1
                if (!bJustADegenerate)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int index = piTriList_out[t0 * 3 + i];
                        piTriList_out[t0 * 3 + i] = piTriList_out[t1 * 3 + i];
                        piTriList_out[t1 * 3 + i] = index;
                    }
                    {
                        TriInfo tri_info = pTriInfos[t0];
                        pTriInfos[t0] = pTriInfos[t1];
                        pTriInfos[t1] = tri_info;
                    }
                }
                else
                {
                    bStillFindingGoodOnes = false;  // this is not supposed to happen
                }
            }

            if (bStillFindingGoodOnes)
            {
                ++t;
            }
        }

        Debug.Assert(bStillFindingGoodOnes);  // code will still work.
        Debug.Assert(iNrTrianglesIn == t);
    }

    void DegenEpilogue(TSpace[] psTspace, TriInfo[] pTriInfos, int[] piTriListIn, MikkTSpaceContext mikkTSpace, int iNrTrianglesIn, int iTotTris)
    {

        // deal with degenerate triangles
        // punishment for degenerate triangles is O(N^2)
        for (int t = iNrTrianglesIn; t < iTotTris; t++)
        {
            // degenerate triangles on a quad with one good triangle are skipped
            // here but processed in the next loop
            bool bSkip = (pTriInfos[t].flag & QUAD_ONE_DEGEN_TRI) != 0;

            if (!bSkip)
            {
                for (int i = 0; i < 3; i++)
                {
                    int index1 = piTriListIn[t * 3 + i];
                    // search through the good triangles
                    bool bNotFound = true;
                    int j = 0;
                    while (bNotFound && j < (3 * iNrTrianglesIn))
                    {
                        int index2 = piTriListIn[j];
                        if (index1 == index2)
                        {
                            bNotFound = false;
                        }
                        else
                        {
                            ++j;
                        }
                    }

                    if (!bNotFound)
                    {
                        int iTri = j / 3;
                        int iVert = j % 3;
                        int iSrcVert = pTriInfos[iTri].vertNum[iVert];
                        int iSrcOffs = pTriInfos[iTri].tSpacesOffs;
                        int iDstVert = pTriInfos[t].vertNum[i];
                        int iDstOffs = pTriInfos[t].tSpacesOffs;

                        // copy tspace
                        psTspace[iDstOffs + iDstVert] = psTspace[iSrcOffs + iSrcVert];
                    }
                }
            }
        }

        // deal with degenerate quads with one good triangle
        for (int t = 0; t < iNrTrianglesIn; t++)
        {
            // this triangle belongs to a quad where the
            // other triangle is degenerate
            if ((pTriInfos[t].flag & QUAD_ONE_DEGEN_TRI) != 0)
            {

                byte[] pV = pTriInfos[t].vertNum;
                int iFlag = (1 << pV[0]) | (1 << pV[1]) | (1 << pV[2]);
                int iMissingIndex = 0;
                if ((iFlag & 2) == 0)
                {
                    iMissingIndex = 1;
                }
                else if ((iFlag & 4) == 0)
                {
                    iMissingIndex = 2;
                }
                else if ((iFlag & 8) == 0)
                {
                    iMissingIndex = 3;
                }

                int iOrgF = pTriInfos[t].orgFaceNumber;
                Vector3 vDstP = GetPosition(mikkTSpace, MakeIndex(iOrgF, iMissingIndex));
                bool bNotFound = true;
                int i = 0;
                while (bNotFound && i < 3)
                {
                    int iVert = pV[i];
                    Vector3 vSrcP = GetPosition(mikkTSpace, MakeIndex(iOrgF, iVert));
                    if (vSrcP == vDstP)
                    {
                        int iOffs = pTriInfos[t].tSpacesOffs;
                        psTspace[iOffs + iMissingIndex] = psTspace[iOffs + iVert];
                        bNotFound = false;
                    }
                    else
                    {
                        ++i;
                    }
                }
                Debug.Assert(!bNotFound);
            }
        }

    }

    /**
     * SubGroup inner class
     */
    public class SubGroup
    {
        public int nrFaces;
        public int[] triMembers;
    }

    public class Group
    {
        public int nrFaces;
        public List<int> faceIndices = new();
        public int vertexRepresentative;
        public bool orientationPreserving;
    }

    public class TriInfo
    {

        public int[] faceNeighbors = new int[3];
        public Group[] assignedGroup = new Group[3];

        // normalized first order face derivatives
        public Vector3 os = new Vector3();
        public Vector3 ot = new Vector3();
        public float magS, magT;  // original magnitudes

        // determines if the current and the next triangle are a quad.
        public int orgFaceNumber;
        public int flag, tSpacesOffs;
        public byte[] vertNum = new byte[4];
    }

    public class TSpace
    {

        public Vector3 os = new Vector3();
        public float magS;
        public Vector3 ot = new Vector3();
        public float magT;
        public int counter;  // this is to average back into quads.
        public bool orient;

        public void Set(TSpace ts)
        {
            os = ts.os;
            magS = ts.magS;
            ot = ts.ot;
            magT = ts.magT;
            counter = ts.counter;
            orient = ts.orient;
        }
    }

    public class TmpVert
    {
        public float[] vert = new float[3];
        public int index;
    }

    public class Edge
    {

        public void SetI0(int i)
        {
            array[0] = i;
        }

        public void SetI1(int i)
        {
            array[1] = i;
        }

        public void SetF(int i)
        {
            array[2] = i;
        }

        public int GetI0()
        {
            return array[0];
        }

        public int GetI1()
        {
            return array[1];
        }

        public int GetF()
        {
            return array[2];
        }

        public int[] array = new int[3];
    }

}
