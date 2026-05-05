using LearnJP.Services;
using Xunit;

namespace LearnJP.Tests.SaveData;

/// <summary>
/// Tests the failure-handling state machine in <see cref="MauiPreferenceBackend"/>: when
/// the underlying platform preferences API throws, the backend should latch into
/// "fallback" mode and keep serving reads/writes from an in-memory dict for the lifetime
/// of the instance. The pure happy path is exercised indirectly via
/// <see cref="SettingsServiceTests"/>; this class focuses on the throw/fallback transitions.
/// </summary>
public sealed class MauiPreferenceBackendTests
{
    /// <summary>Test stub for <see cref="MauiPreferenceBackend.IPlatformPreferences"/>.
    /// Counts calls and lets the test toggle "throw" mode on demand.</summary>
    private sealed class StubPlatform : MauiPreferenceBackend.IPlatformPreferences
    {
        public int GetCalls;
        public int SetCalls;
        public bool ThrowOnNextCall;
        public readonly Dictionary<string, object> Underlying = new();

        public T Get<T>(string key, T defaultValue)
        {
            GetCalls++;
            if (ThrowOnNextCall) throw new InvalidOperationException("simulated platform failure");
            return Underlying.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            SetCalls++;
            if (ThrowOnNextCall) throw new InvalidOperationException("simulated platform failure");
            Underlying[key] = value!;
        }
    }

    [Fact]
    public void HappyPath_DelegatesToPlatform()
    {
        var stub = new StubPlatform();
        var b = new MauiPreferenceBackend(stub);

        b.Set("k", "v");
        Assert.Equal("v", b.Get("k", string.Empty));
        Assert.Equal(1, stub.SetCalls);
        Assert.Equal(1, stub.GetCalls);
    }

    [Fact]
    public void Set_WhenPlatformThrows_FallsBackToInMemoryDict()
    {
        var stub = new StubPlatform { ThrowOnNextCall = true };
        var b = new MauiPreferenceBackend(stub);

        b.Set("k", "v");                     // throws inside platform → caught → stored in fallback
        stub.ThrowOnNextCall = false;        // even if platform recovers, backend stays in fallback

        var getCallsBefore = stub.GetCalls;
        Assert.Equal("v", b.Get("k", "default"));
        // The recovered platform was NEVER consulted on the read — proving the latch.
        Assert.Equal(getCallsBefore, stub.GetCalls);
    }

    [Fact]
    public void Get_WhenPlatformThrows_FallsBackToInMemoryDict()
    {
        var stub = new StubPlatform();
        var b = new MauiPreferenceBackend(stub);

        // Pre-populate the fallback by writing first under throwing conditions, but the
        // contract we want to verify here is the read-side latch: a Get that throws should
        // also flip the backend into fallback mode.
        stub.ThrowOnNextCall = true;
        var read = b.Get("missing", "default");
        Assert.Equal("default", read);

        // Subsequent writes should go to the fallback dict — never touching the platform.
        var setCallsBeforeWrite = stub.SetCalls;
        b.Set("after", 42);
        Assert.Equal(setCallsBeforeWrite, stub.SetCalls);
        Assert.Equal(42, b.Get("after", 0));
    }

    [Fact]
    public void Fallback_IsScopedPerInstance()
    {
        // Documented (and accepted) limitation: each backend keeps its own fallback dict.
        // Spinning up a new MauiPreferenceBackend after the platform has failed gets a
        // fresh empty store. This test pins that behaviour so a future refactor that
        // changes it (e.g. to a static dict) doesn't slip through unnoticed.
        var stub1 = new StubPlatform { ThrowOnNextCall = true };
        var b1 = new MauiPreferenceBackend(stub1);
        b1.Set("k", "from-instance-1");

        var stub2 = new StubPlatform { ThrowOnNextCall = true };
        var b2 = new MauiPreferenceBackend(stub2);
        Assert.Equal("default", b2.Get("k", "default"));
    }

    [Fact]
    public void Fallback_Honors_TypeMismatch_AsDefault()
    {
        // If a key was stored as one type and is read as another, the fallback returns the
        // default value (not a coerced value). Mirrors what Preferences.Default does in
        // production when a type clash happens, so this isn't a regression — but it IS a
        // load-bearing behaviour worth pinning.
        var stub = new StubPlatform { ThrowOnNextCall = true };
        var b = new MauiPreferenceBackend(stub);
        b.Set("k", 7);                     // stored as int via fallback
        stub.ThrowOnNextCall = true;       // keep reads in fallback too
        Assert.Equal("nope", b.Get("k", "nope"));
    }
}
