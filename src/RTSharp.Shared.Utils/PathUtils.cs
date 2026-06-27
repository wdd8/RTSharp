using System.Text;

namespace RTSharp.Shared.Utils;

public static class PathUtils
{
    public static string GetTopMostDirectoryName(string Directory)
    {
        var sep = Path.TrimEndingDirectorySeparator(Directory).LastIndexOf(Path.DirectorySeparatorChar);
        if (sep == -1)
            return Directory;

        return Directory[(sep + 1)..];
    }

    public static string SanitizeFileName(string Name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var ret = new StringBuilder(Name.Length);

        foreach (var ch in Name) {
            if (invalid.IndexOf(ch) != -1)
                continue;

            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' or ' ')
                ret.Append(ch);
        }
        return ret.ToString();
    }

    public static async ValueTask<string> ExpandTilde(string In)
    {
        if (In.StartsWith("~+/"))
            return Environment.CurrentDirectory + "/" + In[3..];
        if (In.StartsWith("~/"))
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/" + In[2..];
        if (In.StartsWith("~")) {
            var slashIdx = In.IndexOf('/');
            if (slashIdx == -1)
                return In;

            var uname = In[1..slashIdx];

            var passwd = await File.ReadAllLinesAsync("/etc/passwd");

            foreach (var line in passwd) {
                var split = line.Split(':');
                if (split.Length < 6)
                    continue;

                if (split[0] == uname) {
                    var homeDir = split[5];
                    return homeDir + In[slashIdx..];
                }
            }

            return In[slashIdx..];
        }

        return In;
    }
}
