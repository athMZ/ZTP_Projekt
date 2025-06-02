using System.Net.Http.Json;

namespace ImgProcessorClient;

public static class UrlShortener
{
	private static readonly HttpClient _http = new HttpClient();
	private static readonly string _apiKey = "17aac31a-e945-48d5-a5ab-cb0d10476519";

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

	private class ShlinkResponse
	{
		public string ShortUrl { get; set; }
	}
}