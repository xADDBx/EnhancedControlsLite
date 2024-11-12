using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityModManagerNet;

namespace EnhancedControlsLite {
    public class Settings : UnityModManager.ModSettings {
        public bool EndTurnHotkeyEnabled = false;
        public bool EndTurnKeyBindShouldAlsoPause = false;
        public bool RightClickRotate = true;
        public bool MiddleClickAlsoRotate = false;
        public bool FastForwardHotkeyEnabled = false;
        public override void Save(UnityModManager.ModEntry modEntry) {
            Save(this, modEntry);
        }
    }
}
