namespace LlmSemverBump.IntegrationTests;

internal static class StdoutCapture
{
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<string> CaptureAsync(Func<Task<int>> action)
    {
        await _lock.WaitAsync();
        try
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
        finally
        {
            _lock.Release();
        }
    }
}
