using System.Reflection;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Sbroglione;

using Xunit;

namespace Sbroglione.Tests;

// ISingleViewApplicationLifetime is marked [NotClientImplementable] in Avalonia 11.2.8:
// the compiler refuses any type that declares `: ISingleViewApplicationLifetime` directly
// (CS0535 on an unspeakable member injected by Avalonia's analyzer). DispatchProxy sidesteps
// this because the interface implementation is emitted by the runtime (Reflection.Emit), not
// by Roslyn compiling a user-authored `: ISingleViewApplicationLifetime` declaration.
file class FakeSingleViewLifetime : DispatchProxy
{
    public Control? MainView { get; set; }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null)
            return null;

        if (targetMethod.Name == "get_MainView")
            return MainView;

        if (targetMethod.Name == "set_MainView")
        {
            MainView = (Control?)args![0];
            return null;
        }

        return null;
    }

    public static (ISingleViewApplicationLifetime Lifetime, FakeSingleViewLifetime Fake) Create()
    {
        object proxy = DispatchProxy.Create<ISingleViewApplicationLifetime, FakeSingleViewLifetime>()!;
        return ((ISingleViewApplicationLifetime)proxy, (FakeSingleViewLifetime)proxy);
    }
}

public class AppLifetimeBranchTests
{
    [Fact]
    public void OnFrameworkInitializationCompleted_WithSingleViewLifetime_SetsMainView()
    {
        var app = new App();
        var (lifetime, fake) = FakeSingleViewLifetime.Create();
        app.SetApplicationLifetimeForTests(lifetime);

        app.OnFrameworkInitializationCompleted();

        Assert.NotNull(fake.MainView);
    }
}
