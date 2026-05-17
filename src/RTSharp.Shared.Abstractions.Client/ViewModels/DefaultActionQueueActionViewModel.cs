using System;

using Avalonia;
using Avalonia.Media;

using RTSharp.Shared.Abstractions;

namespace RTSharp.Shared.Abstractions.Client.ViewModels;

public class DefaultActionQueueActionViewModel
{
    private static readonly IBrush WaitingBrush = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E));
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.FromRgb(0x42, 0xA5, 0xF5));
    private static readonly IBrush DoneBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.FromRgb(0xEF, 0x53, 0x50));
    private static readonly IBrush CancelledBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA7, 0x26));

    public required string Name { get; init; }
    public required int Depth { get; init; }
    public required float ProgressDone { get; init; }
    public required string ProgressString { get; init; }
    public required ACTION_STATE State { get; init; }

    public bool HasProgress => ProgressDone > 0;

    public string ProgressLabel => ProgressDone > 0
        ? $"{Math.Round(ProgressDone, 1)}%" + (string.IsNullOrEmpty(ProgressString) ? "" : $" {ProgressString}")
        : "";

    public Thickness IndentMargin => new(Depth * 16, 0, 0, 0);

    public IBrush StatusBrush => State switch {
        ACTION_STATE.WAITING => WaitingBrush,
        ACTION_STATE.RUNNING => RunningBrush,
        ACTION_STATE.DONE => DoneBrush,
        ACTION_STATE.FAILED => FailedBrush,
        ACTION_STATE.CANCELLED => CancelledBrush,
        _ => WaitingBrush
    };
}
