# Sprawozdanie z Laboratorium: System Rozproszonego Przetwarzania Obrazów z Wykorzystaniem GPU, RabbitMQ oraz Architektury Klient-Serwer

## 1. Wstęp i założenia projektowe

Celem drugiej części projektu było zaprojektowanie i implementacja systemu rozproszonego, w którym klient i serwer komunikują się za pomocą wybranej technologii rozproszonej, natomiast operacje obliczeniowe realizowane są z wykorzystaniem układów GPU. System powinien być podzielony na część kliencką oraz serwerową, gdzie:

- **Część klienta** pobiera dane od użytkownika, wysyła zlecenie do serwera oraz prezentuje link pozwalający na pobranie obrazu.
- **Część serwera** odbiera zlecenie, wykonuje wskazane obliczenia na GPU i zwraca wynik.

Jako technologię komunikacji rozproszonej zastosowano **RabbitMQ**. Dodatkowo, zamiast przesyłać obrazy bezpośrednio, zaprojektowano architekturę opartą o **MinIO** (storage S3-kompatybilny). Wykorzystano usługę skracania URL (Shlink), co nadało projektowi charakter bliższy rozwiązaniom produkcyjnym.

Wymóg użycia CUDA został zastąpiony przez **OpenCL** ze względu na brak sprzętu NVIDIA — OpenCL zapewnia analogiczną funkcjonalność, umożliwiając uruchamianie obliczeń na dowolnych popularnych układach GPU.

### Uwaga - Bezpieczeństwo implementacji
Niniejszy projekt ma charakter edukacyjny i demonstracyjny. Implementacja zawiera elementy, które z punktu widzenia bezpieczeństwa nie powinny być stosowane w środowisku produkcyjnym:

- W kodzie występują jawne dane logowania do serwisów (np. admin/admin123 dla MinIO, admin/admin dla RabbitMQ)
- Statyczne adresy i porty - wykorzystanie adresów localhost z określonymi portami (9000, 8080, 5672, 15672)
- Brak szyfrowania komunikacji - połączenia nie wykorzystują TLS/SSL
- Jawne klucze API - klucze dostępu do usług nie są zarządzane w bezpieczny sposób
- Uproszczone mechanizmy obsługi błędów - brak kompleksowej strategii dla scenariuszy awaryjnych

W środowisku produkcyjnym powyższe elementy powinny zostać zastąpione przez:

- Zarządzanie sekretami (np. Azure KeyVault, HashiCorp Vault)
- Zmienne środowiskowe lub zewnętrzne pliki konfiguracyjne
- Szyfrowaną komunikację (TLS/SSL)
- Zaawansowane mechanizmy uwierzytelniania i autoryzacji
- Kompleksowy monitoring i obsługę błędów

---

## 2. Architektura rozwiązania

System składa się z następujących komponentów:
- **ImgProcessorClient** (klient) – aplikacja C#, umożliwiająca użytkownikowi wybór obrazów i określenie typu przekształcenia, wysyłająca zlecenie do serwera oraz prezentująca wynik. Aplikacja działa w konsoli i pozwala na podanie ścieżki do obrazu lub katalogu zawierającego wiele obrazów.
- **ImgProcessorServer** (serwer) – aplikacja C#, odbierająca zlecenia poprzez RabbitMQ, pobierająca obraz z MinIO, wykonująca zadane przekształcenie na GPU (OpenCL), zapisująca przetworzony obraz.

- **RabbitMQ** – broker komunikatów (kolejka zadań), zapewniający asynchroniczną wymianę komunikatów między klientem a serwerem.
- **MinIO** – magazyn plików (obrazy wejściowe/wyjściowe), dostępny przez API S3.
- **Shlink** – usługa skracania linków, wykorzystywana do generowania czytelnych adresów URL do plików.

---

## 3. Szczegółowy opis działania RabbitMQ

RabbitMQ stanowi centralny komponent komunikacyjny systemu. Dzięki niemu osiągnięto asynchroniczność oraz skalowalność — klient może przesyłać wiele zleceń, które serwer odbiera i przetwarza w miarę dostępności zasobów.

**Przebieg komunikacji:**
1. Klient, po przesłaniu obrazu do MinIO i wygenerowaniu linku, umieszcza w kolejce `image-queue` wiadomość zawierającą referencję do obrazu.
2. Serwer, działający jako konsument kolejki, odbiera zadania. Po wykonaniu przetwarzania umieszcza wynikowy obraz w MinIO.
3. Klient otrzymuje skrócony adres URL pozwalający na pobranie obrazu.

