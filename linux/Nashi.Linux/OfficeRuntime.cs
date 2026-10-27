using Nashi.Core;
namespace Nashi.Linux;

/// LibreOffice from the package manager (libreoffice-fresh or libreoffice-still on Arch).
/// A private copy under Nashi's tools folder wins, for users who unpack an AppImage there.
internal sealed class OfficeRuntime(string root)
{
    public string? Executable=>new[]{Path.Combine(root,"office","program","soffice")}.FirstOrDefault(SystemTools.IsExecutable)
        ??SystemTools.Find("soffice")??SystemTools.Find("libreoffice");
}
