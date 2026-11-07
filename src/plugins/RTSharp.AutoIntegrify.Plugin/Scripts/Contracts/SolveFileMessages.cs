namespace RTSharp.Scripts.SolveFile;

public partial record SolveFileInput(
    string[] AdditionalPaths,
    string FilePath,
    long Size,
    string CurrentPath,
    bool IsMultiFile,
    bool WideSearch,
    int RecurseLimit
);

public partial record SolveFileResult(string[] Paths);
