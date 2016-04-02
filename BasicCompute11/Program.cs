using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using Buffer = SharpDX.Direct3D11.Buffer;

namespace BasicCompute11
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Console.WriteLine("Creating device...");
            var device = CreateComputeDevice();
            Console.WriteLine("done");

            Console.WriteLine("Creating Compute Shader...");
            var computeShader = CreateComputeShader("BasicCompute11.hlsl", "CSMain", device);
            Console.WriteLine("done");

            Console.WriteLine("Creating buffers and filling them with initial data...");
            var NUM_ELEMENTS = 1024;
            var data0 = new BufType[NUM_ELEMENTS];
            var data1 = new BufType[NUM_ELEMENTS];

            for (int i = 0; i < NUM_ELEMENTS; ++i)
            {
                data0[i].i = i;
                data1[i].i = i;
            }

            var elementSize = Marshal.SizeOf(typeof(BufType));

            var buf0 = CreateStructuredBuffer(device, elementSize, NUM_ELEMENTS, data0);
            var buf1 = CreateStructuredBuffer(device, elementSize, NUM_ELEMENTS, data1);
            var bufResult = CreateStructuredBuffer(device, elementSize, NUM_ELEMENTS);
            Console.WriteLine("done");

            Console.WriteLine("Creating buffer views...");
            var buf0SRV = CreateBufferSRV(device, buf0);
            var buf1SRV = CreateBufferSRV(device, buf1);
            var bufResultUAV = CreateBufferUAV(device, bufResult);
            Console.WriteLine("done");

            Console.WriteLine("Running Compute Shader...");
            RunComputeShader(
                device.ImmediateContext,
                computeShader,
                new[] { buf0SRV, buf1SRV },
                bufResultUAV,
                NUM_ELEMENTS, 1, 1
                );
            Console.WriteLine("done");

            // Read back the result from GPU, verify its correctness against result computed by CPU
            var debugbuf = CreateAndCopyToDebugBuf(device, bufResult);

            DataStream mappedResource;
            device.ImmediateContext.MapSubresource(debugbuf, MapMode.Read, MapFlags.None, out mappedResource);

            // Set a break point here and put down the expression "p" in your watch window to see what has been written out by our CS
            var p = mappedResource.ReadRange<BufType>(NUM_ELEMENTS);

            // Verify that if Compute Shader has done right
            Console.WriteLine("Verifying against CPU result...");
            bool success = true;
            for (int i = 0; i < NUM_ELEMENTS; ++i)
            {
                if (p[i].i != data0[i].i + data1[i].i)
                {
                    Console.WriteLine("failure");
                    success = false;

                    break;
                }
            }

            if (success)
            {
                Console.WriteLine("succeeded");
            }
        }

        private static Device CreateComputeDevice()
        {
            Contract.Ensures(Contract.Result<Device>() != null);

            var creationFlags = DeviceCreationFlags.SingleThreaded;
#if DEBUG
            creationFlags |= DeviceCreationFlags.Debug;
#endif
            var flvl = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            };

            Device device = null;
            bool needWarpDevice = false;
            try
            {
                device = new Device(DriverType.Hardware, creationFlags, flvl);
                // A hardware accelerated device has been created, so check for Compute Shader support

                // If we have a device >= D3D_FEATURE_LEVEL_11_0 created, full CS5.0 support is guaranteed, no need for further checks
                // Otherwise, we need further check whether this device support CS4.x (Compute on 10)
                if (device.FeatureLevel < FeatureLevel.Level_11_0 && !device.CheckFeatureSupport(Feature.D3D10XHardwareOptions))
                {
                    needWarpDevice = true;
                    Console.WriteLine("No hardware Compute Shader capable device found, trying to create software (WARP) device.");
                }
            }
            catch (SharpDXException ex)
            {
                needWarpDevice = true;
                Console.WriteLine(ex);
            }

            if (needWarpDevice)
            {
                if (device != null)
                {
                    device.Dispose();
                }

                // Either because of failure on creating a hardware device or hardware lacking CS capability, we create a WARP (software) device here
                device = new Device(DriverType.Warp, creationFlags, flvl);
            }

            return device;
        }

        private static ComputeShader CreateComputeShader(string fileName, string entrypoint, Device device)
        {
            Contract.Requires(fileName != null);
            Contract.Requires(entrypoint != null);
            Contract.Requires(device != null);
            Contract.Ensures(Contract.Result<ComputeShader>() != null);

            // We generally prefer to use the higher CS shader profile when possible as CS 5.0 is better performance on 11-class hardware
            var profile = (device.FeatureLevel >= FeatureLevel.Level_11_0) ? "cs_5_0" : "cs_4_0";

            var shaderFlags = ShaderFlags.EnableStrictness;
#if DEBUG
            // Set the D3DCOMPILE_DEBUG flag to embed debug information in the shaders.
            // Setting this flag improves the shader debugging experience, but still allows
            // the shaders to be optimized and to run exactly the way they will run in
            // the release configuration of this program.
            shaderFlags |= ShaderFlags.Debug;

            // Disable optimizations to further improve shader debugging
            shaderFlags |= ShaderFlags.SkipOptimization;
#endif

            var byteCode = ShaderBytecode.CompileFromFile(fileName, entrypoint, profile, shaderFlags);
            return new ComputeShader(device, byteCode);
        }

        private static Buffer CreateStructuredBuffer<T>(Device device, int elementSize, int count, params T[] data) where T : struct
        {
            Contract.Requires(device != null);
            Contract.Ensures(Contract.Result<Buffer>() != null);

            var desc = new BufferDescription();
            desc.BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource;
            desc.SizeInBytes = elementSize * count;
            desc.OptionFlags = ResourceOptionFlags.BufferStructured;
            desc.StructureByteStride = elementSize;
            return Buffer.Create(device, data, desc);
        }

        private static Buffer CreateStructuredBuffer(Device device, int elementSize, int count)
        {
            Contract.Requires(device != null);
            Contract.Ensures(Contract.Result<Buffer>() != null);

            var desc = new BufferDescription();
            desc.BindFlags = BindFlags.UnorderedAccess | BindFlags.ShaderResource;
            desc.SizeInBytes = elementSize * count;
            desc.OptionFlags = ResourceOptionFlags.BufferStructured;
            desc.StructureByteStride = elementSize;
            return new Buffer(device, desc);
        }

        private static ShaderResourceView CreateBufferSRV(Device device, Buffer buffer)
        {
            Contract.Requires(device != null);
            Contract.Requires(buffer != null);
            Contract.Ensures(Contract.Result<ShaderResourceView>() != null);

            var desc = new ShaderResourceViewDescription();
            desc.Dimension = ShaderResourceViewDimension.ExtendedBuffer;
            desc.BufferEx.FirstElement = 0;
            desc.Format = SharpDX.DXGI.Format.Unknown;
            desc.BufferEx.ElementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride;

            return new ShaderResourceView(device, buffer, desc);
        }

        private static UnorderedAccessView CreateBufferUAV(Device device, Buffer buffer)
        {
            Contract.Requires(device != null);
            Contract.Requires(buffer != null);
            Contract.Ensures(Contract.Result<UnorderedAccessView>() != null);

            var desc = new UnorderedAccessViewDescription();
            desc.Dimension = UnorderedAccessViewDimension.Buffer;
            desc.Buffer.FirstElement = 0;
            desc.Format = SharpDX.DXGI.Format.Unknown;
            desc.Buffer.ElementCount = buffer.Description.SizeInBytes / buffer.Description.StructureByteStride;

            return new UnorderedAccessView(device, buffer, desc);
        }

        private static void RunComputeShader(
            DeviceContext immediateContext,
            ComputeShader computeShader,
            ShaderResourceView[] shaderResourceViews,
            UnorderedAccessView unorderedAccessView,
            int x, int y, int z
            )
        {
            Contract.Requires(immediateContext != null);
            Contract.Requires(computeShader != null);
            Contract.Requires(shaderResourceViews != null);
            Contract.Requires(unorderedAccessView != null);

            immediateContext.ComputeShader.SetShader(computeShader, null, 0);
            immediateContext.ComputeShader.SetShaderResources(0, shaderResourceViews);
            immediateContext.ComputeShader.SetUnorderedAccessViews(0, unorderedAccessView);

            immediateContext.Dispatch(x, y, z);

            immediateContext.ComputeShader.SetShader(null, null, 0);
            immediateContext.ComputeShader.SetShaderResource(0, null);
            immediateContext.ComputeShader.SetUnorderedAccessView(0, null);
        }

        private static Buffer CreateAndCopyToDebugBuf(Device device, Buffer buffer)
        {
            Contract.Requires(device != null);
            Contract.Requires(buffer != null);
            Contract.Ensures(Contract.Result<Buffer>() != null);

            var desc = buffer.Description;
            desc.CpuAccessFlags = CpuAccessFlags.Read;
            desc.Usage = ResourceUsage.Staging;
            desc.OptionFlags = ResourceOptionFlags.None;
            desc.BindFlags = BindFlags.None;

            Buffer debugbuf = new Buffer(device, desc);
            device.ImmediateContext.CopyResource(buffer, debugbuf);

            return debugbuf;
        }
    }

    public struct BufType
    {
        public int i;
    }
}