**Dodatkowe korzyści**:
- Możliwość równoległego wysyłania zleceń do wielu instancji serwera (skalowanie poziome).
- Buforowanie zadań w przypadku chwilowej niedostępności serwera.
- Odporność na błędy komunikacyjne.

**Metoda odpowiedzialna za upload podanego obrazu do MinIo:**
```csharp
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

		var imageUrl = await minioClient.PresignedGetObjectAsync(new PresignedGetObjectArgs()
			.WithBucket(bucketName)
			.WithObject(objectName)
			.WithExpiry(12 * 60 * 60)); // 12 hours

		var shortenedUrl = await UrlShortener.ShortenAsync(imageUrl);

		Console.WriteLine($"Download URL: {shortenedUrl}");
		Console.WriteLine($"Checkout the provided URL to download the processed file.");

		return $"{bucketName}/{objectName}";
	}
```

**Metoda odpowiedzialna za wysłanie zadania na kolejkę RabbitMq**
```csharp
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
```

**Metoda odpowiedzialna za przygotowanie krótkiego linku**
```csharp
	public static async Task<string> ShortenAsync(string longUrl, string shlinkHost = "http://localhost:8080")
	{
		var requestBody = new
		{
			longUrl = longUrl
		};

		var request = new HttpRequestMessage(HttpMethod.Post, $"{shlinkHost}/rest/v3/short-urls")
		{
			Content = JsonContent.Create(requestBody)
		};

		request.Headers.Add("X-Api-Key", _apiKey);

		var response = await _http.SendAsync(request);
		response.EnsureSuccessStatusCode();

		var json = await response.Content.ReadFromJsonAsync<ShlinkResponse>();
		return json.ShortUrl;
	}
```

---

## 4. Przetwarzanie obrazów na GPU (OpenCL)

Centralnym elementem serwera jest przetwarzanie obrazów z użyciem GPU. W projekcie zaimplementowano filtr Laplace’a, wykorzystując bibliotekę OpenCL. Proces polega na:

1. Odbiorze zadania przez serwer z kolejki RabbitMQ.
2. Pobranie wskazanego obrazu z MinIO.
3. Przygotowanie danych wejściowych, przesłanie ich do pamięci GPU.
4. Uruchomienie kernela OpenCL realizującego filtr Laplace’a.
5. Odczyt wyniku z GPU i konwersja do formatu obrazu.
6. Zapisanie przetworzonego obrazu do MinIO.

Dzięki zastosowaniu OpenCL, rozwiązanie jest niezależne od konkretnego producenta sprzętu i może być uruchamiane na różnych platformach.

**Metoda wykorzystująca OpenCl pozwalająca na wykonanie modyfikacji obrazu z wykorzystaniem GPU**
```cpp
__kernel void laplace_filter(__global uchar* input, __global uchar* output, int width, int height, int stride)
{
	int x = get_global_id(0);
	int y = get_global_id(1);

	if (x <= 0 || x >= width - 1 || y <= 0 || y >= height - 1)
		return;

	int idx = y * stride + x * 4;
	for (int c = 0; c < 3; c++) {
		int center = input[idx + c] * -4;
		int left   = input[idx + c - 4];
		int right  = input[idx + c + 4];
		int top    = input[idx + c - stride];
		int bottom = input[idx + c + stride];
		int value = clamp(center + left + right + top + bottom, 0, 255);
		output[idx + c] = (uchar)value;
	}
	output[idx + 3] = 255;
}
```

