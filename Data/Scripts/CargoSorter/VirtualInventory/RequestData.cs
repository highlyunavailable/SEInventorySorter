using VRage;

namespace CargoSorter
{
    public enum RequestFlags : byte
    {
        None = 0,
        All = 1,
        Minimum = 2,
        Limit = 3,
    }
    public struct RequestData
    {
        public MyFixedPoint Amount;
        public RequestFlags Flag;

        public RequestData(MyFixedPoint amount, RequestFlags flag) : this()
        {
            Amount = amount;
            Flag = flag;
        }
    }
}
