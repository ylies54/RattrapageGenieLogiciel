// EasySave.Core/AppPaths.cs
using System;
using System.IO;

namespace EasySave.Core
{
    /// <summary>
    /// Centralise tous les chemins utilisés par l’application.
    /// Toujours relatifs au dossier où se trouve l’exécutable.
    /// </summary>
    public static class AppPaths
    {
        /// <summary>Racine de l’application portable (dossier de l’exe).</summary>
        public static readonly string Root = AppContext.BaseDirectory;

        public static readonly string Logs = Path.Combine(Root, "Logs");
        public static readonly string Scenarios = Path.Combine(Root, "scenarios.json");
        public static readonly string Settings = Path.Combine(Root, "settings.json");
        public static readonly string LangDir = Path.Combine(Root, "Langages");
        public static readonly string State = Path.Combine(Root, "state.json");
    }
}
