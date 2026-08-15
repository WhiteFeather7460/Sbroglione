using FileExplorer.Services;

namespace FileExplorer.Tests;

public sealed class CredentialStoreFactoryTests
{
    [Fact]
    public void Create_ReturnsNonNullStore()
    {
        var store = CredentialStoreFactory.Create();
        Assert.NotNull(store);
    }

    [Fact]
    public async Task NullCredentialStore_IsUnavailable_AndReturnsNoPassword()
    {
        var store = new NullCredentialStore();
        Assert.False(store.IsAvailable);
        Assert.Null(await store.GetPasswordAsync(Guid.NewGuid()));
        // Set e Delete non devono lanciare: sono no-op.
        await store.SetPasswordAsync(Guid.NewGuid(), "x");
        await store.DeletePasswordAsync(Guid.NewGuid());
    }
}
