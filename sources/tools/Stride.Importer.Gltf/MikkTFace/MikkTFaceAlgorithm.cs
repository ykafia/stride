using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Stride.Importer.Common.MikkTFace;

public class STmpVert
{
    public float[] Vert { get; set; }
    public int Index {get;set;}
}
public class SSubGroup
{
    public int IdFace { get; set; }
    public Vector3 Face { get; set; }
}
public class SGroup
{
    public int NumberFaces { get; set; }
    public uint[] Indices { get; set; }
    public int VertexRepresentativeId { get; set; }
    public bool OrientPreservering { get; set; }
}
public class STriInfo
{

    public int[] FaceNeighbors = new int[3];
    public SGroup[] AssignedGroup = new SGroup[3];
    // normalized first order face derivatives
    public Vector3 VOs { get; set; }
    public Vector3 OvOt { get; set; }
    public float FMagS { get; set; }
    public float FMagT { get; set; } // original magnitudes
    // determines if the current and the next triangle are a quad.
    public int IOrgFaceNumber { get; set; }
    public int IFlag { get; set; }
    public int ITSpacesOffs { get; set; }
    public uint[] vert_num = new uint[4];
}

public class STSpace
{

    public Vector3 VOs {get;set;}
    public float FMagS {get;set;}
    public Vector3 VOt {get;set;}
    public float FMagT {get;set;}
    public int ICounter {get;set;}   // this is to average back into quads.
    public bool Orient { get; set; }
}


public class MikkTFaceAlgorithm
{
    public static int MakeIndex(int idFace, int idVert)
    {
        if (idVert > 2)
            throw new Exception("Wrong index for vector");
        return idFace * 3 + idVert;
    }

    public static (int, int) IndexToData(int idIndexIn)
    {
        return (idIndexIn / 3, idIndexIn % 3);
    }

    public static STSpace AvgTSpace(STSpace pTS0, STSpace pTS1)
    {
        STSpace result;

        if(
            pTS0.FMagS == pTS1.FMagS && 
            pTS0.FMagT == pTS1.FMagT && 
            pTS0.VOs == pTS1.VOs && pTS0.VOt == pTS1.VOt
        )
        {
            result = new STSpace
            {
                FMagS = pTS0.FMagS,
                FMagT = pTS0.FMagT,
                VOs = pTS0.VOs,
                VOt = pTS0.VOt,
            };
        }
        else
        {
            result = new STSpace
            {
                FMagS = 0.5f * (pTS0.FMagS + pTS1.FMagS),
                FMagT = 0.5f * (pTS0.FMagT + pTS1.FMagT),
                VOs = pTS0.VOs + pTS1.VOs,
                VOt = pTS0.VOt + pTS1.VOt
            };
            if (result.VOs != Vector3.Zero) result.VOs = Vector3.Normalize(result.VOs);
            if (result.VOt != Vector3.Zero) result.VOt = Vector3.Normalize(result.VOt);
        }
        return result;
    }
    
}


