using System;
using Stride.Core;
using Stride.Rendering;
using Stride.Graphics;
using Stride.Shaders;
using Stride.Core.Mathematics;
using Buffer = Stride.Graphics.Buffer;

namespace Stride.Rendering.Lights
{
    public static partial class LightDiscGroupKeys
    {
        public static readonly ValueParameterKey<DiscLightData> Lights = ParameterKeys.NewValue<DiscLightData>();
    }
}
