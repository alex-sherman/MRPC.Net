using Replicate;
using Replicate.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace MRPC.Net {
    [ReplicateType]
    public class Message {
        public int id;
        public Guid src;
    }
    [ReplicateType]
    public class Request : Message {
        public string dst;
        public Blob value;
    }
    [ReplicateType]
    public class Response : Message {
        public Guid dst;
        public Blob result;
        public Blob error;
    }
}
