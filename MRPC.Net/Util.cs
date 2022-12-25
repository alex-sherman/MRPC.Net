using System;
using System.Collections.Generic;
using System.Text;

namespace MRPC {
    public static class Util {
        public static Func<DateTime> NowProivder = () => DateTime.Now;
        public static DateTime Now => NowProivder();
    }
}
