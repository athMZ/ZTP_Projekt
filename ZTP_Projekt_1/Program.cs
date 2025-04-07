using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime;

#pragma warning disable CA1416

namespace ZTP_Projekt_1;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public struct Settings
{
	public string INPUT_DIR { get; }
	public string OUTPUT_DIR { get; }

	public bool VERSION_MANAGED { get; }
	public bool ENABLE_PARALLEL { get; }
	public bool GC_COLLECT { get; }
	public bool COMPACT_ONCE { get; }
	public bool DISPOSE { get; }
	public bool USE_FIXED { get; }
	public bool USE_POOLING { get; }
	public bool LOW_LATENCY { get; }
	public bool SUSTAINED_LOW_LATENCY { get; }

	public Settings()
	{
		INPUT_DIR = Environment.GetEnvironmentVariable("INPUT_DIR") ?? @"D:\ZTP-Imgs\in";
		Console.WriteLine($"INPUT_DIR: {INPUT_DIR}");

		OUTPUT_DIR = Environment.GetEnvironmentVariable("OUTPUT_DIR") ?? @"D:\ZTP-Imgs\out";
		Console.WriteLine($"OUTPUT_DIR: {OUTPUT_DIR}");

		ENABLE_PARALLEL = Environment.GetEnvironmentVariable("ENABLE_PARALLEL") == "true";
		Console.WriteLine($"ENABLE_PARALLEL: {ENABLE_PARALLEL}");

		VERSION_MANAGED = Environment.GetEnvironmentVariable("VERSION_MANAGED") == "true";
		Console.WriteLine($"VERSION_MANAGED: {VERSION_MANAGED}");

		GC_COLLECT = Environment.GetEnvironmentVariable("GC_COLLECT") == "true";
		Console.WriteLine($"GC_COLLECT: {GC_COLLECT}");

		COMPACT_ONCE = Environment.GetEnvironmentVariable("COMPACT_ONCE") == "true";
		Console.WriteLine($"COMPACT_ONCE: {COMPACT_ONCE}");

		DISPOSE = Environment.GetEnvironmentVariable("DISPOSE") == "true";
		Console.WriteLine($"DISPOSE: {DISPOSE}");

		USE_FIXED = Environment.GetEnvironmentVariable("USE_FIXED") == "true";
		Console.WriteLine($"USE_FIXED: {USE_FIXED}");

		USE_POOLING = Environment.GetEnvironmentVariable("USE_POOLING") == "true";
		Console.WriteLine($"USE_POOLING: {USE_POOLING}");

		LOW_LATENCY = Environment.GetEnvironmentVariable("LOW_LATENCY") == "true";
		Console.WriteLine($"LOW_LATENCY: {LOW_LATENCY}");

		SUSTAINED_LOW_LATENCY = Environment.GetEnvironmentVariable("SUSTAINED_LOW_LATENCY") == "true";
		Console.WriteLine($"SUSTAINED_LOW_LATENCY: {SUSTAINED_LOW_LATENCY}");
	}
}

public class Program
{
	private static readonly Settings Settings = new();

	private static readonly int[,] LaplaceKernel = new[,]
	{
		{ 0, 0, -1, 0, 0 },
		{ 0, -1, -2, -1, 0 },
		{ -1, -2, 16, -2, -1 },
		{ 0, -1, -2, -1, 0 },
		{ 0, 0, -1, 0, 0 }
	};

	// Define a delegate type for image processing methods
	private delegate Bitmap FilterMethod(Bitmap input);

	static Bitmap ApplyLaplaceFilterManaged(Bitmap bitmap)
	{
		Bitmap result = new Bitmap(bitmap.Width, bitmap.Height);
		int width = bitmap.Width;
		int height = bitmap.Height;

		// Allocate byte arrays on the heap to store image data
		// Each pixel needs 3 bytes (R,G,B)
		byte[] inputPixels;
		byte[] outputPixels;

		// Use pooled memory if requested
		if (Settings.USE_POOLING)
		{
			inputPixels = ArrayPool<byte>.Shared.Rent(height * width * 3);
			outputPixels = ArrayPool<byte>.Shared.Rent(height * width * 3);
		}
		else
		{
			inputPixels = new byte[height * width * 3];
			outputPixels = new byte[height * width * 3];
		}

		try
		{
			// First pass: read all pixels into the byte array
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Color pixel = bitmap.GetPixel(x, y);
					int index = (y * width + x) * 3;
					inputPixels[index] = pixel.R;     // R
					inputPixels[index + 1] = pixel.G; // G
					inputPixels[index + 2] = pixel.B; // B
				}
			}

