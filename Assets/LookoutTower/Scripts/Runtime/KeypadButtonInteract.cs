using NavKeypad;
using UnityEngine;

namespace LookoutTower
{
    /// <summary>
    /// Makes one button of the asset-store keypad pressable through <see cref="IInteractable"/>, so the desktop explorer
    /// (E) and the VR controllers (trigger) can use it. The asset's own KeypadInteractionFPV reads the legacy Input
    /// Manager, which this project has turned off.
    /// </summary>
    [RequireComponent(typeof(KeypadButton))]
    public class KeypadButtonInteract : MonoBehaviour, IInteractable
    {
        KeypadButton _button;
        string _label;

        public string Prompt => $"Press {Label}";

        string Label
        {
            get
            {
                if (_label == null)
                {
                    // the buttons are named bttn0..bttn9 and bttnEnter
                    string n = gameObject.name;
                    _label = n.StartsWith("bttn") ? n.Substring(4) : n;
                }
                return _label;
            }
        }

        public void Interact()
        {
            if (_button == null) _button = GetComponent<KeypadButton>();
            _button.PressButton();
        }
    }
}
