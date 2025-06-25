using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;

namespace EasySave.Core
{
    /// <summary>
    /// Represents **one single log entry** (a complete backup run).
    /// It can be serialized to JSON or XML and appended to the daily log file
    /// in a thread- and process-safe manner.
    /// </summary>
    public class LogEntry
    {
        /* ──────────────────────────── Data ─────────────────────────── */

        private DateTime timestamp;      // When the backup finished
        private List<Folder> listFolder;    // All items processed
        private string jobName;        // Friendly name of the scenario
        private BackupType backupType;     // Full or Differential
        private string sourceUNC;      // UNC / absolute source path
        private string targetUNC;      // UNC / absolute target path
        private long fileSizeBytes;  // Cumulated size of copied files
        private long durationMs;     // Total duration of the job
        private BackupState state;          // Final state (Success, Failed, …)

        /* ──────────────────────── Synchronisation ──────────────────── */

        // In-process protection (multi-thread)
        private static readonly object fileLock = new();

        // Cross-process protection (only one process writes at a time)
        private static readonly Mutex _logMutex =
            new(initiallyOwned: false, name: @"Global\EasySave_LogFile");

        /* ───────────────────────── Constructors ────────────────────── */

        /// <summary>Default constructor – creates an “empty” entry.</summary>
        public LogEntry()
        {
            timestamp = DateTime.Now;
            listFolder = new List<Folder>();
            jobName = string.Empty;
            backupType = BackupType.Full;
            sourceUNC = string.Empty;
            targetUNC = string.Empty;
            fileSizeBytes = 0;
            durationMs = 0;
            state = BackupState.Pending;
        }

        /// <summary>Full constructor – every field can be set at once.</summary>
        public LogEntry(
            DateTime timestamp,
            string jobName,
            BackupType backupType,
            string sourceUNC,
            string targetUNC,
            long fileSizeBytes,
            long durationMs,
            BackupState state,
            List<Folder> listFolder)
        {
            this.timestamp = timestamp;
            this.jobName = jobName;
            this.backupType = backupType;
            this.sourceUNC = sourceUNC;
            this.targetUNC = targetUNC;
            this.durationMs = durationMs;
            this.state = state;
            this.listFolder = listFolder ?? new List<Folder>();

            // If size was not provided, compute it from the folder list
            this.fileSizeBytes = fileSizeBytes > 0
                               ? fileSizeBytes
                               : this.listFolder.Sum(f => f.GetSize());
        }

        /* ───────────────────── Getters / Setters ───────────────────── */

        public DateTime GetTimestamp() => timestamp;
        public string GetJobName() => jobName;
        public BackupType GetBackupType() => backupType;
        public string GetSourceUNC() => sourceUNC;
        public string GetTargetUNC() => targetUNC;
        public long GetFileSizeBytes() => fileSizeBytes;
        public long GetDurationMs() => durationMs;
        public BackupState GetState() => state;
        public List<Folder> GetListFolder() => listFolder;

        public void SetTimestamp(DateTime v) => timestamp = v;
        public void SetJobName(string v) => jobName = v;
        public void SetBackupType(BackupType v) => backupType = v;
        public void SetSourceUNC(string v) => sourceUNC = v;
        public void SetTargetUNC(string v) => targetUNC = v;
        public void SetDurationMs(long v) => durationMs = v;
        public void SetState(BackupState v) => state = v;
        public void SetListFolder(List<Folder> v) => listFolder = v;

        /* ────────────────── List helpers (convenience) ────────────── */

        public void AddFolder(Folder f) => listFolder.Add(f);
        public void RemoveFolder(Folder f) => listFolder.Remove(f);

        /* ───────────────────── Human-readable dump ─────────────────── */

        /// <summary>Pretty string for debugging / console output.</summary>
        public string Display()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp          : {timestamp}");
            sb.AppendLine($"Job Name           : {jobName}");
            sb.AppendLine($"Backup Type        : {backupType}");
            sb.AppendLine($"Source UNC         : {sourceUNC}");
            sb.AppendLine($"Target UNC         : {targetUNC}");
            sb.AppendLine($"Duration (ms)      : {durationMs}");
            sb.AppendLine($"State              : {state}");
            sb.AppendLine($"Total Size (Bytes) : {fileSizeBytes}");
            sb.AppendLine($"Nb Items           : {listFolder.Count}");
            foreach (var f in listFolder)
            {
                string type = f.GetIsFile() ? "file" : "folder";
                sb.AppendLine($"  - {f.GetPath()} ({f.GetSize()} B) [{type}]");
            }
            return sb.ToString();
        }

        /* ────────────────── JSON serialization ─────────────────────── */

