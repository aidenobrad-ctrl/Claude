namespace Pulsebreak.Client.UI
{
    /// <summary>
    /// Set while the player is typing into something in play (chat), so screens that take
    /// single keys (Esc for the pause menu, B for loadouts, F1, F5) leave the keyboard alone.
    /// </summary>
    public static class TextEntry
    {
        public static bool Active;
    }
}
