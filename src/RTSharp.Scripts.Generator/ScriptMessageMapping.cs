using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RTSharp.Scripts.Generator
{
    internal static class ScriptMessageMapping
    {
        private const string Utils = "global::RTSharp.Shared.Utils";
        private const string Container = "global::System.Collections.Generic.Dictionary<string, object?>";

        private static readonly Dictionary<string, string> ConvertMap = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["bool"] = "ToBoolean",
            ["byte"] = "ToByte",
            ["sbyte"] = "ToSByte",
            ["short"] = "ToInt16",
            ["ushort"] = "ToUInt16",
            ["int"] = "ToInt32",
            ["uint"] = "ToUInt32",
            ["long"] = "ToInt64",
            ["ulong"] = "ToUInt64",
            ["float"] = "ToSingle",
            ["double"] = "ToDouble",
            ["decimal"] = "ToDecimal",
            ["char"] = "ToChar"
        };

        private static readonly HashSet<string> NoConvert = new HashSet<string>(StringComparer.Ordinal) {
            "string", "byte[]"
        };

        public static string? TryEmit(RecordDeclarationSyntax Record, ISet<string> Contracts, Action<string, string> FxUnsupported)
        {
            var name = Record.Identifier.Text;
            var parameters = Record.ParameterList?.Parameters ?? default;

            var to = new StringBuilder();
            var from = new StringBuilder();
            var ok = true;

            foreach (var parameter in parameters) {
                var property = parameter.Identifier.Text;
                var type = parameter.Type!.ToString();

                if (!TryMap(type, property, Contracts, out var write, out var read)) {
                    FxUnsupported(property, type);
                    ok = false;
                    continue;
                }

                to.AppendLine($"            [nameof({property})] = {write},");
                from.AppendLine($"            {read},");
            }

            if (!ok)
                return null;

            return $@"partial record {name} : {Utils}.IScriptMessage<{name}>
{{
    public {Container} ToMessage() => new() {{
{to.ToString().TrimEnd(',', '\r', '\n')}
    }};

    public static {name} FromMessage({Container} Message) => new(
{from.ToString().TrimEnd(',', '\r', '\n')}
    );
}}
";
        }

        private static bool TryMap(string Type, string Property, ISet<string> Contracts, out string Write, out string Read)
        {
            var key = $"Message[nameof({Property})]";
            var nullable = Type.EndsWith("?", StringComparison.Ordinal);
            var bare = nullable ? Type.Substring(0, Type.Length - 1) : Type;

            Write = Property;
            Read = "";

            if (bare.EndsWith("[]", StringComparison.Ordinal) && bare != "byte[]") {
                var element = bare.Substring(0, bare.Length - 2);

                if (!TryMapElement(element, "x", Contracts, out var writeElement, out var readElement))
                    return false;

                Write = $"global::System.Array.ConvertAll({Property}, x => (object?){writeElement})";
                Read = $"global::System.Array.ConvertAll((object[]){key}!, x => {readElement})";
                return true;
            }

            if (NoConvert.Contains(bare)) {
                Read = nullable ? $"({Type}){key}" : $"({bare}){key}!";
                return true;
            }

            if (ConvertMap.TryGetValue(bare, out var convert)) {
                Read = nullable
                    ? $"{key} is null ? null : global::System.Convert.{convert}({key})"
                    : $"global::System.Convert.{convert}({key})";
                return true;
            }

            if (Contracts.Contains(bare)) {
                Write = nullable ? $"{Property}?.ToMessage()" : $"{Property}.ToMessage()";
                Read = nullable
                    ? $"{key} is null ? null : {bare}.FromMessage({Utils}.ScriptMessage.Nested({key}))"
                    : $"{bare}.FromMessage({Utils}.ScriptMessage.Nested({key}))";
                return true;
            }

            return false;
        }

        private static bool TryMapElement(string Type, string Value, ISet<string> Contracts, out string Write, out string Read)
        {
            Write = Value;
            Read = "";

            if (NoConvert.Contains(Type)) {
                Read = $"({Type}){Value}!";
                return true;
            }

            if (ConvertMap.TryGetValue(Type, out var convert)) {
                Read = $"global::System.Convert.{convert}({Value})";
                return true;
            }

            if (Contracts.Contains(Type)) {
                Write = $"{Value}.ToMessage()";
                Read = $"{Type}.FromMessage({Utils}.ScriptMessage.Nested({Value}))";
                return true;
            }

            return false;
        }
    }
}
