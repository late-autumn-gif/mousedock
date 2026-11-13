namespace MouseDock.Services;

internal sealed class TrayHoverState
{
    internal enum Change { None, Open, Close }
    private long? entered, outside;
    internal bool Owned { get; private set; }

    internal Change Tick(long now, bool enabled, bool overTray, bool overWindow, bool busy, bool visible)
    {
        if (!enabled)
        {
            bool close = Owned;
            Pin();
            return close ? Change.Close : Change.None;
        }
        if (Owned && !visible) Pin();
        if (!Owned)
        {
            if (visible || !overTray || busy) { entered = null; return Change.None; }
            entered ??= now;
            if (now - entered.Value < 350) return Change.None;
            Owned = true;
            entered = outside = null;
            return Change.Open;
        }
        if (overTray || overWindow || busy) outside = null;
        else
        {
            outside ??= now;
            if (now - outside.Value >= 650)
            {
                Pin();
                return Change.Close;
            }
        }
        return Change.None;
    }

    internal bool Pin()
    {
        bool wasOwned = Owned;
        Owned = false;
        entered = outside = null;
        return wasOwned;
    }
}
