using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace Magpie.Desktop.Controls;

public partial class NewProfileFlyoutContent
{
    private MenuFlyout _nameMenu=null!;
    private MenuItem _nameProcess=null!,_nameTitle=null!,_nameCut=null!,_nameCopy=null!,_nameUndo=null!,_nameRedo=null!,_nameSelectAll=null!;
    private void InitializeNameMenu()
    {
        _nameMenu=new MenuFlyout();
        MenuItem Item(string key,Action action)
        {
            var item=new MenuItem {Header=LocExtension.Get(key)};item.Click+=(_,_)=>action();_nameMenu.Items.Add(item);return item;
        }
        _nameCut=Item("TextMenuFlyout_Cut",()=>NameInput.Cut());
        _nameCopy=Item("TextMenuFlyout_Copy",()=>NameInput.Copy());
        Item("TextMenuFlyout_Paste",()=>NameInput.Paste());
        _nameUndo=Item("TextMenuFlyout_Undo",()=>NameInput.Undo());
        _nameRedo=Item("TextMenuFlyout_Redo",()=>NameInput.Redo());
        _nameSelectAll=Item("TextMenuFlyout_SelectAll",()=>NameInput.SelectAll());
        _nameProcess=Item("Root_NewProfileFlyout_NameContextFlyout_ProcessName",()=>FillName(false));
        _nameTitle=Item("Root_NewProfileFlyout_NameContextFlyout_WindowTitle",()=>FillName(true));
        _nameMenu.Opening+=(_,_)=>RefreshNameMenu();NameInput.ContextFlyout=_nameMenu;
        PopupAlignment.Track(NameInput,_nameMenu);
        NameInput.KeyDown+=Name_KeyDown;
    }
    private void RefreshNameMenu()
    {
        _nameCut.IsVisible=_nameCopy.IsVisible=!string.IsNullOrEmpty(NameInput.SelectedText);
        _nameUndo.IsVisible=NameInput.CanUndo;_nameRedo.IsVisible=NameInput.CanRedo;
        _nameSelectAll.IsVisible=!string.IsNullOrEmpty(NameInput.Text);
        _nameProcess.IsVisible=_nameTitle.IsVisible=_selectedWindow is not null;
    }
    private void FillName(bool title)
    {
        if(_selectedWindow is not {} source)return;
        string value=title?source.Title:source.DefaultName;
        if(NameInput.Text==value)return;
        NameInput.Text=value;NameInput.SelectionStart=NameInput.SelectionEnd=NameInput.CaretIndex=value.Length;NameInput.Focus();
    }
    private void Name_KeyDown(object? sender,KeyEventArgs args)
    {
        if(args.Key!=Key.Enter||!CreateButton.IsEnabled||args.Handled)return;
        Create_Click(CreateButton,new RoutedEventArgs(Button.ClickEvent));args.Handled=true;
    }
}
