using System;
using System.IO;
using Xunit;

namespace BBDownT.Tests;

public class RepoLayoutTests
{
    [Fact]
    public void FindRepoRoot_LocatesTheAncestorContainingTheProjectFile()
    {
        var repo = Path.Combine(Path.GetTempPath(), "bbrepo-" + Guid.NewGuid().ToString("N"));
        var deep = Path.Combine(repo, "a", "b", "c");
        Directory.CreateDirectory(deep);
        Directory.CreateDirectory(Path.Combine(repo, "BBDownT"));
        File.WriteAllText(Path.Combine(repo, "BBDownT", "BBDownT.csproj"), "<x/>");
        try
        {
            Assert.Equal(repo, RepoLayout.FindRepoRoot(deep));
        }
        finally { Directory.Delete(repo, true); }
    }

    [Fact]
    public void FindRepoRoot_ReturnsNull_WhenNoAncestorIsARepository()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbnotrepo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { Assert.Null(RepoLayout.FindRepoRoot(dir)); }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FindRepoRoot_ReturnsNull_ForTheFileSystemRoot()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        Assert.Null(RepoLayout.FindRepoRoot(root));
    }

    [Fact]
    public void IsRepositoryShape_RequiresTheExactRelativePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bbshape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(Path.Combine(dir, "BBDownT.Core"));
        File.WriteAllText(Path.Combine(dir, "BBDownT.sln"), ""); // 只有 sln 不算
        File.WriteAllText(Path.Combine(dir, "BBDownT.Core", "BBDownT.Core.csproj"), "");
        try
        {
            Assert.False(RepoLayout.IsRepositoryShape(dir));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsRepositoryShape_IsTrueForARealRepoLayout()
    {
        var repo = Path.Combine(Path.GetTempPath(), "bbreal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repo, "BBDownT"));
        File.WriteAllText(Path.Combine(repo, "BBDownT", "BBDownT.csproj"), "<x/>");
        try { Assert.True(RepoLayout.IsRepositoryShape(repo)); }
        finally { Directory.Delete(repo, true); }
    }

    [Fact]
    public void IsRepositoryShape_IsFalseForNullOrMissing()
    {
        Assert.False(RepoLayout.IsRepositoryShape(null));
        Assert.False(RepoLayout.IsRepositoryShape(""));
    }
}