**Metoda pozwalająca na wywołanie OpenCl z poziomu aplikacji C#**
```csharp
   public static Bitmap ApplyLaplaceFilter(Bitmap input)
    {
        // Convert input bitmap to byte array (32bpp ARGB)
        int width = input.Width;
        int height = input.Height;
        BitmapData inputData = input.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int byteSize = inputData.Stride * height;
        byte[] inputBytes = new byte[byteSize];
        Marshal.Copy(inputData.Scan0, inputBytes, 0, byteSize);
        input.UnlockBits(inputData);

        // OpenCL setup
        ErrorCode error;
        Platform[] platforms = Cl.GetPlatformIDs(out error);
        Device[] devices = Cl.GetDeviceIDs(platforms[0], DeviceType.Gpu, out error);
        Context context = Cl.CreateContext(null, 1, devices, null, IntPtr.Zero, out error);
        CommandQueue queue = Cl.CreateCommandQueue(context, devices[0], (CommandQueueProperties)0, out error);

        string kernelSource = File.ReadAllText("laplace_filter.cl");

        Program program = Cl.CreateProgramWithSource(context, 1, new[] { kernelSource }, null, out error);
        error = Cl.BuildProgram(program, 1, devices, string.Empty, null, IntPtr.Zero);

        Kernel kernel = Cl.CreateKernel(program, "laplace_filter", out error);

		// Buffers
        IMem inputBuffer = Cl.CreateBuffer(context, MemFlags.ReadOnly | MemFlags.CopyHostPtr, (IntPtr)byteSize, inputBytes, out error);
        IMem outputBuffer = Cl.CreateBuffer(context, MemFlags.WriteOnly, (IntPtr)byteSize, out error);

        // Set kernel args
        Cl.SetKernelArg(kernel, 0, inputBuffer);
        Cl.SetKernelArg(kernel, 1, outputBuffer);
        Cl.SetKernelArg(kernel, 2, width);
        Cl.SetKernelArg(kernel, 3, height);
        Cl.SetKernelArg(kernel, 4, inputData.Stride);

		// Execute kernel
        Event clevent;
        IntPtr[] globalWorkSize = new IntPtr[] { (IntPtr)width, (IntPtr)height };
        error = Cl.EnqueueNDRangeKernel(queue, kernel, 2, null, globalWorkSize, null, 0, null, out clevent);
        Cl.Finish(queue);

        // Read result
        byte[] outputBytes = new byte[byteSize];
        Cl.EnqueueReadBuffer(queue, outputBuffer, Bool.True, IntPtr.Zero, (IntPtr)byteSize, outputBytes, 0, null, out _);

        // Convert back to bitmap
        Bitmap output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        BitmapData outputData = output.LockBits(new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(outputBytes, 0, outputData.Scan0, byteSize);
        output.UnlockBits(outputData);

        // Cleanup
        Cl.ReleaseKernel(kernel);
        Cl.ReleaseProgram(program);
        Cl.ReleaseMemObject(inputBuffer);
        Cl.ReleaseMemObject(outputBuffer);
        Cl.ReleaseCommandQueue(queue);
        Cl.ReleaseContext(context);

        return output;
    }
```

**Metoda służąca do przetworzenia komunikatów odebranych z kolejki**
```csharp
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
```

---

## 5. Obsługa wyników i prezentacja dla użytkownika

Po zakończeniu przetwarzania obraz wynikowy zostaje umieszczony w systemie MinIO, a następnie generowany jest do niego skrócony link (za pomocą usługi Shlink). Klient, wykorzystując ten link, umożliwia użytkownikowi pobranie i wyświetlenie przetworzonego obrazu.

![Podgląd konsoli Klient i Serwer - przykład przetwarzania obrazu](path/to/image.jpg "Przykład przetwarzania obrazu")

![Przykładowy obraz przetworzony przez OpenCL](path/to/image.jpg "Przykładowy obraz przetworzony przez OpenCL")

---

## 6. Wykorzystanie Docker do obsługi infrastruktury

W ramach projektu wykorzystano technologię Docker do uruchomienia i zarządzania kluczowymi komponentami infrastruktury. Dzięki konteneryzacji udało się zapewnić jednolite środowisko, łatwą konfigurację oraz izolację poszczególnych usług.

Broker komunikatów RabbitMQ został uruchomiony jako kontener z udostępnionymi portami 5672 (AMQP) i 15672 (panel administracyjny), co umożliwiło zarówno klientowi, jak i serwerowi bezproblemową komunikację przy wykorzystaniu domyślnej konfiguracji hosta localhost.

Podobnie, magazyn plików MinIO działa w kontenerze z wyeksponowanym portem 9000, co pozwoliło na łatwą integrację i zarządzanie obiektami bez konieczności lokalnej instalacji.

Usługa Shlink, odpowiedzialna za skracanie linków do przetworzonych obrazów, również została uruchomiona jako kontener Docker z udostępnionym portem 8080, co zapewniło spójne API do generowania krótkich adresów URL.

Takie podejście znacząco uprościło proces konfiguracji środowiska oraz zwiększyło przenośność całego rozwiązania.