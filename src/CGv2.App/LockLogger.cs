using Microsoft.Win32;
using CGv2.Core;

namespace CGv2.App;

public sealed class LockLogger : IDisposable
{
    private readonly Store _store;

    public LockLogger(Store store)
    {
        _store = store;
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            _store.Append(EventKind.Lock, DateTime.Now);
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
            _store.Append(EventKind.Unlock, DateTime.Now);
    }

    public void Dispose() => SystemEvents.SessionSwitch -= OnSessionSwitch;
}
