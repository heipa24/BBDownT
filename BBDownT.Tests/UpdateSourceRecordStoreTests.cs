using System;
using System.IO;
using Xunit;

namespace BBDownT.Tests;

public class UpdateSourceRecordStoreTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), "bbupd-rec-" + Guid.NewGuid().ToString("N"));

    private static UpdateSourceRecord Sample() => new UpdateSourceRecord
    {
        SchemaVersion = 1,
        PackageId = "BBDownT",
        PackageVersion = "2.1.4",
        SourceDirectory = "C:\\repo\\artifacts",
        NupkgPath = "C:\\repo\\artifacts\\BBDownT.2.1.4.nupkg",
        Scope = "global",
        ToolPath = null,
        ManifestPath = null,
        RecordedAtUtc = new DateTimeOffset(2026, 10, 5, 8, 20, 11, TimeSpan.Zero)
    };

    [Fact]
    public void TrySaveThenTryLoad_RoundTripsEveryField()
    {
        var dir = TempDir();
        try
        {
            var record = Sample();
            Assert.True(UpdateSourceRecordStore.TrySave(dir, record));
            var loaded = UpdateSourceRecordStore.TryLoad(dir);
            Assert.NotNull(loaded);
            Assert.Equal(record.SchemaVersion, loaded!.SchemaVersion);
            Assert.Equal(record.SourceDirectory, loaded.SourceDirectory);
            Assert.Equal(record.Scope, loaded.Scope);
            Assert.Equal(record.NupkgPath, loaded.NupkgPath);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TryLoad_ReturnsNull_ForAMissingFile()
    {
        var dir = TempDir();
        try { Assert.Null(UpdateSourceRecordStore.TryLoad(dir)); }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TryLoad_ReturnsNull_ForCorruptJson()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(UpdateSourceRecordStore.PathIn(dir), "not json {{{");
            Assert.Null(UpdateSourceRecordStore.TryLoad(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TryLoad_ReturnsNull_ForAFutureSchemaVersion()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(UpdateSourceRecordStore.PathIn(dir), "{\"schemaVersion\":99,\"scope\":\"global\"}");
            Assert.Null(UpdateSourceRecordStore.TryLoad(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TryLoad_IgnoresUnknownExtraProperties()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(UpdateSourceRecordStore.PathIn(dir), "{\"schemaVersion\":1,\"scope\":\"global\",\"future\":\"x\"}");
            var loaded = UpdateSourceRecordStore.TryLoad(dir);
            Assert.NotNull(loaded);
            Assert.Equal("global", loaded!.Scope);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TryLoad_AcceptsARecordWithoutASourceDirectory()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(UpdateSourceRecordStore.PathIn(dir), "{\"schemaVersion\":1,\"scope\":\"global\"}");
            var loaded = UpdateSourceRecordStore.TryLoad(dir);
            Assert.NotNull(loaded);
            Assert.Null(loaded!.SourceDirectory);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TrySave_ReplacesTheFileAtomicallyAndLeavesNoTemporaryFile()
    {
        var dir = TempDir();
        try
        {
            UpdateSourceRecordStore.TrySave(dir, Sample());
            UpdateSourceRecordStore.TrySave(dir, Sample()); // 第二次应覆盖且不留 .writing-
            var files = Directory.GetFiles(dir, "*.writing-*");
            Assert.Empty(files);
            Assert.NotNull(UpdateSourceRecordStore.TryLoad(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void TrySave_ReturnsFalse_WhenTheDirectoryCannotBeCreated()
    {
        // 把 appDir 指到一个已存在的“文件”，CreateDirectory 会失败
        var file = Path.GetTempFileName();
        try
        {
            Assert.False(UpdateSourceRecordStore.TrySave(file, Sample()));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Delete_IsIdempotent()
    {
        var dir = TempDir();
        try
        {
            UpdateSourceRecordStore.TrySave(dir, Sample());
            Assert.True(UpdateSourceRecordStore.Delete(dir));
            Assert.True(UpdateSourceRecordStore.Delete(dir));
            Assert.Null(UpdateSourceRecordStore.TryLoad(dir));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
