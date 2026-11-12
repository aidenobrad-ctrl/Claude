using UnityEngine.UIElements;

namespace Pulsebreak.Client.UI
{
    /// <summary>Small bits of movement for the interface, driven by the transitions in HudStyles.uss.</summary>
    public static class UiMotion
    {
        /// <summary>Fades and lifts an element into place: it starts with the "enter" class, which comes off a frame later.</summary>
        public static void Enter(VisualElement element, long delayMs = 0)
        {
            if (element == null) return;
            element.AddToClassList("enter");
            element.schedule.Execute(() => element.RemoveFromClassList("enter")).StartingIn(16 + delayMs);
        }

        /// <summary>Re-runs a punch-in on an element whose text just changed.</summary>
        public static void Pop(VisualElement element, string startClass)
        {
            if (element == null) return;
            element.AddToClassList(startClass);
            element.schedule.Execute(() => element.RemoveFromClassList(startClass)).StartingIn(16);
        }
    }
}
