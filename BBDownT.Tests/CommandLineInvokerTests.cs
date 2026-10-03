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
}
