using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpGLTF.Schema2;
using Stride.Core.Mathematics;

namespace Stride.Importer.Gltf;

public static class GltfExtensions
{
    public static Color ToColor(this IReadOnlyList<IMaterialParameter> parameters)
    {
        if (parameters[0].ValueType == typeof(float) && parameters.Count == 4)
            return new Color(parameters.Select(x => x.Value).Cast<float>().ToArray());
        else if (parameters[0].ValueType == typeof(float) && parameters.Count == 3)
            return new Color(parameters.Select(x => x.Value).Cast<float>().Append(0).ToArray());
        else if (parameters[0].ValueType == typeof(float) && parameters.Count == 2)
            return new Color(parameters.Select(x => x.Value).Cast<float>().Append(0).Append(0).ToArray());
        else if (parameters[0].ValueType == typeof(System.Numerics.Vector4))
            return ((System.Numerics.Vector4)parameters[0].Value).ToColor();
        else return Color.CornflowerBlue;
    }
    public static float ToFloat(this IReadOnlyList<IMaterialParameter> parameters)
    {
        if (parameters[0].ValueType == typeof(float))
            return (float)parameters[0].Value;
        else return 0;
    }
    public static Color ToColor(this System.Numerics.Vector4 vector4)
    {
        return new Color(vector4.X, vector4.Y, vector4.Z, vector4.W);
    }

    public static byte[] ToBytes(this System.Numerics.Vector4 v)
    {
        return BitConverter.GetBytes(v.X).Concat(BitConverter.GetBytes(v.Y)).Concat(BitConverter.GetBytes(v.Z)).Concat(BitConverter.GetBytes(v.W)).ToArray();
    }
    public static byte[] ToBytes(this System.Numerics.Vector3 v)
    {
        return BitConverter.GetBytes(v.X).Concat(BitConverter.GetBytes(v.Y)).Concat(BitConverter.GetBytes(v.Z)).ToArray();
    }
    public static byte[] ToBytes(this System.Numerics.Vector2 v)
    {
        return BitConverter.GetBytes(v.X).Concat(BitConverter.GetBytes(v.Y)).ToArray();
    }


    public static Graphics.PrimitiveType AsSdPrim(this PrimitiveType p)
    {
        return p switch
        {
            PrimitiveType.POINTS => Graphics.PrimitiveType.PointList,
            PrimitiveType.LINES => Graphics.PrimitiveType.LineList,
            PrimitiveType.LINE_LOOP => throw new NotImplementedException(),
            PrimitiveType.LINE_STRIP => Graphics.PrimitiveType.LineStrip,
            PrimitiveType.TRIANGLES => Graphics.PrimitiveType.TriangleList,
            PrimitiveType.TRIANGLE_STRIP => Graphics.PrimitiveType.TriangleStrip,
            PrimitiveType.TRIANGLE_FAN => throw new NotImplementedException(),
            _ => throw new NotImplementedException(),
        };
    }
}
