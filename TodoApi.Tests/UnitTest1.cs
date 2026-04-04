namespace TodoApi.Tests;

public class UnitTest1
{
    [Theory]
    [InlineData(5, 3)]
    public void Test1(int a, int b)
    {
        Assert.True(a < b, $"{a} is not greater than {b}");
    }
}
