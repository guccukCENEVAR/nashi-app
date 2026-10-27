using System.Reflection;
using System.Text.Json;
using Nashi.Core;
namespace Nashi.Linux;

internal sealed record LinuxUpdate(string Version,string Url);

/// Linux copies are installed and replaced by the package manager, so Nashi only
/// announces a newer public release; it never downloads or replaces its own files.
internal static class Updates
{
    internal const string Releases="https://api.github.com/repos/"+PublicUpdates.Repository+"/releases/latest";
    internal static Version Current=>PublicUpdates.Normalize(Assembly.GetExecutingAssembly().GetName().Version??new Version(0,0));
    internal static async Task<LinuxUpdate?> CheckAsync(Version current,CancellationToken token=default)
    {
        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(30)};client.DefaultRequestHeaders.UserAgent.ParseAdd("Nashi/"+current);client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var response=await client.GetAsync(Releases,HttpCompletionOption.ResponseHeadersRead,token);
        if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return null;response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,token))>0){if(memory.Length+count>1_000_000)throw new InvalidDataException("Release information is too large.");memory.Write(buffer,0,count);}
        return Parse(memory.ToArray(),current);
    }
    internal static LinuxUpdate? Parse(byte[] json,Version current)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        var tag=root.TryGetProperty("tag_name",out var name)?name.GetString()??"":"";var page=root.TryGetProperty("html_url",out var url)?url.GetString()??"":"";
        // Tags look like v0.7.10-41: the version precedes the build number.
        var text=tag.TrimStart('v','V').Split('-')[0];
        if(!Version.TryParse(text,out var version))throw new InvalidDataException("Invalid release version.");
        if(!Uri.TryCreate(page,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/"+PublicUpdates.Repository+"/releases/",StringComparison.Ordinal))throw new InvalidDataException("Invalid release location.");
        return PublicUpdates.Normalize(version)>PublicUpdates.Normalize(current)?new(version.ToString(),uri.AbsoluteUri):null;
    }
}
