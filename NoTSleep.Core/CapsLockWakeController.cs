namespace NoTSleep.Core;

public interface ICapsLockState
{
    bool IsOn { get; }
}

public readonly record struct LidActions(uint OnAcPower, uint OnBattery);

public interface IWakePlatform
{
    void SetAutomaticSleepBlocked(bool blocked);
    LidActions GetLidActions();
    void SetLidActions(LidActions actions);
    void JiggleMouse();
}

/// <summary>
/// Keeps the computer awake only while Caps Lock is enabled. The controller is
/// deliberately independent of Windows APIs so its timing and restore logic
/// can be tested without affecting the computer.
/// </summary>
public sealed class CapsLockWakeController : IDisposable
{
    private static readonly TimeSpan JiggleInterval = TimeSpan.FromMinutes(10);
    private readonly ICapsLockState _capsLock;
    private readonly IWakePlatform _platform;
    private bool _isActive;
    private bool _lidActionWasChanged;
    private LidActions _originalLidActions;
    private DateTimeOffset _nextJiggleAt;

    public CapsLockWakeController(ICapsLockState capsLock, IWakePlatform platform)
    {
        _capsLock = capsLock;
        _platform = platform;
    }

    public bool IsActive => _isActive;
    public string Status { get; private set; } = "Caps Lock desligado: proteção inativa.";

    public event EventHandler<string>? StatusChanged;

    public void Tick(DateTimeOffset now)
    {
        if (_capsLock.IsOn)
        {
            if (!_isActive)
            {
                Activate(now);
            }

            if (_isActive && now >= _nextJiggleAt)
            {
                _platform.JiggleMouse();
                _nextJiggleAt = now.Add(JiggleInterval);
                SetStatus("Ativo: mouse movimentado discretamente; próxima ação em 10 minutos.");
            }

            return;
        }

        if (_isActive)
        {
            Deactivate();
        }
    }

    public void Dispose() => Deactivate();

    private void Activate(DateTimeOffset now)
    {
        _platform.SetAutomaticSleepBlocked(true);
        _isActive = true;
        _nextJiggleAt = now.Add(JiggleInterval);

        try
        {
            _originalLidActions = _platform.GetLidActions();
            _platform.SetLidActions(new LidActions(OnAcPower: 0, OnBattery: 0));
            _lidActionWasChanged = true;
            SetStatus("Ativo: o computador não deve bloquear, suspender ou hibernar por inatividade.");
        }
        catch (Exception exception)
        {
            // The mouse movement and the execution-state request still protect
            // against an idle lock. A managed policy can prevent changing the lid action.
            SetStatus($"Ativo, mas a ação da tampa não pôde ser alterada: {exception.Message}");
        }
    }

    private void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        try
        {
            if (_lidActionWasChanged)
            {
                _platform.SetLidActions(_originalLidActions);
            }
        }
        finally
        {
            _lidActionWasChanged = false;
            _platform.SetAutomaticSleepBlocked(false);
            _isActive = false;
            SetStatus("Caps Lock desligado: configurações de energia restauradas.");
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke(this, status);
    }
}
