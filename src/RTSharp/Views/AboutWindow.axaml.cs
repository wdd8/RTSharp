using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RTSharp.Views;

public partial class AboutWindow : VmWindow<AboutWindowViewModel>
{
    private const double MIN_Y = -50;
    private const double MAX_Y = 225;

    private const double SPEED_X = 35;
    private const double SPEED_Y = 2;

    private const double FRAME_TIME = 0.016;

    private readonly DispatcherTimer PlaneTimer;
    private readonly Stopwatch Sw = new();

    private double CurX;
    private double CurY;
    private double VelY;
    private double TargetY;

    private TranslateTransform[]? TrailTransforms;
    private int[]? TrailFrames;
    private double[]? YHistory;
    private int HistoryIndex;

    public AboutWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm.UnrestrictPageHeight = () => {
                MaxHeight = Double.PositiveInfinity;
                Height = 540;
            };
        });

        PlaneTimer = new DispatcherTimer {
            Interval = TimeSpan.FromMilliseconds(FRAME_TIME * 1000)
        };
        PlaneTimer.Tick += OnPlaneTick;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        SetupTrail();
        ResetPlane();
        Sw.Restart();
        PlaneTimer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        PlaneTimer.Stop();
        Sw.Stop();
    }

    private void SetupTrail()
    {
        var transforms = new List<TranslateTransform>();

        foreach (var child in PlanePanel.Children) {
            if (child is TextBlock dot) {
                var transform = new TranslateTransform();
                dot.RenderTransform = transform;
                transforms.Add(transform);
            }
        }

        TrailTransforms = [.. transforms];
        TrailFrames = new int[TrailTransforms.Length];
        YHistory = new double[128];

        /*
         * Leftmost dot lags the most, rightmost (closest to the plane) the least.
         * Convert each lag into a number of frames to look back in the history.
         */
        for (var x = 0; x < TrailFrames.Length; x++) {
            var lag = 0.07 * (TrailFrames.Length - x);
            TrailFrames[x] = Math.Min(YHistory.Length - 1, (int)Math.Round(lag / FRAME_TIME));
        }
    }

    private void UpdateTrail()
    {
        HistoryIndex = (HistoryIndex + 1) % YHistory!.Length;
        YHistory[HistoryIndex] = CurY;

        for (var x = 0; x < TrailTransforms!.Length; x++) {
            var idx = (HistoryIndex - TrailFrames![x] + YHistory.Length) % YHistory.Length;
            var offset = Math.Clamp(YHistory[idx] - CurY, -40, 40);
            TrailTransforms[x].Y = offset;
        }
    }

    private void ResetPlane()
    {
        var planeWidth = PlanePanel.Bounds.Width;
        if (planeWidth <= 0)
            planeWidth = 140;

        CurX = -planeWidth;
        CurY = MIN_Y + Random.Shared.NextDouble() * (MAX_Y - MIN_Y);
        VelY = 0;

        Array.Fill(YHistory!, CurY);
        HistoryIndex = 0;

        PickNewTarget();
    }

    private void PickNewTarget()
    {
        TargetY += ((Random.Shared.NextDouble() * 2) - 1) * 70;
        TargetY = Math.Clamp(TargetY, MIN_Y, MAX_Y);
    }

    private void OnPlaneTick(object? sender, EventArgs e)
    {
        var delta = Sw.Elapsed.TotalSeconds;
        Sw.Restart();

        if (delta <= 0 || delta > 0.1)
            delta = FRAME_TIME;

        var canvasWidth = PlaneCanvas.Bounds.Width;
        var planeWidth = PlanePanel.Bounds.Width;

        CurX += SPEED_X * delta;
        if (CurX > canvasWidth) {
            CurX = -planeWidth;
            VelY = 0;
            PickNewTarget();
        }

        var kick = (Random.Shared.NextDouble() * 2 - 1) * 320;
        VelY += (SPEED_Y * (TargetY - CurY) - 2.6 * VelY + kick) * delta;
        CurY += VelY * delta;

        if (CurY < MIN_Y) {
            CurY = MIN_Y;
            VelY = 0;
        } else if (CurY > MAX_Y) {
            CurY = MAX_Y;
            VelY = 0;
        }

        if (Math.Abs(TargetY - CurY) < 10)
            PickNewTarget();

        UpdateTrail();

        Canvas.SetLeft(PlanePanel, CurX);
        Canvas.SetTop(PlanePanel, CurY);
    }
}
