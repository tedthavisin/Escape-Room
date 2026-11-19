using System.Collections.Generic;
using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// The fuses the player is carrying: one in hand, the rest in a pocket. Works the same for the desktop explorer and the VR rig: aim at a fuse and
    /// interact to pick it up, aim at a slot (or the tin / crate) and interact to put it in. Picking up while already
    /// holding a fuse swaps the two, so a fuse is never lost or stuck in the hand.
    ///
    /// The held fuse floats in the lower right of the view (whatever camera is Camera.main: desktop or headset).
    /// </summary>
    public static class FuseHands
    {
        /// <summary>The fuse in hand (the one a slot gets).</summary>
        public static Fuse Held { get; private set; }

        /// <summary>Fuses carried but not in hand. Q (or A/X on a Quest controller) swaps the next one into hand.</summary>
        public static IReadOnlyList<Fuse> Pocket => s_Pocket;
        static readonly List<Fuse> s_Pocket = new List<Fuse>();

        /// <summary>Most fuses carried at once (hand + pocket).</summary>
        public static int Capacity = 4;
        public static int Count => (Held != null ? 1 : 0) + s_Pocket.Count;
        public static bool HasRoom => Count < Capacity;

        /// <summary>Where the held fuse sits relative to the camera (lower right, band towards the player).</summary>
        public static Vector3 HoldOffset = new Vector3(0.16f, -0.15f, 0.38f);
        public static Vector3 HoldEuler = new Vector3(25f, 0f, -40f);

        public static event System.Action<Fuse> HeldChanged;

        /// <summary>
        /// Pick up a fuse. Whatever was in hand goes in the pocket, unless <paramref name="swapIntoSource"/> (a panel
        /// slot: the held fuse goes in where this one came out) or the pocket is full (then it swaps too).
        /// </summary>
        public static void PickUp(Fuse f, bool swapIntoSource = false)
        {
            if (f == null || f == Held || s_Pocket.Contains(f)) return;
            var from = f.Socket;
            if (from != null && from.IsBusy) return;
            var previous = Held;
            bool swap = previous != null && (swapIntoSource || !HasRoom);
            Vector3 pos = f.transform.position;
            Quaternion rot = f.transform.rotation;
            Transform parent = f.transform.parent;
            if (from != null) from.Release(f, notify: !swap);

            Held = null;
            if (previous != null)
            {
                if (!swap) Stow(previous);
                else if (from != null) from.Insert(previous, animate: true);
                else Drop(previous, pos, rot, parent);
            }
            Hold(f);
            PuzzleSfx.Play(PuzzleSfx.Pickup, f.transform.position, 0.6f);
        }

        /// <summary>Hand the held fuse over (to a socket); the next pocketed fuse comes into hand. Null if empty-handed.</summary>
        public static Fuse TakeHeld()
        {
            var f = Held;
            if (f == null) return null;
            Held = null;
            f.SetCollidersEnabled(true);
            f.SetHeld(false);
            if (s_Pocket.Count > 0) Hold(Unstow());
            else HeldChanged?.Invoke(null);
            return f;
        }

        /// <summary>Put the held fuse in the pocket and bring out the next one.</summary>
        public static void Cycle()
        {
            if (Held == null || s_Pocket.Count == 0) return;
            var h = Held;
            Held = null;
            Stow(h);
            Hold(Unstow());
            PuzzleSfx.Play(PuzzleSfx.Pickup, Held.transform.position, 0.4f);
        }

        static void Hold(Fuse f)
        {
            Held = f;
            f.gameObject.SetActive(true);
            f.SetCollidersEnabled(false);
            f.SetHeld(true);
            f.transform.SetParent(null, true);
            FuseHandsRunner.Ensure();
            HeldChanged?.Invoke(f);
        }

        static void Stow(Fuse f)
        {
            s_Pocket.Add(f);
            f.transform.SetParent(null, true);
            f.gameObject.SetActive(false);
        }

        static Fuse Unstow()
        {
            var n = s_Pocket[0];
            s_Pocket.RemoveAt(0);
            return n;
        }

        static void Drop(Fuse f, Vector3 pos, Quaternion rot, Transform parent)
        {
            f.transform.SetParent(parent, true);
            f.transform.SetPositionAndRotation(pos, rot);
            f.SetCollidersEnabled(true);
            f.SetHeld(false);
        }

        /// <summary>Empty hands and pocket without placing the fuses anywhere (e.g. when resetting the room).</summary>
        public static void Forget()
        {
            Held = null;
            s_Pocket.Clear();
            HeldChanged?.Invoke(null);
        }

        /// <summary>"10 A fuse (red)" plus what's in the pocket, for the HUD.</summary>
        public static string Describe()
        {
            if (Held == null) return "";
            var sb = new System.Text.StringBuilder("Holding: " + Held.Describe());
            if (s_Pocket.Count > 0)
            {
                sb.Append("     Pocket: ");
                for (int i = 0; i < s_Pocket.Count; i++) sb.Append(i > 0 ? ", " : "").Append(s_Pocket[i].Describe());
                sb.Append("     [Q] switch");
            }
            return sb.ToString();
        }
    }

    /// <summary>Short on-screen messages for puzzles (desktop). Also logged, so they show up while testing in VR.</summary>
    public static class PuzzleHints
    {
        public static event System.Action<string> Shown;

        public static void Show(string msg)
        {
            Debug.Log("[LookoutTower] " + msg);
            FuseHandsRunner.Ensure();
            Shown?.Invoke(msg);
        }
    }
}
