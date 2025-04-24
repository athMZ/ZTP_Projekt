using System.Text;
using Minio;
using Minio.DataModel.Args;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImgProcessorServer;

internal class Program
{
	static async Task Main(string[] args)
	{
		try
		{
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

				// Download the image
				var localFilePath = Path.Combine("downloads", objectName);
				Directory.CreateDirectory("downloads");
				await minioClient.GetObjectAsync(new GetObjectArgs()
					.WithBucket(bucketName)
					.WithObject(objectName)
					.WithFile(localFilePath));

				Console.WriteLine($"Downloaded {objectName} to {localFilePath}.");

				// Process the image (placeholder for actual processing logic)
				Console.WriteLine($"Processing {localFilePath}...");
			};

			// Start consuming messages from the queue
			await channel.BasicConsumeAsync("image-queue", autoAck: true, consumer: consumer);

			Console.WriteLine(" Press [enter] to exit.");
			Console.ReadLine();
		}
		catch (Exception e)
		{
			Console.WriteLine(e.ToString());
		}
	}
}