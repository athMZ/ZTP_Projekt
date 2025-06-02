namespace ImgProcessorClient;

internal class Program
{
	static async Task Main()
	{
		Console.WriteLine("Client starting...");

		Console.WriteLine("Works with .jpg files.");
		Console.WriteLine("Enter the path to the image file to upload. You can also enter a directory with many images.");
		var inputPath = Console.ReadLine();

		if (string.IsNullOrEmpty(inputPath) || (!File.Exists(inputPath) && !Directory.Exists(inputPath)))
		{
			Console.WriteLine("Invalid path.");
			return;
		}

		if (Directory.Exists(inputPath))
		{
			var files = Directory.GetFiles(inputPath, "*.*", SearchOption.TopDirectoryOnly)
				.Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
			foreach (var file in files)
			{
				await ImageProcessingClient.UploadImageAndSendTask(file);
			}
		}
		else
		{
			await ImageProcessingClient.UploadImageAndSendTask(inputPath);
		}

		Console.WriteLine("Press any key to exit.");
		Console.ReadLine();
	}
}