        public string ToJson(bool indent = false)
        {
            var payload = new
            {
                timestamp = timestamp.ToString("o"),
                jobName,
                backupType = backupType.ToString(),
                sourceUNC,
                targetUNC,
                fileSizeBytes,
                durationMs,
                state,
                listFolder = listFolder.Select(f => new
                {
                    path = f.GetPath(),
                    size = f.GetSize(),
                    type = f.GetIsFile() ? "file" : "folder",
                    encryptionTimeMs = f.GetEncryptionTimeMs()
                })
            };

            return JsonSerializer.Serialize(
                       payload,
                       new JsonSerializerOptions { WriteIndented = indent })
                   + Environment.NewLine;
        }

        /* ────────────────── XML serialization ──────────────────────── */

        public string ToXml(bool indent = false)
        {
            XElement entry = new("logEntry",
                new XElement("timestamp", timestamp.ToString("o")),
                new XElement("jobName", jobName),
                new XElement("backupType", backupType),
                new XElement("sourceUNC", sourceUNC),
                new XElement("targetUNC", targetUNC),
                new XElement("fileSizeBytes", fileSizeBytes),
                new XElement("durationMs", durationMs),
                new XElement("state", state),
                new XElement("listFolder",
                    listFolder.Select(f => new XElement("item",
                        new XAttribute("path", f.GetPath()),
                        new XAttribute("size", f.GetSize()),
                        new XAttribute("type", f.GetIsFile() ? "file" : "folder"),
                        new XAttribute("encryptionTimeMs", f.GetEncryptionTimeMs())
                )))
            );

            return indent
                 ? entry.ToString(SaveOptions.None) + Environment.NewLine
                 : entry.ToString(SaveOptions.DisableFormatting) + Environment.NewLine;
        }

        /* ───────────────────── File-append (thread + process safe) ─── */

        /// <summary>
        /// Appends the entry to the daily log file (JSON or XML).
        /// Uses a named **global mutex** for cross-process safety and an
        /// **in-process lock** for multi-thread safety.
        /// </summary>
        public void AppendToFile(LogFormat format = LogFormat.Json)
        {
            // 1) We reuse the existing “pretty” JSON to produce the old 2-spaces
            //    indent expected by the historical algorithm.
            string rawJson = ToJson(indent: true).TrimEnd();
            string indentedJson = string.Join(
                Environment.NewLine,
                rawJson.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                       .Select(l => "  " + l));

            // 2) Determine the log file path (one file per day & format)
            Directory.CreateDirectory(AppPaths.Logs);
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string fileName = format == LogFormat.Json
                              ? $"log-{date}.json"
                              : $"log-{date}.xml";
            string path = Path.Combine(AppPaths.Logs, fileName);

            // 3) Try to enter the cross-process mutex with retries
            const int delayMs = 300;
            const int maxTry = 10;

            for (int attempt = 0; attempt < maxTry; attempt++)
            {
                if (!_logMutex.WaitOne(0))
                {
                    Thread.Sleep(delayMs);
                    continue;                       // try again later
                }

                try
                {
                    lock (fileLock)               // intra-process protection
                    {
                        if (format == LogFormat.Json)
                            AppendJson(path, indentedJson);
                        else
                            AppendXml(path, ToXml(indent: true));
                    }
                    return;                        // success → leave method
                }
                finally
                {
                    _logMutex.ReleaseMutex();
                }
            }

            // Could not acquire the mutex after several tries
            throw new IOException("Log file busy for too long.");
        }

        /* ─────────────────── Internal helpers ─────────────────────── */

        /// <summary>Append a JSON record while preserving the array structure.</summary>
        private static void AppendJson(string path, string entry)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                // First entry → create array
                File.WriteAllText(path, "[\n" + entry + "\n]", Encoding.UTF8);
            }
            else
            {
                string all = File.ReadAllText(path, Encoding.UTF8);
                int idx = all.LastIndexOf(']');
                if (idx < 0) all = "["; // malformed file → reset

                string updated = all[..idx].TrimEnd() + ",\n" + entry + "\n]";
                File.WriteAllText(path, updated, Encoding.UTF8);
            }
        }

        /// <summary>Append an XML element to &lt;logEntries&gt; … &lt;/logEntries&gt;.</summary>
        private static void AppendXml(string path, string entryXml)
        {
            XElement entry = XElement.Parse(entryXml);

            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                new XDocument(new XElement("logEntries", entry)).Save(path);
            }
            else
            {
                var doc = XDocument.Load(path);
                doc.Root!.Add(entry);
                doc.Save(path);
            }
        }
    }
}
    