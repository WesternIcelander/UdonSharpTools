using SiggiUSTools;
using UdonSharp;
using VRC.SDKBase;

namespace SiggiUSTools {
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NoOneAuth : SimpleAuth {
        public override bool IsAuthorized(VRCPlayerApi who) {
            return false;
        }
    }
}
