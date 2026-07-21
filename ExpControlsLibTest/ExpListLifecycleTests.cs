using System.Reflection;
using ExpControlsLib;
using WindowsApiLib.Shell;

namespace ExpControlsLibTest;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ExpListLifecycleTests
{
    private ShellController _shellController = null!;

    [SetUp]
    public void SetUp()
    {
        _shellController = new ShellController();
    }

    [TearDown]
    public void TearDown()
    {
        _shellController.Dispose();
    }

    [Test]
    public void Dispose_ReleasesControlOwnedStaRunnerAndContextMenu()
    {
        using var expList = new ExpList();
        expList.Initialize(_shellController);
        using var form = new Form();
        form.Controls.Add(expList);
        form.Show();
        Application.DoEvents();

        var runner = GetPrivateField<StaThreadRunner>(expList, "_staRunner");
        Assert.That(runner, Is.Not.Null, "The control should create its STA runner when loaded.");

        expList.Dispose();

        Assert.That(GetPrivateField<StaThreadRunner>(expList, "_staRunner"), Is.Null);
        Assert.That(GetPrivateField<bool>(GetPrivateField<ExpControlsLib.ContextMenu>(expList, "m_WindowsContextMenu"), "_disposed"), Is.True);
        Assert.Throws<ObjectDisposedException>(() => runner!.EnqueueWork(() => { }));
    }

    [Test]
    public void Dispose_IsIdempotent()
    {
        using var expList = new ExpList();

        Assert.DoesNotThrow(() =>
        {
            expList.Dispose();
            expList.Dispose();
        });
    }

    [Test]
    public void DeleteSelectedItems_WhenShellContextIsUnavailable_IsSafeNoOp()
    {
        using var expList = new ExpList();
        expList.Initialize(_shellController);

        // No current folder means there is no shell context to invoke. This is the
        // failure path used when a delete request races with navigation/teardown.
        Assert.DoesNotThrow(() => expList.DeleteSelectedItems());
        Assert.That(expList.Count, Is.EqualTo(0));
    }

    [Test]
    public void ContextMenu_Dispose_IsSafeWhenNoMenuWasCreated()
    {
        using var menu = new ExpControlsLib.ContextMenu();

        Assert.That(GetPrivateField<bool>(menu, "_disposed"), Is.False);
        Assert.DoesNotThrow(() =>
        {
            menu.Dispose();
            menu.Dispose();
        });
        Assert.That(GetPrivateField<bool>(menu, "_disposed"), Is.True);
    }

    private static T GetPrivateField<T>(object instance, string name)
    {
        var field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Private field '{name}' was not found.");
        return (T)field!.GetValue(instance)!;
    }
}
