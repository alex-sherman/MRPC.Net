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
        bool setResponse = false;
        bool finished = false;

        public RequestContext(MRPCRequest request, MRPCClient client) {
            requestId = request.id;
            this.client = client;
            bytes = client.Serializer.SerializeBytes(request);
            PathEntry = client.PathCache[request.dst];
            RemainingNodes = new HashSet<IPAddress>(PathEntry.Nodes);
        }
        public void Start() {
            started = Util.Now;
            SendTo(client.Broadcast);
            RetryRemaining();
        }
        public void Poll() {
            if (Util.Now - started > client.Timeout) Finish();
            if (!setResponse && Util.Now - lastRetry > client.RetryDelay) RetryRemaining();
        }
        void RetryRemaining() {
            lastRetry = Util.Now;
            // We can return early if we got all the reponses we expected, and got at least 1 response.
            // Note that we don't finish here, and instead leave the context outstanding for the full timeout.
            if (!RemainingNodes.Any() && Responses.Any()) {
                SetResponse();
                return;
            }
            lock (client.Udp) {
                foreach (var missing in RemainingNodes)
                    SendTo(missing);
            }
        }
        void SendTo(IPAddress remote) {
            client.Udp.Send(bytes, bytes.Length, new IPEndPoint(remote, client.Port));
        }
        public void OnResponse(IPAddress remote, MRPCResponse response) {
            PathEntry.MarkSuccess(remote);
            Responses.Add(response);
            RemainingNodes.Remove(remote);
            if (!RemainingNodes.Any()) SetResponse();
        }
        void SetResponse() {
            if (setResponse) return;
            setResponse = true;
            Source.SetResult(Responses);
        }
        void Finish() {
            if (finished) return;
            finished = true;
            SetResponse();
            foreach (var missing in RemainingNodes) {
                PathEntry.MarkError(missing);
            }
            client.Outstanding.TryRemove(requestId, out var _);
        }
    }
    public class MRPCClient {
        public readonly Guid UUID = Guid.NewGuid();
        private int NextId = 0xFAFF;
        internal int Port;
        internal TimeSpan Timeout;
        internal TimeSpan RetryDelay;
        internal ConcurrentDictionary<int, RequestContext> Outstanding =
            new ConcurrentDictionary<int, RequestContext>();
        internal JSONSerializer Serializer =
            new JSONSerializer(ReplicationModel.Default, new JSONSerializer.Configuration() { Strict = false });
        internal UdpClient Udp;
        internal PathCache PathCache = new PathCache();
        internal IPAddress Broadcast;
        public MRPCClient(string broadcastAddress, int port) : this(broadcastAddress, port, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100)) { }
        public MRPCClient(string broadcastAddress, int port, TimeSpan timeout, TimeSpan retryDelay) {
            Broadcast = IPAddress.Parse(broadcastAddress);
            Port = port;
            Timeout = timeout;
            RetryDelay = retryDelay;
            Udp = new UdpClient(port);
            Udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, true);
        }

        public int GetNextId() { lock (this) return NextId++; }

        public void Listen() {
            Task.Run(async () => {
                while (true) {
                    bool didWork = false;
                    if (Udp.Available > 0) {
                        didWork = true;
                        // This will throw if the socket is closed
                        var receive = await Udp.ReceiveAsync();

                        try {
                            if (receive.RemoteEndPoint == Udp.Client.LocalEndPoint) continue;
                            var response = Serializer.Deserialize<MRPCResponse>(receive.Buffer);
                            if (!Outstanding.TryGetValue(response.id, out var source)) {
                                continue;
                            }
                            source.OnResponse(receive.RemoteEndPoint.Address, response);
                        } catch (Exception e) {
                            // TODO: This gets hit on requests as well since path dsts don't parse as Guids
                        }
                    }
                    var contexts = Outstanding.Values.ToArray();
                    foreach (var context in contexts) context.Poll();
                    if (didWork) continue;
                    await Task.Delay(10);
                }
            });
        }

        public Task<List<MRPCResponse>> Call(MRPCRequest request) {
            var requestContext = new RequestContext(request, this);
            if (!Outstanding.TryAdd(request.id, requestContext))
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
                value = Blob.FromString(Serializer.SerializeString(value)),
            };
            var response = (await Call(request)).FirstOrDefault();
            if (response == null) throw new TimeoutException();
            if (response.error != null) throw new Exception(response.error.ReadString());
            return Serializer.Deserialize<TResult>(response.result.Stream);
        }
    }
}
