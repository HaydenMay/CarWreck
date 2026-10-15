using System.Runtime.InteropServices;

namespace CarWreck.Driving
{
    /// <summary>WebGL-only bridge to the hosting page (see Plugins/WebGL/CarWreckBridge.jslib).</summary>
    public static class CarWreckBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void CarWreck_Report(string json);
        public static void Report(string json) => CarWreck_Report(json);
#else
        public static void Report(string json) { }
#endif
    }
}
