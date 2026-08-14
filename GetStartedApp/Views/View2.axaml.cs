using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GetStartedApp.ViewModels;

namespace GetStartedApp.Views;

public partial class View2 : UserControl
{
    public View2()
    {
        InitializeComponent();
        DataContext = new View2ViewModel();
    }

}
