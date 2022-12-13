using Replicate.MetaData;
using Replicate.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MRPC.Net {
    public class Client {
        public readonly Guid UUID = Guid.NewGuid();
        private int nextId = 0xFAFF;
        private int port;
        private ConcurrentDictionary<int, TaskCompletionSource<Response>> outstanding =
            new ConcurrentDictionary<int, TaskCompletionSource<Response>>();
        private JSONSerializer serializer =
            new JSONSerializer(ReplicationModel.Default, new JSONSerializer.Configuration() { Strict = false });
        private UdpClient udp;

        public Client(int port) {
            this.port = port;
            udp = new UdpClient(port);
        }

        public int GetNextId() { lock (this) return nextId++; }

        public void Listen() {
            Task.Run(async () => {
                while (true) {
                    // This will throw if the socket is closed
                    var receive = await udp.ReceiveAsync();

                    try {
                        if (receive.RemoteEndPoint == udp.Client.LocalEndPoint) continue;
                        var response = serializer.Deserialize<Response>(receive.Buffer);
                        if (!outstanding.TryRemove(response.id, out var source)) {
                            continue;
                        }
                        source.TrySetResult(response);
                    } catch (Exception e) {
                        // TODO: Log
                        // TODO: This gets hit on requests as well since path dsts don't parse as Guids
                    }
                }
            });
        }
        public Task<Response> Call(Request request) {
            var resultSource = new TaskCompletionSource<Response>();
            if (!outstanding.TryAdd(request.id, resultSource))
                throw new Exception("Failed to create request");

            var bytes = serializer.SerializeBytes(request);
            udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Parse("192.168.1.255"), port));

            return resultSource.Task;
        }

        public async Task<TResult> Call<TResult, TValue>(string path, TValue value) {
            var request = new Request() {
                id = GetNextId(),
                dst = path,
                src = UUID,
                value = Blob.FromString(serializer.SerializeString(value)),
            };
            var response = await Call(request);
            if (response.error != null) throw new Exception(response.error.ReadString());
            return serializer.Deserialize<TResult>(response.result.Stream);
        }
    }
}
