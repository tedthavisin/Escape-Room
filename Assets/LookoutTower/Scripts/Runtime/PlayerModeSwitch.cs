using UnityEngine;
using UnityEngine.XR.Management;

namespace LookoutTower
{
    /// <summary>
    /// Turns on the VR rig when an XR runtime is running (a Quest, Quest Link, or the Meta XR Simulator)
    /// and the desktop explorer otherwise, so the same scene works for both.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class PlayerModeSwitch : MonoBehaviour
    {
        public GameObject desktopPlayer;
        public GameObject xrRig;
        [Tooltip("How long to wait for the XR runtime to start before settling on desktop mode.")]
        public float waitForXrSeconds = 2f;

        bool _decided;
        float _until;

        void Awake()
        {
            // Neither player runs until we know which one is wanted (avoids two cameras for a frame).
            if (desktopPlayer != null) desktopPlayer.SetActive(false);
            if (xrRig != null) xrRig.SetActive(false);
            _until = Time.realtimeSinceStartup + waitForXrSeconds;
            Update();
        }

        void Update()
        {
            if (_decided) return;
            bool xr = XrRunning();
            if (!xr && Time.realtimeSinceStartup < _until) return;
            _decided = true;
            if (xrRig != null) xrRig.SetActive(xr);
            if (desktopPlayer != null) desktopPlayer.SetActive(!xr || xrRig == null);
            Debug.Log($"[LookoutTower] Player mode: {(xr && xrRig != null ? "VR rig" : "desktop explorer")}");
            enabled = false;
        }

        static bool XrRunning()
        {
            var settings = XRGeneralSettings.Instance;
            return settings != null && settings.Manager != null && settings.Manager.activeLoader != null;
        }
    }
}
