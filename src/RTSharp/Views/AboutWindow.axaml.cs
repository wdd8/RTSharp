using Avalonia.Controls;
using Avalonia.Media;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

using System;
using System.Linq;

namespace RTSharp.Views;

public partial class AboutWindow : VmWindow<AboutWindowViewModel>
{
    private const double MIN_Y = -50;
    private const double MAX_Y = 225;

    private const double SPEED_X = 35;
    private const double SPEED_Y = 2;

    private const double FRAME_TIME = 0.016;
    private const double TRAIL_LAG = 0.07;

    // Position of the plane
    private readonly TranslateTransform Plane = new(-160, 64);

    // Vertical offsets of the trail dots relative to the plane, closest to the plane first
    private readonly TranslateTransform[] Trail;

    private TimeSpan LastFrame;
    private double VelY;
    private double TargetY;

    public AboutWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm.UnrestrictPageHeight = () => {
                MaxHeight = Double.PositiveInfinity;
                Height = 540;
            };
        });

        PlanePanel.RenderTransform = Plane;

        Trail = [.. PlanePanel.Children.OfType<TextBlock>().Reverse().Select(dot => {
            var transform = new TranslateTransform();
            dot.RenderTransform = transform;
            return transform;
        })];
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var planeWidth = PlanePanel.Bounds.Width;
        if (planeWidth <= 0)
            planeWidth = 140;

        Plane.X = -planeWidth;
        Plane.Y = MIN_Y + Random.Shared.NextDouble() * (MAX_Y - MIN_Y);
        VelY = 0;

        // Targets wander from the starting height, so no part of the range is favoured
        TargetY = Plane.Y;

        foreach (var dot in Trail)
            dot.Y = 0;

        RequestAnimationFrame(OnPlaneFrame);
    }

    private void PickNewTarget()
    {
        TargetY += ((Random.Shared.NextDouble() * 2) - 1) * 70;
        TargetY = Math.Clamp(TargetY, MIN_Y, MAX_Y);
    }

    private void OnPlaneFrame(TimeSpan time)
    {
        if (!IsVisible)
            return;

        var delta = (time - LastFrame).TotalSeconds;
        if (delta <= 0 || delta > 0.1)
            delta = FRAME_TIME;
        LastFrame = time;

        var x = Plane.X + SPEED_X * delta;
        if (x > PlaneCanvas.Bounds.Width) {
            x = -PlanePanel.Bounds.Width;
            VelY = 0;
            PickNewTarget();
        }

        var prevY = Plane.Y;

        // Random kick scales with sqrt of the frame time so turbulence looks the same at any refresh rate
        var kick = (Random.Shared.NextDouble() * 2 - 1) * 320 * Math.Sqrt(delta * FRAME_TIME);
        VelY += (SPEED_Y * (TargetY - prevY) - 2.6 * VelY) * delta + kick;
        var y = prevY + VelY * delta;

        if (y < MIN_Y || y > MAX_Y) {
            y = Math.Clamp(y, MIN_Y, MAX_Y);
            VelY = 0;
        }

        if (Math.Abs(TargetY - y) < 10)
            PickNewTarget();

        /*
         * Each dot chases the height of whatever is in front of it (the plane for the first dot),
         * so every dot further back trails TRAIL_LAG seconds more behind.
         */
        var follow = 1 - Math.Exp(-delta / TRAIL_LAG);
        var leaderY = y;
        foreach (var dot in Trail) {
            var dotY = prevY + dot.Y;
            dotY += (leaderY - dotY) * follow;

            dot.Y = Math.Clamp(dotY - y, -40, 40);
            leaderY = dotY;
        }

        Plane.X = x;
        Plane.Y = y;

        RequestAnimationFrame(OnPlaneFrame);
    }
}
