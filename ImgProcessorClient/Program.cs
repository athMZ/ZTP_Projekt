using System.Text;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImgProcessorClient;

internal class Program
{
	static async Task Main(string[] args)
	{
		Console.WriteLine("Client starting...");

		// MinIO configuration
		var minioClient = new MinioClient()
			.WithEndpoint("localhost:9000")
			.WithCredentials("admin", "admin123")
			.Build();

		// Ensure the bucket exists
		const string bucketName = "tmp-images";
		bool bucketExists = await minioClient.BucketExistsAsync(new BucketExistsArgs().WithBucket(bucketName));
		if (!bucketExists)
		{
			await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));
		}

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

		// Upload an image and send a message
		Console.WriteLine("Enter the path to the image file to upload:");
		var filePath = Console.ReadLine();
		if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
		{
			Console.WriteLine("Invalid file path.");
			return;
		}

		var objectName = Path.GetFileName(filePath);
		await minioClient.PutObjectAsync(new PutObjectArgs()
			.WithBucket(bucketName)
			.WithObject(objectName)
			.WithFileName(filePath));

		Console.WriteLine($"Uploaded {objectName} to bucket {bucketName}.");

		// Send a message with the image reference
		var message = $"{bucketName}/{objectName}";
		var body = Encoding.UTF8.GetBytes(message);

		await channel.BasicPublishAsync(exchange: string.Empty, routingKey: "image-queue", body: body);
		Console.WriteLine($" [x] Sent reference to {message}");

	}
}