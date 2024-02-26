using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

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

    public override void Render(DrawingContext Ctx)
    {
        if (_pieces == null)
            return;

        var singlePieceSize = this.Bounds.Width / _pieces.Count();
        double pieceSize = 0;
        double previousX = 0;

        for (var x = 0;x < _pieces.Count;x++) {
            var thisPiece = _pieces[x];
            var nextPiece = (x + 1) == _pieces.Count ? PieceState.Unknown : _pieces[x + 1];

            pieceSize += singlePieceSize;

            if (thisPiece == nextPiece)
                continue;

            Ctx.FillRectangle(thisPiece switch {
                PieceState.NotDownloaded => Brushes.White,
                PieceState.Downloading => Brushes.Yellow,
                PieceState.Downloaded => Brushes.Blue,
                _ => throw new ArgumentOutOfRangeException()
            }, new Rect(previousX, 0, previousX + pieceSize, this.Bounds.Height));

            previousX += pieceSize;
            pieceSize = 0;
        }
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