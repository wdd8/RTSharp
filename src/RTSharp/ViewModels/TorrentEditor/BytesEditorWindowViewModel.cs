using AvaloniaHex.Document;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.TorrentEditor;

public partial class BytesEditorWindowViewModel : ObservableObject
{
    public Func<Task<string?>> OpenFileDialog { get; set; } = null!; // view set

    public Func<Task<string?>> SaveFileDialog { get; set; } = null!; // view set

    /// <summary>Closes with the edited bytes, or <c>null</c> to discard</summary>
    public Action<byte[]?> CloseWindow { get; set; } = null!; // view set

    [ObservableProperty]
    public partial DynamicBinaryDocument Document { get; set; }

    [ObservableProperty]
    public partial ulong Length { get; set; }

    public BytesEditorWindowViewModel(byte[] Bytes)
    {
        Document = new DynamicBinaryDocument(Bytes);
    }

    partial void OnDocumentChanged(DynamicBinaryDocument value)
    {
        Length = value.Length;
        value.Changed += (_, _) => Length = value.Length;
    }

    [RelayCommand]
    public async Task ImportClick()
    {
        var path = await OpenFileDialog();
        if (path == null)
            return;

        Document = new DynamicBinaryDocument(await System.IO.File.ReadAllBytesAsync(path));
    }

    [RelayCommand]
    public async Task ExportClick()
    {
        var path = await SaveFileDialog();
        if (path == null)
            return;

        await System.IO.File.WriteAllBytesAsync(path, Document.ToArray());
    }

    [RelayCommand]
    private void OkClick()
    {
        CloseWindow(Document.ToArray());
    }

    [RelayCommand]
    private void CancelClick()
    {
        CloseWindow(null);
    }
}
