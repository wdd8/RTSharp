using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using RTSharp.Shared.Abstractions;

namespace RTSharp.Shared.Controls.Views;

public partial class PieceProgressBar : UserControl
{
    private WriteableBitmap? Bitmap;
    private Rect Size;
    private bool ResetPending = true;

    public static readonly DirectProperty<PieceProgressBar, IList<PieceState>> PiecesProperty =
        AvaloniaProperty.RegisterDirect<PieceProgressBar, IList<PieceState>>(
            nameof(Pieces),
            o => o.Pieces,
            (o, v) => o.Pieces = v);

    private IList<PieceState> _pieces = [];
    public IList<PieceState> Pieces {
        get { return _pieces; }
        set {
            SetAndRaise(PiecesProperty, ref _pieces, value);
            ResetPending = true;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        if (_pieces == null || _pieces.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
            return;

        int boundsW = (int)Bounds.Width;
        int boundsH = (int)Bounds.Height;

        if (Bitmap == null || ResetPending || (int)Size.Width != boundsW || (int)Size.Height != boundsH) {
            Bitmap?.Dispose();
            Size = Bounds;
            ResetPending = true;
        }

        if (ResetPending) {
            Bitmap = BuildBitmap();
            ResetPending = false;
        }

        ctx.DrawImage(Bitmap!, Bounds);
    }

    private unsafe WriteableBitmap BuildBitmap()
    {
        var w = Math.Min(_pieces.Count, (int)Bounds.Width);
        var h = (int)Bounds.Height;

        var bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888);
        int count = _pieces.Count;

        using var fb = bitmap.Lock();
        var origPtr = fb.Address;
        var bufferPtr = fb.Address;

        for (int x = 0; x < w; x++) {
            // Highlighted > Downloading > Downloaded > NotDownloaded
            PieceState state = PieceState.NotDownloaded;
            for (int i = x * count / w; i < (x + 1) * count / w; i++) {
                var curState = _pieces[i];
                if (curState == PieceState.Highlighted) {
                    state = curState;
                    break;
                } else if (curState == PieceState.Downloading)
                    state = curState;
                else if (curState == PieceState.Downloaded && state == PieceState.NotDownloaded)
                    state = curState;
            }

            byte r, g, b;
            if (state == PieceState.NotDownloaded) {
                r = g = b = 255;
            } else if (state == PieceState.Downloading) {
                r = 0;
                g = 255;
                b = 0;
            } else if (state == PieceState.Downloaded) {
                r = 0;
                g = 0;
                b = 255;
            } else if (state == PieceState.Highlighted) {
                r = 200;
                g = 40;
                b = 40;
            } else {
                throw new ArgumentOutOfRangeException(nameof(Pieces));
            }

            *(int*)bufferPtr = (255 << 24) | (r << 16) | (g << 8) | b;
            bufferPtr += 4;
        }

        for (int y = 1; y < h; y++) {
            Buffer.MemoryCopy((void*)origPtr, (void*)bufferPtr, w * 4L, w * 4L);
            bufferPtr += 4 * w;
        }

        return bitmap;
    }

    static PieceProgressBar()
    {
        AffectsRender<PieceProgressBar>(PiecesProperty);
    }

    public PieceProgressBar()
    {
        InitializeComponent();
        Unloaded += (_, _) => {
            Bitmap?.Dispose();
            Bitmap = null;
        };
    }
}
