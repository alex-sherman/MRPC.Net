using Replicate.MetaData;
using Replicate.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MRPC.Net {
    public class Client {
        private Guid uuid;
        private int nextId = 0xFAFF;
        private Dictionary<int, TaskCompletionSource<Response>> outstanding =
            new Dictionary<int, TaskCompletionSource<Response>>();
        private JSONSerializer serializer =
            new JSONSerializer(ReplicationModel.Default, new JSONSerializer.Configuration() { Strict = false });
        private UdpClient udp = new UdpClient();

        public void Listen() {
            Task.Run(async () => {
                while (true) {
                    // This will throw if the socket is closed
                    var receive = await udp.ReceiveAsync();

                    try {
                        var response = serializer.Deserialize<Response>(receive.Buffer);
                        lock (this) {
                            if (!outstanding.ContainsKey(response.id)) continue;
                            outstanding[response.id].TrySetResult(response);
                            outstanding.Remove(response.id);
                        }
                    } catch (Replicate.SerializationError) {
                        // TODO: Log
                    }
                }
            });
        }
        public Task<Response> Call(Request request) {
            var resultSource = new TaskCompletionSource<Response>();
            lock (this) {
                outstanding[request.id] = resultSource;
            }

            var bytes = serializer.SerializeBytes(request);
            udp.Send(bytes, bytes.Length);

            return resultSource.Task;
        }

        public async Task<TResult> Call<TResult, TValue>(string path, TValue value) {
            var request = new Request() {
                id = nextId++,
                dst = path,
                src = uuid,
                value = Blob.FromString(serializer.SerializeString(value)),
            };
            var response = await Call(request);
            if (response.error != null) throw new Exception(response.error.ReadString());
            return serializer.Deserialize<TResult>(response.result.Stream);
        }
    }
}
