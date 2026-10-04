namespace BBDownT.Tests;

public class Aria2cDefaultTests
{
    [Fact]
    public void ShouldUseAria2c_IsFalseByDefault()
    {
        Assert.False(Program.ShouldUseAria2c(new MyOption()));
    }

    [Fact]
    public void ShouldUseAria2c_IsTrueOnlyWhenExplicitlyEnabled()
    {
        Assert.True(Program.ShouldUseAria2c(new MyOption { UseAria2c = true }));
    }

    [Fact]
    public void ShouldUseAria2c_IsFalseWhenNoAria2Overrides()
    {
        Assert.False(Program.ShouldUseAria2c(new MyOption { UseAria2c = true, NoAria2c = true }));
    }
}
