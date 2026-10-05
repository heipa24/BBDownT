using System;
using System.IO;
using System.IO.Compression;
using Xunit;

namespace BBDownT.Tests;

public class NupkgInspectorTests
{
    private static string MakeNupkg(string dir, string fileName, string version)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("BBDownT.nuspec");
        using var w = new StreamWriter(entry.Open());
        w.Write($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                $"<package xmlns=\"http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd\">\n" +
                $"  <metadata><id>BBDownT</id><version>{version}</version></metadata>\n" +
                $"</package>");
        return path;
    }

    [Fact]
    public void ReadPackageVersion_ReadsTheVersionFromTheRootNuspec()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        var path = MakeNupkg(dir, "BBDownT.2.1.4.nupkg", "2.1.4");
        try
        {
            Assert.Equal(new Version(2, 1, 4), NupkgInspector.ReadPackageVersion(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadPackageVersion_IgnoresThePackageNamespace()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "BBDownT.2.2.0.nupkg");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("BBDownT.nuspec");
            using var w = new StreamWriter(entry.Open());
            w.Write("<package xmlns=\"urn:whatever\"><metadata><id>BBDownT</id><version>2.2.0</version></metadata></package>");
        }
        try
        {
            Assert.Equal(new Version(2, 2, 0), NupkgInspector.ReadPackageVersion(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadPackageVersion_RejectsAZipWithoutANuspec()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "BBDownT.2.1.4.nupkg");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt").Open().Dispose();
        }
        try
        {
            Assert.Throws<InvalidDataException>(() => NupkgInspector.ReadPackageVersion(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadPackageVersion_RejectsAnNuspecWithoutAVersionElement()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "BBDownT.2.1.4.nupkg");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("BBDownT.nuspec");
            using var w = new StreamWriter(entry.Open());
            w.Write("<package><metadata><id>BBDownT</id></metadata></package>");
        }
        try
        {
            Assert.Throws<InvalidDataException>(() => NupkgInspector.ReadPackageVersion(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ReadPackageVersion_RejectsAnUnparsableVersion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        var path = MakeNupkg(dir, "BBDownT.2.1.4.nupkg", "latest");
        try { Assert.Throws<InvalidDataException>(() => NupkgInspector.ReadPackageVersion(path)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindCandidateNupkg_PicksTheHighestVersion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        MakeNupkg(dir, "BBDownT.2.1.4.nupkg", "2.1.4");
        MakeNupkg(dir, "BBDownT.2.1.10.nupkg", "2.1.10");
        MakeNupkg(dir, "BBDownT.2.1.9.nupkg", "2.1.9");
        try
        {
            var picked = NupkgInspector.FindCandidateNupkg(dir);
            Assert.NotNull(picked);
            Assert.EndsWith("BBDownT.2.1.10.nupkg", picked);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindCandidateNupkg_IgnoresOtherPackageIds()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnup-" + Guid.NewGuid().ToString("N"));
        MakeNupkg(dir, "Foo.1.0.0.nupkg", "1.0.0");
        try { Assert.Null(NupkgInspector.FindCandidateNupkg(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindCandidateNupkg_ReturnsNull_ForAMissingDirectory()
    {
        Assert.Null(NupkgInspector.FindCandidateNupkg(Path.Combine(Path.GetTempPath(), "bbnup-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
