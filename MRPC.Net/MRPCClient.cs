using Replicate.MetaData;
using Replicate.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MRPC {
    class RequestContext {
        public PathCacheEntry PathEntry;
        TaskCompletionSource<List<MRPCResponse>> Source = new TaskCompletionSource<List<MRPCResponse>>();
        private HashSet<IPAddress> RemainingNodes;
        private List<MRPCResponse> Responses = new List<MRPCResponse>();
        public Task<List<MRPCResponse>> ResponseTask => Source.Task;
        MRPCClient client;
        byte[] bytes;
        int requestId;
        DateTime started;
        DateTime lastRetry;
        bool finished = false;

        public RequestContext(MRPCRequest request, MRPCClient client) {
            requestId = request.id;
            this.client = client;
            bytes = client.serializer.SerializeBytes(request);
            PathEntry = client.pathCache[request.dst];
            RemainingNodes = new HashSet<IPAddress>(PathEntry.Nodes);
        }
        public void Start() {
            started = Util.Now;
            RetryRemaining();
        }
        public void Poll() {
            if (Util.Now - started > client.timeout) Finish();
            if (Util.Now - lastRetry > client.retryDelay) RetryRemaining();
        }
        public void RetryRemaining() {
            lastRetry = Util.Now;
            // Only finish if we get at least a single response, otherwise wait for timeout.
            if (!RemainingNodes.Any() && Responses.Any()) {
                Finish();
                return;
            }
            lock (client.udp) {
                client.udp.Send(bytes, bytes.Length, new IPEndPoint(client.broadcast, client.port));
                foreach (var missing in RemainingNodes) {
                    client.udp.Send(bytes, bytes.Length, new IPEndPoint(missing, client.port));
                }
            }
        }
        public void OnResponse(IPAddress remote, MRPCResponse response) {
            PathEntry.MarkSuccess(remote);
            Responses.Add(response);
            RemainingNodes.Remove(remote);
            if (!RemainingNodes.Any()) Finish();
        }
        public void Finish() {
            if (finished) return;
            finished = true;
            foreach (var missing in RemainingNodes) {
                PathEntry.MarkError(missing);
            }
            Source.SetResult(Responses);
            client.outstanding.TryRemove(requestId, out var _);
        }
    }
    public class MRPCClient {
        public readonly Guid UUID = Guid.NewGuid();
        private int nextId = 0xFAFF;
        internal int port;
        internal TimeSpan timeout;
        internal TimeSpan retryDelay;
        internal ConcurrentDictionary<int, RequestContext> outstanding =
            new ConcurrentDictionary<int, RequestContext>();
        internal JSONSerializer serializer =
            new JSONSerializer(ReplicationModel.Default, new JSONSerializer.Configuration() { Strict = false });
        internal UdpClient udp;
        internal PathCache pathCache = new PathCache();
        internal IPAddress broadcast;
        public MRPCClient(string broadcastAddress, int port) : this(broadcastAddress, port, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100)) { }
        public MRPCClient(string broadcastAddress, int port, TimeSpan timeout, TimeSpan retryDelay) {
            broadcast = IPAddress.Parse(broadcastAddress);
            this.port = port;
            this.timeout = timeout;
            this.retryDelay = retryDelay;
            udp = new UdpClient(port);
        }

        public int GetNextId() { lock (this) return nextId++; }

        public void Listen() {
            Task.Run(async () => {
                while (true) {
                    bool didWork = false;
                    if (udp.Available > 0) {
                        didWork = true;
                        // This will throw if the socket is closed
                        var receive = await udp.ReceiveAsync();

                        try {
                            if (receive.RemoteEndPoint == udp.Client.LocalEndPoint) continue;
                            var response = serializer.Deserialize<MRPCResponse>(receive.Buffer);
                            if (!outstanding.TryGetValue(response.id, out var source)) {
                                continue;
                            }
                            source.OnResponse(receive.RemoteEndPoint.Address, response);
                        } catch (Exception e) {
                            // TODO: Log
                            // TODO: This gets hit on requests as well since path dsts don't parse as Guids
                        }
                    }
                    var contexts = outstanding.Values.ToArray();
                    foreach(var context in contexts) context.Poll();
                    if (didWork) continue;
                    await Task.Delay(10);
                }
            });
        }

        public Task<List<MRPCResponse>> Call(MRPCRequest request) {
            var requestContext = new RequestContext(request, this);
            if (!outstanding.TryAdd(request.id, requestContext))
                throw new Exception("Failed to create request");
            requestContext.Start();
            return requestContext.ResponseTask;
        }

        // Expects a single response and returns the result (or error) from the first response received.
        public async Task<TResult> Call<TResult, TValue>(string path, TValue value) {
            var request = new MRPCRequest() {
                id = GetNextId(),
                dst = path,
                src = UUID,
                value = Blob.FromString(serializer.SerializeString(value)),
            };
            var response = (await Call(request)).FirstOrDefault();
            if (response == null) throw new TimeoutException();
            if (response.error != null) throw new Exception(response.error.ReadString());
            return serializer.Deserialize<TResult>(response.result.Stream);
        }
    }
}
