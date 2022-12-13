using MRPC.Net;
using Replicate;
using Replicate.Web;
using System;
using System.Threading.Tasks;

[ReplicateType]
[ReplicateRoute(Route = "/mrpc")]
public class Proxy {
    [FromDI] public Client client;

    [ReplicateRPC]
    public Task<Response> Call(Request request) {
        if (request == null) throw new HTTPError("Null request");
        request.id = client.GetNextId();
        request.src = client.UUID;
        return client.Call(request);
    } 
}