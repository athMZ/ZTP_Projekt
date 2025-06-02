using System.Drawing;
using Minio;
using Minio.DataModel.Args;

namespace ImgProcessorServer;

internal class MinoHandler
{
	private readonly string _endpoint;
	private readonly string _accessKey;
	private readonly string _secretKey;

	private readonly IMinioClient _minioClient;

	public MinoHandler()
	{
		//In a real application, these values should be stored in a secure configuration file or environment variables.
		this._endpoint = "localhost:9000";
		this._accessKey = "admin";
		this._secretKey = "admin123";

		this._minioClient = new MinioClient()
			.WithEndpoint(this._endpoint)
			.WithCredentials(this._accessKey, this._secretKey)
			.Build();
	}

	public async Task<Bitmap> ConsumeBitmap(string bucketName, string objectName)
	{
		using var memoryStream = new MemoryStream();
		await this._minioClient.GetObjectAsync(new GetObjectArgs()
			.WithBucket(bucketName)
			.WithObject(objectName)
			.WithCallbackStream(stream => stream.CopyTo(memoryStream)));

		memoryStream.Seek(0, SeekOrigin.Begin);
		return new Bitmap(memoryStream);
	}

	public async Task UploadBitmap(Bitmap image, string bucketName,string objectName)
	{
		using var outputStream = new MemoryStream();
		image.Save(outputStream, System.Drawing.Imaging.ImageFormat.Png);
		outputStream.Seek(0, SeekOrigin.Begin);

		await this._minioClient.PutObjectAsync(new PutObjectArgs()
			.WithBucket(bucketName)
			.WithObject(objectName)
			.WithStreamData(outputStream)
			.WithObjectSize(outputStream.Length)
			.WithContentType("image/png"));
	}

	public async Task<string> GetDownloadUrl(string bucketName, string objectName)
	{
		var url = await this._minioClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
			.WithBucket(bucketName)
			.WithObject(objectName)
			.WithExpiry(12 * 60 * 60)); // 12 hours

		return url;
	}
}