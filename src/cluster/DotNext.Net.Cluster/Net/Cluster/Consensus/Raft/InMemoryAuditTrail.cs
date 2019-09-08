using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace DotNext.Net.Cluster.Consensus.Raft
{
    using Replication;
    using Threading;

    /// <summary>
    /// Represents in-memory audit trail for Raft-based cluster node.
    /// </summary>
    /// <remarks>
    /// In-memory audit trail doesn't support compaction.
    /// It is recommended to use this audit trail for testing purposes only.
    /// </remarks>
    public sealed class InMemoryAuditTrail : AsyncReaderWriterLock, IPersistentState
    {
        private sealed class BufferedLogEntry : BinaryTransferObject, IRaftLogEntry
        {
            private BufferedLogEntry(ReadOnlyMemory<byte> content, long term, DateTimeOffset timestamp) 
                : base(content)
            {
                Term = term;
                Timestamp = timestamp;
            }

            internal static async Task<BufferedLogEntry> CreateBufferedEntryAsync(IRaftLogEntry entry, CancellationToken token = default)
            {
                ReadOnlyMemory<byte> content;
                using (var ms = new MemoryStream(1024))
                {
                    await entry.CopyToAsync(ms, token).ConfigureAwait(false);
                    ms.Seek(0, SeekOrigin.Begin);
                    content = ms.TryGetBuffer(out var segment)
                        ? segment
                        : new ReadOnlyMemory<byte>(ms.ToArray());
                }

                return new BufferedLogEntry(content, entry.Term, entry.Timestamp.ToUniversalTime());
            }

            bool ILogEntry.IsSnapshot => false;

            public long Term { get; }

            public DateTimeOffset Timestamp { get; }
        }

        private sealed class InitialLogEntry : IRaftLogEntry
        {
            long? IDataTransferObject.Length => 0L;
            Task IDataTransferObject.CopyToAsync(Stream output, CancellationToken token) => token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;

            ValueTask IDataTransferObject.CopyToAsync(PipeWriter output, CancellationToken token) => new ValueTask();

            long IRaftLogEntry.Term => 0L;

            bool IDataTransferObject.IsReusable => true;

            bool ILogEntry.IsSnapshot => false;

            DateTimeOffset ILogEntry.Timestamp => default;
        }

        internal static readonly IRaftLogEntry[] EmptyLog = { new InitialLogEntry() };

        private long commitIndex;
        private volatile IRaftLogEntry[] log;

        private long term;
        private volatile IRaftClusterMember votedFor;
        private readonly AsyncManualResetEvent commitEvent;

        /// <summary>
        /// Initializes a new audit trail with empty log.
        /// </summary>
        public InMemoryAuditTrail()
        {
            log = EmptyLog;
            commitEvent = new AsyncManualResetEvent(false);
        }

        long IPersistentState.Term => term.VolatileRead();

        bool IPersistentState.IsVotedFor(IRaftClusterMember member)
        {
            var lastVote = votedFor;
            return lastVote is null || ReferenceEquals(lastVote, member);
        }

        ValueTask IPersistentState.UpdateTermAsync(long value)
        {
            term.VolatileWrite(value);
            return new ValueTask();
        }

        ValueTask<long> IPersistentState.IncrementTermAsync() => new ValueTask<long>(term.IncrementAndGet());

        ValueTask IPersistentState.UpdateVotedForAsync(IRaftClusterMember member)
        {
            votedFor = member;
            return new ValueTask();
        }

        /// <summary>
        /// Gets index of the committed or last log entry.
        /// </summary>
        /// <param name="committed"><see langword="true"/> to get the index of highest log entry known to be committed; <see langword="false"/> to get the index of the last log entry.</param>
        /// <returns>The index of the log entry.</returns>
        public long GetLastIndex(bool committed)
            => committed ? commitIndex.VolatileRead() : Math.Max(0, log.LongLength - 1L);

        private IReadOnlyList<IRaftLogEntry> GetEntries(long startIndex, long endIndex)
        {
            if(startIndex < 0L)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            if(endIndex < 0L)
                throw new ArgumentOutOfRangeException(nameof(endIndex));
            if (endIndex >= log.Length)
                throw new IndexOutOfRangeException(ExceptionMessages.InvalidEntryIndex(endIndex));
            return endIndex < startIndex ? 
                Array.Empty<IRaftLogEntry>() :
                log.Slice(startIndex, endIndex - startIndex + 1);
        }

        async Task<IReadOnlyList<IRaftLogEntry>> IAuditTrail<IRaftLogEntry>.GetEntriesAsync(long startIndex, long? endIndex)
        {
            using (await this.AcquireReadLockAsync(CancellationToken.None).ConfigureAwait(false))
                return GetEntries(startIndex, endIndex ?? GetLastIndex(false));
        }

        private long Append(IRaftLogEntry[] entries, long? startIndex)
        {
            long result;
            if (startIndex.HasValue)
            {
                result = startIndex.Value;
                if (result <= commitIndex.VolatileRead())
                    throw new InvalidOperationException(ExceptionMessages.InvalidAppendIndex);
                log = log.RemoveLast(log.LongLength - result);
            }
            else
                result = log.LongLength;
            var newLog = new IRaftLogEntry[entries.Length + log.LongLength];
            Array.Copy(log, newLog, log.LongLength);
            entries.CopyTo(newLog, log.LongLength);
            log = newLog;
            return result;
        }

        async Task<long> IAuditTrail<IRaftLogEntry>.AppendAsync(IReadOnlyList<IRaftLogEntry> entries, long? startIndex)
        {
            if (entries.Count == 0)
                throw new ArgumentException(ExceptionMessages.EntrySetIsEmpty, nameof(entries));
            using (await this.AcquireWriteLockAsync(CancellationToken.None).ConfigureAwait(false))
            {
                var bufferedEntries = new IRaftLogEntry[entries.Count];
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    bufferedEntries[i] = entry.IsReusable
                        ? entry
                        : await BufferedLogEntry.CreateBufferedEntryAsync(entry).ConfigureAwait(false);
                }
                return Append(bufferedEntries, startIndex);
            }
        }

        async Task<long> IAuditTrail.CommitAsync(long? endIndex)
        {
            long startIndex, count;
            using (await this.AcquireWriteLockAsync(CancellationToken.None).ConfigureAwait(false))
            {
                startIndex = commitIndex.VolatileRead() + 1L;
                count = (endIndex ?? GetLastIndex(false)) - startIndex + 1L;
                if (count > 0)
                    commitIndex.VolatileWrite(startIndex + count - 1);
            }
            return count;
        }

        Task IAuditTrail.WaitForCommitAsync(long index, TimeSpan timeout, CancellationToken token)
            => index > 1L ? CommitEvent.WaitForCommitAsync(this, commitEvent, index, timeout, token) : Task.FromException(new ArgumentOutOfRangeException(nameof(index)));

        ref readonly IRaftLogEntry IAuditTrail<IRaftLogEntry>.First => ref EmptyLog[0];

        /// <summary>
        /// Releases all resources associated with this audit trail.
        /// </summary>
        /// <param name="disposing">Indicates whether the <see cref="Dispose(bool)"/> has been called directly or from finalizer.</param>
        protected override void Dispose(bool disposing)
        {
            if(disposing)
                commitEvent.Dispose();
            base.Dispose(disposing);
        }
    }
}