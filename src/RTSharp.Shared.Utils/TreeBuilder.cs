namespace RTSharp.Shared.Utils;

public class Node(string Path)
{

    public string Path { get; } = Path;
    public List<Node> Children { get; } = [];

    private string? _name;
    public string Name => _name ??= Path[(Path.AsSpan().LastIndexOf('/') + 1)..];

    public Node AddChild(Node Child)
    {
        Children.Add(Child);
        return Child;
    }
}

public static class TreeBuilder
{
    public static Node Build(IEnumerable<string> paths)
    {
        var root = new Node("./");
        var nodes = new Dictionary<string, Node>();
        var lookup = nodes.GetAlternateLookup<ReadOnlySpan<char>>();
        foreach (var path in paths) {
            var parent = root;
            var startIdx = 0;
            while (startIdx < path.Length) {
                var slashIdx = path.IndexOf('/', startIdx);
                var endIdx = slashIdx == -1 ? path.Length : slashIdx;

                if (!lookup.TryGetValue(path.AsSpan(0, endIdx), out var node)) {
                    node = new Node(path[..endIdx]);
                    nodes[node.Path] = node;
                    parent.AddChild(node);
                }
                parent = node;
                startIdx = endIdx + 1;
            }
        }
        return root;
    }
}
