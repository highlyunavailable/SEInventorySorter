using Sandbox.ModAPI;
using System.Text;
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

        // Check to see if the flags are at least the minimum for specials and block requests, above any of the normal requestable flags.
        public static bool IsSpecial(TypeRequests typeRequests)
        {
            return typeRequests.HasFlag(TypeRequests.Special);
        }

        //
        // Summary:
        //     Removes whitespace from the end. Copied because the real one is prohibited
        public static StringBuilder TrimTrailingWhitespace(StringBuilder sb)
        {
            int num = sb.Length;
            while (num > 0 && (sb[num - 1] == ' ' || sb[num - 1] == '\r' || sb[num - 1] == '\n'))
            {
                num--;
            }

            sb.Length = num;
            return sb;
        }
    }
}
