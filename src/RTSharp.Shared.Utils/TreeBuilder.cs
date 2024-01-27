using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RTSharp.Shared.Utils
{
	public class Node
	{
		public string Path { get; }
		public List<Node> Children { get; }

		public Node(String Path)
		{
			this.Path = Path;
			Children = new();
		}

		public String Name => Path.Split('/').Last();
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
			Node root = new Node("./");
			foreach (var path in paths) {
				AddNode(root, path.Split('/').ToArray(), "");
			}
			return root;
		}

		private static void AddNode(Node node, string[] path, string nodePath)
		{
			if (!path.Any())
				return;

			nodePath = nodePath == "" ? path[0] : $"{nodePath}/{path[0]}";

			foreach (Node actual in node.Children) {
				if (actual.Name == path[0]) {
					AddNode(actual, path[1..], nodePath);
					return;
				}
			}
			AddNode(node.AddChild(new Node(nodePath)), path[1..], nodePath);
		}
	}
}
