using System.Text;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;

namespace ImgProcessorClient;

public class ImageProcessingClient
{
	public static async Task UploadImageAndSendTask(string imagePath)
	{
		try
		{
			var minioStoredImageReference = await UploadImage(imagePath);
			await SendTask(minioStoredImageReference);
		}
		catch (Exception e)
		{
			Console.WriteLine($"Error processing image: {e.Message}");
		}
	}

	public static async Task<string> UploadImage(string imagePath)
	{
		var minioClient = new MinioClient()
			.WithEndpoint("localhost:9000")
			.WithCredentials("admin", "admin123")
			.Build();

		const string bucketName = "tmp-images";
		bool bucketExists = await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
		if (!bucketExists)
		{
			await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
		}

		var objectName = Path.GetFileName(imagePath);
		objectName = $"{Guid.NewGuid()}-{objectName}";

		await minioClient.PutObjectAsync(new PutObjectArgs()
			.WithBucket(bucketName)
			.WithObject(objectName)
			.WithFileName(imagePath));

		Console.WriteLine($"Uploaded {objectName} to bucket {bucketName}.");

		return $"{bucketName}/{objectName}";
	}

	public static async Task SendTask(string imageReference)
	{
		const string queue = "image-queue";

		var factory = new ConnectionFactory
		{
			HostName = "localhost",
			UserName = "admin",
			Password = "admin",
		};

		var connection = await factory.CreateConnectionAsync();
		var channel = await connection.CreateChannelAsync();

		await channel.QueueDeclareAsync(queue, durable: false, exclusive: false, autoDelete: false, arguments: null);

		var messageBody = Encoding.UTF8.GetBytes(imageReference);
		var props = new BasicProperties
		{
			ContentType = "text/plain"
		};

		await channel.BasicPublishAsync(exchange: "", routingKey: queue, false, props, messageBody);

		Console.WriteLine($" [x] Sent reference to {imageReference}");
		await channel.CloseAsync();
		await connection.CloseAsync();
	}
}

//D:\ZTP-Imgs\in\a7.jpg

internal class Program
{
	static async Task Main()
	{
		Console.WriteLine("Client starting...");

		Console.WriteLine("Enter the path to the image file to upload:");
		var filePath = Console.ReadLine();

		if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
		{
			Console.WriteLine("Invalid file path.");
			return;
		}

		await ImageProcessingClient.UploadImageAndSendTask(filePath);
	}
}