// Fails when any package version pinned in Directory.Packages.props was published on
// nuget.org less than 24 hours ago, or cannot be found there.
// Run from the repo root: dotnet run scripts/check-package-age.cs

// File-based apps default to Native AOT, which restores compiler packages; this script needs none.
#:property PublishAot=false

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml.Linq;

var minimumAge = TimeSpan.FromHours(24);
var propsPath = args.Length > 0 ? args[0] : "Directory.Packages.props";

if (!File.Exists(propsPath))
{
	Console.Error.WriteLine($"Not found: {propsPath}. Run from the repo root.");
	return 2;
}

var pins = XDocument.Load(propsPath)
	.Descendants("PackageVersion")
	.Select(e => (Id: (string?)e.Attribute("Include"), Version: (string?)e.Attribute("Version")))
	.ToList();

if (pins.Any(p => string.IsNullOrWhiteSpace(p.Id) || string.IsNullOrWhiteSpace(p.Version)))
{
	Console.Error.WriteLine("Every PackageVersion needs an Include and an exact Version.");
	return 2;
}

if (pins.Count == 0)
{
	Console.WriteLine("No pinned packages.");
	return 0;
}

using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All });
var now = DateTimeOffset.UtcNow;
var failures = 0;

foreach (var (id, version) in pins)
{
	// Registration leaf for one version; its "published" field is the nuget.org publish time.
	var url = new Uri($"https://api.nuget.org/v3/registration5-gz-semver2/{id!.ToLowerInvariant()}/{version!.ToLowerInvariant()}.json");
	using var response = await http.GetAsync(url);
	if (!response.IsSuccessStatusCode)
	{
		Console.Error.WriteLine($"FAIL {id} {version}: not found on nuget.org ({(int)response.StatusCode}).");
		failures++;
		continue;
	}

	await using var stream = await response.Content.ReadAsStreamAsync();
	using var leaf = await JsonDocument.ParseAsync(stream);
	if (!leaf.RootElement.TryGetProperty("published", out var publishedElement)
		|| !DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published))
	{
		Console.Error.WriteLine($"FAIL {id} {version}: no publish time in the registration leaf.");
		failures++;
		continue;
	}

	var age = now - published;
	var stamp = published.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
	if (age < minimumAge)
	{
		Console.Error.WriteLine($"FAIL {id} {version}: published {stamp} UTC, {age.TotalHours:F1} hours ago; the minimum is {minimumAge.TotalHours:F0}.");
		failures++;
	}
	else
	{
		Console.WriteLine($"ok   {id} {version}: published {stamp} UTC.");
	}
}

return failures == 0 ? 0 : 1;
