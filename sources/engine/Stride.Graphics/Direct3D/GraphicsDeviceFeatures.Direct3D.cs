// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if STRIDE_GRAPHICS_API_DIRECT3D11
// Copyright (c) 2010-2012 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Direct3D;
using Silk.NET.Core.Native;


namespace Stride.Graphics
{
    /// <summary>
    /// Features supported by a <see cref="GraphicsDevice"/>.
    /// </summary>
    /// <remarks>
    /// This class gives also features for a particular format, using the operator this[dxgiFormat] on this structure.
    /// </remarks>
    public partial struct GraphicsDeviceFeatures
    {
        private static readonly List<Format> ObsoleteFormatToExcludes = new List<Format>() { Format.FormatR1Unorm, Format.FormatB5G6R5Unorm, Format.FormatB5G5R5A1Unorm };

        internal GraphicsDeviceFeatures(GraphicsDevice deviceRoot)
        {
            var nativeDevice = deviceRoot.NativeDevice;

            HasSRgb = true;

            mapFeaturesPerFormat = new FeaturesPerFormat[256];

            // Set back the real GraphicsProfile that is used
            RequestedProfile = deviceRoot.RequestedProfile;
            CurrentProfile = GraphicsProfileHelper.FromFeatureLevel(nativeDevice.Get().GetFeatureLevel());

            HasResourceRenaming = true;
            FeatureDataD3D10XHardwareOptions opt;
            FeatureDataDoubles dbs;
            FeatureDataThreading thr;



            unsafe
            {
                nativeDevice.Get().CheckFeatureSupport(Silk.NET.Direct3D11.Feature.FeatureD3D10XHardwareOptions, (void*)&opt, (uint)sizeof(FeatureDataD3D10XHardwareOptions));
                HasComputeShaders = opt.ComputeShadersPlusRawAndStructuredBuffersViaShader4X > 0;

                nativeDevice.Get().CheckFeatureSupport(Silk.NET.Direct3D11.Feature.FeatureDoubles, (void*)&dbs, (uint)sizeof(FeatureDataDoubles));
                HasDoublePrecision = dbs.DoublePrecisionFloatShaderOps > 0;

                nativeDevice.Get().CheckFeatureSupport(Silk.NET.Direct3D11.Feature.FeatureThreading, (void*)&thr, (uint)sizeof(FeatureDataThreading));
                HasMultiThreadingConcurrentResources = thr.DriverConcurrentCreates > 0;
                HasDriverCommandLists = thr.DriverCommandLists > 0;


            }

            HasDepthAsSRV = (CurrentProfile >= GraphicsProfile.Level_10_0);
            HasDepthAsReadOnlyRT = CurrentProfile >= GraphicsProfile.Level_11_0;
            HasMultisampleDepthAsSRV = CurrentProfile >= GraphicsProfile.Level_11_0;

            // Check features for each DXGI.Format
            foreach (var format in Enum.GetValues(typeof(SharpDX.DXGI.Format)))
            {
                var dxgiFormat = (Format)format;
                var maximumMultisampleCount = MultisampleCount.None;
                var computeShaderFormatSupport = 0;
                var formatSupport = FormatSupport.None;

                if (!ObsoleteFormatToExcludes.Contains(dxgiFormat))
                {
                    maximumMultisampleCount = GetMaximumMultisampleCount(nativeDevice, dxgiFormat);
                    if (HasComputeShaders)
                        computeShaderFormatSupport = opt.ComputeShadersPlusRawAndStructuredBuffersViaShader4X; //nativeDevice.Get().CheckComputeShaderFormatSupport(dxgiFormat);

                    formatSupport = new FormatSupport();
                    unsafe
                    {
                        nativeDevice.Get().CheckFormatSupport(dxgiFormat, (uint*)&formatSupport);
                    }
                }

                //mapFeaturesPerFormat[(int)dxgiFormat] = new FeaturesPerFormat((PixelFormat)dxgiFormat, maximumMultisampleCount, computeShaderFormatSupport, formatSupport);
                mapFeaturesPerFormat[(int)dxgiFormat] = new FeaturesPerFormat((PixelFormat)dxgiFormat, maximumMultisampleCount, formatSupport);
            }
        }

        /// <summary>
        /// Gets the maximum multisample count for a particular <see cref="PixelFormat" />.
        /// </summary>
        /// <param name="device">The device.</param>
        /// <param name="pixelFormat">The pixelFormat.</param>
        /// <returns>The maximum multisample count for this pixel pixelFormat</returns>
        private static MultisampleCount GetMaximumMultisampleCount(ComPtr<ID3D11Device> device, Format pixelFormat)
        {
            int maxCount = 1;
            for (int i = 1; i <= 8; i *= 2)
            {
                unsafe
                {
                    if (device.Get().CheckMultisampleQualityLevels(pixelFormat, (uint)i, null) != 0)
                        maxCount = i;
                }
            }
            return (MultisampleCount)maxCount;
        }
    }
}
#endif
