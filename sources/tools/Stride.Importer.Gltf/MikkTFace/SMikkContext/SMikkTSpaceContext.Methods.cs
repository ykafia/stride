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

        public int GenerateInitialVerticesIndexList(List<STriInfo> pTriInfos, ref int[][] piTriList_out, int iNrTrianglesIn)
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
                                int[] pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_A[0] = 0; pVerts_A[1] = 1; pVerts_A[2] = 2;
                            }
                            piTriList_out[iDstTriIndex][0] = i0;
                            piTriList_out[iDstTriIndex][1] = i1;
                            piTriList_out[iDstTriIndex][2] = i2;
                            ++iDstTriIndex; // next
                            {
                                int[] pVerts_B = pTriInfos[iDstTriIndex].vert_num;
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
                                int[] pVerts_A = pTriInfos[iDstTriIndex].vert_num;
                                pVerts_A[0] = 0; pVerts_A[1] = 1; pVerts_A[2] = 3;
                            }
                            piTriList_out[iDstTriIndex][0] = i0;
                            piTriList_out[iDstTriIndex][1] = i1;
                            piTriList_out[iDstTriIndex][2] = i3;
                            ++iDstTriIndex; // next
                            {
                                int[] pVerts_B = pTriInfos[iDstTriIndex].vert_num;
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
                pTriInfos[t].Flag = 0;

            // return total amount of tspaces
            return iTSpacesOffs;
        }

        public bool GenerateTSpaces(List<STSpace> psTspace, List<STriInfo> pTriInfos, List<SGroup> pGroups,
                             int iNrActiveGroups, int[][] piTriListIn, float fThresCos)
        {

            STSpace[] pSubGroupTspace = null;
            SSubGroup[] pUniSubGroups = null;
            int[] pTmpMembers = null;
            int iMaxNrFaces = 0, iUniqueTspaces = 0, g = 0, i = 0;
            for (g = 0; g < iNrActiveGroups; g++)
                if (iMaxNrFaces < pGroups[g].NumberFaces)

                    iMaxNrFaces = pGroups[g].NumberFaces;

            if (iMaxNrFaces == 0) return true;

            // make initial allocations
            pSubGroupTspace = new STSpace[iMaxNrFaces];// (STSpace*)malloc(sizeof(STSpace) * iMaxNrFaces);
            pUniSubGroups = new SSubGroup[iMaxNrFaces];// (SSubGroup*)malloc(sizeof(SSubGroup) * iMaxNrFaces);
            pTmpMembers = new int[iMaxNrFaces];// (int*)malloc(sizeof(int) * iMaxNrFaces);
            if (pSubGroupTspace == null || pUniSubGroups == null || pTmpMembers == null)
            {
                //if (pSubGroupTspace != null) free(pSubGroupTspace);
                //if (pUniSubGroups != null) free(pUniSubGroups);
                //if (pTmpMembers != null) free(pTmpMembers);
                return false;
            }


            iUniqueTspaces = 0;
            for (g = 0; g < iNrActiveGroups; g++)
            {
                SGroup pGroup = pGroups[g];
                int iUniqueSubGroups = 0, s = 0;

                for (i = 0; i < pGroup.NumberFaces; i++)  // triangles
                {
                    int f = pGroup.Indices[i];  // triangle number
                    int index = -1, iVertIndex = -1, iOF_1 = -1, iMembers = 0, j = 0, l = 0;
                    SSubGroup tmp_group;
                    bool bFound;
                    Vector3 n, vOs, vOt;
                    if (pTriInfos[f].AssignedGroup[0] == pGroup) index = 0;
                    else if (pTriInfos[f].AssignedGroup[1] == pGroup) index = 1;
                    else if (pTriInfos[f].AssignedGroup[2] == pGroup) index = 2;
                    //assert(index >= 0 && index < 3);

                    iVertIndex = piTriListIn[f][index];
                    //assert(iVertIndex == pGroup.iVertexRepresentitive);

                    // is normalized already
                    n = GetNormal(iVertIndex);

                    // project
                    vOs = pTriInfos[f].VOs - Vector3.Dot(n, pTriInfos[f].VOs) * n;
                    vOt = pTriInfos[f].VOt - Vector3.Dot(n, pTriInfos[f].VOt) * n;
                    if (vOs != Vector3.Zero) vOs = Vector3.Normalize(vOs);
                    if (vOt != Vector3.Zero) vOt = Vector3.Normalize(vOt);

                    // original face number
                    iOF_1 = pTriInfos[f].IOrgFaceNumber;

                    iMembers = 0;
                    for (j = 0; j < pGroup.NumberFaces; j++)
                    {
                        int t = pGroup.Indices[j];  // triangle number
                        int iOF_2 = pTriInfos[t].IOrgFaceNumber;

                        // project
                        Vector3 vOs2 = (pTriInfos[t].VOs - (Vector3.Dot(n, pTriInfos[t].VOs) * n));
                        Vector3 vOt2 = (pTriInfos[t].VOt - (Vector3.Dot(n, pTriInfos[t].VOt) * n));
                        if (vOs2 != Vector3.Zero) vOs2 = Vector3.Normalize(vOs2);
                        if (vOt2 != Vector3.Zero) vOt2 = Vector3.Normalize(vOt2);

                        {
                            bool bAny = ((pTriInfos[f].Flag | pTriInfos[t].Flag) & GROUP_WITH_ANY) != 0;
                            // make sure triangles which belong to the same quad are joined.
                            bool bSameOrgFace = iOF_1 == iOF_2;

                            float fCosS = Vector3.Dot(vOs, vOs2);
                            float fCosT = Vector3.Dot(vOt, vOt2);

                            //assert(f! = t || bSameOrgFace); // sanity check
                            if (bAny || bSameOrgFace || (fCosS > fThresCos && fCosT > fThresCos))
                                pTmpMembers[iMembers++] = t;
                        }
                    }

                    // sort pTmpMembers
                    tmp_group.NumberFaces = iMembers;
                    tmp_group.TriMembers = pTmpMembers;
                    Array.Sort(pTmpMembers);
                    // look for an existing match
                    bFound = false;
                    l = 0;
                    while (l < iUniqueSubGroups && !bFound)
                    {
                        bFound = CompareSubGroups(tmp_group, pUniSubGroups[l]);
                        if (!bFound) ++l;
                    }

                    // assign tangent space index
                    //assert(bFound || l == iUniqueSubGroups);
                    //piTempTangIndices[f*3+index] = iUniqueTspaces+l;

                    // if no match was found we allocate a new subgroup
                    if (!bFound)
                    {
                        // insert new subgroup
                        int[] pIndices = new int[iMembers];// (int*)malloc(sizeof(int) * iMembers);
                        if (pIndices == null)
                        {
                            // clean up and return false
                            //for (s = 0; s < iUniqueSubGroups; s++)
                            //    free(pUniSubGroups[s].pTriMembers);
                            //free(pUniSubGroups);
                            //free(pTmpMembers);
                            //free(pSubGroupTspace);
                            return false;
                        }
                        pUniSubGroups[iUniqueSubGroups].NumberFaces = iMembers;
                        pUniSubGroups[iUniqueSubGroups].TriMembers = pIndices;
                        Array.Copy(tmp_group.TriMembers, pIndices, tmp_group.TriMembers.Length);
                        pSubGroupTspace[iUniqueSubGroups] =
                            EvalTspace(tmp_group.TriMembers, iMembers, piTriListIn, pTriInfos, pGroup.VertexRepresentativeId);
                        ++iUniqueSubGroups;
                    }

                    // output tspace
                    {
                        int iOffs = pTriInfos[f].ITSpacesOffs;
                        int iVert = pTriInfos[f].vert_num[index];
                        STSpace pTS_out = psTspace[iOffs + iVert];
                        //assert(pTS_out.iCounter < 2);
                        //assert(((pTriInfos[f].iFlag & ORIENT_PRESERVING) != 0) == pGroup.bOrientPreservering);
                        if (pTS_out.ICounter == 1)
                        {
                            pTS_out = AvgTSpace(pTS_out, pSubGroupTspace[l]);
                            pTS_out.ICounter = 2;  // update counter
                            pTS_out.Orient = pGroup.OrientPreservering;
                        }
                        else
                        {
                            //assert(pTS_out.iCounter == 0);
                            pTS_out = pSubGroupTspace[l];
                            pTS_out.ICounter = 1;  // update counter
                            pTS_out.Orient = pGroup.OrientPreservering;
                        }
                    }
                }

                // clean up and offset iUniqueTspaces
                //for (s = 0; s < iUniqueSubGroups; s++)
                //    free(pUniSubGroups[s].TriMembers);
                iUniqueTspaces += iUniqueSubGroups;
            }

            // clean up

            return true;
        }
        public bool AssignRecur(int[][] piTriListIn, List<STriInfo> psTriInfos, int iMyTriIndex, SGroup pGroup)
        {
            STriInfo pMyTriInfo = psTriInfos[iMyTriIndex];

            // track down vertex
            int iVertRep = pGroup.VertexRepresentativeId;
            int[] pVerts = piTriListIn[3 * iMyTriIndex + 0];
            int i = -1;
            if (pVerts[0] == iVertRep) i = 0;
            else if (pVerts[1] == iVertRep) i = 1;
            else if (pVerts[2] == iVertRep) i = 2;
            //assert(i >= 0 && i < 3);

            // early out
            if (pMyTriInfo.AssignedGroup[i] == pGroup) return true;
            else if (pMyTriInfo.AssignedGroup[i] != null) return false;
            if ((pMyTriInfo.Flag & GROUP_WITH_ANY) != 0)
            {
                // first to group with a group-with-anything triangle
                // determines it's orientation.
                // This is the only existing order dependency in the code!!
                if (pMyTriInfo.AssignedGroup[0] == null &&
                    pMyTriInfo.AssignedGroup[1] == null &&
                    pMyTriInfo.AssignedGroup[2] == null)
                {
                    pMyTriInfo.Flag &= (~ORIENT_PRESERVING);
                    pMyTriInfo.Flag |= (pGroup.OrientPreservering ? ORIENT_PRESERVING : 0);
                }
            }
            {
                bool bOrient = (pMyTriInfo.Flag & ORIENT_PRESERVING) != 0 ? true : false;
                if (bOrient != pGroup.OrientPreservering) return false;
            }

            AddTriToGroup(pGroup, iMyTriIndex);
            pMyTriInfo.AssignedGroup[i] = pGroup;

            {
                int neigh_indexL = pMyTriInfo.FaceNeighbors[i];
                int neigh_indexR = pMyTriInfo.FaceNeighbors[i > 0 ? (i - 1) : 2];
                if (neigh_indexL >= 0)
                    AssignRecur(piTriListIn, psTriInfos, neigh_indexL, pGroup);
                if (neigh_indexR >= 0)
                    AssignRecur(piTriListIn, psTriInfos, neigh_indexR, pGroup);
            }
            return true;
        }
        public bool CompareSubGroups(SSubGroup pg1, SSubGroup pg2)
        {

            bool bStillSame = true;
            int i = 0;
            if (pg1.NumberFaces != pg2.NumberFaces) return false;
            while (i < pg1.NumberFaces && bStillSame)
            {
                bStillSame = pg1.TriMembers[i] == pg2.TriMembers[i] ? true : false;
                if (bStillSame) ++i;
            }
            return bStillSame;
        }

        public STSpace EvalTspace(int[] face_indices, int iFaces, int[][] piTriListIn, List<STriInfo> pTriInfos, int iVertexRepresentitive)
        {

            STSpace res = new();
            float fAngleSum = 0;
            int face = 0;
            res.VOs.X = 0.0f; res.VOs.Y = 0.0f; res.VOs.Z = 0.0f;
            res.VOt.X = 0.0f; res.VOt.Y = 0.0f; res.VOt.Z = 0.0f;
            res.FMagS = 0; res.FMagT = 0;

            for (face = 0; face < iFaces; face++)
            {
                int f = face_indices[face];

                // only valid triangles get to add their contribution
                if ((pTriInfos[f].Flag & GROUP_WITH_ANY) == 0)
                {
                    Vector3 n, vOs, vOt, p0, p1, p2, v1, v2;
                    float fCos, fAngle, fMagS, fMagT;
                    int i = -1, index = -1, i0 = -1, i1 = -1, i2 = -1;
                    if (piTriListIn[f][0] == iVertexRepresentitive) i = 0;
                    else if (piTriListIn[f][1] == iVertexRepresentitive) i = 1;
                    else if (piTriListIn[f][2] == iVertexRepresentitive) i = 2;
                    //assert(i >= 0 && i < 3);

                    // project
                    index = piTriListIn[f][i];
                    n = GetNormal(index);
                    vOs = (pTriInfos[f].VOs - (Vector3.Dot(n, pTriInfos[f].VOs) * n));
                    vOt = (pTriInfos[f].VOt - (Vector3.Dot(n, pTriInfos[f].VOt) * n));
                    if (vOs != Vector3.Zero) vOs = Vector3.Normalize(vOs);
                    if (vOt != Vector3.Zero) vOt = Vector3.Normalize(vOt);

                    i2 = piTriListIn[f][(i < 2 ? (i + 1) : 0)];
                    i1 = piTriListIn[f][i];
                    i0 = piTriListIn[f][(i > 0 ? (i - 1) : 2)];

                    p0 = GetPosition(i0);
                    p1 = GetPosition(i1);
                    p2 = GetPosition(i2);
                    v1 = p0 - p1;
                    v2 = p2 - p1;

                    // project
                    v1 -= (Vector3.Dot(n, v1) * n); if (v1 != Vector3.Zero) v1 = Vector3.Normalize(v1);
                    v2 -= (Vector3.Dot(n, v2) * n); if (v2 != Vector3.Zero) v2 = Vector3.Normalize(v2);

                    // weight contribution by the angle
                    // between the two edge vectors
                    fCos = Vector3.Dot(v1, v2); fCos = fCos > 1 ? 1 : (fCos < (-1) ? (-1) : fCos);
                    fAngle = (float)Math.Acos(fCos);
                    fMagS = pTriInfos[f].FMagS;
                    fMagT = pTriInfos[f].FMagT;

                    res.VOs += (fAngle * vOs);
                    res.VOt += (fAngle * vOt);
                    res.FMagS += (fAngle * fMagS);
                    res.FMagT += (fAngle * fMagT);
                    fAngleSum += fAngle;
                }
            }

            // normalize
            if (res.VOs != Vector3.Zero) res.VOs = Vector3.Normalize(res.VOs);
            if (res.VOt != Vector3.Zero) res.VOt = Vector3.Normalize(res.VOt);
            if (fAngleSum > 0)
            {
                res.FMagS /= fAngleSum;
                res.FMagT /= fAngleSum;
            }

            return res;
        }

        static STSpace AvgTSpace(STSpace pTS0, STSpace pTS1)
        {

            STSpace ts_res = new();

            // this if is important. Due to floating point precision
            // averaging when ts0==ts1 will cause a slight difference
            // which results in tangent space splits later on
            if (pTS0.FMagS == pTS1.FMagS && pTS0.FMagT == pTS1.FMagT &&
               pTS0.VOs == pTS1.VOs && pTS0.VOt == pTS1.VOt)
            {
                ts_res.FMagS = pTS0.FMagS;
                ts_res.FMagT = pTS0.FMagT;
                ts_res.VOs = pTS0.VOs;
                ts_res.VOt = pTS0.VOt;
            }
            else
            {
                ts_res.FMagS = 0.5f * (pTS0.FMagS + pTS1.FMagS);
                ts_res.FMagT = 0.5f * (pTS0.FMagT + pTS1.FMagT);
                ts_res.VOs = pTS0.VOs + pTS1.VOs;
                ts_res.VOt = pTS0.VOt + pTS1.VOt;
                if (ts_res.VOs != Vector3.Zero) ts_res.VOs = Vector3.Normalize(ts_res.VOs);
                if (ts_res.VOt != Vector3.Zero) ts_res.VOt = Vector3.Normalize(ts_res.VOt);
            }

            return ts_res;
        }
    }
}
