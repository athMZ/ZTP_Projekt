using System.Buffers;
using System.Drawing;

namespace ImgProcessorServer;

internal class ImageProcessor
{
	private static readonly int[,] LaplaceKernel = new[,]
	{
		{ 0, 0, -1, 0, 0 },
		{ 0, -1, -2, -1, 0 },
		{ -1, -2, 16, -2, -1 },
		{ 0, -1, -2, -1, 0 },
		{ 0, 0, -1, 0, 0 }
	};

	public static Bitmap ApplyLaplaceFilterManaged(Bitmap bitmap)
	{
		Bitmap result = new Bitmap(bitmap.Width, bitmap.Height);
		int width = bitmap.Width;
		int height = bitmap.Height;

		// Allocate byte arrays on the heap to store image data
		// Each pixel needs 3 bytes (R,G,B)
		var inputPixels = ArrayPool<byte>.Shared.Rent(height * width * 3);
		var outputPixels = ArrayPool<byte>.Shared.Rent(height * width * 3);

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
			ArrayPool<byte>.Shared.Return(inputPixels);
		}
	}

}