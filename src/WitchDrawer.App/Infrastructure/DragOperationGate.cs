using System.Threading;

namespace WitchDrawer.App.Infrastructure;

internal sealed class DragOperationGate
{
    private int _isEntered;

    public bool IsEntered => Volatile.Read(ref _isEntered) != 0;

    public bool TryEnter()
    {
        return Interlocked.CompareExchange(ref _isEntered, 1, 0) == 0;
    }

    public void Exit()
    {
        Volatile.Write(ref _isEntered, 0);
    }
}
