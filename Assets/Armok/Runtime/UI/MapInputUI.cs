using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class MapInputUI
{
    public static bool HasKeyboardFocus
    {
        get
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            // A selected button can remain selected after the panel closes.
            // Only a text field currently being edited should consume shortcuts.
            if (selected == null || !selected.activeInHierarchy) return false;
            var input = selected.GetComponent<InputField>();
            var tmpInput = selected.GetComponent<TMPro.TMP_InputField>();
            return (input != null && input.isFocused) || (tmpInput != null && tmpInput.isFocused);
        }
    }

    public static bool PointerOverUI => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
}
