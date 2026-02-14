using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using RTSharp.Shared.Abstractions;

namespace RTSharp.Shared.Controls.Views;

public partial class PieceProgressBar : UserControl
{
    private IList<PieceState> _pieces;

    /// <summary>
    /// Defines the <see cref="Pieces"/> property.
    /// </summary>
    public static readonly DirectProperty<PieceProgressBar, IList<PieceState>> PiecesProperty =
        AvaloniaProperty.RegisterDirect<PieceProgressBar, IList<PieceState>>(
            nameof(Pieces),
            o => o.Pieces,
            (o, v) => o.Pieces = v);

    /// <summary>
    /// Gets or sets pieaces list
    /// </summary>
    public IList<PieceState> Pieces {
        get { return _pieces; }
        set { SetAndRaise(PiecesProperty, ref _pieces, value); }
    }

    public override unsafe void Render(DrawingContext Ctx)
    {
        if (_pieces == null || _pieces.Count == 0 || Double.IsNaN(Height) || Height == 0)
            return;

        using var writeableBitmap = new WriteableBitmap(new PixelSize(_pieces.Count, (int)Height), new Vector(96, 96), PixelFormat.Bgra8888);

        using (var lockedFrameBuffer = writeableBitmap.Lock()) {
            unsafe {
                IntPtr origPtr = new IntPtr(lockedFrameBuffer.Address.ToInt64());
                IntPtr bufferPtr = new IntPtr(lockedFrameBuffer.Address.ToInt64());

                for (int x = 0;x < _pieces.Count;x++) {
                    var piece = _pieces[x];

                    byte r = 0, g = 0, b = 0;
                    if (piece == PieceState.NotDownloaded) {
                        r = g = b = 255;
                    } else if (piece == PieceState.Downloading) {
                        r = 0;
                        g = 255;
                        b = 0;
                    } else if (piece == PieceState.Downloaded) {
                        r = 0;
                        g = 0;
                        b = 255;
                    } else if (piece == PieceState.Highlighted) {
                        r = 200;
                        g = 40;
                        b = 40;
                    }

                    *(int*)bufferPtr = (255 << 24) | (r << 16) | (g << 8) | (b << 0);
                    bufferPtr += 4;
                }
                for (int y = 1;y < (int)Height;y++) {
                    Buffer.MemoryCopy((void*)origPtr, (void*)bufferPtr, _pieces.Count * 4, _pieces.Count * 4);
                    bufferPtr += 4 * _pieces.Count;
                }
            }
        }

        Ctx.DrawImage(writeableBitmap, Bounds);
    }

    static PieceProgressBar()
    {
        AffectsRender<PieceProgressBar>(PiecesProperty);
    }

    public PieceProgressBar()
    {
        InitializeComponent();
    }
}