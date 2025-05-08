using System.Buffers;
using System.Drawing;
using System.Text;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImgProcessorServer;

internal class Program
{
	private static readonly int[,] LaplaceKernel = new[,]
	{
		{ 0, 0, -1, 0, 0 },
		{ 0, -1, -2, -1, 0 },
		{ -1, -2, 16, -2, -1 },
		{ 0, -1, -2, -1, 0 },
		{ 0, 0, -1, 0, 0 }
	};

	static Bitmap ApplyLaplaceFilterManaged(Bitmap bitmap)
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


	static async Task Main(string[] args)
	{
		/*		try
				{*/
		Console.WriteLine("Server starting...");

		// MinIO configuration
		var minioClient = new MinioClient()
			.WithEndpoint("localhost:9000")
			.WithCredentials("admin", "admin123")
			.Build();

		// Create a connection to the RabbitMQ server
		var factory = new ConnectionFactory
		{
			HostName = "localhost",
			UserName = "admin",
			Password = "admin",
		};
		using var connection = await factory.CreateConnectionAsync();
		using var channel = await connection.CreateChannelAsync();

		// Declare a queue to send messages to
		await channel.QueueDeclareAsync(queue: "image-queue", durable: false, exclusive: false, autoDelete: false,
			arguments: null);

		Console.WriteLine(" [*] Waiting for messages.");

		// Create a consumer to receive messages
		var consumer = new AsyncEventingBasicConsumer(channel);
		consumer.ReceivedAsync += async (model, ea) =>
		{
			var body = ea.Body.ToArray();
			var message = Encoding.UTF8.GetString(body);
			Console.WriteLine($" [x] Received {message}");

			// Parse the bucket and object name
			var parts = message.Split('/');
			if (parts.Length != 2)
			{
				Console.WriteLine("Invalid message format.");
				return;
			}

			var bucketName = parts[0];
			var objectName = parts[1];

			using var memoryStream = new MemoryStream();
			await minioClient.GetObjectAsync(new GetObjectArgs()
				.WithBucket(bucketName)
				.WithObject(objectName)
				.WithCallbackStream(stream => stream.CopyTo(memoryStream)));

			memoryStream.Seek(0, SeekOrigin.Begin);

			using var bitmap = new Bitmap(memoryStream);
			using var processedImage = ApplyLaplaceFilterManaged(bitmap);
			using var outputStream = new MemoryStream();
			processedImage.Save(outputStream, System.Drawing.Imaging.ImageFormat.Png); // Save the processed image to the stream
			outputStream.Seek(0, SeekOrigin.Begin); // Reset the stream position to the beginning

			var processedObjectName = $"processed-{objectName}";

			await minioClient.PutObjectAsync(new PutObjectArgs()
				.WithBucket(bucketName)
				.WithObject(processedObjectName)
				.WithStreamData(outputStream)
				.WithObjectSize(outputStream.Length) // Use the actual size of the stream
				.WithContentType("image/png"));

			Console.WriteLine($"Processed image saved as {processedObjectName} in bucket {bucketName}.");
		};

		// Start consuming messages from the queue
		await channel.BasicConsumeAsync("image-queue", autoAck: true, consumer: consumer);

		Console.WriteLine(" Press [enter] to exit.");
		Console.ReadLine();
		/*		}
				catch (Exception e)
				{
					Console.WriteLine(e.ToString());
				}*/
	}
}