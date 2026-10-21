
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace SiggiUSTools {
    public abstract class SimpleAuth : UdonSharpBehaviour {
        /// <summary>
        /// Check if the specified player is authorized.
        /// </summary>
        /// <param name="who">the player to check</param>
        /// <returns>true if the player is authorized, false otherwise</returns>
        public abstract bool IsAuthorized(VRCPlayerApi who);

        /// <summary>
        /// Check if the specified player is authorized, and if implemented, display a message on a failure. The default
        /// implementation does nothing with the message.
        /// </summary>
        /// <param name="who">the player to check</param>
        /// <param name="messageOnFail">the message to display on a failure</param>
        /// <returns>true if the player is authorized, false otherwise</returns>
        public bool IsAuthorized(VRCPlayerApi who, string messageOnFail)
        {
            return IsAuthorized(who);
        }
    }
}
