using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace MRPC {
    public class PathCacheEntry {
        // Keys are nodes that have responded, and values are number of consecutive missed responses.
        private Dictionary<IPAddress, int> nodes = new Dictionary<IPAddress, int>();

        public List<IPAddress> Nodes { get { lock (this) return nodes.Keys.ToList(); } }
        public void MarkError(IPAddress node) {
            lock (this) {
                Console.WriteLine($"Error: {node}");
                int value = 0;
                nodes.TryGetValue(node, out value);
                nodes[node] = value++;
                if (value >= PathCache.MAX_ERRORS)
                    nodes.Remove(node);
            }
        }
        public void MarkSuccess(IPAddress node) {
            lock (this) nodes[node] = 0;
        }
    }
    public class PathCache {
        public static readonly int MAX_ERRORS = 5;
        Dictionary<string, PathCacheEntry> entries = new Dictionary<string, PathCacheEntry>();
        public PathCacheEntry this[string path] {
            get {
                lock (this) {
                    entries.TryAdd(path, new PathCacheEntry());
                    return entries[path];
                }
            }
        }
    }
}
