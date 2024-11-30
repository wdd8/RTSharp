namespace RTSharp.Shared.Abstractions
{
    public interface IScript
    {
        public Task Execute(Dictionary<string, string> variables, IScriptSession session, CancellationToken cancellationToken);
    }
}
