// Copyright @ MyScript. All rights reserved.

using MyScript.IInk.UIReferenceImplementation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Windows.Graphics.Display;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MyScript.IInk.Demo
{
    public class FlyoutCommand : System.Windows.Input.ICommand
    {
        public delegate void InvokedHandler(FlyoutCommand command);

        public string Id { get; set; }
        private InvokedHandler _handler = null;

        public FlyoutCommand(string id, InvokedHandler handler)
        {
            Id = id;
            _handler = handler;
        }

        public bool CanExecute(object parameter)
        {
            return _handler != null;
        }

        public void Execute(object parameter)
        {
            _handler(this);
        }

        public event EventHandler CanExecuteChanged;
    }

    public sealed partial class MainPage
    {
        public static readonly DependencyProperty EditorProperty =
            DependencyProperty.Register("Editor", typeof(Editor), typeof(MainPage),
                new PropertyMetadata(default(Editor)));

        public Editor Editor
        {
            get => GetValue(EditorProperty) as Editor;
            set => SetValue(EditorProperty, value);
        }
    }

    public sealed partial class MainPage
    {
        private Graphics.Point _lastPointerPosition;
        private ContentBlock _lastSelectedBlock;

        private int _filenameIndex;
        private string _packageName;

        // Offscreen rendering
        private float _dpiX = 96;
        private float _dpiY = 96;

        public MainPage()
        {
            _filenameIndex = 0;
            _packageName = "";

            InitializeComponent();
            Initialize(App.Engine);
            KeyDown +=  Page_KeyDown;
        }

        private void Initialize(Engine engine)
        {
            // Initialize the editor with the engine
            var info = DisplayInformation.GetForCurrentView();
            _dpiX = info.RawDpiX;
            _dpiY = info.RawDpiY;
            var pixelDensity = UcEditor.GetPixelDensity();

            if (pixelDensity > 0.0f)
            {
                _dpiX /= pixelDensity;
                _dpiY /= pixelDensity;
            }

            // RawDpi properties can return 0 when the monitor does not provide physical dimensions and when the user is
            // in a clone or duplicate multiple -monitor setup.
            if (_dpiX == 0 || _dpiY == 0)
                _dpiX = _dpiY = 96;

            var renderer = engine.CreateRenderer(_dpiX, _dpiY, UcEditor);
            renderer.AddListener(new RendererListener(UcEditor));
            var toolController = engine.CreateToolController();
            Initialize(Editor = engine.CreateEditor(renderer, toolController));

            UcEditor.SmartGuide.MoreClicked += ShowSmartGuideMenu;

            NewFile();
        }

        private void Initialize(Editor editor)
        {
            editor.SetViewSize((int)ActualWidth, (int)ActualHeight);
            editor.SetFontMetricsProvider(new FontMetricsProvider(_dpiX, _dpiY));
            editor.AddListener(new EditorListener(UcEditor));
        }

        private void Page_KeyDown(object sender, Windows.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            var ctrlKey = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control);
            var shftKey = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift);

            var ctrl = ctrlKey.HasFlag(CoreVirtualKeyStates.Down);
            var shft = shftKey.HasFlag(CoreVirtualKeyStates.Down);

            if (e.Key == VirtualKey.Z)
            {
                if (ctrl)
                {
                    if (shft)
                        Editor.Redo();
                    else
                        Editor.Undo();

                    e.Handled = true;
                }
            }
            else
            if (e.Key == VirtualKey.Y)
            {
                if (ctrl && !shft)
                {
                    Editor.Redo();
                    e.Handled = true;
                }
            }
        }

        private void AppBar_UndoButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.Undo();
        }

        private void AppBar_RedoButton_Click(object sender, RoutedEventArgs e)
        {
            Editor.Redo();
        }

        private async void AppBar_ConvertButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var supportedStates = Editor.GetSupportedTargetConversionStates(null);

                if ( (supportedStates != null) && (supportedStates.Count() > 0) )
                  Editor.Convert(null, supportedStates[0]);
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private void AppBar_NewPackageButton_Click(object sender, RoutedEventArgs e)
        {
            NewFile();
        }

        private async void AppBar_NewPartButton_Click(object sender, RoutedEventArgs e)
        {
            if (Editor.Part == null)
            {
                NewFile();
                return;
            }

            var partType = await ChoosePartType(true);

            if (!string.IsNullOrEmpty(partType))
            {
                _lastSelectedBlock?.Dispose();
                _lastSelectedBlock = null;

                var previousPart = Editor.Part;
                var package = previousPart.Package;

                try
                {
                    Editor.Part = null;

                    var part = package.CreatePart(partType);
                    Editor.Part = part;
                    Title.Text = _packageName + " - " + part.Type;

                    previousPart.Dispose();
                }
                catch (Exception ex)
                {
                    Editor.Part = previousPart;
                    Title.Text = _packageName + " - " + Editor.Part.Type;

                    var msgDialog = new MessageDialog(ex.ToString());
                    await msgDialog.ShowAsync();
                }

                // Reset viewing parameters
                UcEditor.ResetView(false);
            }
        }

        private void AppBar_PreviousPartButton_Click(object sender, RoutedEventArgs e)
        {
            var part = Editor.Part;

            if (part != null)
            {
                var package = part.Package;
                var index = package.IndexOfPart(part);

                if (index > 0)
                {
                    _lastSelectedBlock?.Dispose();
                    _lastSelectedBlock = null;
                    Editor.Part = null;

                    while (--index >= 0)
                    {
                        ContentPart newPart = null;

                        try
                        {
                            // Select new part
                            newPart = part.Package.GetPart(index);
                            Editor.Part = newPart;
                            Title.Text = _packageName + " - " + newPart.Type;
                            part.Dispose();
                            break;
                        }
                        catch
                        {
                            // Can't set this part, try the previous one
                            Editor.Part = null;
                            Title.Text = "";
                            newPart?.Dispose();
                        }
                    }

                    if (index < 0)
                    {
                        // Restore current part if none can be set
                        Editor.Part = part;
                        Title.Text = _packageName + " - " + part.Type;
                    }

                    // Reset viewing parameters
                    UcEditor.ResetView(false);
                }
            }
        }

        private void AppBar_NextPartButton_Click(object sender, RoutedEventArgs e)
        {
            var part = Editor.Part;

            if (part != null)
            {
                var package = part.Package;
                var count = package.PartCount;
                var index = package.IndexOfPart(part);

                if (index < count - 1)
                {
                    _lastSelectedBlock?.Dispose();
                    _lastSelectedBlock = null;
                    Editor.Part = null;

                    while (++index < count)
                    {
                        ContentPart newPart = null;

                        try
                        {
                            // Select new part
                            newPart = part.Package.GetPart(index);
                            Editor.Part = newPart;
                            Title.Text = _packageName + " - " + newPart.Type;
                            part.Dispose();
                            break;
                        }
                        catch
                        {
                            // Can't set this part, try the next one
                            Editor.Part = null;
                            Title.Text = "";
                            newPart?.Dispose();
                        }
                    }

                    if (index >= count)
                    {
                        // Restore current part if none can be set
                        Editor.Part = part;
                        Title.Text = _packageName + " - " + part.Type;
                    }

                    // Reset viewing parameters
                    UcEditor.ResetView(false);
                }
            }
        }

        private void AppBar_ResetViewButton_Click(object sender, RoutedEventArgs e)
        {
            UcEditor.ResetView(true);
        }

        private void AppBar_ZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            UcEditor.ZoomIn(1);
        }

        private void AppBar_ZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            UcEditor.ZoomOut(1);
        }

        private async void AppBar_OpenPackageButton_Click(object sender, RoutedEventArgs e)
        {
            List<string> files = new List<string>();

            // List iink files inside LocalFolders
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            var items = await localFolder.GetItemsAsync();
            foreach (var item in items)
            {
                if(item.IsOfType(StorageItemTypes.File) && item.Path.EndsWith(".iink"))
                    files.Add(item.Name.ToString());
            }
            if (files.Count == 0)
                return;

            // Display file list
            ListBox fileList = new ListBox
            {
                ItemsSource = files,
                SelectedIndex = 0
            };
            ContentDialog fileNameDialog = new ContentDialog
            {
                Title = "Select Package Name",
                Content = fileList,
                IsSecondaryButtonEnabled = true,
                PrimaryButtonText = "Ok",
                SecondaryButtonText = "Cancel",
            };
            if (await fileNameDialog.ShowAsync() == ContentDialogResult.Secondary)
                return;

            var fileName = fileList.SelectedValue.ToString();
            var filePath = System.IO.Path.Combine(localFolder.Path.ToString(), fileName);

            _lastSelectedBlock?.Dispose();
            _lastSelectedBlock = null;

            try
            {
                // Save and close current package
                SavePackage();
                ClosePackage();

                // Open package and select first part
                var package = Editor.Engine.OpenPackage(filePath);
                var part = package.GetPart(0);
                Editor.Part = part;
                _packageName = fileName;
                Title.Text = _packageName + " - " + part.Type;
            }
            catch (Exception ex)
            {
                ClosePackage();

                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }

            // Reset viewing parameters
            UcEditor.ResetView(false);
        }

        private async void AppBar_SavePackageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var part = Editor.Part;
                var package = part?.Package;
                package?.Save();
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private async void AppBar_SaveAsButton_Click(object sender, RoutedEventArgs e)
        {
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;

            // Show file name input dialog
            TextBox inputTextBox = new TextBox
            {
                AcceptsReturn = false,
                Height = 32
            };
            ContentDialog fileNameDialog = new ContentDialog
            {
                Title = "Enter New Package Name",
                Content = inputTextBox,
                IsSecondaryButtonEnabled = true,
                PrimaryButtonText = "Ok",
                SecondaryButtonText = "Cancel",
            };

            if (await fileNameDialog.ShowAsync() == ContentDialogResult.Secondary)
                return;

            var fileName = inputTextBox.Text;
            if (fileName == null || fileName == "")
                return;

            // Add iink extension if needed
            if (!fileName.EndsWith(".iink"))
                fileName = fileName + ".iink";

            // Display overwrite dialog (if needed)
            string filePath = null;
            var item = await localFolder.TryGetItemAsync(fileName);
            if (item != null)
            {
                ContentDialog overwriteDialog = new ContentDialog
                {
                    Title = "File Already Exists",
                    Content = "A file with that name already exists, overwrite it?",
                    PrimaryButtonText = "Cancel",
                    SecondaryButtonText = "Overwrite"
                };

                ContentDialogResult result = await overwriteDialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                    return;

                filePath = item.Path.ToString();
            }
            else
            {
                filePath = System.IO.Path.Combine(localFolder.Path.ToString(), fileName);
            }

            var part = Editor.Part;
            if (part != null)
            {
                try
                {
                    // Save Package with new name
                    part.Package.SaveAs(filePath);

                    // Update internals
                    _packageName = fileName;
                    Title.Text = _packageName + " - " + part.Type;
                }
                catch (Exception ex)
                {
                    var msgDialog = new MessageDialog(ex.ToString());
                    await msgDialog.ShowAsync();
                }
            }
        }

        private void DisplayContextualMenu(Windows.Foundation.Point globalPos)
        {
            var part = Editor.Part;
            if (Editor.Part == null)
                return;

            using (var rootBlock = Editor.GetRootBlock())
            {
                var contentBlock = _lastSelectedBlock;
                if (contentBlock == null)
                    return;

                var isRoot = contentBlock.Id == rootBlock.Id;
                if (!isRoot && (contentBlock.Type == "Container") )
                    return;

                var onRawContent = part.Type == "Raw Content";
                var onTextDocument = part.Type == "Text Document";

                var isEmpty = Editor.IsEmpty(contentBlock);

                var supportedTypes = Editor.SupportedAddBlockTypes;
                var supportedExports = Editor.GetSupportedExportMimeTypes(onRawContent ? rootBlock : contentBlock);
                var supportedImports = Editor.GetSupportedImportMimeTypes(contentBlock);
                var supportedStates = Editor.GetSupportedTargetConversionStates(contentBlock);

                var hasTypes = (supportedTypes != null) && supportedTypes.Any();
                var hasExports = (supportedExports != null) && supportedExports.Any();
                var hasImports = (supportedImports != null) && supportedImports.Any();
                var hasStates = (supportedStates != null) && supportedStates.Any();

                var displayConvert  = hasStates && !isEmpty;
                var displayAddBlock = hasTypes && isRoot;
                var displayAddImage = false; // hasTypes && isRoot;
                var displayRemove   = !isRoot;
                var displayCopy     = !onTextDocument || !isRoot;
                var displayPaste    = isRoot;
                var displayImport   = hasImports;
                var displayExport   = hasExports;
                var displayClipboard = hasExports && supportedExports.Contains(MimeType.OFFICE_CLIPBOARD);

                var flyoutMenu = new MenuFlyout();

                if (displayAddBlock || displayAddImage)
                {
                    var flyoutSubItem = new MenuFlyoutSubItem { Text = "Add..." };
                    flyoutMenu.Items.Add(flyoutSubItem);

                    if (displayAddBlock)
                    {
                        for (var i = 0; i < supportedTypes.Count(); ++i)
                        {
                            var command = new FlyoutCommand(supportedTypes[i], (cmd) => { Popup_CommandHandler_AddBlock(cmd); });
                            var flyoutItem = new MenuFlyoutItem { Text = "Add " + supportedTypes[i], Command = command };
                            flyoutSubItem.Items.Add(flyoutItem);
                        }
                    }

                    if (displayAddImage)
                    {
                        var command = new FlyoutCommand("Image", (cmd) => { Popup_CommandHandler_AddImage(cmd); });
                        var flyoutItem = new MenuFlyoutItem { Text = "Add Image", Command = command };
                        flyoutSubItem.Items.Add(flyoutItem);
                    }
                }

                if (displayRemove)
                {
                    var command = new FlyoutCommand("Remove", (cmd) => { Popup_CommandHandler_Remove(cmd); });
                    var flyoutItem = new MenuFlyoutItem { Text = "Remove", Command = command };
                    flyoutMenu.Items.Add(flyoutItem);
                }

                if (displayConvert)
                {
                    var command = new FlyoutCommand("Convert", (cmd) => { Popup_CommandHandler_Convert(cmd); });
                    var flyoutItem = new MenuFlyoutItem { Text = "Convert", Command = command };
                    flyoutMenu.Items.Add(flyoutItem);
                }

                if (displayCopy || displayClipboard || displayPaste)
                {
                    var flyoutSubItem = new MenuFlyoutSubItem { Text = "Copy/Paste..." };
                    flyoutMenu.Items.Add(flyoutSubItem);

                    //if (displayCopy)
                    {
                        var command = new FlyoutCommand("Copy", (cmd) => { Popup_CommandHandler_Copy(cmd); });
                        var flyoutItem = new MenuFlyoutItem { Text = "Copy", Command = command,  IsEnabled = displayCopy };
                        flyoutSubItem.Items.Add(flyoutItem);
                    }

                    //if (displayClipboard)
                    {
                        var command = new FlyoutCommand("Copy To Clipboard (Microsoft Office)", (cmd) => { Popup_CommandHandler_OfficeClipboard(cmd); });
                        var flyoutItem = new MenuFlyoutItem { Text = "Copy To Clipboard (Microsoft Office)", Command = command, IsEnabled = displayClipboard };
                        flyoutSubItem.Items.Add(flyoutItem);
                    }

                    //if (displayPaste)
                    {
                        var command = new FlyoutCommand("Paste", (cmd) => { Popup_CommandHandler_Paste(cmd); });
                        var flyoutItem = new MenuFlyoutItem { Text = "Paste", Command = command, IsEnabled = displayPaste };
                        flyoutSubItem.Items.Add(flyoutItem);
                    }
                }

                if (displayImport || displayExport)
                {
                    var flyoutSubItem = new MenuFlyoutSubItem { Text = "Import/Export..." };
                    flyoutMenu.Items.Add(flyoutSubItem);

                    //if (displayImport)
                    {
                        var command = new FlyoutCommand("Import", (cmd) => { Popup_CommandHandler_Import(cmd); });
                        var flyoutItem = new MenuFlyoutItem { Text = "Import", Command = command, IsEnabled = displayImport };
                        flyoutSubItem.Items.Add(flyoutItem);
                    }

                    //if (displayExport)
                    {
                        var command = new FlyoutCommand("Export", (cmd) => { Popup_CommandHandler_Export(cmd); });
                        var flyoutItem = new MenuFlyoutItem { Text = "Export", Command = command, IsEnabled = displayExport };
                        flyoutSubItem.Items.Add(flyoutItem);
                    }
                }

                if (flyoutMenu.Items.Count > 0)
                {
                    flyoutMenu.ShowAt(null, globalPos);
                }
            }
        }

        private void UcEditor_Holding(object sender, Windows.UI.Xaml.Input.HoldingRoutedEventArgs e)
        {
            // Only for Pen and Touch (but it should not been fired for a Mouse)
            // Do not wait for the Release event, open the menu immediately

            if (e.PointerDeviceType == Windows.Devices.Input.PointerDeviceType.Mouse)
                return;

            if (e.HoldingState != Windows.UI.Input.HoldingState.Started)
                return;

            var uiElement = sender as UIElement;
            var pos = e.GetPosition(uiElement);

            _lastPointerPosition = new Graphics.Point((float)pos.X, (float)pos.Y);
            _lastSelectedBlock?.Dispose();
            _lastSelectedBlock = Editor.HitBlock(_lastPointerPosition.X, _lastPointerPosition.Y);

            if ( (_lastSelectedBlock == null) || (_lastSelectedBlock.Type == "Container") )
            {
                _lastSelectedBlock?.Dispose();
                _lastSelectedBlock = Editor.GetRootBlock();
            }

            // Discard current stroke
            UcEditor.CancelSampling(UcEditor.GetPointerId(e));

            if (_lastSelectedBlock != null)
            {
                var globalPos = e.GetPosition(null);
                DisplayContextualMenu(globalPos);
            }

            e.Handled = true;
        }

        private void UcEditor_RightTapped(object sender, Windows.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            // Only for Mouse to avoid issue with LongPress becoming RightTap with Pen/Touch
            if (e.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Mouse)
                return;

            var uiElement = sender as UIElement;
            var pos = e.GetPosition(uiElement);

            _lastPointerPosition = new Graphics.Point((float)pos.X, (float)pos.Y);
            _lastSelectedBlock?.Dispose();
            _lastSelectedBlock = Editor.HitBlock(_lastPointerPosition.X, _lastPointerPosition.Y);

            if ( (_lastSelectedBlock == null) || (_lastSelectedBlock.Type == "Container") )
            {
                _lastSelectedBlock?.Dispose();
                _lastSelectedBlock = Editor.GetRootBlock();
            }

            if (_lastSelectedBlock != null)
            {
                var globalPos = e.GetPosition(null);
                DisplayContextualMenu(globalPos);
            }

            e.Handled = true;
        }

        private void UcEditor_RightDown(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            // Only for Pen to avoid issue with LongPress becoming RightTap with Pen/Touch
            var uiElement = sender as UIElement;
            var p = e.GetCurrentPoint(uiElement);

            if (e.Pointer.PointerDeviceType != Windows.Devices.Input.PointerDeviceType.Pen)
                return;

            if (!p.Properties.IsRightButtonPressed)
                return;

            _lastPointerPosition = new Graphics.Point((float)p.Position.X, (float)p.Position.Y);
            _lastSelectedBlock?.Dispose();
            _lastSelectedBlock = Editor.HitBlock(_lastPointerPosition.X, _lastPointerPosition.Y);

            if ( (_lastSelectedBlock == null) || (_lastSelectedBlock.Type == "Container") )
            {
                _lastSelectedBlock?.Dispose();
                _lastSelectedBlock = Editor.GetRootBlock();
            }

            if (_lastSelectedBlock != null)
            {
                var globalPos = e.GetCurrentPoint(null).Position;
                DisplayContextualMenu(globalPos);
            }

            e.Handled = true;
        }

        private void ShowSmartGuideMenu(Windows.Foundation.Point globalPos)
        {
            _lastSelectedBlock?.Dispose();
            _lastSelectedBlock = UcEditor.SmartGuide.ContentBlock?.ShallowCopy();

            if (_lastSelectedBlock != null)
                DisplayContextualMenu(globalPos);
        }

        private async void Popup_CommandHandler_Convert(FlyoutCommand command)
        {
            try
            {
                if (_lastSelectedBlock != null)
                {
                    var supportedStates = Editor.GetSupportedTargetConversionStates(_lastSelectedBlock);

                    if ( (supportedStates != null) && (supportedStates.Count() > 0) )
                        Editor.Convert(_lastSelectedBlock, supportedStates[0]);
                }
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private async void Popup_CommandHandler_AddBlock(FlyoutCommand command)
        {
            try
            {
              // Uses Id as block type
              var blockType = command.Id.ToString();
              var mimeTypes = Editor.GetSupportedAddBlockDataMimeTypes(blockType);
              var useDialog = (mimeTypes != null) && (mimeTypes.Count() > 0);

              if (!useDialog)
              {
                  Editor.AddBlock(_lastPointerPosition.X, _lastPointerPosition.Y, blockType);
                  UcEditor.Invalidate(LayerType.LayerType_ALL);
              }
              else
              {
                var result = await EnterImportData("Add Content Block", mimeTypes);

                if (result != null)
                {
                    var idx = result.Item1;
                    var data = result.Item2;

                    if ( (idx >= 0) && (idx < mimeTypes.Count()) && (String.IsNullOrWhiteSpace(data) == false) )
                    {
                      Editor.AddBlock(_lastPointerPosition.X, _lastPointerPosition.Y, blockType, mimeTypes[idx], data);
                      UcEditor.Invalidate(LayerType.LayerType_ALL);
                    }
                }
              }
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private void Popup_CommandHandler_AddImage(FlyoutCommand command)
        {
            // TODO
        }

        private async void Popup_CommandHandler_Remove(FlyoutCommand command)
        {
            try
            {
                if (_lastSelectedBlock != null && _lastSelectedBlock.Type != "Container")
                {
                    Editor.RemoveBlock(_lastSelectedBlock);
                    _lastSelectedBlock.Dispose();
                    _lastSelectedBlock = null;
                }
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private async void Popup_CommandHandler_Copy(FlyoutCommand command)
        {
            try
            {
                if (_lastSelectedBlock != null)
                    Editor.Copy(_lastSelectedBlock);
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private async void Popup_CommandHandler_Paste(FlyoutCommand command)
        {
            try
            {
                Editor.Paste(_lastPointerPosition.X, _lastPointerPosition.Y);
            }
            catch (Exception ex)
            {
                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private async void Popup_CommandHandler_Import(FlyoutCommand command)
        {
            var part = Editor.Part;
            if (part == null)
                return;

            if (_lastSelectedBlock == null)
                return;

            var mimeTypes = Editor.GetSupportedImportMimeTypes(_lastSelectedBlock);

            if (mimeTypes == null)
                return;

            if (mimeTypes.Count() == 0)
                return;

            var result = await EnterImportData("Import", mimeTypes);

            if (result != null)
            {
                var idx = result.Item1;
                var data = result.Item2;

                if ( (idx >= 0) && (idx < mimeTypes.Count()) && (String.IsNullOrWhiteSpace(data) == false) )
                {
                    try
                    {
                        Editor.Import_(mimeTypes[idx], data, _lastSelectedBlock);
                    }
                    catch (Exception ex)
                    {
                        var msgDialog = new MessageDialog(ex.ToString());
                        await msgDialog.ShowAsync();
                    }
                }

            }
        }

        private async void Popup_CommandHandler_Export(FlyoutCommand command)
        {
            var part = Editor.Part;
            if (part == null)
                return;

            using (var rootBlock = Editor.GetRootBlock())
            {
                var onRawContent = part.Type == "Raw Content";
                var contentBlock = onRawContent ? rootBlock : _lastSelectedBlock;

                if (contentBlock == null)
                    return;

                var mimeTypes = Editor.GetSupportedExportMimeTypes(contentBlock);

                if (mimeTypes == null)
                    return;

                if (mimeTypes.Count() == 0)
                    return;

                // Show export dialog
                var fileName = await ChooseExportFilename(mimeTypes);

                if (!string.IsNullOrEmpty(fileName))
                {
                    var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
                    var item = await localFolder.TryGetItemAsync(fileName);
                    string filePath = null;

                    if (item != null)
                    {
                        ContentDialog overwriteDialog = new ContentDialog
                        {
                            Title = "File Already Exists",
                            Content = "A file with that name already exists, overwrite it?",
                            PrimaryButtonText = "Cancel",
                            SecondaryButtonText = "Overwrite"
                        };

                        ContentDialogResult result = await overwriteDialog.ShowAsync();
                        if (result == ContentDialogResult.Primary)
                            return;

                        filePath = item.Path.ToString();
                    }
                    else
                    {
                        filePath = System.IO.Path.Combine(localFolder.Path.ToString(), fileName);
                    }

                    try
                    {
                        var drawer = new ImageDrawer();

                        drawer.ImageLoader = UcEditor.ImageLoader;

                        Editor.WaitForIdle();
                        Editor.Export_(contentBlock, filePath, drawer);

                        var file = await StorageFile.GetFileFromPathAsync(filePath);
                        await Windows.System.Launcher.LaunchFileAsync(file);
                    }
                    catch (Exception ex)
                    {
                        var msgDialog = new MessageDialog(ex.ToString());
                        await msgDialog.ShowAsync();
                    }
                }
            }
        }

        private async void Popup_CommandHandler_OfficeClipboard(FlyoutCommand command)
        {
            try
            {
                MimeType[] mimeTypes = null;

                if (_lastSelectedBlock != null)
                    mimeTypes = Editor.GetSupportedExportMimeTypes(_lastSelectedBlock);

                if (mimeTypes != null && mimeTypes.Contains(MimeType.OFFICE_CLIPBOARD))
                {
                    // export block to a file
                    var localFolder = ApplicationData.Current.LocalFolder.Path;
                    var clipboardPath = System.IO.Path.Combine(localFolder.ToString(), "tmp/clipboard.gvml");
                    var drawer = new ImageDrawer();

                    drawer.ImageLoader = UcEditor.ImageLoader;

                    Editor.Export_(_lastSelectedBlock, clipboardPath.ToString(), MimeType.OFFICE_CLIPBOARD, drawer);

                    // read back exported data
                    var clipboardData = File.ReadAllBytes(clipboardPath);
                    var clipboardStream = new MemoryStream(clipboardData);

                    // store the data into clipboard
                    Windows.ApplicationModel.DataTransfer.Clipboard.Clear();
                    var clipboardContent = new Windows.ApplicationModel.DataTransfer.DataPackage();
                    clipboardContent.SetData(MimeTypeF.GetTypeName(MimeType.OFFICE_CLIPBOARD), clipboardStream.AsRandomAccessStream());
                    Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(clipboardContent);
                }
            }
            catch (Exception ex)
            {
                MessageDialog msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private void SavePackage()
        {
            var part = Editor.Part;
            var package = part?.Package;
            package?.Save();
        }

        private void ClosePackage()
        {
            var part = Editor.Part;
            var package = part?.Package;
            Editor.Part = null;
            part?.Dispose();
            package?.Dispose();
            Title.Text = "";
        }

        private async void NewFile()
        {
            var cancelable = Editor.Part != null;
            var partType = await ChoosePartType(cancelable);
            if (string.IsNullOrEmpty(partType))
                return;

            _lastSelectedBlock?.Dispose();
            _lastSelectedBlock = null;

            try
            {
                // Save and close current package
                SavePackage();
                ClosePackage();

                // Create package and part
                var packageName = MakeUntitledFilename();
                var package = Editor.Engine.CreatePackage(packageName);
                var part = package.CreatePart(partType);
                Editor.Part = part;
                _packageName = System.IO.Path.GetFileName(packageName);
                Title.Text = _packageName + " - " + part.Type;
            }
            catch (Exception ex)
            {
                ClosePackage();

                var msgDialog = new MessageDialog(ex.ToString());
                await msgDialog.ShowAsync();
            }
        }

        private string MakeUntitledFilename()
        {
            var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            var tempFolder = Editor.Engine.Configuration.GetString("content-package.temp-folder");
            string fileName;
            string folderName;

            do
            {
                var baseName = "File" + (++_filenameIndex) + ".iink";
                fileName = System.IO.Path.Combine(localFolder, baseName);
                var tempName = baseName + "-file";
                folderName = System.IO.Path.Combine(tempFolder, tempName);
            }
            while (System.IO.File.Exists(fileName) || System.IO.File.Exists(folderName));

            return fileName;
        }


        private async System.Threading.Tasks.Task<string> ChoosePartType(bool cancelable)
        {
            var types = Editor.Engine.SupportedPartTypes.ToList();

            if (types.Count == 0)
                return null;

            var view = new ListView
            {
                ItemsSource = types,
                IsItemClickEnabled = true,
                SelectionMode = ListViewSelectionMode.Single,
                SelectedIndex = -1
            };

            var grid = new Grid();
            grid.Children.Add(view);

            var dialog = new ContentDialog
            {
                Title = "Choose type of content",
                Content = grid,
                PrimaryButtonText = "OK",
                SecondaryButtonText = cancelable ? "Cancel" : "",
                IsPrimaryButtonEnabled = false,
                IsSecondaryButtonEnabled = cancelable
            };

            view.ItemClick += (sender, args) => { dialog.IsPrimaryButtonEnabled = true; };
            dialog.PrimaryButtonClick += (sender, args) => { if (view.SelectedIndex < 0) args.Cancel = true; };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
                return types[view.SelectedIndex];
            else
                return null;
        }

        private async System.Threading.Tasks.Task<Tuple<int, string>> EnterImportData(string title, MimeType[] mimeTypes)
        {
            const bool defaultWrapping = false;
            const double defaultWidth = 400;

            var mimeTypeTextBlock = new TextBlock
            {
                Text = "Choose a mime type",
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                Width = defaultWidth,
            };

            var mimeTypeComboBox = new ComboBox
            {
                IsTextSearchEnabled = true,
                SelectedIndex = -1,
                Margin = new Thickness(0, 5, 0, 5),
                Width = defaultWidth
            };

            foreach (var mimeType in mimeTypes)
                mimeTypeComboBox.Items.Add(MimeTypeF.GetTypeName(mimeType));

            mimeTypeComboBox.SelectedIndex = 0;

            var dataTextBlock = new TextBlock
            {
                Text = "Enter some text",
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 0),
                Width = defaultWidth
            };

            var dataTextBox = new TextBox
            {
                Text = "",
                AcceptsReturn = true,
                TextWrapping = (defaultWrapping ? TextWrapping.Wrap : TextWrapping.NoWrap),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Margin = new Thickness(0),
                Width = defaultWidth,
                Height = 200,
            };

            ScrollViewer.SetVerticalScrollBarVisibility(dataTextBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(dataTextBox, ScrollBarVisibility.Auto);

            var dataWrappingCheckBox = new CheckBox
            {
                Content = "Wrapping",
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 5),
                Width = defaultWidth,
                IsChecked = defaultWrapping,
            };

            dataWrappingCheckBox.Checked    += new RoutedEventHandler( (sender, e) =>  { dataTextBox.TextWrapping = TextWrapping.Wrap; } );
            dataWrappingCheckBox.Unchecked  += new RoutedEventHandler( (sender, e) =>  { dataTextBox.TextWrapping = TextWrapping.NoWrap; } );

            var panel = new StackPanel
            {
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            panel.Children.Add(mimeTypeTextBlock);
            panel.Children.Add(mimeTypeComboBox);
            panel.Children.Add(dataTextBlock);
            panel.Children.Add(dataTextBox);
            panel.Children.Add(dataWrappingCheckBox);


            var dialog = new ContentDialog
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = "OK",
                SecondaryButtonText = "Cancel",
                IsPrimaryButtonEnabled = true,
                IsSecondaryButtonEnabled = true
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                // Convert '\r' to '\n'
                // https://stackoverflow.com/questions/42867242/uwp-textbox-puts-r-only-how-to-set-linebreak
                var text = dataTextBox.Text.Replace('\r', '\n');
                return new Tuple<int, string>(mimeTypeComboBox.SelectedIndex, text);
            }

            return null;
        }

        private async System.Threading.Tasks.Task<string> ChooseExportFilename(MimeType[] mimeTypes)
        {
            var mimeTypeTextBlock = new TextBlock
            {
                Text = "Choose a mime type",
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                Width = 300,
            };

            var mimeTypeComboBox = new ComboBox
            {
                IsTextSearchEnabled = true,
                SelectedIndex = -1,
                Margin = new Thickness(0, 5, 0, 0),
                Width = 300
            };

            foreach (var mimeType in mimeTypes)
                mimeTypeComboBox.Items.Add(MimeTypeF.GetTypeName(mimeType));

            mimeTypeComboBox.SelectedIndex = 0;

            var nameTextBlock = new TextBlock
            {
                Text = "Enter Export File Name",
                MaxLines = 1,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                Width = 300
            };

            var nameTextBox = new TextBox
            {
                Text = "",
                AcceptsReturn = false,
                MaxLength = 1024 * 1024,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 10),
                Width = 300
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            panel.Children.Add(mimeTypeTextBlock);
            panel.Children.Add(mimeTypeComboBox);
            panel.Children.Add(nameTextBlock);
            panel.Children.Add(nameTextBox);


            var dialog = new ContentDialog
            {
                Title = "Export",
                Content = panel,
                PrimaryButtonText = "OK",
                SecondaryButtonText = "Cancel",
                IsPrimaryButtonEnabled = true,
                IsSecondaryButtonEnabled = true
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var fileName = nameTextBox.Text;
                var extIndex = mimeTypeComboBox.SelectedIndex;
                var extensions = MimeTypeF.GetFileExtensions(mimeTypes[extIndex]).Split(',');

                int ext;
                for (ext = 0; ext < extensions.Count(); ++ext)
                {
                    if (fileName.EndsWith(extensions[ext], StringComparison.OrdinalIgnoreCase))
                        break;
                }

                if (ext >= extensions.Count())
                    fileName += extensions[0];

                return fileName;
            }

            return null;
        }

        private void AppBar_EnableSmartGuide_Click(object sender, RoutedEventArgs e)
        {
            AppBarToggleButton checkBox = sender as AppBarToggleButton;
            UcEditor.SmartGuideEnabled = (bool)checkBox.IsChecked;
        }
    }
}
