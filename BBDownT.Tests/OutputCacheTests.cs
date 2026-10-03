namespace BBDownT.Tests;

public class OutputCacheTests
{
    [Fact]
    public void RequiresAnExistingNonemptyArtifact()
    {
        using var files = new MediaTestDirectory();
        var path = files.FilePath("title.mp4");
        Assert.False(Program.IsUsableArtifact(path));
        File.WriteAllText(path, "");
        Assert.False(Program.IsUsableArtifact(path));
        File.WriteAllText(path, "completed media");
        Assert.True(Program.IsUsableArtifact(path));
    }

    [Fact]
    public void RawStreamModeDoesNotSkipBecauseAMuxedFileExists()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");
        var option = new MyOption { AudioOnly = true, VideoOnly = true };
        Program.HandleConflictingOptions(option);

        Assert.False(Program.ShouldUseMuxedOutputCache(option, path));
        Assert.True(Program.ShouldUseMuxedOutputCache(new(), path));
    }

    [Fact]
    public void MetadataRefreshOnlyAppliesToAnExistingMuxedArtifact()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");
        var missing = files.FilePath("missing.mp4");

        Assert.True(Program.ShouldRefreshExistingMetadata(new MyOption { MetadataOnly = true }, path));
        Assert.False(Program.ShouldRefreshExistingMetadata(new(), path));
        Assert.False(Program.ShouldRefreshExistingMetadata(new MyOption { MetadataOnly = true }, missing));
    }

    [Fact]
    public void MetadataRefreshDoesNotApplyWhenMuxingIsSkipped()
    {
        using var files = new MediaTestDirectory();
        var path = files.Write("title.mp4", "completed media");

        Assert.False(Program.ShouldRefreshExistingMetadata(new MyOption { MetadataOnly = true, SkipMux = true }, path));
    }
}
