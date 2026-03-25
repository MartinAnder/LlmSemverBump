namespace LlmSemverBump.Tests;

internal static class StdoutCapture
{
    public static async Task<string> CaptureAsync(Func<Task<int>> action)
    {
        var buffer = new StringWriter();
        var original = Console.Out;
        Console.SetOut(buffer);

        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(original);
        }

        return buffer.ToString();
    }
}
