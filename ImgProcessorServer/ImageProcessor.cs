using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCL.Net;

public static class ImageProcessor
{
    public static Bitmap ApplyLaplaceFilter(Bitmap input)
    {
        // Convert input bitmap to byte array (32bpp ARGB)
        int width = input.Width;
        int height = input.Height;
        BitmapData inputData = input.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int byteSize = inputData.Stride * height;
        byte[] inputBytes = new byte[byteSize];
        Marshal.Copy(inputData.Scan0, inputBytes, 0, byteSize);
        input.UnlockBits(inputData);

        // OpenCL setup
        ErrorCode error;
        Platform[] platforms = Cl.GetPlatformIDs(out error);
        Device[] devices = Cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, out error);
        Context context = Cl.CreateContext(null, 1, devices, null, IntPtr.Zero, out error);
        CommandQueue queue = Cl.CreateCommandQueue(context, devices[0], (CommandQueueProperties)0, out error);

        string kernelSource = File.ReadAllText("laplace_filter.cl");

        Program program = Cl.CreateProgramWithSource(context, 1, new[] { kernelSource }, null, out error);
        error = Cl.BuildProgram(program, 1, devices, string.Empty, null, IntPtr.Zero);

        Kernel kernel = Cl.CreateKernel(program, "laplace_filter", out error);

		// Buffers
        IMem inputBuffer = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)byteSize, inputBytes, out error);
        IMem outputBuffer = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)byteSize, out error);

        // Set kernel args
        Cl.SetKernelArg(kernel, 0, inputBuffer);
        Cl.SetKernelArg(kernel, 1, outputBuffer);
        Cl.SetKernelArg(kernel, 2, width);
        Cl.SetKernelArg(kernel, 3, height);
        Cl.SetKernelArg(kernel, 4, inputData.Stride);

		// Execute kernel
        Event clevent;
        IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)width, (IntPtr)height };
        error = Cl.EnqueueNDRangeKernel(queue, kernel, 2, null, globalWorkSize, null, 0, null, out clevent);
        Cl.Finish(queue);

        // Read result
        byte[] outputBytes = new byte[byteSize];
        Cl.EnqueueReadBuffer(queue, outputBuffer, Bool.True, IntPtr.Zero, (IntPtr)byteSize, outputBytes, 0, null, out _);

        // Convert back to bitmap
        Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData outputData = output.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(outputBytes, 0, outputData.Scan0, byteSize);
        output.UnlockBits(outputData);

        // Cleanup
        Cl.ReleaseKernel(kernel);
        Cl.ReleaseProgram(program);
        Cl.ReleaseMemObject(inputBuffer);
        Cl.ReleaseMemObject(outputBuffer);
        Cl.ReleaseCommandQueue(queue);
        Cl.ReleaseContext(context);

        return output;
    }
}
