// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if STRIDE_GRAPHICS_API_DIRECT3D11
using System;
using System.Collections.Generic;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Stride.Graphics
{
    public partial class Buffer
    {
        private BufferDesc nativeDescription;

        internal unsafe ComPtr<ID3D11Buffer> NativeBuffer
        {
            get
            {
                return new ComPtr<ID3D11Buffer>((ID3D11Buffer*)NativeDeviceChild.Handle);
            }
            set
            {
                NativeDeviceChild = new((ID3D11DeviceChild*)value.Handle);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Buffer" /> class.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <param name="viewFlags">Type of the buffer.</param>
        /// <param name="viewFormat">The view format.</param>
        /// <param name="dataPointer">The data pointer.</param>
        protected Buffer InitializeFromImpl(BufferDescription description, BufferFlags viewFlags, PixelFormat viewFormat, IntPtr dataPointer)
        {
            bufferDescription = description;
            nativeDescription = ConvertToNativeDescription(Description);
            ViewFlags = viewFlags;
            InitCountAndViewFormat(out this.elementCount, ref viewFormat);
            ViewFormat = viewFormat;
            unsafe
            {
                var pdata = new SubresourceData { PSysMem = (void*)dataPointer };
                var buffer = new ComPtr<ID3D11Buffer>();
                SilkMarshal.ThrowHResult(NativeDevice.Get().CreateBuffer(ref nativeDescription, ref pdata, ref buffer.Handle));
                NativeBuffer = buffer;
            }
            // Staging resource don't have any views

            if (nativeDescription.Usage != Silk.NET.Direct3D11.Usage.UsageStaging)
                this.InitializeViews();

            if (GraphicsDevice != null)
            {
                GraphicsDevice.RegisterBufferMemoryUsage(SizeInBytes);
            }

            return this;
        }

        /// <inheritdoc/>
        protected internal override void OnDestroyed()
        {
            if (GraphicsDevice != null)
            {
                GraphicsDevice.RegisterBufferMemoryUsage(-SizeInBytes);
            }

            base.OnDestroyed();
        }

        /// <inheritdoc/>
        protected internal override bool OnRecreate()
        {
            base.OnRecreate();

            if (Description.Usage == GraphicsResourceUsage.Immutable
                || Description.Usage == GraphicsResourceUsage.Default)
                return false;

            unsafe
            {
                var buffer = new ComPtr<ID3D11Buffer>();
                SilkMarshal.ThrowHResult(NativeDevice.Get().CreateBuffer(ref nativeDescription, null, ref buffer.Handle));
                NativeBuffer = buffer;
            }

            // Staging resource don't have any views
            if (nativeDescription.Usage != Silk.NET.Direct3D11.Usage.UsageStaging)
                this.InitializeViews();

            return true;
        }

        /// <summary>
        /// Explicitly recreate buffer with given data. Usually called after a <see cref="GraphicsDevice"/> reset.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataPointer"></param>
        public void Recreate(IntPtr dataPointer)
        {
            unsafe
            {
                var pdata = new SubresourceData { PSysMem = (void*)dataPointer };
                var buffer = new ComPtr<ID3D11Buffer>();
                SilkMarshal.ThrowHResult(NativeDevice.Get().CreateBuffer(ref nativeDescription, ref pdata, ref buffer.Handle));
                NativeBuffer = buffer;
            }
            // Staging resource don't have any views
            if (nativeDescription.Usage != Silk.NET.Direct3D11.Usage.UsageStaging)
                this.InitializeViews();
        }

        /// <summary>
        /// Gets a <see cref="ShaderResourceView"/> for a particular <see cref="PixelFormat"/>.
        /// </summary>
        /// <param name="viewFormat">The view format.</param>
        /// <returns>A <see cref="ShaderResourceView"/> for the particular view format.</returns>
        /// <remarks>
        /// The buffer must have been declared with <see cref="Graphics.BufferFlags.ShaderResource"/>. 
        /// The ShaderResourceView instance is kept by this buffer and will be disposed when this buffer is disposed.
        /// </remarks>
        internal ComPtr<ID3D11ShaderResourceView> GetShaderResourceView(PixelFormat viewFormat)
        {
            ComPtr<ID3D11ShaderResourceView> srv = new();
            if ((nativeDescription.BindFlags & (uint)BindFlag.BindShaderResource) != 0)
            {
                var description = new ShaderResourceViewDesc
                {
                    Format = (Format)viewFormat,
                    ViewDimension = D3DSrvDimension.D3DSrvDimensionBufferex,
                    Anonymous =
                    {
                        BufferEx =
                        { 
                            NumElements = (uint)ElementCount,
                            FirstElement = 0,
                            Flags = 0,
                        }
                    }
                };

                if (((ViewFlags & BufferFlags.RawBuffer) == BufferFlags.RawBuffer))
                    description.BufferEx.Flags |= (uint)BufferexSrvFlag.BufferexSrvFlagRaw;
                unsafe
                {
                    SilkMarshal.ThrowHResult(GraphicsDevice.NativeDevice.Get().CreateShaderResourceView(NativeResource.Handle, &description, &srv.Handle));
                }
            }
            return srv;
        }

        /// <summary>
        /// Gets a <see cref="RenderTargetView" /> for a particular <see cref="PixelFormat" />.
        /// </summary>
        /// <param name="pixelFormat">The view format.</param>
        /// <param name="width">The width in pixels of the render target.</param>
        /// <returns>A <see cref="RenderTargetView" /> for the particular view format.</returns>
        /// <remarks>The buffer must have been declared with <see cref="Graphics.BufferFlags.RenderTarget" />.
        /// The RenderTargetView instance is kept by this buffer and will be disposed when this buffer is disposed.</remarks>
        internal ComPtr<ID3D11RenderTargetView> GetRenderTargetView(PixelFormat pixelFormat, int width)
        {
            ComPtr<ID3D11RenderTargetView> srv = null;
            if ((nativeDescription.BindFlags & (uint)BindFlag.BindRenderTarget) != 0)
            {
                var description = new RenderTargetViewDesc()
                {
                    Format = (Format)pixelFormat,
                    ViewDimension = RtvDimension.RtvDimensionBuffer,
                    Anonymous =
                    {
                        Buffer =
                        {
                            ElementWidth = (uint)pixelFormat.SizeInBytes() * (uint)width,
                            ElementOffset = 0,
                        },
                    }
                    
                };
                unsafe
                {
                    SilkMarshal.ThrowHResult(GraphicsDevice.NativeDevice.Get().CreateRenderTargetView(NativeResource.Handle, &description, &srv.Handle));
                }
            }
            return srv;
        }

        protected override void OnNameChanged()
        {
            base.OnNameChanged();
            if (GraphicsDevice != null && GraphicsDevice.IsDebugMode)
            {
                //if (NativeShaderResourceView.Handle != null)
                //    NativeShaderResourceView.DebugName = Name == null ? null : string.Format("{0} SRV", Name);

                //if (NativeUnorderedAccessView != null)
                //    NativeUnorderedAccessView.DebugName = Name == null ? null : string.Format("{0} UAV", Name);
            }
        }

        private void InitCountAndViewFormat(out int count, ref PixelFormat viewFormat)
        {
            if (Description.StructureByteStride == 0)
            {
                // TODO: The way to calculate the count is not always correct depending on the ViewFlags...etc.
                if ((ViewFlags & BufferFlags.RawBuffer) != 0)
                {
                    count = Description.SizeInBytes / sizeof(int);
                }
                else if ((ViewFlags & BufferFlags.ShaderResource) != 0)
                {
                    count = Description.SizeInBytes / viewFormat.SizeInBytes();
                }
                else
                {
                    count = 0;
                }
            }
            else
            {
                // For structured buffer
                count = Description.SizeInBytes / Description.StructureByteStride;
                viewFormat = PixelFormat.None;
            }
        }

        private static BufferDesc ConvertToNativeDescription(BufferDescription bufferDescription)
        {
            var desc = new BufferDesc()
            {
                ByteWidth = (uint)bufferDescription.SizeInBytes,
                StructureByteStride = (uint)bufferDescription.StructureByteStride,
                CPUAccessFlags = (uint)GetCpuAccessFlagsFromUsage(bufferDescription.Usage),
                BindFlags = 0,
                MiscFlags = 0,
                Usage = (Usage)bufferDescription.Usage,
            };

            var bufferFlags = bufferDescription.BufferFlags;

            if ((bufferFlags & BufferFlags.ConstantBuffer) != 0)
                desc.BindFlags |= (uint)BindFlag.BindConstantBuffer;

            if ((bufferFlags & BufferFlags.IndexBuffer) != 0)
                desc.BindFlags |= (uint)BindFlag.BindIndexBuffer;

            if ((bufferFlags & BufferFlags.VertexBuffer) != 0)
                desc.BindFlags |= (uint)BindFlag.BindVertexBuffer;

            if ((bufferFlags & BufferFlags.RenderTarget) != 0)
                desc.BindFlags |= (uint)BindFlag.BindRenderTarget;

            if ((bufferFlags & BufferFlags.ShaderResource) != 0)
                desc.BindFlags |= (uint)BindFlag.BindShaderResource;

            if ((bufferFlags & BufferFlags.UnorderedAccess) != 0)
                desc.BindFlags |= (uint)BindFlag.BindUnorderedAccess;

            if ((bufferFlags & BufferFlags.StructuredBuffer) != 0)
            {
                desc.MiscFlags |= (uint)ResourceMiscFlag.ResourceMiscBufferStructured;
                if (bufferDescription.StructureByteStride <= 0)
                    throw new ArgumentException("Element size cannot be less or equal 0 for structured buffer");
            }

            if ((bufferFlags & BufferFlags.RawBuffer) == BufferFlags.RawBuffer)
                desc.MiscFlags |= (uint)ResourceMiscFlag.ResourceMiscBufferAllowRawViews;

            if ((bufferFlags & BufferFlags.ArgumentBuffer) == BufferFlags.ArgumentBuffer)
                desc.MiscFlags |= (uint)ResourceMiscFlag.ResourceMiscDrawindirectArgs;

            if ((bufferFlags & BufferFlags.StreamOutput) != 0)
                desc.BindFlags |= (uint)BindFlag.BindStreamOutput;

            return desc;
        }

        /// <summary>
        /// Initializes the views.
        /// </summary>
        private void InitializeViews()
        {
            var bindFlags = nativeDescription.BindFlags;

            var srvFormat = ViewFormat;
            var uavFormat = ViewFormat;

            if (((ViewFlags & BufferFlags.RawBuffer) != 0))
            {
                srvFormat = PixelFormat.R32_Typeless;
                uavFormat = PixelFormat.R32_Typeless;
            }

            if ((bindFlags & (uint)BindFlag.BindShaderResource) != 0)
            {
                this.NativeShaderResourceView = GetShaderResourceView(srvFormat);
            }

            if ((bindFlags & (uint)BindFlag.BindUnorderedAccess) != 0)
            {
                var description = new UnorderedAccessViewDesc()
                {
                    Format = (Format)uavFormat,
                    ViewDimension = UavDimension.UavDimensionBuffer,
                    Anonymous =
                    {
                        Buffer =
                        {
                            NumElements = (uint)ElementCount,
                            FirstElement = 0,
                            Flags = 0,
                        }
                    }
                };

                if (((ViewFlags & BufferFlags.RawBuffer) == BufferFlags.RawBuffer))
                    description.Buffer.Flags |= (uint)BufferUavFlag.BufferUavFlagRaw;

                if (((ViewFlags & BufferFlags.StructuredAppendBuffer) == BufferFlags.StructuredAppendBuffer))
                    description.Buffer.Flags |= (uint)BufferUavFlag.BufferUavFlagAppend;

                if (((ViewFlags & BufferFlags.StructuredCounterBuffer) == BufferFlags.StructuredCounterBuffer))
                    description.Buffer.Flags |= (uint)BufferUavFlag.BufferUavFlagCounter;
                var uav = new ComPtr<ID3D11UnorderedAccessView>();
                unsafe
                {
                    SilkMarshal.ThrowHResult(GraphicsDevice.NativeDevice.Get().CreateUnorderedAccessView(NativeResource.Handle, &description, &uav.Handle));
                }

                this.NativeUnorderedAccessView = uav;
            }
        }
    }
} 
#endif 
