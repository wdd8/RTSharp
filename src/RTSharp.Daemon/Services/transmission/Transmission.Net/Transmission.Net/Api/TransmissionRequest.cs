using Newtonsoft.Json;

namespace Transmission.Net.Api;

/// <summary>
/// Transmission request 
/// </summary>
public class TransmissionRequest : CommunicateBase
{
    /// <summary>
    /// Name of the method to invoke
    /// </summary>
    [JsonProperty("method")]
    public string Method;

    /// <summary>
    /// Initialize request
    /// </summary>
    /// <param name="method">Method name</param>
    public TransmissionRequest(string method)
    {
        Method = method;
    }

    /// <summary>
    /// Initialize request 
    /// </summary>
    /// <param name="method">Method name</param>
    /// <param name="arguments">Arguments</param>
    public TransmissionRequest(string method, ArgumentsBase arguments)
    {
        Method = method;
        Arguments = arguments.Data;
    }

    /// <summary>
    /// Initialize request 
    /// </summary>
    /// <param name="method">Method name</param>
    /// <param name="arguments">Arguments</param>
    public TransmissionRequest(string method, Dictionary<string, object> arguments)
    {
        Method = method;
        Arguments = arguments;
    }
}
