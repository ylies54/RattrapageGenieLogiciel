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
    public class LogEntry
    {
        /*──────────────────── Données ───────────────────*/
        private DateTime timestamp;
        private List<Folder> listFolder;
        private string jobName;
        private BackupType backupType;
        private string sourceUNC;
        private string targetUNC;
        private long fileSizeBytes;
        private long durationMs;
        private BackupState state;

        /*──────────────────── Verrous ───────────────────*/
        private static readonly object fileLock = new object();
        private static readonly Mutex _logMutex =
            new(false, @"Global\EasySave_LogFile");

        /*──────────────────── Constructeurs ─────────────*/
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

        public LogEntry(DateTime timestamp, string jobName, BackupType backupType,
                        string sourceUNC, string targetUNC, long fileSizeBytes,
                        long durationMs, BackupState state, List<Folder> listFolder)
        {
            this.timestamp = timestamp;
            this.jobName = jobName;
            this.backupType = backupType;
            this.sourceUNC = sourceUNC;
            this.targetUNC = targetUNC;
            this.durationMs = durationMs;
            this.state = state;
            this.listFolder = listFolder ?? new List<Folder>();
            this.fileSizeBytes = fileSizeBytes > 0
                                ? fileSizeBytes
                                : this.listFolder.Sum(f => f.GetSize());
        }

        /*──────────────────── Getters / Setters ─────────*/
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

        /*──────────────────── Helpers liste ─────────────*/
        public void AddFolder(Folder f) => listFolder.Add(f);
        public void RemoveFolder(Folder f) => listFolder.Remove(f);

        /*──────────────────── Affichage ─────────────────*/
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
                sb.AppendLine($"  - {f.GetPath()} ({f.GetSize()} o) [{type}]");
            }
            return sb.ToString();
        }

        /*──────────────────── Sérialisation JSON ─────────*/
        public string ToJson(bool indent = false)
        {
            var anon = new
            {
                timestamp = timestamp.ToString("o"),
                jobName,
                backupType = backupType.ToString(),
                sourceUNC,
                targetUNC,
                fileSizeBytes,
                durationMs,
                state,
                listFolder = listFolder.ConvertAll(f => new
                {
                    path = f.GetPath(),
                    size = f.GetSize(),
                    type = f.GetIsFile() ? "file" : "folder",
                    encryptionTimeMs = f.GetEncryptionTimeMs()
                })
            };
            return JsonSerializer.Serialize(
                       anon,
                       new JsonSerializerOptions { WriteIndented = indent })
                   + Environment.NewLine;
        }

        /*──────────────────── Sérialisation XML ─────────*/
        public string ToXml(bool indent = false)
        {
            var entry = new XElement("logEntry",
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

        /*──────────────────── Écriture fichier protégée ─────────*/
        public void AppendToFile(LogFormat format = LogFormat.Json)
        {
            /* 1) contenu JSON indenté pour l’algo existant */
            string rawJson = ToJson(true).TrimEnd();
            string indentedJson = string.Join(Environment.NewLine,
                                      rawJson.Split(new[] { "\r\n", "\n" },
                                                    StringSplitOptions.None)
                                             .Select(l => "  " + l));

            /* 2) chemin du fichier */
            Directory.CreateDirectory(AppPaths.Logs);
            string date = DateTime.Now.ToString("yyyy-MM-dd");
            string fileName = format == LogFormat.Json
                              ? $"log-{date}.json"
                              : $"log-{date}.xml";
            string path = Path.Combine(AppPaths.Logs, fileName);

            /* 3) tentative mutex inter-processus */
            const int delay = 300, maxTry = 10;
            for (int t = 0; t < maxTry; t++)
            {
                if (!_logMutex.WaitOne(0))
                {
                    Thread.Sleep(delay);
                    continue;
                }

                try
                {
                    lock (fileLock)                    // intra-threads
                    {
                        if (format == LogFormat.Json)
                            AppendJson(path, indentedJson);
                        else
                            AppendXml(path, ToXml(true));
                    }
                    return;                             // succès
                }
                finally
                {
                    _logMutex.ReleaseMutex();
                }
            }
            throw new IOException("Log file busy for too long.");
        }

        /*──────────────────── Helpers internels ─────────*/
        private static void AppendJson(string path, string entry)
        {
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                File.WriteAllText(path, "[\n" + entry + "\n]", Encoding.UTF8);
            }
            else
            {
                string all = File.ReadAllText(path, Encoding.UTF8);
                int idx = all.LastIndexOf(']');
                if (idx < 0) all = "[";
                string updated = all[..idx].TrimEnd() + ",\n" + entry + "\n]";
                File.WriteAllText(path, updated, Encoding.UTF8);
            }
        }

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
