namespace Transmission.Net.Core.Util;

/// <summary>
/// Async extension
/// </summary>
public static class AsyncExtensions
{
    /// <summary>
    /// Wait and unwrap exception
    /// </summary>
    /// <param name="task"></param>
    public static void WaitAndUnwrapException(this Task task)
    {
        try
        {
            task.Wait();
        }
        catch (System.Exception e)
        {
            if (e.InnerException != null)
            {
                throw e.InnerException;
            }

            throw;
        }
    }
}
