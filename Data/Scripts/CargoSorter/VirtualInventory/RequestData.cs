using VRage;
using VRage.Game;

namespace CargoSorter
{
    public enum RequestFlags : byte
    {
        None,
        All,
        Percent,
        Max,
        Minimum,
        Limit,
    }

    public struct RequestData
    {
        public MyDefinitionId DefinitionId;
        public MyFixedPoint Amount;
        public RequestFlags Flag;

        public RequestData(MyDefinitionId definitionId, MyFixedPoint amount, RequestFlags flag) : this()
        {
            DefinitionId = definitionId;
            Amount = amount;
            Flag = flag;
        }
    }
}