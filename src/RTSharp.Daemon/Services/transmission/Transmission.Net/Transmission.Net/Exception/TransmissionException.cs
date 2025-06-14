namespace Transmission.Net.Exception;

/// <summary>
/// Represents that an error emitted by the Transmission RPC API
/// </summary>
[Serializable]
public class TransmissionException : System.Exception
{
    /// <inheritdoc/>
    public TransmissionException() { }
    /// <inheritdoc/>
    public TransmissionException(string message) : base(message) { }
    /// <inheritdoc/>
    public TransmissionException(string message, System.Exception inner) : base(message, inner) { }
    /// <inheritdoc/>
    protected TransmissionException(
      System.Runtime.Serialization.SerializationInfo info,
      System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
