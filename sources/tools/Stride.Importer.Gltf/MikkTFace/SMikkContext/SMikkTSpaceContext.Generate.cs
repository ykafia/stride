using System;
using System.Collections.Generic;
using System.Linq;
using Stride.Core.Mathematics;
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
            List<STriInfo> pTriInfos = new();
            List<SGroup> pGroups = new();
            List<STSpace> psTspace = new();
            int iNrTrianglesIn = 0, f = 0, t = 0, i = 0;
            int iNrTSPaces = 0, iTotTris = 0, iDegenTriangles = 0, iNrMaxGroups = 0;
            int iNrActiveGroups = 0, index = 0;
            int iNrFaces = GetNumFaces();
            bool bRes = false;
            float fThresCos = (float)Math.Cos((fAngularThreshold * (float)Math.PI) / 180.0f);

            // count triangles on supported faces

            iNrTrianglesIn = iNrFaces;

            // allocate memory for an index list
            piTriListIn = new int[iNrTrianglesIn][].Select(x => new int[3]).ToArray();// (int*)malloc(sizeof(int) * 3 * iNrTrianglesIn);
            pTriInfos = new List<STriInfo>(iNrTrianglesIn); //(STriInfo*)malloc(sizeof(STriInfo) * iNrTrianglesIn);


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
                int i0 = piTriListIn[t][0];
                int i1 = piTriListIn[t][1];
                int i2 = piTriListIn[t][2];
                Vector3 p0 = GetPosition(i0);
                Vector3 p1 = GetPosition(i1);
                Vector3 p2 = GetPosition(i2);
                if (p0 == p1 || p0 == p2 || p1 == p2)  // degenerate
                {
                    pTriInfos[t].Flag |= MARK_DEGENERATE;
                    ++iDegenTriangles;
                }
            }
            iNrTrianglesIn = iTotTris - iDegenTriangles;

            // mark all triangle pairs that belong to a quad with only one
            // good triangle. These need special treatment in DegenEpilogue().
            // Additionally, move all good triangles to the start of
            // pTriInfos[] and piTriListIn[] without changing order and
            // put the degenerate triangles last.
            DegenPrologue(pTriInfos, ref piTriListIn, iNrTrianglesIn, iTotTris);


            // evaluate triangle level attributes and neighbor list
            //printf("gen neighbors list begin\n");
            InitTriInfo(pTriInfos, ref piTriListIn, iNrTrianglesIn);
            //printf("gen neighbors list end\n");


            // based on the 4 rules, identify groups based on connectivity
            iNrMaxGroups = iNrTrianglesIn * 3;
            pGroups = new List<SGroup>(iNrMaxGroups); // (SGroup*)malloc(sizeof(SGroup) * iNrMaxGroups);
            piGroupTrianglesBuffer = new int[iNrTrianglesIn][].Select(x => new int[3]).ToArray();// (int*)malloc(sizeof(int) * iNrTrianglesIn * 3);
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

            psTspace = new List<STSpace>(iNrTSPaces);// (STSpace*)malloc(sizeof(STSpace) * iNrTSPaces
            
            for (t = 0; t < iNrTSPaces; t++)
            {
                psTspace[t].VOs.X = 1.0f; psTspace[t].VOs.Y = 0.0f; psTspace[t].VOs.Z = 0.0f; psTspace[t].FMagS = 1.0f;
                psTspace[t].VOt.X = 0.0f; psTspace[t].VOt.Y = 1.0f; psTspace[t].VOt.Z = 0.0f; psTspace[t].FMagT = 1.0f;
            }

            // make tspaces, each group is split up into subgroups if necessary
            // based on fAngularThreshold. Finally a tangent space is made for
            // every resulting subgroup
            //printf("gen tspaces begin\n");
            bRes = GenerateTSpaces(psTspace, pTriInfos, pGroups, iNrActiveGroups, piTriListIn, fThresCos);
            //printf("gen tspaces end\n");

            // clean up

            if (!bRes)  // if an allocation in GenerateTSpaces() failed
            {
                // clean up and return false
                return false;
            }


            // degenerate quads with one good triangle will be fixed by copying a space from
            // the good triangle to the coinciding vertex.
            // all other degenerate triangles will just copy a space from any good triangle
            // with the same welded index in piTriListIn[].
            DegenEpilogue(psTspace, pTriInfos, piTriListIn, iNrTrianglesIn, iTotTris);


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
                    Vector3 bitang = new(pTSpace.VOt.X, pTSpace.VOt.Y, pTSpace.VOt.Z);
                    if (SetTSpace != null)
                        (tang, bitang) = SetTSpace(pTSpace.FMagS, pTSpace.FMagT, pTSpace.Orient, f, i);
                    if (SetTSpaceBasic != null)
                        tang = SetTSpaceBasic(pTSpace.Orient == true ? 1.0f : (-1.0f), f, i);

                    ++index;
                }
            }


            return true;
        }

        
        public int Build4RuleGroups(List<STriInfo> pTriInfos, List<SGroup> pGroups, int[][] piGroupTrianglesBuffer, int[][] piTriListIn, int iNrTrianglesIn)
        {

            int iNrMaxGroups = iNrTrianglesIn * 3;
            int iNrActiveGroups = 0;
            int iOffset = 0, f = 0, i = 0;

            for (f = 0; f < iNrTrianglesIn; f++)
            {
                for (i = 0; i < 3; i++)
                {
                    // if not assigned to a group
                    if ((pTriInfos[f].Flag & GROUP_WITH_ANY) == 0 && pTriInfos[f].AssignedGroup[i] == null)
                    {
                        bool bOrPre;
                        int neigh_indexL, neigh_indexR;
                        int vert_index = piTriListIn[f][i];
                        //assert(iNrActiveGroups < iNrMaxGroups);
                        pTriInfos[f].AssignedGroup[i] = pGroups[iNrActiveGroups];
                        pTriInfos[f].AssignedGroup[i].VertexRepresentativeId = vert_index;
                        pTriInfos[f].AssignedGroup[i].OrientPreservering = (pTriInfos[f].Flag & ORIENT_PRESERVING) != 0;
                        pTriInfos[f].AssignedGroup[i].NumberFaces = 0;
                        pTriInfos[f].AssignedGroup[i].Indices = piGroupTrianglesBuffer[iOffset];
                        ++iNrActiveGroups;

                        AddTriToGroup(pTriInfos[f].AssignedGroup[i], f);
                        bOrPre = (pTriInfos[f].Flag & ORIENT_PRESERVING) != 0 ? true : false;
                        neigh_indexL = pTriInfos[f].FaceNeighbors[i];
                        neigh_indexR = pTriInfos[f].FaceNeighbors[i > 0 ? (i - 1) : 2];
                        if (neigh_indexL >= 0) // neighbor
                        {
                            bool bAnswer =
                                AssignRecur(piTriListIn, pTriInfos, neigh_indexL,
                                            pTriInfos[f].AssignedGroup[i]);

                            bool bOrPre2 = (pTriInfos[neigh_indexL].Flag & ORIENT_PRESERVING) != 0;
                            bool bDiff = bOrPre != bOrPre2;
                            //assert(bAnswer || bDiff);
                            //(void)bAnswer, (void)bDiff;  /* quiet warnings in non debug mode */
                        }
                        if (neigh_indexR >= 0) // neighbor
                        {
                            bool bAnswer =
                                AssignRecur(piTriListIn, pTriInfos, neigh_indexR,
                                            pTriInfos[f].AssignedGroup[i]);

                            bool bOrPre2 = (pTriInfos[neigh_indexR].Flag & ORIENT_PRESERVING) != 0 ? true : false;
                            bool bDiff = bOrPre != bOrPre2 ? true : false;
                            //assert(bAnswer || bDiff);
                            //(void)bAnswer, (void)bDiff;  /* quiet warnings in non debug mode */
                        }

                        // update offset
                        iOffset += pTriInfos[f].AssignedGroup[i].NumberFaces;
                        // since the groups are disjoint a triangle can never
                        // belong to more than 3 groups. Subsequently something
                        // is completely screwed if this assertion ever hits.
                        //assert(iOffset <= iNrMaxGroups);
                    }
                }
            }

            return iNrActiveGroups;
        }

        public void DegenEpilogue(List<STSpace> psTspace, List<STriInfo> pTriInfos, int[][] piTriListIn, int iNrTrianglesIn, int iTotTris)
        {

            int t = 0, i = 0;
            // deal with degenerate triangles
            // punishment for degenerate triangles is O(N^2)
            for (t = iNrTrianglesIn; t < iTotTris; t++)
            {
                // degenerate triangles on a quad with one good triangle are skipped
                // here but processed in the next loop
                bool bSkip = (pTriInfos[t].Flag & QUAD_ONE_DEGEN_TRI) != 0 ? true : false;

                if (!bSkip)
                {
                    for (i = 0; i < 3; i++)
                    {
                        int index1 = piTriListIn[t][i];
                        // search through the good triangles
                        bool bNotFound = true;
                        int j = 0;
                        while (bNotFound && j < (3 * iNrTrianglesIn))
                        {
                            int index2 = piTriListIn[j / 3][j % 3];
                            if (index1 == index2) bNotFound = false;
                            else ++j;
                        }

                        if (!bNotFound)
                        {
                            int iTri = j / 3;
                            int iVert = j % 3;
                            int iSrcVert = pTriInfos[iTri].vert_num[iVert];
                            int iSrcOffs = pTriInfos[iTri].ITSpacesOffs;
                            int iDstVert = pTriInfos[t].vert_num[i];
                            int iDstOffs = pTriInfos[t].ITSpacesOffs;

                            // copy tspace
                            psTspace[iDstOffs + iDstVert] = psTspace[iSrcOffs + iSrcVert];
                        }
                    }
                }
            }

            // deal with degenerate quads with one good triangle
            for (t = 0; t < iNrTrianglesIn; t++)
            {
                // this triangle belongs to a quad where the
                // other triangle is degenerate
                if ((pTriInfos[t].Flag & QUAD_ONE_DEGEN_TRI) != 0)
                {
                    Vector3 vDstP;
                    bool bNotFound;
                    int[] pV = pTriInfos[t].vert_num;
                    int iFlag = (1 << pV[0]) | (1 << pV[1]) | (1 << pV[2]);
                    int iMissingIndex = 0;
                    if ((iFlag & 2) == 0) iMissingIndex = 1;
                    else if ((iFlag & 4) == 0) iMissingIndex = 2;
                    else if ((iFlag & 8) == 0) iMissingIndex = 3;

                    var iOrgF = pTriInfos[t].IOrgFaceNumber;
                    vDstP = GetPosition(MakeIndex(iOrgF, iMissingIndex));
                    bNotFound = true;
                    i = 0;
                    while (bNotFound && i < 3)
                    {
                        int iVert = pV[i];
                        Vector3 vSrcP = GetPosition(MakeIndex(iOrgF, iVert));
                        if (vSrcP == vDstP)
                        {
                            int iOffs = pTriInfos[t].ITSpacesOffs;
                            psTspace[iOffs + iMissingIndex] = psTspace[iOffs + iVert];
                            bNotFound = false;
                        }
                        else
                            ++i;
                    }
                    //assert(!bNotFound);
                }
            }
        }
        public void AddTriToGroup(SGroup pGroup, int iTriIndex)
        {
            pGroup.Indices[pGroup.NumberFaces] = iTriIndex;
	        ++pGroup.NumberFaces;
        }
}
}
