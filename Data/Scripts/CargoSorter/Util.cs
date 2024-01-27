using Sandbox.ModAPI;
using System;
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
    public static class UtilExtensions
    {

        //
        // Summary:
        //     Removes whitespace from the end. Copied because the real one is prohibited
        public static bool InsensitiveContains(this string inString, string value)
        {
            return inString.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
