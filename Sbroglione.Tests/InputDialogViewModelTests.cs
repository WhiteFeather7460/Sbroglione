using Sbroglione.ViewModels;

namespace Sbroglione.Tests;

public sealed class InputDialogViewModelTests
{
    [Fact]
    public void Constructor_SetsTitleMessageAndInitialText()
    {
        var vm = new InputDialogViewModel("Titolo", "Messaggio", "iniziale");

        Assert.Equal("Titolo", vm.Title);
        Assert.Equal("Messaggio", vm.Message);
        Assert.Equal("iniziale", vm.Text);
    }

    [Fact]
    public void Constructor_NullInitialText_TextIsEmpty()
    {
        var vm = new InputDialogViewModel("T", "M", null);

        Assert.Equal(string.Empty, vm.Text);
    }

    [Fact]
    public void CanConfirm_EmptyOrWhitespaceText_IsFalse()
    {
        var vm = new InputDialogViewModel("T", "M");
        Assert.False(vm.CanConfirm);

        vm.Text = "   ";
        Assert.False(vm.CanConfirm);
    }

    [Fact]
    public void Text_Changed_RaisesTextAndCanConfirm()
    {
        var vm = new InputDialogViewModel("T", "M");
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Text = "Backup foto";

        Assert.True(vm.CanConfirm);
        Assert.Contains(nameof(InputDialogViewModel.Text), raised);
        Assert.Contains(nameof(InputDialogViewModel.CanConfirm), raised);
    }
}
