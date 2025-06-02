using System.Drawing;
using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ImgProcessorServer;

internal class Program
{
	static async Task Main()
	{
		const string queue = "image-queue";

		Console.WriteLine("Server starting...");

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
			var body = ea.Body.ToArray();
			var imageReference = Encoding.UTF8.GetString(body);

			Console.WriteLine($" [x] Received reference to {imageReference}");

			var imageUrl = "itGOesHere";

			Console.WriteLine($" [x] Saved to url: {imageUrl}");
		};

		await channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer);

		Console.WriteLine(" [x] Awaiting requests");
		Console.WriteLine(" Press [enter] to exit.");
		Console.ReadLine();
	}
}