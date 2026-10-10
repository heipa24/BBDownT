using System.CommandLine;

namespace BBDownT.Tests;

public class CommandLineInvokerTests
{
    [Fact]
    public async Task SimplyMuxOption_IsRegisteredAndBound()
    {
        MyOption? boundOption = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(option =>
        {
            boundOption = option;
            return Task.CompletedTask;
        });

        var exitCode = await rootCommand.InvokeAsync(["BV1xx411c7mD", "--simply-mux"]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(boundOption);
        Assert.True(boundOption.SimplyMux);
    }

    [Fact]
    public async Task MetadataOnlyOption_IsRegisteredAndBound()
    {
        MyOption? boundOption = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(option =>
        {
            boundOption = option;
            return Task.CompletedTask;
        });

        var exitCode = await rootCommand.InvokeAsync(["BV1xx411c7mD", "--metadata-only"]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(boundOption);
        Assert.True(boundOption.MetadataOnly);
    }

    [Fact]
    public async Task MetadataOnlyOption_DefaultsToDisabled()
    {
        MyOption? boundOption = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(option =>
        {
            boundOption = option;
            return Task.CompletedTask;
        });

        await rootCommand.InvokeAsync(["BV1xx411c7mD"]);

        Assert.NotNull(boundOption);
        Assert.False(boundOption.MetadataOnly);
    }

    [Fact]
    public void UpdateDownloaded_RegistersUdAlias()
    {
        var command = CommandLineInvoker.GetUpdateDownloadedCommand((option, dir) => Task.CompletedTask);

        Assert.True(command.HasAlias("ud"));
    }

    [Fact]
    public async Task UpdateDownloaded_BindsDirectoryAndInheritedRootOptions()
    {
        MyOption? boundOption = null;
        string? boundDir = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.AddCommand(CommandLineInvoker.GetUpdateDownloadedCommand((option, dir) =>
        {
            boundOption = option;
            boundDir = dir;
            return Task.CompletedTask;
        }));

        Assert.Equal(0, await rootCommand.InvokeAsync(["ud", "D:\\anime", "-dd", "-p", "ALL", "--delay-per-video", "3"]));

        Assert.NotNull(boundOption);
        Assert.Equal("D:\\anime", boundDir);
        Assert.Equal("", boundOption.Url);
        Assert.True(boundOption.DownloadDanmaku);
        Assert.Equal("ALL", boundOption.SelectPage);
        Assert.True(boundOption.SelectPageSpecified);
        Assert.Equal(3, boundOption.DelayPerVideo);
    }

    [Fact]
    public async Task UpdateDownloaded_WithoutDirectory_BindsEmptyDirectory()
    {
        string? boundDir = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.AddCommand(CommandLineInvoker.GetUpdateDownloadedCommand((option, dir) =>
        {
            boundDir = dir;
            return Task.CompletedTask;
        }));

        Assert.Equal(0, await rootCommand.InvokeAsync(["update-downloaded"]));

        Assert.Equal("", boundDir);
    }
}
