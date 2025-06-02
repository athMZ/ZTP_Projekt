using System.Drawing;
using System.Text;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImgProcessorServer;

internal class Program
{
	static async Task Main()
	{
		const string queue = "image-queue";

		Console.WriteLine("Server starting...");

		var minioClient = new MinioClient()
			.WithEndpoint("localhost:9000")
			.WithCredentials("admin", "admin123")
			.Build();

		var factory = new ConnectionFactory
		{
			HostName = "localhost",
			UserName = "admin",
			Password = "admin",
		};

		var connection = await factory.CreateConnectionAsync();
		var channel = await connection.CreateChannelAsync();

		await channel.QueueDeclareAsync(queue, durable: false, exclusive: false, autoDelete: false, arguments: null);

		var consumer = new AsyncEventingBasicConsumer(channel);
		consumer.ReceivedAsync += async (model, ea) =>
		{

			try
			{
				var body = ea.Body.ToArray();
				var imageReference = Encoding.UTF8.GetString(body);

				Console.WriteLine($" [x] Received reference to {imageReference}");

				var parts = imageReference.Split('/', 2);
				var bucketName = parts[0];
				var objectName = parts.Length > 1 ? parts[1] : string.Empty;

				using var memoryStream = new MemoryStream();
				await minioClient.GetObjectAsync(new GetObjectArgs()
					.WithBucket(bucketName)
					.WithObject(objectName)
					.WithCallbackStream(stream => stream.CopyTo(memoryStream)));

				memoryStream.Seek(0, SeekOrigin.Begin);
				var srcImage = new Bitmap(memoryStream);

				var resultImage = ImageProcessor.ApplyLaplaceFilter(srcImage);

				using var outputStream = new MemoryStream();
				resultImage.Save(outputStream, System.Drawing.Imaging.ImageFormat.Jpeg);
				outputStream.Seek(0, SeekOrigin.Begin);

				await minioClient.PutObjectAsync(new PutObjectArgs()
					.WithBucket(bucketName)
					.WithObject(objectName)
					.WithStreamData(outputStream)
					.WithObjectSize(outputStream.Length)
					.WithContentType("image/png"));

				await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error processing message: {ex.Message}");
				await channel.BasicNackAsync(deliveryTag: ea.DeliveryTag, multiple: false, requeue: false);
			}

		};

		await channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer);

		Console.WriteLine(" [x] Awaiting requests");
		Console.WriteLine(" Press [enter] to exit.");
		Console.ReadLine();
	}
}