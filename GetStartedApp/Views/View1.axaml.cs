using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using GetStartedApp.ViewModels;

namespace GetStartedApp.Views;

public partial class View1 : UserControl
{
    public View1()
    {
        InitializeComponent();
        DataContext = new View1ViewModel();
    }


}
