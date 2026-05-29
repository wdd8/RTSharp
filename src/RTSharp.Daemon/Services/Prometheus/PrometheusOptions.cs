namespace RTSharp.Daemon.Services.Prometheus;

public class PrometheusOptions
{
    public bool Enabled { get; set; }

    public string Path { get; set; } = "/metrics";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(1);
}
