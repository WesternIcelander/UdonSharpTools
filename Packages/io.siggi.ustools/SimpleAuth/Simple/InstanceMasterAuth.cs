using SiggiUSTools;
using UdonSharp;
using VRC.SDKBase;

namespace SiggiUSTools {
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class InstanceMasterAuth : SimpleAuth {
        public override bool IsAuthorized(VRCPlayerApi who) {
            return Utilities.IsValid(who) && who.isMaster;
        }
    }
}