			// Second pass: apply the filter
			for (int y = 2; y < height - 2; y++)
			{
				for (int x = 2; x < width - 2; x++)
				{
					int r = 0, g = 0, b = 0;

					for (int ky = -2; ky <= 2; ky++)
					{
						for (int kx = -2; kx <= 2; kx++)
						{
							int pixelIndex = ((y + ky) * width + (x + kx)) * 3;
							int kernelValue = LaplaceKernel[ky + 2, kx + 2];

							r += inputPixels[pixelIndex] * kernelValue;     // R
							g += inputPixels[pixelIndex + 1] * kernelValue; // G
							b += inputPixels[pixelIndex + 2] * kernelValue; // B
						}
					}

					r = Math.Clamp(r, 0, 255);
					g = Math.Clamp(g, 0, 255);
					b = Math.Clamp(b, 0, 255);

					int outIndex = (y * width + x) * 3;
					outputPixels[outIndex] = (byte)r;     // R
					outputPixels[outIndex + 1] = (byte)g; // G
					outputPixels[outIndex + 2] = (byte)b; // B
				}
			}

			// Third pass: write processed pixels to the result bitmap
			for (int y = 2; y < height - 2; y++)
			{
				for (int x = 2; x < width - 2; x++)
				{
					int index = (y * width + x) * 3;
					result.SetPixel(x, y, Color.FromArgb(
						outputPixels[index],      // R
						outputPixels[index + 1],  // G
						outputPixels[index + 2]   // B
					));
				}
			}

