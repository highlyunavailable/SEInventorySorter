using VRage;

namespace CargoSorter
{
    public enum RequestFlags : byte
    {
        None,
        All,
        Max,
        Minimum,
        Limit,
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