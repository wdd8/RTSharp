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
        foreach (var path in paths) {
            AddNode(root, path.Split('/'), 0, "");
        }
        return root;
    }

    private static void AddNode(Node node, string[] path, int index, string nodePath)
    {
        if (index >= path.Length)
            return;

        var segment = path[index];
        nodePath = nodePath.Length == 0 ? segment : string.Concat(nodePath, "/", segment);

        foreach (var actual in node.Children) {
            if (actual.Name == segment) {
                AddNode(actual, path, index + 1, nodePath);
                return;
            }
        }
        AddNode(node.AddChild(new Node(nodePath)), path, index + 1, nodePath);
    }
}
