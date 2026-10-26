using NoTSleep.Core;

var tests = new (string Name, Action Run)[]
{
    ("Não faz nada com Caps Lock desligado", CapsLockOffDoesNothing),
    ("Ativa proteção e desabilita ação da tampa", CapsLockOnActivatesProtection),
    ("Move o mouse exatamente a cada 10 minutos", JigglesEveryTenMinutes),
    ("Restaura a ação anterior da tampa ao desligar Caps Lock", CapsLockOffRestoresSettings),
    ("Restaura configurações ao encerrar o programa", DisposeRestoresSettings)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASSOU: {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FALHOU: {test.Name}\n{exception.Message}");
    }
}

return failures == 0 ? 0 : 1;

static void CapsLockOffDoesNothing()
{
    var caps = new FakeCapsLock(false);
    var platform = new FakeWakePlatform();
    using var controller = new CapsLockWakeController(caps, platform);
    controller.Tick(T0());
    Equal(0, platform.SleepRequests);
    Equal(0, platform.JiggleCount);
}

static void CapsLockOnActivatesProtection()
{
    var caps = new FakeCapsLock(true);
    var platform = new FakeWakePlatform { CurrentLidActions = new LidActions(1, 2) };
    using var controller = new CapsLockWakeController(caps, platform);
    controller.Tick(T0());
    True(controller.IsActive);
    EqualSequence(new[] { true }, platform.SleepRequestValues);
    Equal(new LidActions(0, 0), platform.CurrentLidActions);
}

static void JigglesEveryTenMinutes()
{
    var caps = new FakeCapsLock(true);
    var platform = new FakeWakePlatform();
    using var controller = new CapsLockWakeController(caps, platform);
    controller.Tick(T0());
    controller.Tick(T0().AddMinutes(9).AddSeconds(59));
    Equal(0, platform.JiggleCount);
    controller.Tick(T0().AddMinutes(10));
    Equal(1, platform.JiggleCount);
    controller.Tick(T0().AddMinutes(20));
    Equal(2, platform.JiggleCount);
}

static void CapsLockOffRestoresSettings()
{
    var caps = new FakeCapsLock(true);
    var platform = new FakeWakePlatform { CurrentLidActions = new LidActions(3, 1) };
    using var controller = new CapsLockWakeController(caps, platform);
    controller.Tick(T0());
    caps.IsOn = false;
    controller.Tick(T0().AddMinutes(1));
    Equal(new LidActions(3, 1), platform.CurrentLidActions);
    EqualSequence(new[] { true, false }, platform.SleepRequestValues);
    False(controller.IsActive);
}

static void DisposeRestoresSettings()
{
    var caps = new FakeCapsLock(true);
    var platform = new FakeWakePlatform { CurrentLidActions = new LidActions(1, 1) };
    var controller = new CapsLockWakeController(caps, platform);
    controller.Tick(T0());
    controller.Dispose();
    Equal(new LidActions(1, 1), platform.CurrentLidActions);
    EqualSequence(new[] { true, false }, platform.SleepRequestValues);
}

static DateTimeOffset T0() => new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Esperado: {expected}; atual: {actual}.");
    }
}

static void EqualSequence<T>(IEnumerable<T> expected, IEnumerable<T> actual)
{
    if (!expected.SequenceEqual(actual))
    {
        throw new InvalidOperationException($"Esperado: [{string.Join(", ", expected)}]; atual: [{string.Join(", ", actual)}].");
    }
}

static void True(bool value)
{
    if (!value) throw new InvalidOperationException("O valor deveria ser verdadeiro.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("O valor deveria ser falso.");
}

file sealed class FakeCapsLock(bool isOn) : ICapsLockState
{
    public bool IsOn { get; set; } = isOn;
}

file sealed class FakeWakePlatform : IWakePlatform
{
    public int SleepRequests { get; private set; }
    public List<bool> SleepRequestValues { get; } = [];
    public int JiggleCount { get; private set; }
    public LidActions CurrentLidActions { get; set; } = new(1, 1);

    public void SetAutomaticSleepBlocked(bool blocked)
    {
        SleepRequests++;
        SleepRequestValues.Add(blocked);
    }

    public LidActions GetLidActions() => CurrentLidActions;
    public void SetLidActions(LidActions actions) => CurrentLidActions = actions;
    public void JiggleMouse() => JiggleCount++;
}
