using System.Runtime;
using System.Windows;

namespace Sonolume.Standalone;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        base.OnStartup(e);
    }
}
