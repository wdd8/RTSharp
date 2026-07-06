namespace RTSharp.Daemon.Exceptions;

public class DataProviderFailure(string Message, Exception Inner, string Destination) : Exception(Message, Inner)
{
    public string Destination { get; } = Destination;
}
