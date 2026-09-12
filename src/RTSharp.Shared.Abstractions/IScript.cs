namespace RTSharp.Shared.Abstractions
{
    public interface IScript
    {
        public Task Execute(IScriptSession session, CancellationToken cancellationToken);
    }
}
