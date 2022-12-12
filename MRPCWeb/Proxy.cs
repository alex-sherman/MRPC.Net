using MRPC.Net;
using Replicate;
using Replicate.Web;
using System.Threading.Tasks;

[ReplicateType]
public class Proxy {

    [FromDI] Client client;

    [ReplicateRPC]
    public Task<Response> Call(Request request) {
        return client.Call(request);
    } 
}