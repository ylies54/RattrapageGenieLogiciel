using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace EasySave_G3_V3_0
{
    public class PriorityManager
    {
        // ---------------------------------------------------------------------
        // Fields
        // ---------------------------------------------------------------------
        private readonly HashSet<string> _priorityExts;   // extensions with high prio

        private int _pendingCount = 0;                    // global remaining prio files

        // Completed only when _pendingCount reaches 0
        private TaskCompletionSource<bool> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly object _lock = new();            // protects TCS recreation

        // ---------------------------------------------------------------------
        // Ctor – normalises extensions (“.txt”, “…”) and fills the HashSet
        // ---------------------------------------------------------------------
        public PriorityManager(IEnumerable<string> priorityExtensions)
        {
            _priorityExts = new HashSet<string>(
                priorityExtensions.Select(ext =>
                    ext.StartsWith('.') ? ext.ToLower() : "." + ext.ToLower()));
        }

        // ---------------------------------------------------------------------
        // Called ONCE per job to announce how many priority files it contains
        // ---------------------------------------------------------------------
        public void RegisterPendingFiles(IEnumerable<string> filePaths)
        {
            int add = filePaths.Count(path =>
                _priorityExts.Contains(Path.GetExtension(path).ToLower()));
            if (add == 0) return;

            /* If we were at 0, we must create a brand-new, non-completed TCS */
            if (Volatile.Read(ref _pendingCount) == 0)
            {
                lock (_lock)
                {
                    if (_pendingCount == 0)              // double-check
                        _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            Interlocked.Add(ref _pendingCount, add);     // thread-safe
        }

        // ---------------------------------------------------------------------
        // Await before copying a file. For priority ones -> returns immediately.
        // ---------------------------------------------------------------------
        public Task WaitIfNonPriorityAsync(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();

            // Non-priority + still pending → await global Task
            if (!_priorityExts.Contains(ext) && Volatile.Read(ref _pendingCount) > 0)
                return _tcs.Task;        // asynchronous wait

            return Task.CompletedTask;   // no wait required
        }

        // ---------------------------------------------------------------------
        // Must be called when a priority file finishes copying/encrypting
        // ---------------------------------------------------------------------
        public void SignalPriorityFileDone(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLower();
            if (!_priorityExts.Contains(ext)) return;     // ignore non-priority

            // If this was the last one, release everyone
            if (Interlocked.Decrement(ref _pendingCount) == 0)
                _tcs.TrySetResult(true);
        }
    }
}