			return result;
		}
		finally
		{
			// Return arrays to the pool if pooling is enabled
			if (Settings.USE_POOLING)
			{
				ArrayPool<byte>.Shared.Return(inputPixels);
				ArrayPool<byte>.Shared.Return(outputPixels);
			}
		}
	}

	static Bitmap ApplyLaplaceFilterManagedFixed(Bitmap bitmap)
	{
		Bitmap result = new Bitmap(bitmap.Width, bitmap.Height);
		int width = bitmap.Width;
		int height = bitmap.Height;

		// Allocate byte arrays for image data
		byte[] inputPixels;
		byte[] outputPixels;

		// Use pooled memory if requested
		if (Settings.USE_POOLING)
		{
			inputPixels = ArrayPool<byte>.Shared.Rent(height * width * 3);
			outputPixels = ArrayPool<byte>.Shared.Rent(height * width * 3);
		}
		else
		{
			inputPixels = new byte[height * width * 3];
			outputPixels = new byte[height * width * 3];
		}

		try
		{
			// First pass: read all pixels into the byte array
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Color pixel = bitmap.GetPixel(x, y);
					int index = (y * width + x) * 3;
					inputPixels[index] = pixel.R;     // R
					inputPixels[index + 1] = pixel.G; // G
					inputPixels[index + 2] = pixel.B; // B
				}
			}

			// Second pass: apply the filter using fixed pointers
			unsafe
			{
				fixed (byte* inputPtr = inputPixels)
				fixed (byte* outputPtr = outputPixels)
				fixed (int* kernelPtr = &LaplaceKernel[0, 0])
				{
					for (int y = 2; y < height - 2; y++)
					{
						for (int x = 2; x < width - 2; x++)
						{
							int r = 0, g = 0, b = 0;

							for (int ky = -2; ky <= 2; ky++)
							{
								for (int kx = -2; kx <= 2; kx++)
								{
									byte* pixel = inputPtr + (((y + ky) * width + (x + kx)) * 3);
									int kernelValue = kernelPtr[(ky + 2) * 5 + (kx + 2)];

									r += pixel[0] * kernelValue;     // R
									g += pixel[1] * kernelValue;     // G
									b += pixel[2] * kernelValue;     // B
								}
							}

							r = Math.Clamp(r, 0, 255);
							g = Math.Clamp(g, 0, 255);
							b = Math.Clamp(b, 0, 255);

							byte* output = outputPtr + ((y * width + x) * 3);
							output[0] = (byte)r;     // R
							output[1] = (byte)g;     // G
							output[2] = (byte)b;     // B
						}
					}
				}
			}

			// Third pass: write processed pixels to the result bitmap
			for (int y = 2; y < height - 2; y++)
			{
				for (int x = 2; x < width - 2; x++)
				{
					int index = (y * width + x) * 3;
					result.SetPixel(x, y, Color.FromArgb(
						outputPixels[index],      // R
						outputPixels[index + 1],  // G
						outputPixels[index + 2]   // B
					));
				}
			}

			return result;
		}
		finally
		{
			// Return arrays to the pool if pooling is enabled
			if (Settings.USE_POOLING)
			{
				ArrayPool<byte>.Shared.Return(inputPixels);
				ArrayPool<byte>.Shared.Return(outputPixels);
			}
		}
	}

	static Bitmap ApplyLaplaceFilterUnmanaged(Bitmap bitmap)
	{
		Bitmap result = new Bitmap(bitmap.Width, bitmap.Height);

		BitmapData inputData = bitmap.LockBits(
			new Rectangle(0, 0, bitmap.Width, bitmap.Height),
			ImageLockMode.ReadOnly,
			PixelFormat.Format24bppRgb);

		BitmapData outputData = result.LockBits(
			new Rectangle(0, 0, result.Width, result.Height),
			ImageLockMode.WriteOnly,
			PixelFormat.Format24bppRgb);

		unsafe
		{
			byte* inputPtr = (byte*)inputData.Scan0;
			byte* outputPtr = (byte*)outputData.Scan0;

			int stride = inputData.Stride;

			for (int y = 2; y < bitmap.Height - 2; y++)
			{
				for (int x = 2; x < bitmap.Width - 2; x++)
				{
					int r = 0, g = 0, b = 0;

					for (int ky = -2; ky <= 2; ky++)
					{
						for (int kx = -2; kx <= 2; kx++)
						{
							byte* pixel = inputPtr + ((y + ky) * stride) + ((x + kx) * 3);
							int kernelValue = LaplaceKernel[ky + 2, kx + 2];

							b += pixel[0] * kernelValue;
							g += pixel[1] * kernelValue;
							r += pixel[2] * kernelValue;
						}
					}

					r = Math.Clamp(r, 0, 255);
					g = Math.Clamp(g, 0, 255);
					b = Math.Clamp(b, 0, 255);

					byte* outputPixel = outputPtr + (y * stride) + (x * 3);
					outputPixel[0] = (byte)b;
					outputPixel[1] = (byte)g;
					outputPixel[2] = (byte)r;
				}
			}
		}

		bitmap.UnlockBits(inputData);
		result.UnlockBits(outputData);

		return result;
	}

	static Bitmap ApplyLaplaceFilterUnmanagedFixed(Bitmap bitmap)
	{
		Bitmap result = new Bitmap(bitmap.Width, bitmap.Height);

		BitmapData inputData = bitmap.LockBits(
			new Rectangle(0, 0, bitmap.Width, bitmap.Height),
			ImageLockMode.ReadOnly,
			PixelFormat.Format24bppRgb);

		BitmapData outputData = result.LockBits(
			new Rectangle(0, 0, result.Width, result.Height),
			ImageLockMode.WriteOnly,
			PixelFormat.Format24bppRgb);

		unsafe
		{
			byte* inputPtr = (byte*)inputData.Scan0;
			byte* outputPtr = (byte*)outputData.Scan0;

			int stride = inputData.Stride;
			int width = bitmap.Width;
			int height = bitmap.Height;

			// Use fixed to pin the kernel in memory for faster access
			fixed (int* kernelPtr = &LaplaceKernel[0, 0])
			{
				for (int y = 2; y < height - 2; y++)
				{
					byte* rowInputPtr = inputPtr + (y * stride);
					byte* rowOutputPtr = outputPtr + (y * stride);

					for (int x = 2; x < width - 2; x++)
					{
						int r = 0, g = 0, b = 0;

						for (int ky = -2; ky <= 2; ky++)
						{
							byte* kernelRowPtr = inputPtr + ((y + ky) * stride);

							for (int kx = -2; kx <= 2; kx++)
							{
								byte* pixel = kernelRowPtr + ((x + kx) * 3);
								int kernelValue = kernelPtr[(ky + 2) * 5 + (kx + 2)];

								b += pixel[0] * kernelValue;
								g += pixel[1] * kernelValue;
								r += pixel[2] * kernelValue;
							}
						}

						r = Math.Clamp(r, 0, 255);
						g = Math.Clamp(g, 0, 255);
						b = Math.Clamp(b, 0, 255);

						byte* outputPixel = rowOutputPtr + (x * 3);
						outputPixel[0] = (byte)b;
						outputPixel[1] = (byte)g;
						outputPixel[2] = (byte)r;
					}
				}
			}
		}

		bitmap.UnlockBits(inputData);
		result.UnlockBits(outputData);

		return result;
	}

	// New pooled version for unmanaged code
	static Bitmap ApplyLaplaceFilterUnmanagedPooled(Bitmap bitmap)
	{
		Bitmap result = new Bitmap(bitmap.Width, bitmap.Height);

		BitmapData inputData = bitmap.LockBits(
			new Rectangle(0, 0, bitmap.Width, bitmap.Height),
			ImageLockMode.ReadOnly,
			PixelFormat.Format24bppRgb);

		BitmapData outputData = result.LockBits(
			new Rectangle(0, 0, result.Width, result.Height),
			ImageLockMode.WriteOnly,
			PixelFormat.Format24bppRgb);

		// Use kernel array pool for the kernel values
		int[] kernelArrayPooled = ArrayPool<int>.Shared.Rent(25); // 5x5 kernel
		try
		{
			// Copy kernel values to the pooled array
			for (int y = 0; y < 5; y++)
			{
				for (int x = 0; x < 5; x++)
				{
					kernelArrayPooled[y * 5 + x] = LaplaceKernel[y, x];
				}
			}

			unsafe
			{
				byte* inputPtr = (byte*)inputData.Scan0;
				byte* outputPtr = (byte*)outputData.Scan0;

				int stride = inputData.Stride;
				int width = bitmap.Width;
				int height = bitmap.Height;

				fixed (int* kernelPtr = kernelArrayPooled)
				{
					for (int y = 2; y < height - 2; y++)
					{
						for (int x = 2; x < width - 2; x++)
						{
							int r = 0, g = 0, b = 0;

							for (int ky = -2; ky <= 2; ky++)
							{
								for (int kx = -2; kx <= 2; kx++)
								{
									byte* pixel = inputPtr + ((y + ky) * stride) + ((x + kx) * 3);
									int kernelValue = kernelPtr[(ky + 2) * 5 + (kx + 2)];

									b += pixel[0] * kernelValue;
									g += pixel[1] * kernelValue;
									r += pixel[2] * kernelValue;
								}
							}

							r = Math.Clamp(r, 0, 255);
							g = Math.Clamp(g, 0, 255);
							b = Math.Clamp(b, 0, 255);

							byte* outputPixel = outputPtr + (y * stride) + (x * 3);
							outputPixel[0] = (byte)b;
							outputPixel[1] = (byte)g;
							outputPixel[2] = (byte)r;
						}
					}
				}
			}
		}
		finally
		{
			ArrayPool<int>.Shared.Return(kernelArrayPooled);
			bitmap.UnlockBits(inputData);
			result.UnlockBits(outputData);
		}

		return result;
	}

	// Generic method to process images using a specific filter method
	static void ProcessImages(string[] imagePaths, string outputDirectory, FilterMethod filterMethod, string filterName)
	{
		static void ProcessSingleImage(string imagePath, string outputDirectory, FilterMethod filterMethod, string filterName)
		{
			Bitmap inputBitmap = null;
			Bitmap outputBitmap = null;

			try
			{
				inputBitmap = new Bitmap(imagePath);
				outputBitmap = filterMethod(inputBitmap);

				string baseFileName = Path.GetFileNameWithoutExtension(imagePath);
				string outputPath = Path.Combine(outputDirectory, $"{baseFileName}_{filterName}_result.png");
				outputBitmap.Save(outputPath);
			}
			finally
			{
				if (Settings.DISPOSE)
				{
					inputBitmap?.Dispose();
					outputBitmap?.Dispose();
				}
			}

			if (Settings.GC_COLLECT) GC.Collect();
		}

		Stopwatch sw = new Stopwatch();
		sw.Start();

		if (Settings.ENABLE_PARALLEL)
		{
			Parallel.ForEach(imagePaths, imagePath =>
			{
				ProcessSingleImage(imagePath, outputDirectory, filterMethod, filterName);
			});
		}
		else
		{
			foreach (var imagePath in imagePaths)
			{
				ProcessSingleImage(imagePath, outputDirectory, filterMethod, filterName);
			}
		}

		sw.Stop();

		var timeFilePath = Path.Combine(Settings.OUTPUT_DIR, "time.txt");
		using (var writer = new StreamWriter(timeFilePath, true))
		{
			writer.WriteLine($"Time ({filterName}): {sw.ElapsedMilliseconds}");
		}
	}

	static void Main()
	{
		//Get all images from input directory
		var imagePaths = Directory.GetFiles(Settings.INPUT_DIR, "*.jpg");

		if (Settings.COMPACT_ONCE) GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
		if (Settings.LOW_LATENCY) GCSettings.LatencyMode = GCLatencyMode.LowLatency;
		if (Settings.SUSTAINED_LOW_LATENCY) GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

		// Select the appropriate filter method based on settings
		FilterMethod filterMethod;
		string filterName;

		if (Settings.VERSION_MANAGED)
		{
			if (Settings.USE_FIXED)
			{
				filterMethod = ApplyLaplaceFilterManagedFixed;
				filterName = "managed_fixed";
			}
			else
			{
				filterMethod = ApplyLaplaceFilterManaged;
				filterName = "managed";
			}
		}
		else
		{
			if (Settings.USE_POOLING)
			{
				filterMethod = ApplyLaplaceFilterUnmanagedPooled;
				filterName = "unmanaged_pooled";
			}
			else if (Settings.USE_FIXED)
			{
				filterMethod = ApplyLaplaceFilterUnmanagedFixed;
				filterName = "unmanaged_fixed";
			}
			else
			{
				filterMethod = ApplyLaplaceFilterUnmanaged;
				filterName = "unmanaged";
			}
		}

		// Process images with the selected filter method
		ProcessImages(imagePaths, Settings.OUTPUT_DIR, filterMethod, filterName);
	}
}