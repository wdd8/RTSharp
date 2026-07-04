namespace RTSharp.Daemon.Exceptions;

public class DataProviderFailure : Exception
{
    public string Destination { get; }

    public DataProviderFailure(string Message, Exception Inner, string Destination) : base(Message, Inner)
    {
        this.Destination = Destination;
    }
}
