using Replicate;
using Replicate.Serialization;
using System;
using System.Collections.Generic;
using System.Text;

namespace MRPC {
    [ReplicateType]
    public class MRPCMessage {
        public int id;
        public Guid src;
    }
    [ReplicateType]
    public class MRPCRequest : MRPCMessage {
        public string dst;
        public Blob value;
    }
    [ReplicateType]
    public class MRPCResponse : MRPCMessage {
        public Guid dst;
        public Blob result;
        public Blob error;
    }
}
