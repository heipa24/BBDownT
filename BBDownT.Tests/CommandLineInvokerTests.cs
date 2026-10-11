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
    public async Task JsonOnlyOption_IsRegisteredAndBound()
    {
        MyOption? boundOption = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(option =>
        {
            boundOption = option;
            return Task.CompletedTask;
        });

        var exitCode = await rootCommand.InvokeAsync(["BV1xx411c7mD", "--json-only"]);

        Assert.Equal(0, exitCode);
        Assert.NotNull(boundOption);
        Assert.True(boundOption.JsonOnly);
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

    [Fact]
    public async Task Login_BindsImageAndStyleOptions()
    {
        LoginQr? bound = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.AddCommand(CommandLineInvoker.GetLoginCommand("login", "登录", qr =>
        {
            bound = qr;
            return Task.CompletedTask;
        }));

        Assert.Equal(0, await rootCommand.InvokeAsync(["login", "-img", "--qr-style", "block"]));

        Assert.NotNull(bound);
        Assert.True(bound.Value.OpenImage);
        Assert.Equal("block", bound.Value.Style);
    }

    [Fact]
    public async Task Login_WithoutOptions_DefaultsToTerminalCompact()
    {
        LoginQr? bound = null;
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.AddCommand(CommandLineInvoker.GetLoginCommand("login", "登录", qr =>
        {
            bound = qr;
            return Task.CompletedTask;
        }));

        Assert.Equal(0, await rootCommand.InvokeAsync(["login"]));

        Assert.NotNull(bound);
        Assert.False(bound.Value.OpenImage);
        Assert.Null(bound.Value.Style);
        Assert.True(QrConsoleRender.UsesHalfBlocks(bound.Value.Style));
    }

    [Fact]
    public async Task Login_InvalidStyle_IsRejectedBeforeHandlerRuns()
    {
        var invoked = false;
        var rootCommand = CommandLineInvoker.GetRootCommand(_ => Task.CompletedTask);
        rootCommand.AddCommand(CommandLineInvoker.GetLoginCommand("login", "登录", _ =>
        {
            invoked = true;
            return Task.CompletedTask;
        }));

        Assert.NotEqual(0, await rootCommand.InvokeAsync(["login", "-qs", "half"]));

        Assert.False(invoked);
    }
}
