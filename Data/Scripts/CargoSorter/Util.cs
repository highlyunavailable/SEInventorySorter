using Sandbox.ModAPI;
using VRage.ModAPI;

namespace CargoSorter
{
    public class Util
    {
        public static bool IsDedicatedServer =>
            MyAPIGateway.Multiplayer.MultiplayerActive && MyAPIGateway.Utilities.IsDedicated;

        public static bool IsClient =>
            MyAPIGateway.Multiplayer.MultiplayerActive && !MyAPIGateway.Multiplayer.IsServer;

        public static bool IsValid(IMyEntity obj)
        {
            return obj != null && !obj.MarkedForClose && !obj.Closed;
        }
    }
}
