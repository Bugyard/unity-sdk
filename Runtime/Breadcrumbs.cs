using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace BugyardSDK
{
    /// <summary>
    /// A single gameplay breadcrumb recorded via <see cref="Bugyard.Track(string, object)"/>:
    /// a short event name, the UTC time it happened, and an optional free-form payload. Captured
    /// into the report's <c>events.json</c> attachment so a dev can see the sequence of actions
    /// that led to the bug ("StartedBossFight" → "PlayerDied" → "Respawned").
    /// </summary>
    public readonly struct Breadcrumb
    {
        public readonly string name;
        public readonly string ts;     // ISO 8601 UTC, correlates with log timestamps
        public readonly object payload; // optional; serialized verbatim by ContextJson

        public Breadcrumb(string name, string ts, object payload)
        {
            this.name = name;
            this.ts = ts;
            this.payload = payload;
        }
    }

    /// <summary>
    /// Bounded FIFO buffer of recent breadcrumbs. The newest <c>max</c> entries are kept; older
    /// ones are dropped as new ones arrive. Thread-safe so <see cref="Bugyard.Track"/> can be
    /// called from any thread (mirrors the log buffer's locking). Serializes to a JSON array via
    /// <see cref="ContextJson.SerializeValue"/> for the <c>events.json</c> attachment.
    /// </summary>
    public sealed class BreadcrumbBuffer
    {
        readonly Queue<Breadcrumb> _items = new Queue<Breadcrumb>();
        readonly object _lock = new object();
        int _max;

        public BreadcrumbBuffer(int max)
        {
            _max = Mathf.Max(1, max);
        }

        public int Count
        {
            get { lock (_lock) return _items.Count; }
        }

        /// <summary>Record a breadcrumb. A null/empty name is ignored. The UTC timestamp is taken
        /// here (thread-safe, unlike <c>Time.realtimeSinceStartup</c>) so it's correct off the main
        /// thread too.</summary>
        public void Add(string name, object payload)
        {
            if (string.IsNullOrEmpty(name)) return;
            var crumb = new Breadcrumb(name, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), payload);
            lock (_lock)
            {
                _items.Enqueue(crumb);
                while (_items.Count > _max) _items.Dequeue();
            }
        }

        public void Clear()
        {
            lock (_lock) _items.Clear();
        }

        /// <summary>
        /// Serialize the buffered breadcrumbs to a UTF-8 JSON array, oldest first, or null when the
        /// buffer is empty (so the caller omits the <c>events.json</c> attachment entirely). Each
        /// entry is <c>{ "name", "ts", "payload"? }</c>; <c>payload</c> is omitted when not set.
        /// </summary>
        public byte[] ToJsonBytes()
        {
            Breadcrumb[] snapshot;
            lock (_lock)
            {
                if (_items.Count == 0) return null;
                snapshot = _items.ToArray();
            }

            var list = new List<object>(snapshot.Length);
            foreach (Breadcrumb c in snapshot)
            {
                var entry = new Dictionary<string, object> { { "name", c.name }, { "ts", c.ts } };
                if (c.payload != null) entry["payload"] = c.payload;
                list.Add(entry);
            }

            return Encoding.UTF8.GetBytes(ContextJson.SerializeValue(list));
        }
    }
}
