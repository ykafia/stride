using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Stride.Core.Mathematics;

namespace Stride.Rendering.Lights
{
    public struct DiscLightData
    {
        public Vector3 PositionWS;
        float padding0;
        public Vector3 PlaneNormalWS;
        float padding1;
        public Color3 Color;
        public float Range;
        public float Radius;
        public float Intensity;
    }
}
