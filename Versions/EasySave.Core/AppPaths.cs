// EasySave.Core/AppPaths.cs
using System;
using System.IO;

namespace EasySave.Core
{
    /// <summary>
    /// Holds every path the application relies on.
    /// All paths are expressed relative to the folder that contains the executable.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Root folder of the portable application (i.e., the folder that contains the EXE).</summary>
        public static readonly string Root = AppContext.BaseDirectory;

        // ── Sub-paths ──────────────────────────────────────────────────────────────
        public static readonly string Logs = Path.Combine(Root, "Logs");          // Daily JSON / XML logs
        public static readonly string Scenarios = Path.Combine(Root, "scenarios.json"); // User-defined backup jobs
        public static readonly string Settings = Path.Combine(Root, "settings.json");  // Global settings
        public static readonly string LangDir = Path.Combine(Root, "Langages");       // Localised message files
    }
}
